using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class CoordinateBreakEditingTests
{
    private const string Train = "MODE SEQ\nUNIT MM\nENPD 2\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDISZ 10\n"
        + "SURF 2\nTYPE COORDBRK\nPARM 1 1\nPARM 3 15\nDISZ 20\nSURF 3\nTYPE COORDBRK\nDISZ 5\nSURF 4\n";

    [Theory]
    [InlineData(false, "MM", 1)]
    [InlineData(true, "MM", 1)]
    [InlineData(false, "CM", 10)]
    [InlineData(true, "CM", 10)]
    public void ImportedFixedCoordinatesRetainEditablePhysicalParametersAndOrder(bool tiltFirst, string unit, double scale)
    {
        var source = Train.Replace("UNIT MM", "UNIT " + unit).Replace("PARM 3 15", $"PARM 3 15\nPARM 2 2\nPARM 4 20\nPARM 5 30\nPARM 6 {(tiltFirst ? 1 : 0)}");
        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var operation = Assert.IsType<CoordinateBreakParameters>(optic.SurfaceGroup.Items[2].Placement?.Break);
        Assert.Equal(scale, operation.X); Assert.Equal(2 * scale, operation.Y);
        Assert.Equal(15, operation.TiltX); Assert.Equal(20, operation.TiltY); Assert.Equal(30, operation.TiltZ);
        Assert.Equal(tiltFirst, operation.TiltFirst);
        var before = optic.SurfaceGroup.Items.Select(s => s.CoordinateSystem).ToArray();
        optic.SurfaceGroup.Renumber();
        for (var i = 0; i < before.Length; i++) Near(before[i].Origin, optic.SurfaceGroup.Items[i].CoordinateSystem.Origin);
        var copy = Optic.FromSnapshot(optic.ToSnapshot());
        var restored = copy.SurfaceGroup.Items[2].Placement!.Break!;
        Assert.Equal(operation.X, restored.X); Assert.Equal(operation.Y, restored.Y);
        Assert.Equal(operation.TiltFirst, restored.TiltFirst); Assert.Equal(operation.TiltZ, restored.TiltZ);
    }

    [Fact]
    public async Task NeutralInsertionPreservesPhysicalGeometryMediumAndTracedRay()
    {
        var rochon = SequentialExampleCatalog.Examples.Single(e => e.RelativePath.EndsWith("/Rochon prism.zmx"));
        var crystal = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(rochon.FilePath)).ActiveOptic;
        foreach (var optic in new[] { Optic.CreateCookeTriplet(), crystal })
        {
            var poses = optic.SurfaceGroup.Items.Select(s => s.CoordinateSystem).ToArray();
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, .1, .1, optic.Wavelengths.First(w => w.IsPrimary).Micrometers);
            var before = optic.SequentialRayTracer.TraceFinalSamples(ray).Single();
            Assert.NotNull(before);
            var inserted = optic.SurfaceGroup.InsertCoordinateBreak(3);
            Assert.IsType<CoordinateReferenceInteractionModel>(inserted.InteractionModel);
            Assert.Equal(optic.SurfaceGroup.Items[2].MaterialAfter.Name, inserted.MaterialAfter.Name);
            Assert.Equal(0, inserted.Thickness);
            for (var i = 0; i < poses.Length; i++)
            {
                var current = optic.SurfaceGroup.Items[i < 3 ? i : i + 1].CoordinateSystem;
                Near(poses[i].Origin, current.Origin);
                Near(poses[i].ToGlobalDirection(new(0, 0, 1)), current.ToGlobalDirection(new(0, 0, 1)));
            }
            var after = optic.SequentialRayTracer.TraceFinalSamples(ray).Single();
            Assert.NotNull(after); Assert.False(after.Vignetted);
            Near(before.Position, after.Position, 1e-8); Near(before.Direction, after.Direction, 1e-10);
        }
    }

    [Theory]
    [InlineData(CoordinateReturnMode.Orientation)]
    [InlineData(CoordinateReturnMode.PositionAndOrientation)]
    public void ReturnTracksTheReferenceAndSurvivesInsertDeleteAndSnapshots(CoordinateReturnMode mode)
    {
        var optic = OpticalFormatCatalog.Import(Train, ".zmx");
        var s = optic.SurfaceGroup.Items;
        s[3].Placement = s[3].Placement! with { Break = s[3].Placement!.Break! with { Return = new(2, mode) } };
        optic.SurfaceGroup.Renumber();
        var expected = mode == CoordinateReturnMode.Orientation ? s[2].CoordinateSystem.ToGlobalPoint(new(0, 0, 20)) : s[2].CoordinateSystem.Origin;
        Near(expected, s[3].CoordinateSystem.Origin);
        s[2].Placement = s[2].Placement! with { Break = s[2].Placement!.Break! with { TiltY = 27, X = 3 } };
        optic.SurfaceGroup.Renumber();
        Near(s[2].CoordinateSystem.ToGlobalDirection(new(0, 0, 1)), s[3].CoordinateSystem.ToGlobalDirection(new(0, 0, 1)));
        optic.SurfaceGroup.InsertDefaultSurface(2);
        Assert.Equal(3, s[4].Placement!.Break!.Return!.SurfaceNumber);
        optic.SurfaceGroup.Remove(s[2]);
        Assert.Equal(2, s[3].Placement!.Break!.Return!.SurfaceNumber);
        Assert.Throws<InvalidOperationException>(() => optic.SurfaceGroup.Remove(s[2]));
        var copy = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(new CoordinateFrameReturn(2, mode), copy.SurfaceGroup.Items[3].Placement!.Break!.Return);
        Near(s[3].CoordinateSystem.Origin, copy.SurfaceGroup.Items[3].CoordinateSystem.Origin);
    }

    [Fact]
    public void VirtualReturnUsesSignedPropagationWithoutChangingTheRayDirection()
    {
        var optic = OpticalFormatCatalog.Import(Train, ".zmx");
        var s = optic.SurfaceGroup.Items;
        s[3].Placement = s[3].Placement! with { Break = s[3].Placement!.Break! with { Return = new(1, CoordinateReturnMode.PositionAndOrientation) } };
        optic.SurfaceGroup.Renumber();
        using var trace = optic.SequentialRayTracer.Trace(new RealRayBundle([new RealRay(new(0, 0, -1), new(0, 0, 1), 587.6)]), TraceRequest.FullHistory());
        Assert.True(trace.TryGetSample(0, 4, out var end));
        Assert.False(end.Vignetted); Near(new(0, 0, 5), end.Position); Near(new(0, 0, 1), end.Direction);
        Assert.Equal(6 * optic.Materials.Resolve("Air").RefractiveIndex(587.6), end.PhaseInclusiveOpticalPathLength!.Value, 8);
    }

    [Fact]
    public void InsertionRemapsOnlyTypedMeritSurfaceSlotsAndProtectsReferencedRows()
    {
        var optic = OpticalFormatCatalog.Import(Train + "REAY 2 1 0 0 0 0 0 1 0 0\n", ".zmx");
        var operand = Assert.Single(optic.MeritFunctionOperands);
        optic.SurfaceGroup.InsertCoordinateBreak(2);
        Assert.Equal(3, operand.Surface); Assert.Equal(new[] { 3, 1 }, operand.ZemaxIntegerParameters);
        Assert.Throws<InvalidOperationException>(() => optic.SurfaceGroup.Remove(optic.SurfaceGroup.Items[3]));
        optic.SurfaceGroup.Remove(optic.SurfaceGroup.Items[2]);
        Assert.Equal(2, operand.Surface); Assert.Equal(1, operand.ZemaxIntegerParameters[1]);
    }

    [Fact]
    public async Task ServiceEditsAreAtomicUndoableRevisionSafeAndPersistent()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var initial = app.Prescription.GetSurfaces().Count;
        var number = app.Prescription.InsertCoordinateBreak(1, false, app.Events.Revision);
        Assert.Equal(initial + 1, app.Prescription.GetSurfaces().Count);
        Assert.True(app.Documents.Undo()); Assert.Equal(initial, app.Prescription.GetSurfaces().Count);
        Assert.True(app.Documents.Redo());
        var edit = app.Prescription.GetCoordinateBreak(number);
        var updated = edit.Settings with { X = 2, TiltY = 3, Variables = new[] { true, false, false, false, false } };
        app.Prescription.UpdateCoordinateBreak(number, updated, edit.Revision);
        Assert.Equal(2, app.Prescription.GetSurfaces()[2].Inspection!.OriginX, 10);
        var revision = app.Events.Revision;
        Assert.Throws<InvalidOperationException>(() => app.Prescription.UpdateCoordinateBreak(number, updated with { X = 4 }, edit.Revision));
        Assert.Throws<ArgumentException>(() => app.Prescription.UpdateCoordinateBreak(number, updated with { X = double.NaN }, revision));
        Assert.Throws<ArgumentException>(() => app.Prescription.UpdateCoordinateBreak(number,
            updated with { ReturnKind = CoordinateReturnKind.Orientation, ReturnSurface = number }, revision));
        Assert.Equal(revision, app.Events.Revision); Assert.Equal(2, app.Prescription.GetCoordinateBreak(number).Settings.X);
        Assert.True(app.Documents.Undo()); Assert.Equal(0, app.Prescription.GetCoordinateBreak(number).Settings.X);
        Assert.True(app.Documents.Redo());
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await app.Documents.SaveAsync(path); await app.Documents.OpenAsync(path);
            var saved = app.Prescription.GetCoordinateBreak(number).Settings;
            Assert.Equal(2, saved.X); Assert.Equal(3, saved.TiltY); Assert.True(saved.Variables![0]);
            var returned = saved with { ReturnKind = CoordinateReturnKind.PositionAndOrientation, ReturnSurface = 0 };
            app.Prescription.UpdateCoordinateBreak(number, returned, app.Events.Revision);
            await app.Documents.SaveAsync(path);
            var document = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(new CoordinateFrameReturn(0, CoordinateReturnMode.PositionAndOrientation), document.Configurations[0].SurfaceGroup.Items[number].Placement!.Break!.Return);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InsertionCoversAllConfigurationsWhileParameterEditsStayInTheSelectedConfiguration()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.MultiConfiguration.Add();
        var number = app.Prescription.InsertCoordinateBreak(1, false, app.Events.Revision);
        var count = app.Prescription.GetSurfaces().Count;
        app.Prescription.UpdateCoordinateBreak(number, new(1, 0, 0, 2, 0, false), app.Events.Revision);
        app.MultiConfiguration.Activate(1);
        Assert.Equal(count, app.Prescription.GetSurfaces().Count);
        Assert.Equal(0, app.Prescription.GetCoordinateBreak(number).Settings.X);
        app.Prescription.UpdateCoordinateBreak(number, new(3, 0, 0, 5, 0, true), app.Events.Revision);
        app.MultiConfiguration.Activate(0);
        Assert.Equal(1, app.Prescription.GetCoordinateBreak(number).Settings.X);
        app.Prescription.RemoveSurface(number);
        app.MultiConfiguration.Activate(1);
        Assert.Equal(count - 1, app.Prescription.GetSurfaces().Count);
    }

    [Fact]
    public void LegacyReferenceUpgradeAndInvalidSnapshotDependenciesAreExplicit()
    {
        var optic = OpticalFormatCatalog.Import(Train, ".zmx");
        foreach (var surface in optic.SurfaceGroup.Items) surface.Placement = null;
        var before = optic.SurfaceGroup.Items.Select(s => s.CoordinateSystem).ToArray();
        SequentialCoordinateEditing.EnsurePlacements(optic); optic.SurfaceGroup.Renumber();
        for (var i = 0; i < before.Length; i++) Near(before[i].Origin, optic.SurfaceGroup.Items[i].CoordinateSystem.Origin);
        Assert.Throws<InvalidOperationException>(() => SequentialCoordinateEditing.ValidateEdit(optic, 1, new(0, 0, 0, 0, 0, false)));
        var invalid = optic.ToSnapshot();
        invalid.Surfaces[3] = invalid.Surfaces[3] with
        {
            Placement = invalid.Surfaces[3].Placement! with { Break = new(0, 0, 0, 0, 0, false) { Return = new(3, CoordinateReturnMode.Orientation) } }
        };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(invalid));
    }

    private static void Near(Vector3D expected, Vector3D actual, double tolerance = 1e-9) => Assert.InRange((actual - expected).Length, 0, tolerance);

    [Fact]
    public void CoordinateEditsChangeRealRefractionAndZeroingRestoresThePhysicalRay()
    {
        var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
        var rays = runtime.CurrentOptic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, .1, .1,
            runtime.CurrentOptic.Wavelengths.First(w => w.IsPrimary).Micrometers);
        var original = runtime.CurrentOptic.SequentialRayTracer.TraceFinalSamples(rays).Single()!;
        var number = runtime.InsertCoordinateBreak(1, false);
        runtime.UpdateCoordinateBreak(number, new(1, 0, 0, 1, 0, false));
        var changed = runtime.CurrentOptic.SequentialRayTracer.TraceFinalSamples(rays).Single()!;
        Assert.NotNull(changed); Assert.False(changed.Vignetted);
        Assert.True((changed.Position - original.Position).Length > .01);
        runtime.UpdateCoordinateBreak(number, new(0, 0, 0, 0, 0, false));
        var restored = runtime.CurrentOptic.SequentialRayTracer.TraceFinalSamples(rays).Single()!;
        Near(original.Position, restored.Position, 1e-8); Near(original.Direction, restored.Direction, 1e-10);
    }

    [Fact]
    public async Task ImportedCoordinatePickupsRecomputeAndCannotBeOverwrittenByTheEditor()
    {
        using var app = WorkbenchApplication.Create();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zmx");
        try
        {
            await File.WriteAllTextAsync(path, Train.Replace("SURF 3\nTYPE COORDBRK\nDISZ 5", "SURF 3\nTYPE COORDBRK\nPPAR 1 2 -1 0\nDISZ 5"));
            await app.Documents.OpenAsync(path);
            var edit = app.Prescription.GetCoordinateBreak(3);
            Assert.Contains(0, edit.ControlledParameters);
            Assert.Throws<InvalidOperationException>(() => app.Prescription.UpdateCoordinateBreak(3, edit.Settings with { X = 100 }, edit.Revision));
            app.Prescription.UpdateCoordinateBreak(2, app.Prescription.GetCoordinateBreak(2).Settings with { X = 2 }, app.Events.Revision);
            Assert.Equal(-2, app.Prescription.GetCoordinateBreak(3).Settings.X);
            Assert.True(app.Documents.Undo()); Assert.Equal(-1, app.Prescription.GetCoordinateBreak(3).Settings.X);
        }
        finally { File.Delete(path); }
    }
}
