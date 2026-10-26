using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using Xunit.Abstractions;

namespace OpticalSystemDesign.Tests;

public sealed class PhysicalStopDerivativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(80)]
    public void PhysicalStopAndTwoDimensionalFieldsMatchFormalCoreAndEveryParameterDifference(double stopZ)
    {
        var optic = Example(stopZ);
        var front = stopZ == 0 ? 3 : 2;
        OpticalDesignParameter[] parameters = [new(front, OpticalDesignParameterKind.Curvature, .02),
            new(front, OpticalDesignParameterKind.Conic), new(front, OpticalDesignParameterKind.EvenAsphereCoefficient, 1e-6, 1),
            new(front, OpticalDesignParameterKind.AxialPosition), new(front + 1, OpticalDesignParameterKind.AxialPosition),
            new(optic.SurfaceGroup.Items.Count - 1, OpticalDesignParameterKind.AxialPosition)];
        (double X, double Y)[] fields = [(0, 0), (.3, .4)];
        var pupils = ApertureSampler.GenerateGaussianQuadrature(2, 8).ToArray();
        var actual = DifferentiableOpticalMetrics.EvaluateAtFields(optic, parameters, fields, pupils, usePhysicalStop: true);
        Assert.Equal(optic.Paraxial.EstimateEntrancePupilDiameter(), actual.EntrancePupilDiameterMillimeters.Value, 11);
        Assert.Equal(1 / optic.Paraxial.EstimateFNumber(), actual.InverseFNumber.Value, 11);
        for (var field = 0; field < fields.Length; field++)
        {
            var distortion = DistortionMetrics.ParaxialChiefFractionAtField(optic, fields[field].X, fields[field].Y);
            Assert.Equal(distortion * distortion, actual.Fields[field].DistortionSquaredFraction.Value, 10);
            var physical = SpotMetricEvaluator.EvaluatePupilSamples(optic, fields[field].X, fields[field].Y, pupils, reference: "absolute", includeSurfaceTransmission: false);
            var expected = physical.Wavelengths.SelectMany(wave => wave.Rays).ToArray();
            Assert.Equal(pupils.Length, expected.Length);
            for (var ray = 0; ray < expected.Length; ray++)
            {
                Assert.Equal(expected[ray].X, actual.Fields[field].Rays[ray].X.Value, 9);
                Assert.Equal(expected[ray].Y, actual.Fields[field].Rays[ray].Y.Value, 9);
            }
        }
        var values = OpticalDesignParameters.Read(optic, parameters);
        for (var parameter = 0; parameter < parameters.Length; parameter++)
        {
            const double h = 2e-5;
            var plus = values.ToArray(); var minus = values.ToArray(); plus[parameter] += h; minus[parameter] -= h;
            var a = Evaluate(plus); var b = Evaluate(minus);
            Close(actual.EntrancePupilDiameterMillimeters.Derivative(parameter), (a.EntrancePupilDiameterMillimeters.Value - b.EntrancePupilDiameterMillimeters.Value) / (2 * h));
            Close(actual.InverseFNumber.Derivative(parameter), (a.InverseFNumber.Value - b.InverseFNumber.Value) / (2 * h));
            foreach (var field in Enumerable.Range(0, fields.Length))
            {
                Close(actual.Fields[field].DistortionSquaredFraction.Derivative(parameter),
                    (a.Fields[field].DistortionSquaredFraction.Value - b.Fields[field].DistortionSquaredFraction.Value) / (2 * h));
                foreach (var ray in Enumerable.Range(0, pupils.Length))
                {
                    Close(actual.Fields[field].Rays[ray].X.Derivative(parameter), (a.Fields[field].Rays[ray].X.Value - b.Fields[field].Rays[ray].X.Value) / (2 * h));
                    Close(actual.Fields[field].Rays[ray].Y.Derivative(parameter), (a.Fields[field].Rays[ray].Y.Value - b.Fields[field].Rays[ray].Y.Value) / (2 * h));
                }
            }
        }
        DifferentiableOpticalResult Evaluate(double[] coordinates) => DifferentiableOpticalMetrics.EvaluateAtFields(
            OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, coordinates), parameters, fields, pupils, usePhysicalStop: true);
    }

    [Fact]
    public void PhysicalStopCudaReportsRealExecutionAndMatchesCpu()
    {
        if (!DesignGpu.Status.IsAvailable) { output.WriteLine("CUDA unavailable: " + DesignGpu.Status.UnavailableReason); return; }
        var optic = Example(12);
        OpticalDesignParameter[] parameters = [new(2, OpticalDesignParameterKind.Curvature, .02), new(2, OpticalDesignParameterKind.Conic), new(2, OpticalDesignParameterKind.EvenAsphereCoefficient, 1e-6, 1)];
        var pupils = ApertureSampler.GenerateGaussianQuadrature(2, 8).ToArray();
        var cpu = DifferentiableOpticalMetrics.EvaluateAtFields(optic, parameters, [(.2, .4)], pupils, usePhysicalStop: true);
        var gpu = DifferentiableOpticalMetrics.EvaluateAtFields(optic, parameters, [(.2, .4)], pupils, computeMode: DesignComputeMode.Gpu, usePhysicalStop: true);
        Assert.True(gpu.Execution.GpuBatches > 0); Assert.True(gpu.Execution.GpuRays > 0);
        Assert.True(gpu.PropagationExecution!.PropagatedRays > 0);
        for (var ray = 0; ray < cpu.Fields[0].Rays.Count; ray++)
            for (var parameter = 0; parameter < parameters.Length; parameter++)
            {
                Close(cpu.Fields[0].Rays[ray].X.Derivative(parameter), gpu.Fields[0].Rays[ray].X.Derivative(parameter));
                Close(cpu.Fields[0].Rays[ray].Y.Derivative(parameter), gpu.Fields[0].Rays[ray].Y.Derivative(parameter));
            }
        output.WriteLine($"{gpu.Execution.Backend}: {gpu.Execution.GpuRays} differentiated rays, {gpu.PropagationExecution.PropagatedRays} propagated rays");
    }

    [Fact]
    public void GenericSeparationAndSharedAssessmentCentroidKeepTheirPhysicalMeaning()
    {
        var optic = Example(12);
        Assert.Equal(3, AxialSurfaceSeparation.EvaluateBetween(optic, 2, 3, 0, 0));
        Assert.Equal(110 - 5, AxialSurfaceSeparation.EvaluateBetween(optic, 3, 5, 0, 0));
        var pupils = ApertureSampler.GenerateGaussianQuadrature(2, 8).ToArray();
        var absolute = SpotMetricEvaluator.EvaluatePupilSamples(optic, .3, .4, pupils, reference: "absolute", includeSurfaceTransmission: false);
        var assessment = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, .3, .4, pupils, [new(0, 0, 1)], includeSurfaceTransmission: false);
        var rays = absolute.Wavelengths.SelectMany(wave => wave.Rays).ToArray();
        Assert.Equal(rays.Sum(ray => ray.X * ray.Weight) / rays.Sum(ray => ray.Weight), assessment.CentroidX!.Value, 12);
        Assert.Equal(rays.Sum(ray => ray.Y * ray.Weight) / rays.Sum(ray => ray.Weight), assessment.CentroidY!.Value, 12);
        const double slope = 1e-5;
        var fieldY = Math.Atan(slope) * 180 / Math.PI / 2;
        var chief = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, fieldY, [new(0, 0, 1)], reference: "absolute", includeSurfaceTransmission: false);
        var launch = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, fieldY, .5876, [new(0, 0, 1)], aimAtStop: true).Rays[0];
        var stop = optic.SequentialRayTracer.TraceToSurface(launch, 4);
        output.WriteLine($"scale={optic.Paraxial.EstimateChiefRayImageScale():R}, ray={chief.Wavelengths[0].Rays[0].Y / slope:R}, inputSlope={launch.Direction.Y / launch.Direction.Z:R}, stopY={stop?.Position.Y:R}");
        var paraxial = optic.Paraxial.TraceNormalizedPupil(fieldY, [0.0], .5876);
        for (var surface = 1; surface < optic.SurfaceGroup.Items.Count; surface++)
        {
            var hit = optic.SequentialRayTracer.TraceToSurface(launch, surface);
            output.WriteLine($"surface {surface}: z={hit?.Position.Z:R}, y={hit?.Position.Y:R}, slope={hit?.Direction.Y / hit?.Direction.Z:R}, para-y={paraxial.Heights[surface][0]:R}, n={optic.SurfaceGroup.Items[surface].MaterialAfter.RefractiveIndex(587.6):R}, R={optic.SurfaceGroup.Items[surface].Radius:R}");
        }
        Assert.InRange(Math.Abs(chief.Wavelengths[0].Rays[0].Y / slope - optic.Paraxial.EstimateChiefRayImageScale()), 0, 1e-6);
    }

    private static Optic Example(double stopZ)
    {
        var optic = new Optic("Physical stop derivative");
        List<OpticalSurface> surfaces = [new() { Thickness = double.PositiveInfinity, Material = "Air" },
            new() { Thickness = stopZ == 0 ? 0 : 2, Material = "Air" }];
        if (stopZ == 0) surfaces.Add(new() { IsStop = true, Thickness = 2, SemiDiameter = 1, Material = "Air" });
        surfaces.Add(new() { Radius = 35, Thickness = 3, SemiDiameter = 8, Material = "N-BK7" });
        surfaces.Add(new() { Radius = -45, Thickness = stopZ == 0 ? 105 : stopZ - 5, SemiDiameter = 8, Material = "Air" });
        if (stopZ != 0) surfaces.Add(new() { IsStop = true, Thickness = 110 - stopZ, SemiDiameter = 1, Material = "Air" });
        surfaces.Add(new() { Material = "Air" });
        optic.SurfaceGroup.ImportLegacySurfaces(surfaces, optic.Materials);
        var front = stopZ == 0 ? 3 : 2;
        optic.SurfaceGroup.Items[front].Geometry = new EvenAsphereGeometry(35, -.2, [0, 1e-7]);
        foreach (var surface in optic.SurfaceGroup.Items.Skip(2).SkipLast(1)) { surface.PhysicalAperture = new CircularAperture(surface.SemiDiameter); surface.SemiDiameterFixed = true; }
        OpticalDesignParameters.SynchronizeIncidentMedia(optic);
        optic.Aperture.Kind = ApertureKind.FloatByStopSize; optic.Aperture.Value = 2;
        optic.Fields.Clear(); optic.Fields.Add(new() { YAngleDegrees = 2 });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new() { Nanometers = 587.6, IsPrimary = true });
        optic.RayAimingEnabled = true;
        return optic;
    }

    private static void Close(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 2e-6 * Math.Max(1, Math.Abs(expected)));
}
