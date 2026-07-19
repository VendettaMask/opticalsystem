using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Plugins;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Scattering;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
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
    public void OpticalSurfaceTraceRayOwnsSingleSurfaceKernel()
    {
        var surface = new OpticalSurface
        {
            Number = 4,
            Label = "Kernel plane",
            Geometry = new PlaneGeometry(),
            PhysicalAperture = new CircularAperture(2),
            InteractionModel = new RefractiveReflectiveInteractionModel(),
            CoatingModel = new NoneCoatingModel(),
            CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10))
        };
        var ray = new RealRay(new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), 587.6);
        var air = new AirMaterial();
        var glass = new ConstantIndexMaterial("n=1.5", 1.5);

        var result = surface.TraceRay(ray, air, glass, 0, 0);

        Assert.False(result.StopTracing);
        Assert.Equal(4, result.Sample.SurfaceNumber);
        Assert.Equal(10, result.Sample.Position.Z, precision: 12);
        Assert.Equal(10, result.Sample.SegmentLength, precision: 12);
        Assert.Equal(10, result.Sample.CumulativeOpticalPathLength, precision: 12);
        Assert.Equal(1.5, result.RefractiveIndexAfter, precision: 12);

        var clippedRay = new RealRay(new Vector3D(3, 0, 0), new Vector3D(0, 0, 1), 587.6);
        var clipped = surface.TraceRay(clippedRay, air, glass, 0, 0);

        Assert.True(clipped.StopTracing);
        Assert.True(clipped.Sample.Vignetted);
        Assert.Equal(0, clipped.Sample.Intensity);
    }

    [Fact]
    public void MaterialsOwnPropagationModelsUsedBySurfaceKernel()
    {
        var material = new ConstantIndexMaterial("GRIN test", 1.2, propagationModel: new GrinPropagationModel(0.02));
        var clone = material.Clone();

        Assert.Equal("grin", clone.PropagationModel.Kind);

        var surface = new OpticalSurface
        {
            Number = 2,
            Label = "GRIN surface",
            Geometry = new PlaneGeometry(),
            PhysicalAperture = new CircularAperture(10),
            InteractionModel = new RefractiveReflectiveInteractionModel(),
            CoatingModel = new NoneCoatingModel(),
            CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 5))
        };
        var ray = new RealRay(new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), 587.6);

        var result = surface.TraceRay(ray, clone, new AirMaterial(), 0, 0);

        Assert.True(result.Sample.CumulativeOpticalPathLength > result.Sample.SegmentLength);
        Assert.NotEqual(0, result.Ray.Direction.X, precision: 6);
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
    public void PythonStyleTraceRecordsSurfaceMajorArrays()
    {
        var optic = Optic.CreateDemo();

        var trace = optic.Trace(0, 0.5, 0.5876, sampleCount: 5, distribution: "line_y");

        Assert.Equal(optic.SurfaceGroup.Items.Count, trace.SurfaceTraceData.SurfaceCount);
        Assert.Equal(5, trace.SurfaceTraceData.RayCount);
        Assert.Equal(trace.SurfaceTraceData, optic.SurfaceGroup.RecordedTrace);
        Assert.All(trace.SurfaceTraceData.Surfaces, surface =>
        {
            Assert.Equal(5, surface.X.Count);
            Assert.Equal(5, surface.Y.Count);
            Assert.Equal(5, surface.Z.Count);
            Assert.Equal(5, surface.L.Count);
            Assert.Equal(5, surface.M.Count);
            Assert.Equal(5, surface.N.Count);
            Assert.Equal(5, surface.Intensity.Count);
            Assert.Equal(5, surface.OpticalPathDifference.Count);
        });
        Assert.Contains(trace.SurfaceTraceData.ImageSurface.Intensity, intensity => intensity > 0);
    }

    [Fact]
    public void PythonStyleTraceGenericUsesMicrometerWavelengthAndValidatesNormalizedCoordinates()
    {
        var optic = Optic.CreateDemo();

        var trace = optic.TraceGeneric(0, 0, 0, 0, 0.5876);

        Assert.Equal(1, trace.SurfaceTraceData.RayCount);
        Assert.Contains(trace.RayHistories, history => history.Count == optic.SurfaceGroup.Items.Count);
        Assert.Equal(587.6, RayGenerator.MicrometersToNanometers(0.5876), precision: 12);
        Assert.Equal(0.5876, RayGenerator.NanometersToMicrometers(587.6), precision: 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Trace(1.1, 0, 0.5876));
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.TraceGeneric(0, 0, 1, 1, 0.5876));
    }

    [Fact]
    public void NormalizedAngleFieldsUsePythonRadialMaximum()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { XAngleDegrees = 3, YAngleDegrees = 4 });

        var ray = optic.SequentialRayTracer.RayGenerator
            .GenerateGeneric(0.6, 0.8, 0, 0, 0.55)
            .Rays.Single();

        Assert.Equal(Math.Tan(3 * Math.PI / 180), ray.Direction.X / ray.Direction.Z, precision: 12);
        Assert.Equal(Math.Tan(4 * Math.PI / 180), ray.Direction.Y / ray.Direction.Z, precision: 12);
    }

    [Fact]
    public void WavelengthExposesPythonCompatibleMicrometerUnit()
    {
        var wavelength = new Wavelength { Nanometers = 550 };

        Assert.Equal(0.55, wavelength.Micrometers, precision: 12);
        wavelength.Micrometers = 0.4861;
        Assert.Equal(486.1, wavelength.Nanometers, precision: 12);
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
        Assert.All(scene.Surfaces, surface => Assert.NotEmpty(surface.Faces));
        var curvedSurface = scene.Surfaces.Single(surface => surface.SurfaceNumber == 2);
        var curvedSurfacePoints = curvedSurface.Faces.SelectMany(face => face.Points).ToArray();
        Assert.True(curvedSurfacePoints.Max(point => point.Z) - curvedSurfacePoints.Min(point => point.Z) > 0.1);
        Assert.Contains(scene.Rays, ray => ray.Points.Any(point => Math.Abs(point.X) > 0.01));
        Assert.True(scene.ZMax > scene.ZMin);
        Assert.True(scene.XExtent > 0);
        Assert.True(scene.YExtent > 0);
    }

    [Fact]
    public void LayoutViewerOptionsFilterSurfacesFieldsWavelengthsAndPupilSamples()
    {
        var optic = Optic.CreateTessarLens();
        var builder = new Layout2DBuilder(optic);
        var options = new LayoutBuildOptions(
            FirstSurface: 1,
            LastSurface: 4,
            FieldIndex: 1,
            WavelengthIndex: 2,
            RayCount: 7,
            LowerPupil: -1,
            UpperPupil: 1);

        var scene = builder.Build(surfaceSamples: 17, options);

        Assert.Equal(new[] { 1, 2, 3, 4 }, scene.Surfaces.Select(surface => surface.SurfaceNumber));
        Assert.All(scene.LensElements, element =>
        {
            Assert.InRange(element.FrontSurfaceNumber, 1, 4);
            Assert.InRange(element.BackSurfaceNumber, 1, 4);
        });
        Assert.NotEmpty(scene.Rays);
        Assert.All(scene.Rays, ray =>
        {
            Assert.Equal(1, ray.FieldIndex);
            Assert.Equal(2, ray.WavelengthIndex);
        });
        Assert.Equal(7, scene.Rays.Select(ray => ray.PupilIndex).Distinct().Count());

        var marginalAndChief = builder.Build(
            surfaceSamples: 17,
            options with { MarginalAndChiefOnly = true });
        Assert.Equal(3, marginalAndChief.Rays.Select(ray => ray.PupilIndex).Distinct().Count());
    }

    [Fact]
    public void TessarViewerBuildsFourBoundedLensElementsAndEntrancePupilRays()
    {
        var optic = Optic.CreateTessarLens();
        var scene2D = new Layout2DBuilder(optic).Build(surfaceSamples: 33);
        var scene3D = new Layout2DBuilder(optic).Build3D(surfaceSamples: 17, rimSamples: 24);
        var expectedPairs = new[] { (1, 2), (3, 4), (6, 7), (7, 8) };

        Assert.Equal(expectedPairs, scene2D.LensElements.Select(element => (element.FrontSurfaceNumber, element.BackSurfaceNumber)));
        Assert.Equal(expectedPairs, scene3D.LensElements.Select(element => (element.FrontSurfaceNumber, element.BackSurfaceNumber)));
        Assert.All(scene2D.LensElements, element =>
        {
            Assert.All(element.Boundary, point =>
            {
                Assert.True(double.IsFinite(point.Z));
                Assert.True(double.IsFinite(point.Y));
                Assert.InRange(Math.Abs(point.Y), 0, 0.731);
            });
            Assert.InRange(element.Boundary.Max(point => point.Z) - element.Boundary.Min(point => point.Z), 0, 1.0);
        });
        Assert.InRange(scene2D.YExtent, 0, 2.5);
        Assert.InRange(scene2D.ZMax - scene2D.ZMin, 0, 7.0);
        Assert.NotEmpty(scene2D.Rays);
        Assert.All(scene2D.Rays, ray =>
        {
            Assert.False(ray.Vignetted);
            Assert.True(ray.Points.Count >= optic.SurfaceGroup.Items.Count);
            Assert.True(ray.Points[0].Z < 0);
        });
    }

    [Fact]
    public async Task ViewerGeometryRemainsValidAcrossFactoriesAndImportedFiles()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"optiland-viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var tessar = Optic.CreateTessarLens();
            var systems = new List<(string Name, Optic Optic)>
            {
                ("legacy-demo", Optic.CreateDemo()),
                ("cooke", Optic.CreateCookeTriplet()),
                ("tessar", tessar)
            };

            var jsonPath = Path.Combine(temporaryDirectory, "tessar.optiland.json");
            await OpticJsonStore.SaveAsync(tessar, jsonPath);
            systems.Add(("tessar-json", await OpticJsonStore.LoadAsync(jsonPath)));

            foreach (var extension in new[] { ".zmx", ".seq", ".len" })
            {
                var path = Path.Combine(temporaryDirectory, $"tessar{extension}");
                await File.WriteAllTextAsync(path, OpticalFormatCatalog.Export(tessar, extension));
                systems.Add(($"tessar-{extension[1..]}", OpticalFormatCatalog.Import(await File.ReadAllTextAsync(path), extension)));
            }

            foreach (var system in systems)
            {
                AssertViewerGeometry(system.Name, system.Optic);
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static void AssertViewerGeometry(string name, Optic optic)
    {
        var scene2D = new Layout2DBuilder(optic).Build(surfaceSamples: 33);
        var scene3D = new Layout2DBuilder(optic).Build3D(surfaceSamples: 17, rimSamples: 24);
        var maximumSemiDiameter = optic.SurfaceGroup.Items.Max(surface => surface.SemiDiameter);
        var expectedZScale = Math.Max(10, optic.SurfaceGroup.TotalTrack * 3.0);
        var expectedYScale = Math.Max(5, maximumSemiDiameter * 3.0);

        Assert.True(scene2D.ZMax > scene2D.ZMin, $"{name}: invalid 2D z extent");
        Assert.True(scene2D.ZMax - scene2D.ZMin < expectedZScale, $"{name}: unbounded 2D z extent");
        Assert.True(scene2D.YExtent > 0 && scene2D.YExtent < expectedYScale, $"{name}: invalid 2D y extent");
        Assert.NotEmpty(scene2D.LensElements);
        Assert.NotEmpty(scene2D.Rays);

        foreach (var surface in scene2D.Surfaces)
        {
            Assert.All(surface.Points, point => AssertFinite(name, point.Z, point.Y));
        }

        foreach (var element in scene2D.LensElements)
        {
            Assert.Equal(element.FrontSurfaceNumber + 1, element.BackSurfaceNumber);
            Assert.All(element.Boundary, point => AssertFinite(name, point.Z, point.Y));
            Assert.False(HasProperSelfIntersection(element.Boundary), $"{name}: lens {element.FrontSurfaceNumber}-{element.BackSurfaceNumber} self-intersects");
        }

        foreach (var ray in scene2D.Rays)
        {
            Assert.All(ray.Points, point => AssertFinite(name, point.Z, point.Y));
        }

        Assert.True(scene3D.ZMax > scene3D.ZMin, $"{name}: invalid 3D z extent");
        Assert.True(scene3D.XExtent > 0 && scene3D.YExtent > 0, $"{name}: invalid 3D transverse extent");
        Assert.Equal(scene2D.LensElements.Count, scene3D.LensElements.Count);
        foreach (var surface in scene3D.Surfaces)
        {
            Assert.NotEmpty(surface.Faces);
            Assert.All(
                surface.Rim
                    .Concat(surface.MeridianX)
                    .Concat(surface.MeridianY)
                    .Concat(surface.Faces.SelectMany(face => face.Points)),
                point => AssertFinite(name, point.X, point.Y, point.Z));
        }

        foreach (var ray in scene3D.Rays)
        {
            Assert.All(ray.Points, point => AssertFinite(name, point.X, point.Y, point.Z));
        }
    }

    private static bool HasProperSelfIntersection(IReadOnlyList<Layout2DPoint> polygon)
    {
        for (var first = 0; first < polygon.Count; first++)
        {
            var firstNext = (first + 1) % polygon.Count;
            for (var second = first + 1; second < polygon.Count; second++)
            {
                var secondNext = (second + 1) % polygon.Count;
                if (firstNext == second || secondNext == first)
                {
                    continue;
                }

                if (SegmentsProperlyIntersect(polygon[first], polygon[firstNext], polygon[second], polygon[secondNext]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsProperlyIntersect(Layout2DPoint a, Layout2DPoint b, Layout2DPoint c, Layout2DPoint d)
    {
        static double Orientation(Layout2DPoint p, Layout2DPoint q, Layout2DPoint r)
        {
            return ((q.Z - p.Z) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.Z - p.Z));
        }

        var first = Orientation(a, b, c);
        var second = Orientation(a, b, d);
        var third = Orientation(c, d, a);
        var fourth = Orientation(c, d, b);
        const double tolerance = 1e-12;
        return first * second < -tolerance && third * fourth < -tolerance;
    }

    private static void AssertFinite(string name, params double[] values)
    {
        Assert.True(values.All(double.IsFinite), $"{name}: viewer contains a non-finite coordinate");
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
        Assert.Contains("MMDFT PSF", optic.Analyses.Names);
        Assert.Contains("Huygens PSF", optic.Analyses.Names);
        Assert.Contains("MTF", optic.Analyses.Names);
        Assert.Contains("Huygens MTF", optic.Analyses.Names);
        Assert.Contains("Wavefront", optic.Analyses.Names);
        Assert.Contains("Centroid Sphere Wavefront", optic.Analyses.Names);
        Assert.Contains("Best Fit Sphere Wavefront", optic.Analyses.Names);
        Assert.Equal(32, optic.Analyses.Names.Count);
        Assert.Equal("Spot Diagram", optic.Analyses.Create("Spot Diagram").GenerateData().Name);
    }

    [Fact]
    public void BlankOpticStartsWithEditableReferenceSurfaces()
    {
        var optic = Optic.CreateBlank();

        Assert.Single(optic.Fields);
        Assert.Single(optic.Wavelengths);
        Assert.True(optic.Wavelengths[0].IsPrimary);
        Assert.Equal(2, optic.SurfaceGroup.Items.Count);
        Assert.Equal("Object", optic.SurfaceGroup.Items[0].Label);
        Assert.Equal("Image", optic.SurfaceGroup.Items[1].Label);
        Assert.True(optic.SurfaceGroup.TotalTrack > 0);
    }

    [Theory]
    [InlineData("Spot Diagram", AnalysisSeriesKind.Scatter)]
    [InlineData("Ray Fan", AnalysisSeriesKind.Line)]
    [InlineData("Encircled Energy", AnalysisSeriesKind.Line)]
    [InlineData("RMS vs Field", AnalysisSeriesKind.Line)]
    [InlineData("Through Focus", AnalysisSeriesKind.Line)]
    [InlineData("Y-Ybar", AnalysisSeriesKind.Line)]
    [InlineData("Zernike", AnalysisSeriesKind.Bar)]
    [InlineData("MMDFT PSF", AnalysisSeriesKind.Heatmap)]
    [InlineData("Huygens PSF", AnalysisSeriesKind.Heatmap)]
    [InlineData("MTF", AnalysisSeriesKind.Line)]
    [InlineData("Huygens MTF", AnalysisSeriesKind.Line)]
    public void GraphicalAnalysesExposeStructuredFiniteSeries(string analysisName, AnalysisSeriesKind kind)
    {
        var optic = Optic.CreateDemo();
        optic.SequentialRayTracer.RayGenerator.Settings.SamplesPerField = 3;

        var data = optic.Analyses.Create(analysisName).GenerateData();

        Assert.NotNull(data.Series);
        Assert.Equal(kind, data.Series.Kind);
        Assert.NotEmpty(data.Series.Points);
        Assert.All(data.Series.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
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
    public void OptimizerHonorsAmbientCancellation()
    {
        var value = 0.0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable("x", () => value, next => value = next, -10, 10));
        problem.AddOperand(new Operand("target", 3, 1, () => value));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);

        Assert.Throws<OperationCanceledException>(() =>
            OptimizerCatalog.Create("Least Squares").Optimize(problem, maxIterations: 100));
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
