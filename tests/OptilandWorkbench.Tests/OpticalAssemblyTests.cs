using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis.Assembly;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class OpticalAssemblyTests
{
    internal static Optic Singlet(double r1 = 10, double r2 = -6, double thickness = 3)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.ImportLegacySurfaces(new[]
        {
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Radius = r1, Thickness = thickness, SemiDiameter = 3 },
            new OpticalSurface { Radius = r2, Thickness = 20, SemiDiameter = 3 },
            new OpticalSurface()
        });
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("test-glass", 1.5);
        return optic;
    }

    [Fact]
    public void SingletMatchesIndependentGaussianConjugatesIncludingRearVertex()
    {
        var rows = new AssemblyConjugates(Singlet(), 550).Calculate();
        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Equal(AssemblyImageStatus.Finite, row.Status));
        Assert.Equal(0, rows[0].PositionMillimeters!.Value, 5);
        Assert.Equal(10, rows[1].PositionMillimeters!.Value, 5);
        // Independent spherical refraction reference, confined to this test.
        Assert.Equal(3 / (1.5 - 0.5 * 3 / 10), rows[2].PositionMillimeters!.Value, 5);
        Assert.Equal(-3 / (1.5 + 0.5 * 3 / 10), rows[3].PositionMillimeters!.Value, 5);
    }

    [Fact]
    public void ReverseObservationUsesTheOtherMediaAndTheSameDatum()
    {
        var rows = new AssemblyConjugates(Singlet(), 550, fromRear: true).Calculate();
        Assert.Equal(3, rows[2].PositionMillimeters!.Value, 5);
        Assert.Equal(-3, rows[3].PositionMillimeters!.Value, 5);
        Assert.Equal(3 - 3 / (1.5 - 0.5 * 3 / 6), rows[0].PositionMillimeters!.Value, 5);
    }

    [Fact]
    public void PlaneBehindPoweredSurfaceCanHaveAFiniteCurvatureConjugate()
    {
        var rows = new AssemblyConjugates(Singlet(r2: 0), 550).Calculate();
        // Collimated light inside glass requires focusing toward R/(n-1)*n
        // at the first interface, i.e. the front focal conjugate of that interface.
        Assert.Equal(AssemblyImageStatus.Finite, rows[3].Status);
        Assert.Equal(-20, rows[3].PositionMillimeters!.Value, 4);
        var directPlane = new AssemblyConjugates(Singlet(r1: 0), 550).Calculate();
        Assert.Equal(AssemblyImageStatus.Infinity, directPlane[1].Status);
    }

    [Theory]
    [InlineData(false, 1, AssemblyImageKind.Vertex)]
    [InlineData(false, 1, AssemblyImageKind.CurvatureCenter)]
    [InlineData(false, 2, AssemblyImageKind.Vertex)]
    [InlineData(false, 2, AssemblyImageKind.CurvatureCenter)]
    [InlineData(true, 2, AssemblyImageKind.CurvatureCenter)]
    public void ReticleUsesRoundTripKernelAndFixedMetricDetector(bool rear, int surface, AssemblyImageKind kind)
    {
        var optic = Singlet();
        var before = optic.ToSnapshot();
        var analysis = new AssemblyConjugates(optic, 550, rear);
        var image = analysis.Simulate(surface, kind,
            new AssemblyProbe(NumericalAperture: 0.001, ReticleLength: 0.05, ReticleWidth: 0.002));
        Assert.True(image.ReturnedRays > 0, image.Message);
        Assert.True(image.RecordedRays > 0, image.Message);
        Assert.Equal(4, image.SensorWidth);
        Assert.InRange(Math.Abs(image.Magnification!.Value), 0.99, 1.01);
        Assert.InRange(image.ImageLength!.Value, 0.049, 0.051);
        Assert.Equal(image.RecordedRays, image.Pixels.Sum(), 5);
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before, jsonOptions),
            System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(), jsonOptions));
    }

    [Fact]
    public void UnsupportedAndReflectionlessSurfacesAreExplicit()
    {
        var optic = Singlet();
        optic.SurfaceGroup.Items[2].Conic = -1;
        var rows = new AssemblyConjugates(optic, 550).Calculate();
        Assert.Equal(AssemblyImageStatus.Unsupported, rows[2].Status);
        Assert.Null(rows[2].PositionMillimeters);
        optic = Singlet();
        optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        Assert.All(new AssemblyConjugates(optic, 550).Calculate(),
            row => Assert.Equal(AssemblyImageStatus.NoReflection, row.Status));
    }

    [Fact]
    public void SimulationReportsInaccessibleHeadAndHonorsCancellation()
    {
        var analysis = new AssemblyConjugates(Singlet(), 550);
        var inaccessible = analysis.Simulate(1, AssemblyImageKind.CurvatureCenter,
            new AssemblyProbe(HeadFocalLength: 5));
        Assert.Equal(0, inaccessible.LaunchedRays);
        Assert.Contains("进入镜组", inaccessible.Message);
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var scope = ComputationCancellation.Push(source.Token);
        Assert.Throws<OperationCanceledException>(() => analysis.Calculate());
    }

    [Fact]
    public void ReticlePixelsRespondToPhysicalScaleApertureAndDefocus()
    {
        // A moderate radius keeps the full reticle inside the return pupil.
        var optic = Singlet(r1: 50);
        var analysis = new AssemblyConjugates(optic, 550);
        var probe = new AssemblyProbe(NumericalAperture: 0.01, ReticleLength: 0.4, ReticleWidth: 0.01);
        var focused = analysis.Simulate(1, AssemblyImageKind.CurvatureCenter, probe);
        var doubled = analysis.Simulate(1, AssemblyImageKind.CurvatureCenter, probe with { ReticleLength = 0.8 });
        var defocused = analysis.Simulate(1, AssemblyImageKind.CurvatureCenter, probe with { FocusOffset = 3 });
        static int PixelExtent(AssemblyImage image)
        {
            var positions = image.Pixels.Select((value, index) => (value, x: index % image.Size))
                .Where(item => item.value > 0).Select(item => item.x).ToArray();
            return positions.Length == 0 ? 0 : positions.Max() - positions.Min() + 1;
        }
        Assert.True(PixelExtent(doubled) > PixelExtent(focused) * 1.7,
            $"focused={PixelExtent(focused)}, doubled={PixelExtent(doubled)}, returned={focused.ReturnedRays}; {focused.Message}");
        Assert.True(PixelExtent(defocused) > PixelExtent(focused),
            $"focused={PixelExtent(focused)}, defocused={PixelExtent(defocused)}");
        var cropped = analysis.Simulate(1, AssemblyImageKind.CurvatureCenter, probe with { SensorWidth = 0.1 });
        Assert.True(cropped.RecordedRays < cropped.ReturnedRays);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new OptilandWorkbench.Core.Apertures.CircularAperture(0.02);
        var obstructed = new AssemblyConjugates(optic, 550).Simulate(1, AssemblyImageKind.CurvatureCenter, probe);
        Assert.True(obstructed.ReturnedRays < focused.ReturnedRays);
    }

    [Theory]
    [InlineData(3, 27, AssemblyImageKind.CurvatureCenter)]
    [InlineData(30, -6, AssemblyImageKind.Vertex)]
    public void InfinityConjugateThroughPoweredPrefixIsDetectedAndImaged(double thickness, double radius, AssemblyImageKind kind)
    {
        // Independent first-order reference: air-to-n=1.5 surface R=10
        // focuses parallel rays at z=30; surface 2 has center z=3+27=30.
        var analysis = new AssemblyConjugates(Singlet(r2: radius, thickness: thickness), 550);
        var row = analysis.Calculate().Single(x => x.SurfaceNumber == 2 && x.Kind == kind);
        Assert.Equal(AssemblyImageStatus.Infinity, row.Status);
        var image = analysis.Simulate(2, kind,
            new AssemblyProbe(Mode: AssemblyProbeMode.Infinity, PupilDiameter: 0.5, ReticleLength: 0.1));
        Assert.True(image.MatchesSelectedConjugate);
        Assert.True(image.RecordedRays > 0, image.Message);
        Assert.InRange(Math.Abs(image.Magnification!.Value), 0.999, 1.001);
        var nearby = new AssemblyConjugates(Singlet(r2: radius, thickness: thickness + 0.001), 550)
            .Calculate().Single(x => x.SurfaceNumber == 2 && x.Kind == kind);
        Assert.Equal(AssemblyImageStatus.Finite, nearby.Status);
        Assert.True(double.IsFinite(nearby.PositionMillimeters!.Value));
    }

    [Theory]
    [InlineData(false, 40)]
    [InlineData(false, 160)]
    [InlineData(true, 40)]
    [InlineData(true, 160)]
    public void InfinityPlaneReturnsMetricReticleFromEitherSideAtDifferentDistances(bool rear, double distance)
    {
        var analysis = new AssemblyConjugates(Singlet(0, 0), 550, rear);
        var target = rear ? 2 : 1;
        var probe = new AssemblyProbe(Mode: AssemblyProbeMode.Infinity, InstrumentDistance: distance,
            PupilDiameter: 2, ReticleLength: 0.2,
            HeadFocalLength: double.NaN, NumericalAperture: double.NaN, FocusOffset: double.NaN);
        var image = analysis.Simulate(target, AssemblyImageKind.CurvatureCenter, probe);
        Assert.Equal(AssemblyProbeMode.Infinity, image.Mode);
        Assert.True(image.MatchesSelectedConjugate);
        Assert.Null(image.HeadPosition);
        Assert.Equal(rear ? 3 + distance : -distance, image.CollimatorPosition, 8);
        Assert.True(image.RecordedRays > 0, image.Message);
        Assert.Equal(-1, image.Magnification!.Value, 7);
        Assert.Equal(0.2, image.ImageLength!.Value, 7);
        Assert.Equal(image.RecordedRays, image.Pixels.Sum(), 5);
        var xs = image.Pixels.Select((value, i) => (value, x: i % image.Size))
            .Where(p => p.value > 0).Select(p => p.x).ToArray();
        Assert.InRange((xs.Max() - xs.Min() + 1) * image.SensorWidth / image.Size, 0.19, 0.22);
    }

    [Fact]
    public void InfinityModeTracesFiniteTargetsWithoutClaimingTheyAreFocused()
    {
        var analysis = new AssemblyConjugates(Singlet(r1: 50), 550);
        var image = analysis.Simulate(1, AssemblyImageKind.CurvatureCenter,
            new AssemblyProbe(Mode: AssemblyProbeMode.Infinity, PupilDiameter: 0.5, ReticleLength: 0.1));
        Assert.False(image.MatchesSelectedConjugate);
        Assert.True(image.LaunchedRays > 0);
        Assert.Contains("未对焦", image.Message);
        Assert.Null(image.HeadPosition);
    }

    [Fact]
    public void InfinityModeRejectsInvalidInstrumentSettingsAndFocusedModeRejectsInfinity()
    {
        var analysis = new AssemblyConjugates(Singlet(0, 0), 550);
        var probe = new AssemblyProbe(Mode: AssemblyProbeMode.Infinity);
        foreach (var invalid in new[] { probe with { Mode = (AssemblyProbeMode)99 },
                     probe with { PupilDiameter = 0 }, probe with { InstrumentDistance = double.PositiveInfinity },
                     probe with { CollimatorFocalLength = double.PositiveInfinity } })
            Assert.Throws<ArgumentOutOfRangeException>(() => analysis.Simulate(1, AssemblyImageKind.CurvatureCenter, invalid));
        var ex = Assert.Throws<InvalidOperationException>(() => analysis.Simulate(1, AssemblyImageKind.CurvatureCenter, new AssemblyProbe()));
        Assert.Contains("切换", ex.Message);
    }

    [Fact]
    public async Task ServiceReadsCurrentUnsavedLensAndRejectsStalePreview()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var request = new OpticalAssemblyRequest();
        var first = await app.OpticalAssembly.CalculateAsync(request);
        var surface = app.Prescription.GetSurfaces()[1];
        app.Prescription.UpdateSurface(surface with { Radius = surface.Radius + 1 });
        var updated = await app.OpticalAssembly.CalculateAsync(request);
        Assert.NotEqual(first.SourceRevision, updated.SourceRevision);
        Assert.NotEqual(first.Rows[1].PositionMillimeters, updated.Rows[1].PositionMillimeters);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.OpticalAssembly.SimulateAsync(
            new AssemblyReticleRequest(request, first.SourceRevision, 1, AssemblyConjugateKind.Vertex)));
        app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.OpticalAssembly.CalculateAsync(request));
    }
}
