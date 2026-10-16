using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Tests;

public sealed class ImageCoordinateReferenceRepairTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(-45)]
    public void ImageDistortionIsInvariantToJointFrameRotationAndTranslation(double degrees)
    {
        var optic = Cone();
        var moved = Cone();
        foreach (var surface in moved.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with
            {
                Origin = surface.CoordinateSystem.Origin + new Vector3D(10, -7, 2),
                RotationZDegrees = degrees
            };
        foreach (var (hx, hy) in new[] { (.2, .3), (-.2, .1), (.5, -.6) })
        {
            var before = optic.TraceGenericFinalSample(hx, hy, 0, 0, .55)!;
            var after = moved.TraceGenericFinalSample(hx, hy, 0, 0, .55)!;
            var originalLocal = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(before.Position);
            var movedLocal = moved.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(after.Position);
            Assert.InRange((originalLocal - movedLocal).Length, 0, 1e-12);
            Assert.Equal(before.Intensity, after.Intensity);
        }
        var expected = ImageSimulationEngine.Simulate(optic, Source(), Config());
        var actual = ImageSimulationEngine.Simulate(moved, Source(), Config());
        EqualPixels(expected.Simulated.Values, actual.Simulated.Values, 1e-10);
        Assert.All(actual.Simulated.Values.Cast<double>(), value => Assert.True(value > 0));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(45)]
    [InlineData(90)]
    public void AutomaticPitchUsesTheDetectorFrameInsteadOfGlobalProjections(double degrees)
    {
        var optic = Cone();
        var moved = Cone();
        foreach (var surface in moved.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with { RotationZDegrees = degrees };
        var method = typeof(ImageSimulationEngine).GetMethod("EstimateDetectorPixelPitch", BindingFlags.NonPublic | BindingFlags.Static)!;
        double Pitch(Optic model) => (double)method.Invoke(null, [model, model.Wavelengths[0], .25, .25, 16, 16, 0d, 0d])!;
        Assert.Equal(1d / 15, Pitch(optic), 12);
        Assert.Equal(Pitch(optic), Pitch(moved), 12);
    }

    [Theory]
    [InlineData(1, "chief")]
    [InlineData(2, "chief")]
    [InlineData(7, "chief")]
    [InlineData(8, "chief")]
    [InlineData(9, "chief")]
    [InlineData(16, "chief")]
    [InlineData(32, "chief")]
    [InlineData(1, "centroid")]
    [InlineData(2, "centroid")]
    [InlineData(7, "centroid")]
    [InlineData(8, "centroid")]
    [InlineData(9, "centroid")]
    [InlineData(16, "centroid")]
    [InlineData(32, "centroid")]
    public void IdealGeometricPsfHasTheSameAnchorAsDeltaAndSameConvolution(int size, string reference)
    {
        var optic = ThinLens();
        var config = Config(mode: "Geometric", reference: reference, size: size);
        var basis = ImageSimulationEngine.GenerateBasis(optic, optic.Wavelengths[0], config);
        var anchor = (size - 1) / 2;
        Assert.Equal(1, basis.MeanPsf[anchor, anchor], 12);
        Assert.Equal(1, basis.MeanPsf.Cast<double>().Sum(), 12);
        foreach (var (px, py) in new[] { (0d, 0d), (.5, .5), (-.5, .5), (.7, 0), (0, -.8) })
        {
            var ray = optic.TraceGenericFinalSample(0, 0, px, py, .55)!;
            Assert.InRange(double.Hypot(ray.Position.X, ray.Position.Y), 0, 1e-14);
        }
        var plane = new double[16, 16];
        plane[6, 9] = 1;
        var convolved = ImageSimulationEngine.SpatiallyVariableConvolution(plane, basis.EigenPsfs,
            ImageSimulationEngine.ResizeCoefficientMaps(basis.CoefficientGrid, 16, 16), basis.MeanPsf);
        EqualPixels(plane, convolved);
        var uniform = new RgbImage(new double[1, 16, 16]);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) uniform.Values[0, y, x] = 1;
        var geometric = ImageSimulationEngine.Simulate(optic, uniform, config);
        var delta = ImageSimulationEngine.Simulate(optic, uniform, Config(size: size));
        EqualPixels(delta.Simulated.Values, geometric.Simulated.Values, 1e-10);
    }

    [Theory]
    [InlineData(.2)]
    [InlineData(.5)]
    [InlineData(.8)]
    public void DiffractionCentroidReferenceUsesTheSharedHuygensCenterOffset(double field)
    {
        var optic = Primary();
        var wave = new Wavelength { Nanometers = 550, IsPrimary = true };
        AssertDiffractionReference(optic, wave, field, false);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void ReferenceForwardingRetainsScalarAndBothPolarizationPaths(bool usePolarization, bool unpolarized)
    {
        var optic = Primary();
        var wave = new Wavelength { Nanometers = 550, IsPrimary = true };
        optic.Polarization = new SystemPolarization(Unpolarized: unpolarized, Jx: 1, Jy: .5, YPhaseDegrees: 60);
        // API-wiring control with transparent media, not a new native comparison.
        foreach (var surface in optic.SurfaceGroup.Items)
        {
            surface.MaterialBefore = new ConstantIndexMaterial("reference-transparent-before", surface.MaterialBefore.RefractiveIndex(550));
            surface.MaterialAfter = new ConstantIndexMaterial("reference-transparent-after", surface.MaterialAfter.RefractiveIndex(550));
        }
        AssertDiffractionReference(optic, wave, .5, usePolarization);
    }

    [Fact]
    public void RgbAnalysisPreservesTwoPaneAndDetectorShapeContractsOnASmallDeterministicSample()
    {
        var data = new ImageSimulationAnalysis(ThinLens(), new ImageSimulationConfig
        {
            SourceImage = ImageSimulationEngine.CreateTestChart(16, 16),
            ImageWidth = 16,
            ImageHeight = 16,
            Padding = 0,
            WavelengthsMicrometers = [.65, .55, .45],
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = 8,
            NumRays = 8,
            Components = 1,
            PixelSizeMillimeters = .01,
            FieldHeight = 1,
            AberrationMode = "Geometric",
            UseRelativeIllumination = false,
            DistortionGridSize = 9,
            DistortionPolynomialDegree = 1,
            OutputWidth = 18,
            OutputHeight = 12,
            DisplayAs = "Both"
        }).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal("(1, 3, 12, 18)", data.Values["OutputShape"]);
        Assert.Equal(2, data.PlotPanes!.Count);
        Assert.Equal(2, data.PlotPaneColumns);
        foreach (var pane in data.PlotPanes)
        {
            var raster = Assert.Single(pane.Series);
            Assert.Equal(AnalysisSeriesKind.Raster, raster.Kind);
            Assert.Equal(18 * 12, raster.Points.Count);
            Assert.All(raster.Points, point =>
            {
                Assert.True(double.IsFinite(point.Red!.Value));
                Assert.True(double.IsFinite(point.Green!.Value));
                Assert.True(double.IsFinite(point.Blue!.Value));
            });
        }
        Assert.Contains(data.PlotPanes[0].Series[0].Points,
            point => point.Red != point.Green || point.Green != point.Blue);
    }

    private static void AssertDiffractionReference(Optic optic, Wavelength wave, double field, bool usePolarization)
    {
        const int size = 16, sampling = 16;
        const double pitch = .0005;
        var chief = ImageSimulationEngine.GenerateBasis(optic, wave,
            Config(mode: "Diffraction", size: size, reference: "chief", field: field,
                pitch: pitch, rays: sampling, fieldHeight: 1e-8, polarization: usePolarization));
        var centroid = ImageSimulationEngine.GenerateBasis(optic, wave,
            Config(mode: "Diffraction", size: size, reference: "CeNtRoId", field: field,
                pitch: pitch, rays: sampling, fieldHeight: 1e-8, polarization: usePolarization));
        Assert.Equal("Diffraction", Assert.Single(chief.EffectiveModes!));
        Assert.Equal("Diffraction", Assert.Single(centroid.EffectiveModes!));
        Assert.Equal(0, chief.GeometricFallbackCount);
        Assert.Equal(0, centroid.GeometricFallbackCount);
        var offset = DiffractionEngine.HuygensGeometricCenterOffset(optic, (0, field), [wave], wave,
            sampling, optic.RayAimingEnabled);
        Assert.True(double.Hypot(offset.X, offset.Y) / pitch > .5);
        var expectedChief = DiffractionEngine.ComputeHuygensPsf(optic, (0, field), wave, sampling, size, pitch,
            usePolarization: usePolarization, aimAtStop: optic.RayAimingEnabled).Values;
        var expectedCentroid = DiffractionEngine.ComputeHuygensPsf(optic, (0, field), wave, sampling, size, pitch,
            usePolarization: usePolarization, aimAtStop: optic.RayAimingEnabled, imageCenterOffset: offset).Values;
        Normalize(expectedChief);
        Normalize(expectedCentroid);
        EqualPixels(expectedChief, chief.MeanPsf);
        EqualPixels(expectedCentroid, centroid.MeanPsf);
        Assert.True(Difference(chief.MeanPsf, centroid.MeanPsf) > 1e-5);
    }

    private static Optic Primary() => OpticalFormatCatalog.Import(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");

    private static Optic Cone()
    {
        var optic = new Optic { FieldDefinition = FieldDefinitionKind.ObjectHeight };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint { Y = 2 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 10 },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 2 },
            new OpticalSurface { Thickness = 8 },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static Optic ThinLens()
    {
        var optic = new Optic { FieldDefinition = FieldDefinitionKind.Angle };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint { Y = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 10, InteractionModel = new ThinLensInteractionModel(10) },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static RgbImage Source()
    {
        var values = new double[1, 16, 16];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) values[0, y, x] = .1 + .5 * x / 15 + .3 * y / 15;
        return new(values);
    }

    private static ImageSimulationConfig Config(string mode = "None", string reference = "chief", int size = 8,
        double field = 0, double pitch = .01, int rays = 8, double fieldHeight = 1, bool polarization = false) => new()
        {
            ImageWidth = 16,
            ImageHeight = 16,
            Padding = 0,
            WavelengthsMicrometers = [.55],
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = size,
            NumRays = rays,
            Components = 1,
            PixelSizeMillimeters = pitch,
            FieldHeight = fieldHeight,
            FieldCenterY = field,
            AberrationMode = mode,
            Reference = reference,
            UsePolarization = polarization,
            UseRelativeIllumination = false,
            DistortionGridSize = 9,
            DistortionPolynomialDegree = 1
        };

    private static double Difference(Array a, Array b) => a.Cast<double>().Zip(b.Cast<double>(), (x, y) => Math.Abs(x - y)).Max();
    private static void EqualPixels(Array expected, Array actual, double tolerance = 1e-12) => Assert.InRange(Difference(expected, actual), 0, tolerance);
    private static void Normalize(double[,] values)
    {
        var sum = values.Cast<double>().Sum();
        for (var y = 0; y < values.GetLength(0); y++)
            for (var x = 0; x < values.GetLength(1); x++) values[y, x] /= sum;
    }
}
