using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Scattering;
using OptilandWorkbench.Core.Serialization;
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

    [Fact]
    public void AnalysisCatalogGeneratesDataForEveryRegisteredName()
    {
        var optic = Optic.CreateDemo();

        foreach (var name in optic.Analyses.Names)
        {
            var data = optic.Analyses.Create(name).GenerateData();

            Assert.Equal(name, data.Name);
            Assert.NotEmpty(data.Values);
            Assert.False(string.IsNullOrWhiteSpace(data.ExportText()));
        }
    }

    [Fact]
    public async Task JsonStoreRoundTripsRichSurfaceComponents()
    {
        var optic = Optic.CreateDemo();
        var surface = optic.SurfaceGroup.Items[2];
        surface.Geometry = new EvenAsphereGeometry(44, -0.7, new[] { 1e-5, -2e-8 });
        surface.MaterialBefore = new ConstantIndexMaterial("custom-before", 1.23);
        surface.MaterialAfter = new CauchyMaterial("custom-after", 1.49, 0.004, 1e-5);
        surface.CoatingModel = new ThinFilmStackCoating(new[]
        {
            new ThinFilmLayer("TiO2", 120),
            new ThinFilmLayer("SiO2", 95)
        });
        surface.InteractionModel = new ThinLensInteractionModel(75);
        surface.PhysicalAperture = new RectangularAperture(3, 4);
        surface.ScatteringModel = new LambertianScatteringModel(0.17);

        var path = Path.Combine(Path.GetTempPath(), $"optiland-roundtrip-{Guid.NewGuid():N}.optiland.json");
        try
        {
            await OpticJsonStore.SaveAsync(optic, path);
            var restored = await OpticJsonStore.LoadAsync(path);
            var roundTrippedSurface = restored.SurfaceGroup.Items[2];

            var geometry = Assert.IsType<EvenAsphereGeometry>(roundTrippedSurface.Geometry);
            Assert.Equal(44, geometry.Base.Radius, precision: 12);
            Assert.Equal(-0.7, geometry.Base.Conic, precision: 12);
            Assert.Equal(new[] { 1e-5, -2e-8 }, geometry.Coefficients);

            var materialBefore = Assert.IsType<ConstantIndexMaterial>(roundTrippedSurface.MaterialBefore);
            Assert.Equal("custom-before", materialBefore.Name);
            Assert.Equal(1.23, materialBefore.Index, precision: 12);

            var materialAfter = Assert.IsType<CauchyMaterial>(roundTrippedSurface.MaterialAfter);
            Assert.Equal("custom-after", materialAfter.Name);
            Assert.Equal(1.49, materialAfter.A, precision: 12);
            Assert.Equal(0.004, materialAfter.B, precision: 12);

            var coating = Assert.IsType<ThinFilmStackCoating>(roundTrippedSurface.CoatingModel);
            Assert.Equal(2, coating.Layers.Count);
            Assert.Equal("TiO2", coating.Layers[0].MaterialName);
            Assert.Equal(120, coating.Layers[0].ThicknessNanometers, precision: 12);

            var interaction = Assert.IsType<ThinLensInteractionModel>(roundTrippedSurface.InteractionModel);
            Assert.Equal(75, interaction.FocalLength, precision: 12);

            var aperture = Assert.IsType<RectangularAperture>(roundTrippedSurface.PhysicalAperture);
            Assert.Equal(3, aperture.HalfWidth, precision: 12);
            Assert.Equal(4, aperture.HalfHeight, precision: 12);

            var scattering = Assert.IsType<LambertianScatteringModel>(roundTrippedSurface.ScatteringModel);
            Assert.Equal(0.17, scattering.ScatterFraction, precision: 12);
            Assert.Equal(22, roundTrippedSurface.CoordinateSystem.Origin.Z, precision: 12);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
