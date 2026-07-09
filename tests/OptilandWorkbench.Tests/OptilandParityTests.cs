using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.Tests;

public sealed class OptilandParityTests
{
    [Fact]
    public void OpticExposesDocumentedCoreEntryPoints()
    {
        var optic = Optic.CreateDemo();

        Assert.NotNull(optic.Backend);
        Assert.NotNull(optic.Aperture);
        Assert.NotNull(optic.Materials);
        Assert.NotNull(optic.SequentialRayTracer);
        Assert.NotNull(optic.Analyses);
        Assert.NotNull(optic.CreateOptimizationProblem());
        Assert.NotNull(optic.CreateTolerancing());
    }

    [Fact]
    public void ManagedBackendProvidesVectorOperations()
    {
        var backend = new ManagedCpuBackend();
        var x = new Vector3D(1, 0, 0);
        var y = new Vector3D(0, 1, 0);

        Assert.Equal(0, backend.Dot(x, y));
        Assert.Equal(new Vector3D(0, 0, 1), backend.Cross(x, y));
        Assert.Equal(1, backend.Normalize(new Vector3D(3, 0, 0)).X, precision: 12);
    }

    [Fact]
    public void GeometryModelsReturnFiniteSagAndIntersections()
    {
        IGeometry[] geometries =
        {
            new PlaneGeometry(),
            new StandardGeometry(50, 0),
            new EvenAsphereGeometry(50, 0, new[] { 1e-6 }),
            new OddAsphereGeometry(50, 0, new[] { 1e-6 }),
            new BiconicGeometry(40, 60),
            new ToroidalGeometry(80, 30),
            new PolynomialGeometry(new Dictionary<(int X, int Y), double> { [(2, 0)] = 1e-3 })
        };

        foreach (var geometry in geometries)
        {
            Assert.True(double.IsFinite(geometry.Sag(1, 1)));
            Assert.NotNull(geometry.DistanceToIntersection(new Vector3D(0, 0, -5), new Vector3D(0, 0, 1)));
        }
    }

    [Fact]
    public void MaterialRegistryResolvesCommonGlassTypes()
    {
        var registry = new MaterialRegistry();

        Assert.Equal(1.0, registry.Resolve("Air").RefractiveIndex(587.6), precision: 12);
        Assert.InRange(registry.Resolve("N-BK7").RefractiveIndex(587.6), 1.50, 1.53);
        Assert.InRange(registry.Resolve("Fused Silica").RefractiveIndex(587.6), 1.44, 1.47);
    }

    [Fact]
    public void SequentialTracerRecordsSurfaceHistory()
    {
        var optic = Optic.CreateDemo();
        optic.SequentialRayTracer.RayGenerator.Settings.SamplesPerField = 3;
        optic.SequentialRayTracer.RayGenerator.Settings.Sampling = PupilSampling.Hexapolar;

        var trace = optic.SequentialRayTracer.Trace();

        Assert.NotEmpty(trace.RayHistories);
        Assert.Contains(trace.RayHistories, history => history.Count > 0);
    }

    [Fact]
    public void AnalysisCatalogCreatesDocumentedAnalysisNames()
    {
        var optic = Optic.CreateDemo();

        Assert.Contains("PSF", optic.Analyses.Names);
        Assert.Contains("MTF", optic.Analyses.Names);
        Assert.Contains("Wavefront", optic.Analyses.Names);
        Assert.Equal("Spot Diagram", optic.Analyses.Create("Spot Diagram").GenerateData().Name);
    }

    [Fact]
    public void OptimizationProblemComputesResiduals()
    {
        var value = 5.0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable("x", () => value, next => value = next, -10, 10));
        problem.AddOperand(new Operand("target", 3.0, 2.0, () => value));

        Assert.Equal(4.0, problem.ResidualVector()[0]);
        Assert.Equal(16.0, problem.SumSquared());
    }

    [Fact]
    public void TolerancingPerturbationRevertsAfterSensitivity()
    {
        var optic = Optic.CreateDemo();
        var original = optic.SurfaceGroup.Items[2].Radius;
        var tolerancing = optic.CreateTolerancing();
        tolerancing.AddOperand(new Operand("radius", original, 1, () => optic.SurfaceGroup.Items[2].Radius));
        tolerancing.AddPerturbation(new DelegatePerturbation(
            "radius + 1",
            item => item.SurfaceGroup.Items[2].Radius += 1,
            item => item.SurfaceGroup.Items[2].Radius = original));

        var results = new SensitivityAnalysis(optic, tolerancing).Run();

        Assert.Single(results);
        Assert.Equal(original, optic.SurfaceGroup.Items[2].Radius);
    }

    [Fact]
    public void MultiConfigurationBreaksLinksForZoomParameters()
    {
        var optic = Optic.CreateDemo();
        var multiConfig = new MultiConfiguration(optic);
        var zoomIndex = multiConfig.AddConfiguration();

        multiConfig.SetThickness(zoomIndex, 2, 20);
        multiConfig.SetThickness(0, 2, 5);
        multiConfig.PropagateBaseLinks();

        Assert.Equal(20, multiConfig.Configurations[zoomIndex].SurfaceGroup.Items[2].Thickness);
        Assert.Equal(5, multiConfig.Configurations[0].SurfaceGroup.Items[2].Thickness);
    }

    [Fact]
    public void JsonSnapshotCarriesSchemaAndComponentMetadata()
    {
        var optic = Optic.CreateDemo();
        var snapshot = optic.ToSnapshot();

        Assert.Equal(2, snapshot.SchemaVersion);
        Assert.NotNull(snapshot.Aperture);
        Assert.NotNull(snapshot.BackendName);
        Assert.All(snapshot.Surfaces, surface => Assert.NotNull(surface.Components));
    }
}
