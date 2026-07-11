using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Plugins;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Scattering;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Tolerancing;
using OptilandWorkbench.Core.Visualization;

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
            new PolynomialGeometry(new Dictionary<(int X, int Y), double> { [(2, 0)] = 1e-3 }),
            new ChebyshevGeometry(new Dictionary<(int XOrder, int YOrder), double> { [(2, 0)] = 1e-3, [(0, 2)] = -2e-4 }, 5, 5),
            new ZernikeGeometry(new Dictionary<(int RadialOrder, int AzimuthalFrequency), double> { [(2, 0)] = 1e-3, [(3, 1)] = 2e-4 }, 5),
            new ForbesQGeometry(50, -0.5, 5, new[] { 1e-4, -2e-5 })
        };

        foreach (var geometry in geometries)
        {
            Assert.True(double.IsFinite(geometry.Sag(1, 1)));
            Assert.NotNull(geometry.DistanceToIntersection(new Vector3D(0, 0, -5), new Vector3D(0, 0, 1)));
        }
    }

    [Fact]
    public async Task JsonStoreRoundTripsHighOrderFreeformGeometries()
    {
        IGeometry[] geometries =
        {
            new ChebyshevGeometry(new Dictionary<(int XOrder, int YOrder), double> { [(2, 0)] = 1e-3, [(0, 2)] = -2e-4 }, 5, 7),
            new ZernikeGeometry(new Dictionary<(int RadialOrder, int AzimuthalFrequency), double> { [(2, 0)] = 1e-3, [(3, -1)] = 2e-4 }, 6),
            new ForbesQGeometry(42, -0.6, 8, new[] { 1e-4, -2e-5, 3e-6 })
        };

        foreach (var geometry in geometries)
        {
            var optic = Optic.CreateDemo();
            optic.SurfaceGroup.Items[2].Geometry = geometry;
            var expectedSag = geometry.Sag(1.2, -0.7);
            var path = Path.Combine(Path.GetTempPath(), $"optiland-freeform-{Guid.NewGuid():N}.optiland.json");

            try
            {
                await OpticJsonStore.SaveAsync(optic, path);
                var restored = await OpticJsonStore.LoadAsync(path);
                var restoredGeometry = restored.SurfaceGroup.Items[2].Geometry;

                Assert.Equal(geometry.Kind, restoredGeometry.Kind);
                Assert.Equal(expectedSag, restoredGeometry.Sag(1.2, -0.7), precision: 12);
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
        Assert.Contains(trace.RayHistories, history =>
            history.Count > 1
            && history.Zip(history.Skip(1), (left, right) => right.CumulativeOpticalPathLength >= left.CumulativeOpticalPathLength).All(item => item)
            && history[^1].CumulativeOpticalPathLength > 0
            && double.IsFinite(history[^1].OpticalPathDifference));
    }

    [Fact]
    public void WavefrontAnalysisUsesSequentialOpticalPathDifference()
    {
        var optic = Optic.CreateDemo();
        optic.SequentialRayTracer.RayGenerator.Settings.SamplesPerField = 3;

        var data = optic.Analyses.Create("Wavefront").GenerateData();

        Assert.Contains("RmsOpticalPathDifference", data.Values.Keys);
        Assert.Contains("PeakToValleyOpticalPathDifference", data.Values.Keys);
        Assert.True((double)data.Values["ReferenceOpticalPathLength"] > 0);
        Assert.True(double.IsFinite((double)data.Values["RmsOpticalPathDifference"]));
    }

    [Fact]
    public void EncircledEnergyUsesWeightedSequentialImageSamples()
    {
        var optic = Optic.CreateDemo();
        optic.SequentialRayTracer.RayGenerator.Settings.SamplesPerField = 5;

        var data = optic.Analyses.Create("Encircled Energy").GenerateData();

        Assert.True((int)data.Values["RayCount"] > 0);
        Assert.True((double)data.Values["TotalWeight"] > 0);
        Assert.True((double)data.Values["Radius80"] >= (double)data.Values["Radius50"]);
        Assert.True((double)data.Values["Radius95"] >= (double)data.Values["Radius80"]);
    }

    [Fact]
    public void RmsVsFieldRetainsZeroWeightFieldsButExcludesThemFromAggregate()
    {
        var optic = Optic.CreateDemo();
        optic.SequentialRayTracer.RayGenerator.Settings.SamplesPerField = 3;
        optic.Fields[0].Weight = 0;
        var zeroWeightFieldKey = $"Field {optic.Fields[0].Label}";

        var data = optic.Analyses.Create("RMS vs Field").GenerateData();

        Assert.Contains(zeroWeightFieldKey, data.Values.Keys);
        Assert.True(double.IsFinite((double)data.Values[zeroWeightFieldKey]));
        Assert.Equal(optic.Fields.Skip(1).Sum(field => field.Weight), (double)data.Values["IncludedFieldWeight"], precision: 12);
        Assert.True(double.IsFinite((double)data.Values["WeightedMean"]));
    }

    [Fact]
    public void ThroughFocusReportsBestSequentialFocusSample()
    {
        var optic = Optic.CreateDemo();
        optic.SequentialRayTracer.RayGenerator.Settings.SamplesPerField = 3;

        var data = optic.Analyses.Create("Through Focus").GenerateData();

        Assert.True((double)data.Values["FocusStep"] > 0);
        Assert.True(double.IsFinite((double)data.Values["NominalRms"]));
        Assert.True(double.IsFinite((double)data.Values["BestRmsSpotRadius"]));
        Assert.True((double)data.Values["BestRmsSpotRadius"] <= new[]
        {
            (double)data.Values["Minus2StepRms"],
            (double)data.Values["Minus1StepRms"],
            (double)data.Values["NominalRms"],
            (double)data.Values["Plus1StepRms"],
            (double)data.Values["Plus2StepRms"]
        }.Max() + 1e-12);
    }

    [Fact]
    public void Layout2DBuilderSamplesSaggedSurfacesAndSequentialRays()
    {
        var optic = Optic.CreateDemo();
        optic.SequentialRayTracer.RayGenerator.Settings.SamplesPerField = 3;
        optic.SequentialRayTracer.RayGenerator.Settings.Sampling = PupilSampling.Hexapolar;

        var scene = new Layout2DBuilder(optic).Build(surfaceSamples: 9);
        var curvedSurface = scene.Surfaces.First(surface => surface.SurfaceNumber == 2);
        var zRange = curvedSurface.Points.Max(point => point.Z) - curvedSurface.Points.Min(point => point.Z);

        Assert.True(zRange > 0.01);
        Assert.NotEmpty(scene.LensElements);
        Assert.All(scene.LensElements, element => Assert.True(element.Boundary.Count > 6));
        Assert.NotEmpty(scene.LensEdges);
        Assert.Contains(scene.Rays, ray => ray.Points.Count > 2);
        Assert.Contains(scene.Rays, ray => ray.FieldIndex > 0);
        Assert.True(scene.ZMax > scene.ZMin);
        Assert.True(scene.YExtent > 0);
    }

    [Fact]
    public void Layout2DBuilderExtendsUnequalLensAperturesBeforeClosingBody()
    {
        var optic = Optic.CreateDemo();
        optic.SurfaceGroup.Items[3].SemiDiameter = 8;
        optic.SurfaceGroup.Renumber();

        var scene = new Layout2DBuilder(optic).Build(surfaceSamples: 9);
        var element = scene.LensElements.First(lens => lens.FrontSurfaceNumber == 2 && lens.BackSurfaceNumber == 3);
        var pairs = element.Boundary.Zip(element.Boundary.Skip(1), (A, B) => (A, B)).ToList();
        pairs.Add((element.Boundary[^1], element.Boundary[0]));

        Assert.Equal(13, element.Boundary.Max(point => Math.Abs(point.Y)), precision: 12);
        Assert.Contains(pairs, pair => Close(pair.A.Y, 13) && Close(pair.B.Y, 13) && Math.Abs(pair.A.Z - pair.B.Z) > 1e-6);
        Assert.Contains(pairs, pair => Close(pair.A.Y, -13) && Close(pair.B.Y, -13) && Math.Abs(pair.A.Z - pair.B.Z) > 1e-6);
        Assert.Contains(pairs, pair => Close(pair.A.Y, 13) && Close(pair.B.Y, 8) && Close(pair.A.Z, pair.B.Z));
        Assert.Contains(pairs, pair => Close(pair.A.Y, -8) && Close(pair.B.Y, -13) && Close(pair.A.Z, pair.B.Z));
    }

    [Fact]
    public void Layout2DBuilderCreates3DViewerPrimitives()
    {
        var optic = Optic.CreateDemo();
        var scene = new Layout2DBuilder(optic).Build3D(surfaceSamples: 9, rimSamples: 16);

        Assert.NotEmpty(scene.Surfaces);
        Assert.NotEmpty(scene.LensElements);
        Assert.Contains(scene.Surfaces, surface => surface.Rim.Count >= 16);
        Assert.Contains(scene.Rays, ray => ray.Points.Any(point => Math.Abs(point.X) > 0.01));
        Assert.True(scene.ZMax > scene.ZMin);
        Assert.True(scene.XExtent > 0);
        Assert.True(scene.YExtent > 0);
    }

    private static bool Close(double left, double right)
    {
        return Math.Abs(left - right) < 1e-9;
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
    public void LeastSquaresOptimizerReducesMeritAndHonorsBounds()
    {
        var value = 0.0;
        var variable = new DelegateVariable(
            "x",
            () => value,
            next => value = next,
            -2,
            5,
            stepHint: 1,
            scaler: new UnitRangeScaler(-2, 5));
        var problem = new OptimizationProblem();
        problem.AddVariable(variable);
        problem.AddOperand(new Operand("target", 9, 1, () => value));

        Assert.Equal(2.0 / 7.0, problem.ScaledVariableVector()[0], precision: 12);
        problem.SetScaledVariableVector(new[] { 0.0 });
        Assert.Equal(-2, value, precision: 12);
        value = 0;

        var result = OptimizerCatalog.Create("Least Squares").Optimize(problem, maxIterations: 40);

        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.InRange(value, -2, 5);
        Assert.True(value > 4.8);
        Assert.NotEmpty(result.BestVariables);
        Assert.NotEmpty(result.MeritHistory);
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
    public void MonteCarloUsesSeedAndRestoresPerturbedVariables()
    {
        var value = 10.0;
        var optic = new Optic();
        var variable = new DelegateVariable("x", () => value, next => value = next, 0, 20, stepHint: 1);
        var tolerancing = optic.CreateTolerancing();
        tolerancing.AddOperand(new Operand("target", 10, 1, () => value));
        tolerancing.AddPerturbation(new VariablePerturbation("normal x", variable, new NormalSampler(0, 1)));

        var first = new MonteCarlo(optic, tolerancing).RunDetailed(5, seed: 99).Select(result => result.Merit).ToArray();
        var second = new MonteCarlo(optic, tolerancing).RunDetailed(5, seed: 99).Select(result => result.Merit).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(10, value, precision: 12);
    }

    [Fact]
    public void MonteCarloCompensatorsReducePerturbedMerit()
    {
        var value = 0.0;
        var optic = new Optic();
        var variable = new DelegateVariable("x", () => value, next => value = next, -10, 10, stepHint: 1);
        var tolerancing = optic.CreateTolerancing();
        tolerancing.AddOperand(new Operand("target", 0, 1, () => value));
        tolerancing.AddPerturbation(new VariablePerturbation("constant x", variable, new ConstantSampler(5)));
        tolerancing.AddCompensator(variable);

        var result = new MonteCarlo(optic, tolerancing)
            .RunDetailed(1, seed: 7, compensationIterations: 12)
            .Single();

        Assert.Equal(25, result.Merit, precision: 12);
        Assert.True(result.CompensatedMerit < result.Merit);
        Assert.Equal(0, value, precision: 12);
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

    [Fact]
    public void CommercialFormatCatalogRoundTripsCommonSequentialSubset()
    {
        var optic = Optic.CreateDemo();

        foreach (var extension in new[] { ".zmx", ".seq", ".len" })
        {
            var text = OpticalFormatCatalog.Export(optic, extension);
            var restored = OpticalFormatCatalog.Import(text, extension);

            Assert.Equal(optic.SurfaceGroup.Items.Count, restored.SurfaceGroup.Items.Count);
            Assert.Equal("Imported", restored.Name[..8]);
            Assert.Equal(optic.SurfaceGroup.Items[1].IsStop, restored.SurfaceGroup.Items[1].IsStop);
            Assert.Equal(optic.SurfaceGroup.Items[2].Radius, restored.SurfaceGroup.Items[2].Radius, precision: 5);
            Assert.Equal(optic.SurfaceGroup.Items[2].Thickness, restored.SurfaceGroup.Items[2].Thickness, precision: 12);
            Assert.Equal(optic.SurfaceGroup.Items[2].Material, restored.SurfaceGroup.Items[2].Material);
            Assert.Equal(optic.SurfaceGroup.Items[2].SemiDiameter, restored.SurfaceGroup.Items[2].SemiDiameter, precision: 12);
        }
    }

    [Fact]
    public void PluginLoaderRegistersGoodPluginsAndWarnsForFailingPlugins()
    {
        var registry = new PluginLoader().LoadFromAssembly(typeof(OptilandParityTests).Assembly);

        Assert.Contains("test-plane", registry.Geometries.Keys);
        Assert.Contains("test-analysis", registry.Analyses.Keys);
        Assert.Equal(1.42, registry.Materials.Resolve("TEST-N").RefractiveIndex(587.6), precision: 12);
        Assert.Contains(registry.Warnings, warning => warning.Contains(nameof(FailingOptilandPlugin), StringComparison.Ordinal));
    }
}

public sealed class TestOptilandPlugin : IOptilandPlugin
{
    public string Name => "test-plugin";

    public void Register(PluginRegistry registry)
    {
        registry.RegisterGeometry("test-plane", () => new PlaneGeometry());
        registry.RegisterMaterial(new ConstantIndexMaterial("TEST-N", 1.42));
        registry.RegisterAnalysis("test-analysis", optic => new PlaceholderAnalysis(optic, "Plugin Analysis"));
    }
}

public sealed class FailingOptilandPlugin : IOptilandPlugin
{
    public string Name => "failing-plugin";

    public void Register(PluginRegistry registry)
    {
        throw new InvalidOperationException("plugin failure for test");
    }
}
