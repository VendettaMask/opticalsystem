using System.Collections.Concurrent;
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
    public void SampledMtfZeroFrequencyIsNormalizedAndIndependentOfZernikeResiduals()
    {
        var optic = Optic.CreateCookeTriplet();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);

        foreach (var zernikeTerms in new[] { 5, 12, 37 })
        {
            var evaluator = SampledMtfEngine.Create(
                optic,
                (0, 1),
                wavelength,
                pupilSampling: 16,
                zernikeTerms: zernikeTerms);

            Assert.Equal(1, evaluator.Calculate(0, 0), precision: 12);
        }
    }

    [Fact]
    public void SampledMtfLabelsUseZemaxPupilSpaceTangentialSagittalDirections()
    {
        var optic = Optic.CreateCookeTriplet();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var frequency = 20.0;
        var data = new SampledMtfAnalysis(
            optic,
            pupilSampling: 16,
            zernikeTerms: 37,
            numPoints: 3,
            maximumFrequency: frequency).GenerateData();
        var fields = SpotAnalysisEngine.DefinedFields(optic);

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var evaluator = SampledMtfEngine.Create(optic, fields[fieldIndex], wavelength, 16, 37);
            var tangential = data.PlotSeries[(fieldIndex * 2)];
            var sagittal = data.PlotSeries[(fieldIndex * 2) + 1];

            Assert.Contains("Tangential", tangential.Name);
            Assert.Contains("Sagittal", sagittal.Name);
            Assert.Equal(evaluator.Calculate(0, frequency / 2), tangential.Points[1].Y, precision: 12);
            Assert.Equal(evaluator.Calculate(frequency / 2, 0), sagittal.Points[1].Y, precision: 12);
        }
    }

    [Fact]
    public void ContrastLossMapKeepsExistingZemaxPupilSpaceDirectionLabels()
    {
        var data = new ContrastLossMapAnalysis(
            Optic.CreateCookeTriplet(),
            sampling: 8,
            frequency: 5).GenerateData();

        Assert.Equal("Sagittal", data.PlotSeries[0].Name);
        Assert.Equal("Tangential", data.PlotSeries[1].Name);
    }
    [Fact]
    public void DiffractionPsfUsesSquareRootOfRayPowerAsFieldAmplitude()
    {
        Assert.Equal(0, DiffractionEngine.FieldAmplitudeFromPower(-1), precision: 12);
        Assert.Equal(0, DiffractionEngine.FieldAmplitudeFromPower(double.NaN), precision: 12);
        Assert.Equal(2, DiffractionEngine.FieldAmplitudeFromPower(4), precision: 12);

        var wavefront = new WavefrontResult(
            new[]
            {
                new WavefrontSample(-1, -1, -1, -1, 1, 0, 1),
                new WavefrontSample(1, -1, 1, -1, 1, 0, 1),
                new WavefrontSample(-1, 1, -1, 1, 1, 0, 1),
                new WavefrontSample(1, 1, 1, 1, 1, 0, 9)
            },
            Radius: 1,
            ReferenceOpticalPath: 0,
            VignettedRayCount: 0);

        var uniform = InvokeComplexPupil("CreateUniformPupil", wavefront, 2);
        var fft = InvokeComplexPupil(
            "BuildComplexPupil",
            wavefront,
            null,
            2,
            new Wavelength { Nanometers = 550 },
            1.0,
            1.0,
            false,
            0.0);
        var zemaxCompatibleFft = InvokeComplexPupil(
            "BuildComplexPupil",
            wavefront,
            null,
            2,
            new Wavelength { Nanometers = 550 },
            1.0,
            1.0,
            true,
            0.0);

        Assert.Equal(3, uniform[1, 1].Magnitude / uniform[0, 0].Magnitude, precision: 12);
        Assert.Equal(3, fft[1, 1].Magnitude / fft[0, 0].Magnitude, precision: 12);
        Assert.Equal(3, zemaxCompatibleFft[1, 1].Magnitude / zemaxCompatibleFft[0, 0].Magnitude, precision: 12);

        var huygensWavefront = new WavefrontResult(
            new[] { new WavefrontSample(0, 0, 0, 0, 1, 0, 4) },
            Radius: 1,
            ReferenceOpticalPath: 0,
            VignettedRayCount: 0);
        var imageCoordinates = new[,] { { new Vector3D(0, 0, 2) } };
        var huygens = InvokeHuygensSummation(
            imageCoordinates,
            huygensWavefront,
            new Wavelength { Nanometers = 550 },
            false,
            null);
        Assert.Equal(4, huygens[0, 0], precision: 12);
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
    public void BiconicGeometryUsesSharedRootZemaxSagEquation()
    {
        var biconic = new BiconicGeometry(50, 50, -0.4, -0.4);
        var standard = new StandardGeometry(50, -0.4);
        var separable = new SeparableBiconicGeometry(50, 50, -0.4, -0.4);
        var x = 4.5;
        var y = -3.25;

        Assert.Equal(standard.Sag(x, y), biconic.Sag(x, y), precision: 12);
        Assert.NotEqual(separable.Sag(x, y), biconic.Sag(x, y), precision: 12);
        Assert.NotNull(biconic.DistanceToIntersection(new Vector3D(x, y, -5), new Vector3D(0, 0, 1)));
        Assert.All(new[]
        {
            biconic.SurfaceNormal(new Vector3D(x, y, biconic.Sag(x, y))).X,
            biconic.SurfaceNormal(new Vector3D(x, y, biconic.Sag(x, y))).Y,
            biconic.SurfaceNormal(new Vector3D(x, y, biconic.Sag(x, y))).Z
        }, value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void ConicSagReturnsNaNOutsideExplicitSquareRootDomain()
    {
        var standard = new StandardGeometry(1, 0);
        var asphere = new EvenAsphereGeometry(1, 0, new[] { 1e-6 });
        var biconic = new BiconicGeometry(1, 1, 0, 0);

        Assert.True(double.IsNaN(standard.Sag(1.01, 0)));
        Assert.True(double.IsNaN(asphere.Sag(1.01, 0)));
        Assert.True(double.IsNaN(biconic.Sag(1.01, 0)));
        Assert.All(new[]
        {
            standard.SurfaceNormal(new Vector3D(1.01, 0, 0)).X,
            standard.SurfaceNormal(new Vector3D(1.01, 0, 0)).Y,
            standard.SurfaceNormal(new Vector3D(1.01, 0, 0)).Z
        }, value => Assert.True(double.IsNaN(value)));
        Assert.Equal(1, standard.Sag(1, 0), precision: 12);
    }

    [Fact]
    public void ConicIntersectionsRejectUndefinedSagAndImplicitBackBranch()
    {
        var standard = new StandardGeometry(1, 0);
        var asphere = new EvenAsphereGeometry(1, 0, new[] { 1e-6 });
        var biconic = new BiconicGeometry(1, 1, 0, 0);

        Assert.Null(standard.DistanceToIntersection(new Vector3D(1.01, 0, -5), new Vector3D(0, 0, 1)));
        Assert.Null(asphere.DistanceToIntersection(new Vector3D(1.01, 0, -5), new Vector3D(0, 0, 1)));
        Assert.Null(biconic.DistanceToIntersection(new Vector3D(1.01, 0, -5), new Vector3D(0, 0, 1)));
        Assert.Equal(3, standard.DistanceToIntersection(new Vector3D(0, 0, 3), new Vector3D(0, 0, -1))!.Value, precision: 12);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void StandardGeometryPlanarRadiusReturnsStableLocalZNormal(double radius)
    {
        var geometry = new StandardGeometry(radius);

        var centerNormal = geometry.SurfaceNormal(new Vector3D(0, 0, 0));
        var offAxisNormal = geometry.SurfaceNormal(new Vector3D(1.25, -0.5, 0));

        Assert.Equal(new Vector3D(0, 0, 1), centerNormal);
        Assert.Equal(new Vector3D(0, 0, 1), offAxisNormal);
        Assert.Equal(new PlaneGeometry().Sag(1.25, -0.5), geometry.Sag(1.25, -0.5));
        Assert.Equal(
            new PlaneGeometry().DistanceToIntersection(new Vector3D(0, 0, -5), new Vector3D(0, 0, 1)),
            geometry.DistanceToIntersection(new Vector3D(0, 0, -5), new Vector3D(0, 0, 1)));
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
    public void TotalInternalReflectionKeepsIncidentMaterialAndUsesReflectiveCoatingBranch()
    {
        var surface = new OpticalSurface
        {
            Number = 1,
            Label = "TIR interface",
            Geometry = new PlaneGeometry(),
            InteractionModel = new RefractiveReflectiveInteractionModel(),
            CoatingModel = new SimpleCoatingModel(transmittance: 0.2, reflectance: 0.9),
            CoordinateSystem = new CoordinateSystem(Vector3D.Zero)
        };
        var incidentAngle = Math.PI / 3;
        var ray = new RealRay(
            new Vector3D(0, 0, -1),
            new Vector3D(Math.Sin(incidentAngle), 0, Math.Cos(incidentAngle)),
            587.6);
        var glass = new ConstantIndexMaterial("TIR glass", 1.5);
        var air = new AirMaterial();

        var result = surface.TraceRay(ray, glass, air, 0, 0);

        Assert.Equal(RayInteractionKind.TotalInternalReflection, result.InteractionKind);
        Assert.Same(glass, result.OutgoingMaterial);
        Assert.Equal(1.5, result.OutgoingRefractiveIndex, precision: 12);
        Assert.True(result.Ray.Direction.Z < 0);
        Assert.Equal(0.9, result.Ray.Intensity, precision: 12);
    }

    [Fact]
    public void SequentialTracerKeepsIncidentMaterialAfterReflection()
    {
        var optic = Optic.CreateBlank();
        var air = new AirMaterial();
        var glass = new ConstantIndexMaterial("n=1.5", 1.5);
        optic.SurfaceGroup.Replace(
            new[]
            {
                new OpticalSurface
                {
                    Label = "Reflector",
                    Geometry = new PlaneGeometry(),
                    MaterialAfter = glass,
                    Thickness = -2,
                    InteractionModel = new RefractiveReflectiveInteractionModel(isReflective: true),
                    CoatingModel = new NoneCoatingModel(),
                    CoordinateSystem = new CoordinateSystem(Vector3D.Zero)
                },
                new OpticalSurface
                {
                    Label = "Return plane",
                    Geometry = new PlaneGeometry(),
                    MaterialAfter = air,
                    InteractionModel = new RefractiveReflectiveInteractionModel(),
                    CoatingModel = new NoneCoatingModel(),
                    CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, -2))
                }
            },
            syncComposition: false);
        var bundle = new RealRayBundle(new[]
        {
            new RealRay(
                new Vector3D(0, 0, -1),
                new Vector3D(0, 0, 1),
                587.6)
        });

        var trace = optic.SequentialRayTracer.Trace(bundle);

        var history = Assert.Single(trace.RayHistories);
        Assert.Equal(2, history.Count);
        Assert.Equal(2, history[1].SegmentLength, precision: 12);
        Assert.Equal(2, history[1].SegmentOpticalPathLength, precision: 12);
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
        Assert.Equal(10_000, data.Values["NumRays"]);
        Assert.Equal("sobol", data.Values["Distribution"]);
    }

    [Fact]
    public void RmsVsFieldSpotUsesUniformDirectionalScanIndependentOfEditorWeights()
    {
        var optic = Optic.CreateDemo();
        optic.SequentialRayTracer.RayGenerator.Settings.SamplesPerField = 3;
        optic.Fields[0].Weight = 0;

        var data = optic.Analyses.Create("RMS vs Field").GenerateData();

        Assert.Equal(64, data.Values["FieldCount"]);
        Assert.Equal(63, data.Values["FieldDensity"]);
        Assert.Equal("+y", data.Values["ScanDirection"]);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(64, series.Points.Count);
            Assert.Equal(0, series.Points[0].X, 12);
            Assert.Equal(FieldCoordinates.MaximumRadius(optic.Fields), series.Points[^1].X, 12);
        });
        Assert.Equal(64.0, Convert.ToDouble(data.Values["IncludedFieldWeight"]), precision: 12);
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
        Assert.All(scene.Rays, ray =>
        {
            Assert.Equal(ray.Points.Count - 1, ray.Segments.Count);
            Assert.Equal(LayoutRaySegmentType.Incident, ray.Segments[0].SegmentType);
            Assert.Equal(LayoutRayInteractionType.None, ray.Segments[0].InteractionType);
            Assert.All(ray.Segments, segment =>
            {
                var deltaY = segment.End.Y - segment.Start.Y;
                var deltaZ = segment.End.Z - segment.Start.Z;
                var lengthSquared = (deltaY * deltaY) + (deltaZ * deltaZ);
                if (lengthSquared > 1e-20)
                {
                    Assert.True(
                        (deltaY * segment.Direction.Y)
                        + (deltaZ * segment.Direction.Z) > 0);
                }
            });
        });

        var semanticOptic = Optic.CreateBlank();
        var interactionSurface = semanticOptic.SurfaceGroup.Items[0];
        var targetSurface = semanticOptic.SurfaceGroup.Items[1];
        interactionSurface.InteractionModel = new RefractiveReflectiveInteractionModel(true);
        interactionSurface.IsReflective = true;
        var sourceRay = new RealRay(
            new Vector3D(0, 0, -1),
            new Vector3D(0, 0, 1),
            587.6);
        var reflectedHistory = new[]
        {
            new RayTraceSample(
                interactionSurface.Number,
                interactionSurface.Label,
                new Vector3D(0, 0, 0),
                new Vector3D(0, 0, -1),
                1,
                false,
                InteractionKind: RayInteractionKind.Reflected),
            new RayTraceSample(
                targetSurface.Number,
                targetSurface.Label,
                new Vector3D(0, 0, -2),
                new Vector3D(0, 0, -1),
                1,
                false,
                InteractionKind: RayInteractionKind.Transmitted)
        };
        var reflectedSegments = new Layout2DBuilder(semanticOptic)
            .BuildRaySegments(sourceRay, reflectedHistory);

        Assert.Equal(LayoutRaySegmentType.Incident, reflectedSegments[0].SegmentType);
        Assert.Equal(LayoutRaySegmentType.Reflected, reflectedSegments[1].SegmentType);
        Assert.Equal(LayoutRayInteractionType.Reflective, reflectedSegments[1].InteractionType);
        Assert.True(reflectedSegments[1].Direction.Z < 0);

        interactionSurface.InteractionModel = new RefractiveReflectiveInteractionModel();
        interactionSurface.IsReflective = false;
        var tirHistory = reflectedHistory.ToArray();
        tirHistory[0] = tirHistory[0] with
        {
            InteractionKind = RayInteractionKind.TotalInternalReflection
        };
        var tirSegment = new Layout2DBuilder(semanticOptic)
            .BuildRaySegments(sourceRay, tirHistory)[1];

        Assert.Equal(LayoutRaySegmentType.TotalInternalReflection, tirSegment.SegmentType);
        Assert.Equal(LayoutRayInteractionType.Refractive, tirSegment.InteractionType);
        Assert.True(scene.ZMax > scene.ZMin);
        Assert.True(scene.YExtent > 0);
    }

    [Fact]
    public void LayoutTreatsRefractingStopAsLensSurface()
    {
        var optic = Optic.CreateCookeTriplet();
        var stop = optic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        var scene2D = new Layout2DBuilder(optic).Build(surfaceSamples: 33);
        var scene3D = new Layout2DBuilder(optic).Build3D(surfaceSamples: 17, rimSamples: 24);

        var stop2D = scene2D.Surfaces.Single(surface => surface.SurfaceNumber == stop.Number);
        var stop3D = scene3D.Surfaces.Single(surface => surface.SurfaceNumber == stop.Number);

        Assert.True(stop2D.IsStop);
        Assert.False(stop2D.IsStandaloneStop);
        Assert.True(stop2D.Points.Max(point => point.Z) - stop2D.Points.Min(point => point.Z) > 0.01);
        Assert.True(stop3D.IsStop);
        Assert.False(stop3D.IsStandaloneStop);
    }

    [Fact]
    public void LayoutDistinguishesPlaneAirStopFromPlaneRefractingStop()
    {
        var optic = Optic.CreateTessarLens();
        var stop = optic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        var scene2D = new Layout2DBuilder(optic).Build();
        var scene3D = new Layout2DBuilder(optic).Build3D();

        Assert.True(scene2D.Surfaces.Single(surface => surface.SurfaceNumber == stop.Number).IsStandaloneStop);
        Assert.True(scene3D.Surfaces.Single(surface => surface.SurfaceNumber == stop.Number).IsStandaloneStop);

        var refractingOptic = Optic.CreateCookeTriplet();
        var refractingStop = refractingOptic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        refractingStop.Radius = 0;
        refractingStop.Geometry = new PlaneGeometry();
        var refractingScene = new Layout2DBuilder(refractingOptic).Build();

        Assert.False(refractingScene.Surfaces
            .Single(surface => surface.SurfaceNumber == refractingStop.Number).IsStandaloneStop);
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
    public void Layout3DBuilderRevolvesTheExtended2DMeridianWithoutSlopedExtrapolation()
    {
        var optic = Optic.CreateDemo();
        optic.SurfaceGroup.Items[3].SemiDiameter = 8;
        optic.SurfaceGroup.Renumber();
        var builder = new Layout2DBuilder(optic);

        var scene2D = builder.Build(surfaceSamples: 9);
        var scene3D = builder.Build3D(surfaceSamples: 9, rimSamples: 32);
        var element2D = scene2D.LensElements.First(lens =>
            lens.FrontSurfaceNumber == 2 && lens.BackSurfaceNumber == 3);
        var element3D = scene3D.LensElements.First(lens =>
            lens.FrontSurfaceNumber == 2 && lens.BackSurfaceNumber == 3);
        var radialBoundary = element2D.Boundary
            .Select(point => new Layout2DPoint(point.Z, Math.Abs(point.Y)))
            .ToArray();

        AssertRevolvedFacesFollowProfile(element3D.FrontFaces, radialBoundary);
        AssertRevolvedFacesFollowProfile(element3D.BackFaces, radialBoundary);
        Assert.Equal(element2D.Boundary.Count, element3D.MeridianBoundary.Count);
        Assert.All(
            element2D.Boundary.Zip(element3D.MeridianBoundary),
            pair =>
            {
                Assert.Equal(0, pair.Second.X, precision: 12);
                Assert.Equal(pair.First.Y, pair.Second.Y, precision: 12);
                Assert.Equal(pair.First.Z, pair.Second.Z, precision: 12);
            });

        var backShoulder = element3D.BackFaces
            .SelectMany(face => face.Points)
            .Where(point => Math.Sqrt((point.X * point.X) + (point.Y * point.Y)) > 8 + 1e-9)
            .ToArray();
        Assert.NotEmpty(backShoulder);
        Assert.Equal(
            backShoulder[0].Z,
            backShoulder.Max(point => point.Z),
            precision: 10);
        Assert.Equal(
            backShoulder[0].Z,
            backShoulder.Min(point => point.Z),
            precision: 10);
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
        Assert.All(scene.LensElements, element =>
        {
            Assert.NotEmpty(element.FrontFaces);
            Assert.NotEmpty(element.BackFaces);
            AssertFacesStayInsideRim(element.FrontFaces, element.FrontRim);
            AssertFacesStayInsideRim(element.BackFaces, element.BackRim);
        });
        var curvedSurface = scene.Surfaces.Single(surface => surface.SurfaceNumber == 2);
        var curvedSurfacePoints = curvedSurface.Faces.SelectMany(face => face.Points).ToArray();
        Assert.True(curvedSurfacePoints.Max(point => point.Z) - curvedSurfacePoints.Min(point => point.Z) > 0.1);
        Assert.Contains(scene.Rays, ray => ray.Points.Any(point => Math.Abs(point.X) > 0.01));
        Assert.True(scene.ZMax > scene.ZMin);
        Assert.True(scene.XExtent > 0);
        Assert.True(scene.YExtent > 0);
    }

    private static void AssertFacesStayInsideRim(
        IReadOnlyList<Layout3DSurfaceFace> faces,
        IReadOnlyList<Layout3DPoint> rim)
    {
        var rimRadius = rim.Max(point => Math.Sqrt((point.X * point.X) + (point.Y * point.Y)));
        var faceRadii = faces
            .SelectMany(face => face.Points)
            .Select(point => Math.Sqrt((point.X * point.X) + (point.Y * point.Y)))
            .ToArray();

        Assert.NotEmpty(faceRadii);
        Assert.All(faceRadii, radius => Assert.InRange(radius, 0, rimRadius + 1e-9));
        Assert.Equal(rimRadius, faceRadii.Max(), precision: 8);
    }

    private static void AssertRevolvedFacesFollowProfile(
        IReadOnlyList<Layout3DSurfaceFace> faces,
        IReadOnlyList<Layout2DPoint> profile)
    {
        Assert.NotEmpty(faces);
        foreach (var point in faces.SelectMany(face => face.Points))
        {
            var radius = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
            Assert.Contains(
                profile,
                sample => Math.Abs(sample.Y - radius) < 1e-8
                    && Math.Abs(sample.Z - point.Z) < 1e-8);
        }
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
    public void LayoutHidesVignettedRaysByDefaultAndCanShowThemForDiagnostics()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[^2].PhysicalAperture = new CircularAperture(0.001);
        var builder = new Layout2DBuilder(optic);

        var diagnosticScene = builder.Build(options: new LayoutBuildOptions(
            RayCount: 7,
            DeleteVignetted: false));
        var zemaxStyleScene = builder.Build(options: new LayoutBuildOptions(RayCount: 7));

        Assert.Contains(diagnosticScene.Rays, ray => ray.Vignetted);
        Assert.DoesNotContain(zemaxStyleScene.Rays, ray => ray.Vignetted);
        Assert.True(zemaxStyleScene.Rays.Count < diagnosticScene.Rays.Count);
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
        Assert.Contains("FFT PSF Cross Section", optic.Analyses.Names);
        Assert.Contains("FFT Line Edge Spread", optic.Analyses.Names);
        Assert.DoesNotContain("MMDFT PSF", optic.Analyses.Names);
        Assert.Contains("Huygens PSF", optic.Analyses.Names);
        Assert.Contains("Huygens PSF Cross Section", optic.Analyses.Names);
        Assert.Contains("MTF", optic.Analyses.Names);
        Assert.Contains("Huygens MTF", optic.Analyses.Names);
        Assert.Contains("Geometric MTF", optic.Analyses.Names);
        Assert.Contains("Contrast Loss Map", optic.Analyses.Names);
        Assert.Contains("Fourier Through Focus MTF", optic.Analyses.Names);
        Assert.Contains("Huygens Through Focus MTF", optic.Analyses.Names);
        Assert.Contains("Geometric Through Focus MTF", optic.Analyses.Names);
        Assert.Contains("Fourier MTF vs Field", optic.Analyses.Names);
        Assert.Contains("Huygens MTF vs Field", optic.Analyses.Names);
        Assert.Contains("Geometric MTF vs Field", optic.Analyses.Names);
        Assert.Contains("Optical Path Difference", optic.Analyses.Names);
        Assert.Contains("Foucault Analysis", optic.Analyses.Names);
        Assert.Contains("Wavefront", optic.Analyses.Names);
        Assert.Contains("Centroid Sphere Wavefront", optic.Analyses.Names);
        Assert.Contains("Best Fit Sphere Wavefront", optic.Analyses.Names);
        Assert.Contains("Relative Illumination", optic.Analyses.Names);
        Assert.Contains("Footprint Diagram", optic.Analyses.Names);
        Assert.Contains("Single Ray Trace", optic.Analyses.Names);
        Assert.DoesNotContain("Best Fit Ray Fan", optic.Analyses.Names);
        Assert.Contains("Seidel Coefficients", optic.Analyses.Names);
        Assert.Contains("Seidel Diagram", optic.Analyses.Names);
        Assert.Contains("Color Focus Shift", optic.Analyses.Names);
        Assert.Contains("Lateral Color", optic.Analyses.Names);
        Assert.Contains("Axial Aberration", optic.Analyses.Names);
        Assert.Contains("Field Curvature and Distortion", optic.Analyses.Names);
        Assert.Contains("Full Field Aberration", optic.Analyses.Names);
        Assert.Contains("Diffraction Encircled Energy", optic.Analyses.Names);
        Assert.Contains("Geometric Line Edge Spread", optic.Analyses.Names);
        Assert.Contains("Extended Source Encircled Energy", optic.Analyses.Names);
        Assert.Equal(69, optic.Analyses.Names.Count);
        Assert.Equal("Spot Diagram", optic.Analyses.Create("Spot Diagram").GenerateData().Name);
    }

    [Theory]
    [InlineData(MtfComputationMethod.Fourier)]
    [InlineData(MtfComputationMethod.Huygens)]
    [InlineData(MtfComputationMethod.Geometric)]
    public void EveryMtfMethodSupportsThroughFocusAndFieldScans(MtfComputationMethod method)
    {
        var optic = Optic.CreateDemo();
        var settings = new MtfComputationSettings(
            PupilSampling: 8,
            ImageSize: 16,
            GeometricRayCount: 8,
            Distribution: "uniform");

        var throughFocus = new MtfThroughFocusAnalysis(
            optic,
            method,
            spatialFrequency: 10,
            deltaFocus: 0.05,
            focusPlaneCount: 3,
            settings).GenerateData();
        var versusField = new MtfVsFieldAnalysis(
            optic,
            method,
            spatialFrequency: 10,
            fieldPointCount: 3,
            settings).GenerateData();

        Assert.Equal(6, throughFocus.SeriesList?.Count);
        Assert.Equal(2, versusField.SeriesList?.Count);
        Assert.All(throughFocus.SeriesList!, series => Assert.All(series.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        }));
        Assert.All(versusField.SeriesList!, series => Assert.All(series.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        }));
        Assert.Equal("deg", versusField.Values["FieldUnit"]);
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
    [InlineData("Footprint Diagram", AnalysisSeriesKind.Scatter)]
    [InlineData("Encircled Energy", AnalysisSeriesKind.Line)]
    [InlineData("RMS vs Field", AnalysisSeriesKind.Line)]
    [InlineData("Through Focus", AnalysisSeriesKind.Line)]
    [InlineData("Y-Ybar", AnalysisSeriesKind.Line)]
    [InlineData("Zernike", AnalysisSeriesKind.Bar)]
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
    public void FootprintDiagramPlotsTheSelectedSurfaceAndFiltersVignettedRays()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[^1];
        surface.PhysicalAperture = new CircularAperture(0.001);

        var unfiltered = new FootprintDiagramAnalysis(
            optic,
            rayDensity: 3,
            surfaceNumber: surface.Number,
            wavelengthNumber: 1,
            fieldNumber: 1,
            deleteVignetted: false).GenerateData();
        var filtered = new FootprintDiagramAnalysis(
            optic,
            rayDensity: 3,
            surfaceNumber: surface.Number,
            wavelengthNumber: 1,
            fieldNumber: 1,
            deleteVignetted: true).GenerateData();

        Assert.Equal(surface.Number, unfiltered.Values["SurfaceNumber"]);
        Assert.Equal(1, unfiltered.Values["FieldNumber"]);
        Assert.Equal(1, unfiltered.Values["WavelengthNumber"]);
        Assert.True((int)unfiltered.Values["LaunchedRayCount"] > 0);
        Assert.True((int)unfiltered.Values["PlottedRayCount"] > (int)filtered.Values["PlottedRayCount"]);
        Assert.Contains(unfiltered.PlotSeries, item => item.Kind == AnalysisSeriesKind.Line);
        Assert.All(unfiltered.PlotSeries.SelectMany(item => item.Points), point =>
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

        Assert.Equal(2.0, problem.ResidualVector()[0]);
        Assert.Equal(4.0, problem.SumSquared());
    }

    [Fact]
    public void OptimizationProblemEvaluatesEachOperandOncePerMeritCalculation()
    {
        var evaluations = 0;
        var problem = new OptimizationProblem();
        problem.AddOperand(new Operand("counted", 1, 2, () =>
        {
            evaluations++;
            return 3;
        }));

        Assert.Equal(4, problem.SumSquared());
        Assert.Equal(1, evaluations);
    }

    [Fact]
    public void OptimizationProblemUsesZemaxAbsoluteWeightNormalization()
    {
        var problem = new OptimizationProblem();
        problem.AddOperand(new Operand("first", 0, 1, () => 2));
        problem.AddOperand(new Operand("second", 0, 3, () => 2));
        problem.AddOperand(new Operand("monitor", 0, 0, () => 1000));

        var residuals = problem.ResidualVector();

        Assert.Equal(2, residuals.Length);
        Assert.Equal(1, residuals[0], precision: 12);
        Assert.Equal(Math.Sqrt(3), residuals[1], precision: 12);
        Assert.Equal(4, problem.SumSquared(), precision: 12);
    }

    [Fact]
    public void OptimizerCatalogOnlyListsDistinctImplementedAlgorithms()
    {
        Assert.Equal(
            new[] { "LM / DLS", "Nelder-Mead", "Powell", "Orthogonal Descent" },
            OptimizerCatalog.Names);
    }

    [Fact]
    public void MeritFunctionEvaluatesCommonZemaxStyleOperands()
    {
        var optic = Optic.CreateCookeTriplet();
        var radius = MeritFunctionCatalog.Evaluate(optic, new MeritOperandDefinition
        {
            Type = "RADI",
            Surface = 1,
            Target = optic.SurfaceGroup.Items[1].Radius + 1,
            Weight = 2
        });
        var focalLength = MeritFunctionCatalog.Evaluate(optic, new MeritOperandDefinition
        {
            Type = "EFFL",
            Target = 0,
            Weight = 1
        });
        var rayHeight = MeritFunctionCatalog.Evaluate(optic, new MeritOperandDefinition
        {
            Type = "REAY",
            Field = 2,
            Wavelength = 2,
            Px = 0,
            Py = 0.5
        });

        Assert.Equal(optic.SurfaceGroup.Items[1].Radius, radius.Value, precision: 10);
        Assert.Equal(2, radius.Contribution, precision: 10);
        Assert.True(double.IsFinite(focalLength.Value));
        Assert.True(double.IsFinite(rayHeight.Value));
        Assert.All(new[] { radius, focalLength, rayHeight }, evaluation => Assert.Empty(evaluation.Error));
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
    public void LeastSquaresOptimizerUsesIndependentResidualVectorsForCoupledSystem()
    {
        var x = 0.0;
        var y = 0.0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable(
            "x", () => x, value => x = value, -10, 10, scaler: new UnitRangeScaler(-10, 10)));
        problem.AddVariable(new DelegateVariable(
            "y", () => y, value => y = value, -10, 10, scaler: new UnitRangeScaler(-10, 10)));
        problem.AddOperand(new Operand("sum", 0, 1, () => throw new InvalidOperationException()));
        problem.AddOperand(new Operand("difference", 0, 1, () => throw new InvalidOperationException()));
        problem.SetIndependentValueEvaluator(values => new[]
        {
            values[0] + values[1] - 3,
            (2 * values[0]) - values[1]
        });

        var result = OptimizerCatalog.Create("Least Squares").Optimize(problem, maxIterations: 20);

        Assert.True(result.FinalMerit < 1e-12);
        Assert.Equal(1, x, precision: 6);
        Assert.Equal(2, y, precision: 6);
        Assert.True(problem.SupportsParallelResidualEvaluation);
    }

    [Fact]
    public void LeastSquaresTreatsNegativeWeightAsExactConstraint()
    {
        var value = 0.0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable(
            "x", () => value, next => value = next, -20, 20, scaler: new UnitRangeScaler(-20, 20)));
        problem.AddOperand(new Operand("performance", 10, 1, () => value));
        problem.AddOperand(new Operand("exact", 2, -1, () => value));

        var result = OptimizerCatalog.Create("LM / DLS").Optimize(problem, maxIterations: 20);

        Assert.Equal(2, value, precision: 8);
        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.Equal(32, result.FinalMerit, precision: 6);
    }

    [Fact]
    public void LeastSquaresOptimizesInsideExactConstraintNullSpace()
    {
        var x = 0.0;
        var y = 0.0;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable(
            "x", () => x, value => x = value, -10, 10, scaler: new UnitRangeScaler(-10, 10)));
        problem.AddVariable(new DelegateVariable(
            "y", () => y, value => y = value, -10, 10, scaler: new UnitRangeScaler(-10, 10)));
        problem.AddOperand(new Operand("performance", 5, 1, () => x));
        problem.AddOperand(new Operand("sum constraint", 3, -1, () => x + y));

        OptimizerCatalog.Create("LM / DLS").Optimize(problem, maxIterations: 20);

        Assert.Equal(5, x, precision: 6);
        Assert.Equal(-2, y, precision: 6);
        Assert.Equal(3, x + y, precision: 8);
    }

    [Fact]
    public void LeastSquaresBuildsEveryIndependentJacobianColumn()
    {
        var values = new double[4];
        var evaluations = new ConcurrentQueue<double[]>();
        var problem = new OptimizationProblem();
        for (var index = 0; index < values.Length; index++)
        {
            var variableIndex = index;
            problem.AddVariable(new DelegateVariable(
                $"x{index}",
                () => values[variableIndex],
                value => values[variableIndex] = value,
                -10,
                10,
                scaler: new UnitRangeScaler(-10, 10)));
            problem.AddOperand(new Operand(
                $"target{index}",
                index + 1,
                1,
                () => throw new InvalidOperationException()));
        }

        problem.SetIndependentValueEvaluator(vector =>
        {
            evaluations.Enqueue(vector.ToArray());
            return vector.ToArray();
        });

        OptimizerCatalog.Create("LM / DLS").Optimize(problem, maxIterations: 1);

        var perturbedColumns = evaluations
            .Select(vector => vector
                .Select((value, index) => (value, index))
                .Where(item => Math.Abs(item.value) > 1e-12)
                .Select(item => item.index)
                .ToArray())
            .Where(indices => indices.Length == 1)
            .Select(indices => indices[0])
            .Distinct()
            .Order()
            .ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3 }, perturbedColumns);
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

        Assert.Equal(OpticSnapshotValidator.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.NotNull(snapshot.Aperture);
        Assert.NotNull(snapshot.BackendName);
        Assert.All(snapshot.Surfaces, surface => Assert.NotNull(surface.Components));
    }

    [Fact]
    public void AnalysisCatalogGeneratesDataForEveryRegisteredName()
    {
        var optic = Optic.CreateDemo();
        var missingAxisMetadata = new List<string>();

        foreach (var name in optic.Analyses.Names)
        {
            var data = optic.Analyses.Create(name).GenerateData();

            Assert.Equal(name, data.Name);
            Assert.NotEmpty(data.Values);
            Assert.False(string.IsNullOrWhiteSpace(data.ExportText()));
            var series = data.PlotSeries
                .Concat(data.PlotPanes?.SelectMany(pane => pane.Series)
                    ?? Array.Empty<AnalysisSeries>());
            foreach (var item in series)
            {
                if (!string.IsNullOrWhiteSpace(item.XAxisLabel)
                    && (item.XQuantity == AnalysisAxisQuantity.Unspecified
                        || item.XUnit == AnalysisAxisUnit.Unspecified))
                {
                    missingAxisMetadata.Add($"{name}: X '{item.XAxisLabel}'");
                }

                if (!string.IsNullOrWhiteSpace(item.YAxisLabel)
                    && (item.YQuantity == AnalysisAxisQuantity.Unspecified
                        || item.YUnit == AnalysisAxisUnit.Unspecified))
                {
                    missingAxisMetadata.Add($"{name}: Y '{item.YAxisLabel}'");
                }

                if (item.Points.Any(point => point.Value.HasValue)
                    && !string.IsNullOrWhiteSpace(item.ValueLabel)
                    && (item.ValueQuantity == AnalysisAxisQuantity.Unspecified
                        || item.ValueUnit == AnalysisAxisUnit.Unspecified))
                {
                    missingAxisMetadata.Add($"{name}: Value '{item.ValueLabel}'");
                }
            }
        }

        Assert.True(
            missingAxisMetadata.Count == 0,
            string.Join(Environment.NewLine, missingAxisMetadata.Distinct()));
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
            if (extension == ".zmx")
            {
                Assert.Equal(optic.Name, restored.Name);
            }
            else
            {
                Assert.Equal("Imported", restored.Name[..8]);
            }
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

    private static System.Numerics.Complex[,] InvokeComplexPupil(string methodName, params object?[] parameters)
    {
        var method = typeof(DiffractionEngine).GetMethod(
            methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(DiffractionEngine), methodName);
        return (System.Numerics.Complex[,])method.Invoke(null, parameters)!;
    }

    private static double[,] InvokeHuygensSummation(params object?[] parameters)
    {
        var method = typeof(DiffractionEngine).GetMethod(
            "HuygensSummation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(DiffractionEngine), "HuygensSummation");
        return (double[,])method.Invoke(null, parameters)!;
    }}

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
