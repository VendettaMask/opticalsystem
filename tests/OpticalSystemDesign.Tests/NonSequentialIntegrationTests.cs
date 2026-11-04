using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialIntegrationTests
{
    private const string SimpleZmx = """
        VERS 110214 522 23000
        MODE NSC
        NAME Source and detector control
        UNIT MM
        WAVM 1 0.55 1
        PWAV 1
        SURF 1
          TYPE NONSEQCO
          NSOH NSC_SRCR 0 0
          NSOP 0 0 0 0 0 0
          NSOD 1 10
          NSOD 2 1000
          NSOD 3 1
          NSOD 4 1
          NSOD 6 1
          NSOD 7 1
          NSOH NSC_DETE 0 0
          NSOP 0 0 10 0 0 0 ABSORB
          NSOD 1 2
          NSOD 2 2
          NSOD 3 8
          NSOD 4 8
        """;

    [Fact]
    public void TextNscIntakeRetainsAllBytesAndMapsExplicitNativeSubsetWithoutPartialScenePublication()
    {
        var result = ZemaxNonSequentialReader.Parse(SimpleZmx); Assert.True(result.CanApply);
        Assert.Equal(Encoding.UTF8.GetBytes(SimpleZmx), result.OriginalBytes()); Assert.Equal(2, result.Objects.Count);
        var doc = result.Document!; var trace = new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials);
        Assert.Equal(1, trace.Detectors.Single().TotalPowerWatts, 11);
        var unknown = ZemaxNonSequentialReader.Parse(SimpleZmx.Replace("NSC_SRCR", "NSC_DLL"));
        Assert.False(unknown.CanApply); Assert.Null(unknown.Document); Assert.Equal(2, unknown.Objects.Count);
        Assert.Contains(unknown.Diagnostics, d => d.Code == "ObjectType");
        var coating = ZemaxNonSequentialReader.Parse(SimpleZmx.Replace("NSOP 0 0 10", "NSCS 1 1 I.50\nNSOP 0 0 10"));
        Assert.False(coating.CanApply); Assert.Contains(coating.Diagnostics, d => d.Code == "UnmappedObjectRecord");
        var mixed = ZemaxNonSequentialReader.Parse(SimpleZmx.Replace("MODE NSC", "MODE SEQ"));
        Assert.False(mixed.CanApply); Assert.Contains(mixed.Diagnostics, d => d.Code == "MixedModePorts");
    }

    [Fact]
    public void UnknownParametersMissingAssetsAndInvalidUnitsRemainExplicitBlockingDiagnostics()
    {
        foreach (var (input, code) in new[]
        {
            (SimpleZmx + "\nNSOD 99 7", "UnknownParameter"),
            (SimpleZmx + "\nNSOA 1 \"missing shape.pob\"", "MissingAsset"),
            (SimpleZmx.Replace("UNIT MM", "UNIT IN"), "Units"),
            (SimpleZmx.Replace("NSOH NSC_DETE 0 0", "NSOH NSC_DETE 1 0"), "ObjectReferences")
        })
        { var result = ZemaxNonSequentialReader.Parse(input); Assert.False(result.CanApply); Assert.Null(result.Document); Assert.Contains(result.Diagnostics, d => d.Code == code); }
    }

    [Fact]
    public async Task ApplicationPhysicsDtoProjectRoundTripAndUndoPreserveSourceJonesAndDetectorNormalization()
    {
        using var app = WorkbenchApplication.Create(); var source = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.SourceRay);
        var detector = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.DetectorRectangle);
        var s = app.NonSequential.GetDocument().Objects.Single(o => o.Id == source);
        app.NonSequential.UpdateObject(Update(s) with { Parameters = ((Dto.SourceRayParameters)s.Parameters) with
            { Physics = new(new(false, 1, 1, 0, 90), "laser", 37) } });
        var d = app.NonSequential.GetDocument().Objects.Single(o => o.Id == detector);
        app.NonSequential.UpdateObject(Update(d) with { Z = 10, Parameters = new Dto.DetectorRectangleParameters(4, 4, 4, 4, true, true, NormalizeCoherentPower: true) });
        app.NonSequential.UpdatePhysicsSettings(true, true);
        Assert.True(app.Documents.Undo()); Assert.False(app.NonSequential.GetDocument().TraceSettings.UsePolarization);
        Assert.True(app.Documents.Redo()); Assert.True(app.NonSequential.GetDocument().TraceSettings.CoherentDetection);
        app.Modes.SwitchTo(Dto.OpticalWorkbenchMode.NonSequential);
        await app.NonSequentialAnalysis.TraceAsync(new(Dto.NonSequentialTraceOutputMode.RayDatabase));
        var coherentView = app.NonSequentialAnalysis.GetDetectorView(new(detector, DataType: Dto.NonSequentialDetectorDataType.CoherentPower));
        Assert.Equal(1, coherentView.Values.Sum(), 11); Assert.Equal("W", coherentView.ValueUnit);
        var stokesView = app.NonSequentialAnalysis.GetDetectorView(new(detector, DataType: Dto.NonSequentialDetectorDataType.StokesV));
        Assert.Equal(-1, stokesView.Values.Sum(), 11);
        app.NonSequential.UpdatePhysicsSettings(false, false);
        await app.NonSequentialAnalysis.TraceAsync(new(Dto.NonSequentialTraceOutputMode.RayDatabase));
        Assert.Throws<InvalidOperationException>(() => app.NonSequentialAnalysis.GetDetectorView(new(detector, DataType: Dto.NonSequentialDetectorDataType.CoherentPower)));
        Assert.Throws<InvalidOperationException>(() => app.NonSequentialAnalysis.GetDetectorView(new(detector, DataType: Dto.NonSequentialDetectorDataType.StokesV)));
        app.NonSequential.UpdatePhysicsSettings(true, true);
        var path = Path.Combine(Path.GetTempPath(), $"nsc-jones-{Guid.NewGuid():N}.staropt");
        try
        {
            await app.Documents.SaveAsync(path); var loaded = await StarOptProjectStore.LoadAsync(path);
            var doc = loaded.NonSequentialDocument!; Assert.True(doc.TraceSettings.UsePolarization); Assert.True(doc.TraceSettings.CoherentDetection);
            Assert.Equal("laser", ((SourceParameters)doc.Objects.Single(o => o.Id == source).Parameters).Physics!.CoherenceGroup);
            Assert.Equal(37, ((SourceParameters)doc.Objects.Single(o => o.Id == source).Parameters).Physics!.PhaseDegrees);
            Assert.True(((DetectorRectangleParameters)doc.Objects.Single(o => o.Id == detector).Parameters).NormalizeCoherentPower);
            using var reopened = WorkbenchApplication.Create(); await reopened.Documents.OpenAsync(path);
            Assert.Equal(JsonSerializer.Serialize(app.NonSequential.GetDocument()), JsonSerializer.Serialize(reopened.NonSequential.GetDocument()));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ApplicationDesignIsFrozenStaleApplyIsRejectedAndValidApplyCanBeUndone()
    {
        using var app = WorkbenchApplication.Create();
        var import = ZemaxNonSequentialReader.Parse(SimpleZmx); app.NonSequential.ApplyZemaxNscImport(import, app.NonSequential.GetDesignSceneIdentity());
        var source = app.NonSequential.GetDocument().Objects.First(); var detector = app.NonSequential.GetDocument().Objects.Last();
        app.NonSequential.UpdateObject(Update(source) with { X = .3 });
        var variable = new NonSequentialDesignVariable(source.Id, NonSequentialVariableDomain.Position, "X", -1, 1, .02);
        var goal = new NonSequentialDesignTarget(detector.Id, NonSequentialMetric.CentroidX, 0, .02);
        var task = app.NonSequential.OptimizeAsync([variable], [goal], new(RayCountOverride: 100, MaximumTraceEvaluations: 60, MaximumLaunchedRays: 6000));
        app.NonSequential.UpdateObject(Update(source) with { X = .4 }); var stale = await task;
        Assert.Throws<InvalidOperationException>(() => app.NonSequential.ApplyDesignResult(stale));
        var result = await app.NonSequential.OptimizeAsync([variable], [goal], new(RayCountOverride: 100, MaximumTraceEvaluations: 60, MaximumLaunchedRays: 6000));
        Assert.True(result.Passed); app.NonSequential.ApplyDesignResult(result); Assert.InRange(Math.Abs(app.NonSequential.GetDocument().Objects.First().X - .4), .01, 1);
        Assert.True(app.Documents.Undo()); Assert.Equal(.4, app.NonSequential.GetDocument().Objects.First().X);
        var identity = app.NonSequential.GetDesignSceneIdentity();
        Assert.Throws<InvalidOperationException>(() => app.NonSequential.ApplyZemaxNscImport(ZemaxNonSequentialReader.Parse(SimpleZmx + "\nNSOD 99 8"), identity));
        Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity());
    }

    private static Dto.NonSequentialObjectUpdateDto Update(Dto.NonSequentialObjectRowDto o) => new(o.Id, o.Enabled, o.Visible, o.Kind,
        o.Name, o.ReferenceObjectId, o.ContainingObjectId, o.X, o.Y, o.Z, o.TiltXDegrees, o.TiltYDegrees, o.TiltZDegrees, o.Parameters, o.SurfaceBsdf);
}
