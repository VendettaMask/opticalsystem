using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class HighYieldOperandTests
{
    [Theory]
    [InlineData(1, 1.5, 0)]
    [InlineData(1, 1.5, 30)]
    [InlineData(1, 1.5, 70)]
    [InlineData(1.5, 1, 20)]
    [InlineData(1.5, 1.7, 30)]
    [InlineData(1.7, 1.5, 30)]
    [InlineData(1.5, 1.5, 45)]
    public void OfficialPenaltyUsesTheLargerAngleAtTheLowerIndexSide(double before, double after, double angle)
    {
        var surface = new OpticalSurface();
        var ray = new RealRay(new(0, 0, -1), Direction(angle), 550);
        var trace = surface.TraceRay(ray, Glass(before), Glass(after), 0, 0);
        Assert.Equal(RayInteractionKind.Transmitted, trace.InteractionKind);
        var selectedAngle = after > before ? angle * Math.PI / 180
            : Math.Asin(before / after * Math.Sin(angle * Math.PI / 180));
        var expected = Math.Abs(after - before) * (1 - Math.Cos(selectedAngle));
        Near(expected, RayManufacturingMetrics.HighYieldContribution(surface, trace.Sample));
        Assert.Equal(before, trace.Sample.RefractiveIndexBefore);
        Assert.Equal(after, trace.Sample.RefractiveIndexAfter);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FrameAndPropagationDirectionDoNotChangeTheAcuteInterfacePenalty(bool transformed, bool reverse)
    {
        var frame = transformed ? new CoordinateSystem(new(4, -3, 8), 13, 21, -7) : new CoordinateSystem(new(0, 0, 0));
        var surface = new OpticalSurface { CoordinateSystem = frame };
        var source = new RealRay(frame.ToGlobalPoint(new(0, 0, reverse ? 1 : -1)),
            frame.ToGlobalDirection(reverse ? -Direction(30) : Direction(30)), 550);
        var sample = surface.TraceRay(source, Glass(1), Glass(1.5), 0, 0).Sample;
        Near(.5 * (1 - Math.Sqrt(3) / 2), RayManufacturingMetrics.HighYieldContribution(surface, sample));
    }

    [Fact]
    public void VerySmallIncidenceRetainsAPositivePenalty()
    {
        const double radians = 1e-9;
        var surface = new OpticalSurface();
        var source = new RealRay(new(0, 0, -1), new(radians, 0, 1), 550);
        var sample = surface.TraceRay(source, Glass(1), Glass(1.5), 0, 0).Sample;
        var result = RayManufacturingMetrics.HighYieldContribution(surface, sample);
        Assert.InRange(result / (radians * radians / 4), 1 - 1e-12, 1 + 1e-12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarAndBatchedTraceRetainInterfaceIndicesAcrossRetentionAndPublicCopies(bool batched)
    {
        var optic = Plate();
        foreach (var request in new[] { TraceRequest.FullHistory(), TraceRequest.Selected([1, 2]), TraceRequest.FinalOnly(false) })
        {
            using var trace = optic.SequentialRayTracer.Trace(new([new RealRay(new(0, 0, -1), Direction(30), 550)]),
                request with { UseBatchedBackend = batched, NormalizeOpticalPathDifference = false });
            foreach (var index in request.Retention == TraceRetention.FinalOnly ? new[] { 3 } : new[] { 1, 2 })
            {
                Assert.True(trace.TryGetSample(0, index, out var sample));
                Assert.Equal(index == 2 ? 1.5 : 1, sample.RefractiveIndexBefore);
                Assert.Equal(index == 1 ? 1.5 : 1, sample.RefractiveIndexAfter);
                Assert.Equal(sample, RayTraceSampleValue.FromRayTraceSample(sample.ToRayTraceSample()));
                Near(index == 3 ? 0 : .5 * (1 - Math.Sqrt(3) / 2),
                    RayManufacturingMetrics.HighYieldContribution(optic.SurfaceGroup.Items[index], sample.ToRayTraceSample()));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrinExitUsesTheCurvedRayAndLocalIndexInsteadOfTheVertexIndex(bool batched)
    {
        var optic = Plate(new GradientIndexMaterial("transverse GRIN", new Gradient4IndexProfile(1.5, x1: .08), 100));
        using var trace = optic.SequentialRayTracer.Trace(new([new RealRay(new(0, 0, -1), new(0, 0, 1), 550)]),
            TraceRequest.Selected([1, 2]) with { UseBatchedBackend = batched });
        Assert.True(trace.TryGetSample(0, 2, out var sample));
        var a = .08 * 10 / 1.5;
        var n = 1.5 * Math.Cosh(a);
        var sinExit = 1.5 * Math.Sinh(a);
        Near(n, sample.RefractiveIndexBefore!.Value, 2e-8);
        Assert.Equal(1, sample.RefractiveIndexAfter);
        Near((n - 1) * (1 - Math.Sqrt(1 - sinExit * sinExit)),
            RayManufacturingMetrics.HighYieldContribution(optic.SurfaceGroup.Items[2], sample.ToRayTraceSample()), 2e-8);
    }

    [Fact]
    public void CurvedSurfacePenaltyUsesTheNormalAtTheActualHit()
    {
        var surface = new OpticalSurface { Geometry = new StandardGeometry(20) };
        var sample = surface.TraceRay(new(new(0, 4, -1), new(0, 0, 1), 550), Glass(1), Glass(1.5), 0, 0).Sample;
        Near(.5 * (1 - Math.Sqrt(1 - .2 * .2)), RayManufacturingMetrics.HighYieldContribution(surface, sample));
        Assert.True(sample.Position.Z > 0);
    }

    [Fact]
    public void AGrinReflectionKeepsTheIncidentVolumeFrameInSubsequentIndexMetadata()
    {
        var optic = Plate(new GradientIndexMaterial("axial GRIN", new Gradient3IndexProfile(1.5, axial1: .01), 100));
        optic.SurfaceGroup.Items[2].IsReflective = true;
        optic.SurfaceGroup.Items[2].Thickness = -10;
        optic.SurfaceGroup.Renumber();
        using var trace = optic.SequentialRayTracer.Trace(new([new RealRay(new(0, 0, -1), new(0, 0, 1), 550)]), TraceRequest.Selected([2, 3]));
        Assert.True(trace.TryGetSample(0, 2, out var reflected));
        Near(1.6, reflected.RefractiveIndexBefore!.Value);
        Assert.Equal(1, reflected.RefractiveIndexAfter);
        Assert.Throws<NotSupportedException>(() => RayManufacturingMetrics.HighYieldContribution(optic.SurfaceGroup.Items[2], reflected.ToRayTraceSample()));
        Assert.True(trace.TryGetSample(0, 3, out var returned));
        Near(1.5, returned.RefractiveIndexBefore!.Value);
        Near(0, RayManufacturingMetrics.HighYieldContribution(optic.SurfaceGroup.Items[3], returned.ToRayTraceSample()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissesDoNotInventIndicesAndBlockedIntersectionsKeepThemWithoutAValidOperand(bool batched)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.01);
        using var blocked = optic.SequentialRayTracer.Trace(new([new RealRay(new(0, 1, -1), new(0, 0, 1), 550)]),
            TraceRequest.Selected([1]) with { UseBatchedBackend = batched });
        Assert.True(blocked.TryGetSample(0, 1, out var hit)); Assert.True(hit.Vignetted);
        Assert.Equal(1, hit.RefractiveIndexBefore); Assert.Equal(1.5, hit.RefractiveIndexAfter);
        Assert.Throws<InvalidOperationException>(() => RayManufacturingMetrics.HighYieldContribution(optic.SurfaceGroup.Items[1], hit.ToRayTraceSample()));
        using var missed = optic.SequentialRayTracer.Trace(new([new RealRay(new(0, 1, -1), new(1, 0, 0), 550)]),
            TraceRequest.Selected([1]) with { UseBatchedBackend = batched });
        Assert.True(missed.TryGetSample(0, 1, out var miss));
        Assert.Null(miss.RefractiveIndexBefore); Assert.Null(miss.RefractiveIndexAfter);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MeritRawSlotsChooseTheRayAndSurfaceAndYieldIsNotASquaredPenalty(int surface)
    {
        var optic = Plate(); var row = Row(surface);
        row.Surface = 999; row.Wavelength = 999; row.Hy = 0; row.Target = .01; row.Weight = -3;
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.Empty(result.Error); var expected = .5 * (1 - Math.Sqrt(3) / 2);
        Near(expected, result.Value);
        Near(3 * Math.Pow(expected - .01, 2), result.Contribution);
        row.ZemaxDataParameters[1] = 0; Near(0, Value(optic, row));
    }

    [Fact]
    public void PrimaryWavelengthAndChangesToMaterialAndGeometryInvalidateTheTrace()
    {
        var optic = Plate(new CatalogGlassMaterial("TEST", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.5, 1.6]));
        optic.Wavelengths[0].IsPrimary = false;
        optic.Wavelengths.Add(new Wavelength { Nanometers = 700, IsPrimary = true });
        var row = Row(1); row.ZemaxIntegerParameters[1] = 0;
        var factor = 1 - Math.Sqrt(3) / 2;
        Near(.575 * factor, Value(optic, row));
        row.ZemaxIntegerParameters[1] = 1; Near(.5375 * factor, Value(optic, row));
        optic.SurfaceGroup.Items[1].MaterialAfter = Glass(2); Near(factor, Value(optic, row));
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(30);
        row.ZemaxDataParameters[3] = .5;
        Assert.True(Math.Abs(Value(optic, row) - factor) > .01);
    }

    [Fact]
    public void MeritUsesAxialGrinLocalExitIndexAndProductionDlsCanOptimizeThickness()
    {
        var optic = Plate(new GradientIndexMaterial("axial GRIN", new Gradient3IndexProfile(1.5, axial1: .01), 100));
        var factor = 1 - Math.Sqrt(3) / 2;
        var row = Row(2); Near(.6 * factor, Value(optic, row), 1e-8);
        row.Target = .56 * factor; optic.MeritFunctionOperands.Add(row);
        optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-6);
        Assert.InRange(Math.Abs(runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness - 6), 0, 1e-3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void InvalidReferencesCoordinatesAndUnsupportedModelsAreErrors(int problem)
    {
        var optic = Plate(); var row = Row(1);
        switch (problem)
        {
            case 0: row.ZemaxIntegerParameters[0] = 0; break;
            case 1: row.ZemaxIntegerParameters[0] = 999; break;
            case 2: row.ZemaxIntegerParameters[1] = 999; break;
            case 3: row.ZemaxIntegerParameters[1] = -1; break;
            case 4: row.ZemaxDataParameters[0] = double.NaN; break;
            case 5: row.ZemaxDataParameters[1] = 1.1; break;
            case 6: row.ZemaxDataParameters[2] = row.ZemaxDataParameters[3] = .8; break;
            case 7: optic.SurfaceGroup.Items[1].IsReflective = true; break;
            case 8: optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50); break;
            case 9: optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.01); row.ZemaxDataParameters[3] = 1; break;
        }
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void IncompleteOrInvalidTraceMetadataNeverBecomesASuccessfulZero(int problem)
    {
        var surface = new OpticalSurface();
        var sample = surface.TraceRay(new(new(0, 0, -1), Direction(30), 550), Glass(1), Glass(1.5), 0, 0).Sample;
        sample = problem switch
        {
            0 => sample with { RefractiveIndexBefore = null },
            1 => sample with { RefractiveIndexAfter = double.NaN },
            2 => sample with { RefractiveIndexBefore = 0 },
            3 => sample with { IncidentDirection = null },
            4 => sample with { Direction = new(0, 0, 0) },
            5 => sample with { SurfaceNumber = sample.SurfaceNumber + 1 },
            6 => sample with { Intensity = double.NaN },
            _ => sample with { Position = new(double.PositiveInfinity, 0, 0) }
        };
        Assert.Throws<InvalidOperationException>(() => RayManufacturingMetrics.HighYieldContribution(surface, sample));
    }

    [Fact]
    public void TotalInternalReflectionKeepsBothInterfaceIndicesButIsNotYetAnExecutableYieldMode()
    {
        var surface = new OpticalSurface();
        var sample = surface.TraceRay(new(new(0, 0, -1), Direction(50), 550), Glass(1.5), Glass(1), 0, 0).Sample;
        Assert.Equal(RayInteractionKind.TotalInternalReflection, sample.InteractionKind);
        Assert.Equal(1.5, sample.RefractiveIndexBefore); Assert.Equal(1, sample.RefractiveIndexAfter);
        Assert.Throws<NotSupportedException>(() => RayManufacturingMetrics.HighYieldContribution(surface, sample));
    }

    [Fact]
    public void CancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Plate(), Row(1)));
    }

    [Fact]
    public async Task ApplicationEditorAndStaroptPreserveAllSixSlotsAndRecalculate()
    {
        var optic = Plate(); optic.MeritFunctionOperands.Add(Row(1));
        var path = Path.Combine(Path.GetTempPath(), $"high-yield-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new([optic], 0), path);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            var dto = Assert.Single(app.Optimization.GetMeritFunction());
            var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == "HYLD");
            Assert.False(type.CompatibilityOnly); Assert.Equal(6, type.Parameters!.Count);
            Assert.Contains("不是良率百分比", type.Calculation); Assert.Contains("原生", type.Calculation);
            var editor = new MeritOperandEditorRow(dto, type);
            for (var index = 0; index < 6; index++) Assert.True(editor.IsParameterEditable(index));
            editor.Parameter1 = 2; editor.Parameter4 = .5; editor.Target = .02; editor.Weight = 3;
            app.Optimization.SetMeritFunction([editor.ToDto()]); await app.Documents.SaveAsync(path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var row = Assert.Single(copy.MeritFunctionOperands);
            Assert.False(row.CompatibilityOnly); Assert.Equal(2, row.ZemaxIntegerParameters[0]);
            Assert.Equal(.5, row.ZemaxDataParameters[1]); Assert.Equal(.02, row.Target); Assert.Equal(3, row.Weight);
            Near(.5 * (1 - Math.Cos(15 * Math.PI / 180)), Value(copy, row));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnverifiedNativeRowsRemainReadOnlyAcrossImportAndSnapshot()
    {
        const string line = "HYLD 1 1 .1 .2 .3 .4 .05 7 91 92";
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 .55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 10
            SURF 2
              DISZ 0
            {line}
            """, ".zmx");
        foreach (var copy in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()) })
        {
            var row = Assert.Single(copy.MeritFunctionOperands);
            Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
            Assert.Equal(new[] { 1, 1 }, row.ZemaxIntegerParameters);
            Assert.Equal(new[] { .1, .2, .3, .4 }, row.ZemaxDataParameters);
            Assert.Contains("91 92", row.Comment);
            row.Enabled = true;
            Assert.NotEmpty(MeritFunctionCatalog.Evaluate(copy, row).Error);
        }
    }

    private static Optic Plate(IMaterial? material = null)
    {
        var optic = new Optic("High yield test");
        material ??= Glass(1.5);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 10, MaterialAfter = material, SemiDiameter = 30, SemiDiameterFixed = true, IsStop = true },
            new OpticalSurface { Thickness = 5, MaterialBefore = material, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { SemiDiameter = 30, SemiDiameterFixed = true }
        ]);
        optic.Fields.Add(new FieldPoint { Y = 30 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 10;
        return optic;
    }
    private static MeritOperandDefinition Row(int surface) => new()
    {
        Type = "HYLD",
        Surface = surface,
        Wavelength = 1,
        Hy = 1,
        ZemaxIntegerParameters = [surface, 1],
        ZemaxDataParameters = [0, 1, 0, 0],
        Weight = 1
    };
    private static IMaterial Glass(double index) => new ConstantIndexMaterial("test", index);
    private static Vector3D Direction(double angle) => new(0, Math.Sin(angle * Math.PI / 180), Math.Cos(angle * Math.PI / 180));
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); return result.Value;
    }
    private static void Near(double expected, double actual, double tolerance = 1e-11) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
}
