using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ImageSimulationRepairTests
{
    [Theory]
    [InlineData(3, 4)]
    [InlineData(1, 4)]
    [InlineData(3, 1)]
    [InlineData(1, 1)]
    public void IdentityWarpPreservesEveryPixelIncludingEdges(int height, int width)
    {
        var source = new double[height, width];
        var grid = new (double X, double Y)[height, width];
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
            {
                source[row, column] = 1 + row * width + column;
                grid[row, column] = (Coordinate(column, width), Coordinate(row, height));
            }
        EqualPixels(source, ImageSimulationEngine.WarpImage(source, grid));
    }

    [Fact]
    public void WarpRetainsFlipInterpolationAndRealZeroPadding()
    {
        double[,] source = { { 2, 4, 8 }, { 10, 20, 40 } };
        var grid = new (double X, double Y)[,]
        {
            { (1, -1), (-1, -1), (-.5, -1), (-2, -1) },
            { (1, 1), (-1, 1), (-1.5, 1), (3, 1) }
        };
        EqualPixels(new double[,] { { 8, 2, 3, 0 }, { 40, 10, 5, 0 } },
            ImageSimulationEngine.WarpImage(source, grid));
    }

    [Fact]
    public void SingletonWarpDoesNotExtendOutsideTheFieldOrAcceptNonfiniteCoordinates()
    {
        var grid = new (double X, double Y)[,] { { (0, 0), (2, 0), (0, -2), (double.NaN, 0) } };
        EqualPixels(new double[,] { { 7, 0, 0, 0 } }, ImageSimulationEngine.WarpImage(new double[,] { { 7 } }, grid));
    }

    [Fact]
    public void WarpTreatsExtremeOutsideCoordinatesAsBlackRatherThanOverflowingInterpolation()
    {
        var grid = new (double X, double Y)[,] { { (double.MaxValue, 0), (0, -double.MaxValue), (double.PositiveInfinity, 0) } };
        EqualPixels(new double[1, 3], ImageSimulationEngine.WarpImage(new double[,] { { 1, 1 }, { 1, 1 } }, grid));
    }

    [Fact]
    public void NoAberrationOrIlluminationDoesNotInventABlackBorder()
    {
        var result = ImageSimulationEngine.Simulate(Cone(), UniformSource(), Config(illumination: false));
        Assert.All(result.Simulated.Values.Cast<double>(), value => Assert.Equal(1, value, 10));
        Assert.InRange(result.MeanAbsoluteChange, 0, 1e-10);
    }

    [Theory]
    [InlineData(2, .5)]
    [InlineData(3, .5)]
    [InlineData(3, .9)]
    public void PsfCoefficientRowsRunFromHighFieldToLowField(int rows, double center)
    {
        var optic = PrimaryOptic();
        var wavelength = new Wavelength { Nanometers = 440 };
        var basis = ImageSimulationEngine.GenerateBasis(optic, wavelength, BasisConfig(rows, center, 1.8));
        var high = ImageSimulationEngine.GenerateBasis(optic, wavelength,
            BasisConfig(1, Math.Min(1, center + .2), 1e-8));
        var low = ImageSimulationEngine.GenerateBasis(optic, wavelength,
            BasisConfig(1, Math.Max(-1, center - .2), 1e-8));
        EqualPixels(high.MeanPsf, Reconstruct(basis, 0), 1e-7);
        EqualPixels(low.MeanPsf, Reconstruct(basis, rows - 1), 1e-7);
        if (rows > 2)
        {
            var midpoint = (Math.Max(-1, center - .2) + Math.Min(1, center + .2)) / 2;
            var middle = ImageSimulationEngine.GenerateBasis(optic, wavelength, BasisConfig(1, midpoint, 1e-8));
            EqualPixels(middle.MeanPsf, Reconstruct(basis, 1), 1e-7);
        }
        Assert.Contains(basis.CoefficientGrid.Cast<double>(), value => Math.Abs(value) > 1e-4);
    }

    [Fact]
    public void PolarizationChangesThePhysicalIlluminationImage()
    {
        var optic = Cone(glass: true);
        var scalar = ImageSimulationEngine.Simulate(optic, UniformSource(), Config());
        var polarized = ImageSimulationEngine.Simulate(optic, UniformSource(), Config(polarization: true));
        Assert.True(Difference(scalar.Simulated.Values, polarized.Simulated.Values) > 1e-8);
    }

    [Theory]
    [InlineData("None", true)]
    [InlineData("Geometric", false)]
    [InlineData("Diffraction", false)]
    public void UnsupportedPolarizationCannotSilentlyReturnAScalarImage(string mode, bool illumination)
    {
        Assert.Throws<NotSupportedException>(() => ImageSimulationEngine.Simulate(
            Cone(glass: true, extinction: 1e-6), UniformSource(),
            Config(polarization: true, illumination: illumination, mode: mode)));
    }

    [Fact]
    public void DisablingFixedAperturesMatchesRemovalWithoutMutatingTheSourceOrItsCache()
    {
        var optic = Cone();
        var open = ImageSimulationEngine.Simulate(optic, UniformSource(), Config());
        var aperture = new OffsetRadialAperture(.55, offsetX: .25);
        optic.SurfaceGroup.Items[2].PhysicalAperture = aperture;
        var cache = new RayTraceCache();
        optic.ConfigureRayTraceCache(cache, 17);
        var on = ImageSimulationEngine.Simulate(optic, UniformSource(), Config());
        var before = cache.Statistics;
        var off = ImageSimulationEngine.Simulate(optic, UniformSource(), Config(fixedApertures: false));
        Assert.True(Difference(on.Simulated.Values, off.Simulated.Values) > 1e-4);
        Assert.InRange(Difference(open.Simulated.Values, off.Simulated.Values), 0, 1e-10);
        Assert.Same(aperture, optic.SurfaceGroup.Items[2].PhysicalAperture);
        Assert.True(optic.SurfaceGroup.Items[1].IsStop);
        Assert.Equal(2, optic.Aperture.Value);
        Assert.Equal(before, cache.Statistics);
        var again = ImageSimulationEngine.Simulate(optic, UniformSource(), Config());
        Assert.InRange(Difference(on.Simulated.Values, again.Simulated.Values), 0, 1e-12);
        Assert.True(cache.Statistics.Hits > before.Hits);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(65_536, 1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    [InlineData(3, 200)]
    [InlineData(3, int.MaxValue)]
    public void SimulationRejectsUnsafeDistortionSettingsBeforeTracing(int grid, int degree)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisResourceLimits.ValidateImageSimulation(
            UniformSource(), Config(grid: grid, degree: degree)));
    }

    [Fact]
    public void DirectDistortionEntryRejectsTheOverflowingGrid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSimulationEngine.GenerateDistortionGrid(
            Cone(), new Wavelength { Nanometers = 550 }, 16, 16, 65_536, 1));
    }

    [Fact]
    public void DistortionHonorsCancellationBeforeTracing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => ImageSimulationEngine.GenerateDistortionGrid(
            Cone(), new Wavelength { Nanometers = 550 }, 16, 16, 3, 1));
    }

    [Fact]
    public void SupportedDistortionBoundaryRemainsAvailable()
    {
        AnalysisResourceLimits.ValidateImageSimulation(UniformSource(), Config(grid: 33, degree: 9));
        var grid = ImageSimulationEngine.GenerateDistortionGrid(
            Cone(), new Wavelength { Nanometers = 550 }, 3, 4, 33, 9);
        Assert.All(grid.Cast<(double X, double Y)>(), value =>
        {
            Assert.True(double.IsFinite(value.X));
            Assert.True(double.IsFinite(value.Y));
        });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 200)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void DirectDistortionEntryUsesTheSameEarlyLimits(int grid, int degree)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSimulationEngine.GenerateDistortionGrid(
            Cone(), new Wavelength { Nanometers = 550 }, 16, 16, grid, degree));
    }

    [Fact]
    public void DistortionBudgetIncludesAllWavelengths()
    {
        Assert.Equal(33, AnalysisResourceLimits.ValidateImageDistortion(512, 512, 33, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnalysisResourceLimits.ValidateImageDistortion(512, 512, 33, 9, 16));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(4096, 4096)]
    public void DirectDistortionRejectsInvalidOutputDimensions(int height, int width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSimulationEngine.GenerateDistortionGrid(
            Cone(), new Wavelength { Nanometers = 550 }, height, width, 3, 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IlluminationGridUsesTheSharedSystemPolarizationPower(bool unpolarized)
    {
        var optic = Cone(glass: true);
        optic.Polarization = new SystemPolarization(Unpolarized: unpolarized, Jx: 1, Jy: .5, YPhaseDegrees: 60);
        var source = new double[9, 9];
        for (var y = 0; y < 9; y++)
            for (var x = 0; x < 9; x++) source[y, x] = 1;
        ImageSimulationEngine.ApplyRelativeIllumination(source, optic, optic.Wavelengths[0],
            .25, .25, 8, 0, 0, usePolarization: true);
        var fields = IlluminationMetrics.EvaluateFields(optic, [(0, 0), (.25, .25)], .55, 8,
            usePolarization: true);
        Assert.Equal(fields[1].ProjectedCosineArea / fields[0].ProjectedCosineArea, source[0, 8], 10);
    }

    [Theory]
    [InlineData(true, "chief")]
    [InlineData(false, "chief")]
    [InlineData(false, "centroid")]
    public void GeometricPsfUsesSharedPolarizedRayPower(bool unpolarized, string reference)
    {
        var optic = Cone(glass: true);
        optic.Fields[0].Y = 8;
        optic.Polarization = new SystemPolarization(Unpolarized: unpolarized, Jx: 1, Jy: .5, YPhaseDegrees: 60);
        var basis = ImageSimulationEngine.GenerateBasis(optic, optic.Wavelengths[0], new ImageSimulationConfig
        {
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = 9,
            NumRays = 8,
            Components = 1,
            FieldHeight = 1e-8,
            FieldCenterY = .5,
            PixelSizeMillimeters = .3,
            AberrationMode = "Geometric",
            Reference = reference,
            UsePolarization = true
        });
        var rays = SpotAnalysisEngine.CreatePupilSamples(8, "uniform").Select(pupil =>
        {
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, .5, pupil.X, pupil.Y, .55).Rays.Single();
            var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(ray, stopAtIncidentImage: true, usePolarization: true);
            var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(diagnostic.UnclippedImage!.Position);
            return new SpotRayData(local.X, local.Y, diagnostic.PolarizationWeightedIntensity!.Value);
        }).ToArray();
        var center = SpotAnalysisEngine.Centroid(rays);
        if (reference == "chief")
        {
            var point = optic.TraceGenericFinalSample(0, .5, 0, 0, .55)!.Position;
            center = (point.X, point.Y);
        }
        var expected = new double[9, 9];
        foreach (var ray in rays)
        {
            var x = (int)Math.Round(4 + (ray.X - center.X) / .3);
            var y = (int)Math.Round(4 - (ray.Y - center.Y) / .3);
            if (x is >= 0 and < 9 && y is >= 0 and < 9) expected[y, x] += ray.Intensity;
        }
        var sum = expected.Cast<double>().Sum();
        for (var y = 0; y < 9; y++)
            for (var x = 0; x < 9; x++) expected[y, x] /= sum;
        EqualPixels(expected, basis.MeanPsf, 1e-10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DiffractionPsfForwardsPolarizationToSharedHuygens(bool unpolarized)
    {
        var optic = PrimaryOptic();
        optic.Polarization = new SystemPolarization(Unpolarized: unpolarized, Jx: 1, Jy: .5, YPhaseDegrees: 60);
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        // This is an API-wiring control, not a new native capture: remove the captured glass absorption
        // so both coherent and unpolarized transport use the currently supported transparent contract.
        foreach (var surface in optic.SurfaceGroup.Items)
        {
            surface.MaterialBefore = new ConstantIndexMaterial("transparent-before",
                surface.MaterialBefore.RefractiveIndex(wavelength.Nanometers));
            surface.MaterialAfter = new ConstantIndexMaterial("transparent-after",
                surface.MaterialAfter.RefractiveIndex(wavelength.Nanometers));
        }
        var basis = ImageSimulationEngine.GenerateBasis(optic, wavelength, new ImageSimulationConfig
        {
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = 8,
            NumRays = 8,
            Components = 1,
            FieldHeight = 1e-8,
            PixelSizeMillimeters = .0002,
            AberrationMode = "Diffraction",
            UsePolarization = true
        });
        Assert.Equal("Diffraction", Assert.Single(basis.EffectiveModes!));
        var physical = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wavelength, 8, 9, .0002,
            usePolarization: true, aimAtStop: optic.RayAimingEnabled).Values;
        // Detector zero is (3,3): physical X=column-3, Y=3-row.
        var expected = new double[8, 8];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++) expected[y, x] = physical[7 - y, x + 1];
        var sum = expected.Cast<double>().Sum();
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++) expected[y, x] /= sum;
        EqualPixels(expected, basis.MeanPsf);
    }

    [Fact]
    public void DirectBasisDisablesNonStopAperturesButRetainsTheStopMask()
    {
        var optic = Cone(glass: true);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(1, .3);
        ImageSimulationConfig Basis(bool fixedApertures) => new()
        {
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = 9,
            NumRays = 8,
            Components = 1,
            FieldHeight = 1e-8,
            PixelSizeMillimeters = .3,
            AberrationMode = "Geometric",
            ApplyFixedApertures = fixedApertures
        };
        var open = ImageSimulationEngine.GenerateBasis(optic, optic.Wavelengths[0], Basis(true));
        var aperture = new OffsetRadialAperture(.55, offsetX: .25);
        optic.SurfaceGroup.Items[2].PhysicalAperture = aperture;
        var on = ImageSimulationEngine.GenerateBasis(optic, optic.Wavelengths[0], Basis(true));
        var off = ImageSimulationEngine.GenerateBasis(optic, optic.Wavelengths[0], Basis(false));
        EqualPixels(open.MeanPsf, off.MeanPsf);
        Assert.True(Difference(on.MeanPsf, off.MeanPsf) > 1e-3);
        Assert.Same(aperture, optic.SurfaceGroup.Items[2].PhysicalAperture);
        Assert.IsType<AnnularAperture>(optic.SurfaceGroup.Items[1].PhysicalAperture);
        optic.SurfaceGroup.Items[1].PhysicalAperture = null;
        var noStopMask = ImageSimulationEngine.GenerateBasis(optic, optic.Wavelengths[0], Basis(false));
        Assert.True(Difference(noStopMask.MeanPsf, off.MeanPsf) > 1e-3);
    }

    [Theory]
    [InlineData("UsePolarization", false)]
    [InlineData("ApplyFixedApertures", true)]
    public void ProductSwitchesChangeTheComputedRasterNotJustTheMetadata(string setting, bool masked)
    {
        var optic = Cone(glass: true);
        if (masked) optic.SurfaceGroup.Items[2].PhysicalAperture = new OffsetRadialAperture(.55, offsetX: .25);
        var runtime = new WorkbenchRuntime(optic);
        var settings = new Dictionary<string, string>
        {
            ["ImageWidth"] = "16",
            ["ImageHeight"] = "16",
            ["PsfSize"] = "8",
            ["NumRays"] = "8",
            ["PsfGridRows"] = "1",
            ["PsfGridColumns"] = "1",
            ["EigenPsfComponents"] = "1",
            ["GuardBand"] = "0",
            ["FieldHeight"] = "1",
            ["WavelengthNumber"] = "1",
            ["PixelSize"] = "0.01",
            ["DistortionGridSize"] = "3",
            ["DistortionPolynomialDegree"] = "1",
            ["AberrationMode"] = "无",
            ["FieldNumber"] = "0",
            [setting] = "true"
        };
        var on = runtime.BuildAnalysisData("Image Simulation", settings);
        settings[setting] = "false";
        var off = runtime.BuildAnalysisData("Image Simulation", settings);
        Assert.Equal(AnalysisOutcome.Success, on.Outcome);
        Assert.Equal(AnalysisOutcome.Success, off.Outcome);
        Assert.Equal(true, on.Values[setting]);
        Assert.Equal(false, off.Values[setting]);
        var difference = on.PlotSeries[0].Points.Zip(off.PlotSeries[0].Points,
            (a, b) => Math.Abs(a.Red!.Value - b.Red!.Value)).Max();
        Assert.True(difference > 1e-8);
    }

    private static Optic Cone(bool glass = false, double extinction = 0)
    {
        var optic = new Optic { FieldDefinition = FieldDefinitionKind.ObjectHeight };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint { Y = 2 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        var material = new ConstantIndexMaterial("image-repair-plate", glass ? 1.5 : 1, extinction);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 10 },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 2, MaterialAfter = material },
            new OpticalSurface { Thickness = 8, MaterialBefore = material },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static Optic PrimaryOptic() => OpticalFormatCatalog.Import(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");

    private static ImageSimulationConfig Config(bool polarization = false, bool fixedApertures = true,
        bool illumination = true, string mode = "None", int grid = 3, int degree = 1) => new()
        {
            ImageWidth = 16,
            ImageHeight = 16,
            Padding = 0,
            WavelengthsMicrometers = [.55],
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = 8,
            NumRays = 8,
            Components = 1,
            PixelSizeMillimeters = .01,
            FieldHeight = 1,
            AberrationMode = mode,
            UseRelativeIllumination = illumination,
            UsePolarization = polarization,
            ApplyFixedApertures = fixedApertures,
            DistortionGridSize = grid,
            DistortionPolynomialDegree = degree
        };

    private static ImageSimulationConfig BasisConfig(int rows, double center, double height) => new()
    {
        ImageWidth = 16,
        ImageHeight = 16,
        PsfGridRows = rows,
        PsfGridColumns = 1,
        PsfSize = 16,
        NumRays = 16,
        Components = rows,
        Padding = 0,
        PixelSizeMillimeters = .0005,
        FieldHeight = height,
        FieldCenterY = center,
        AberrationMode = "Geometric",
        Reference = "centroid"
    };

    private static RgbImage UniformSource()
    {
        var values = new double[1, 16, 16];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) values[0, y, x] = 1;
        return new RgbImage(values);
    }

    private static double Coordinate(int index, int count) => count == 1 ? 0 : -1 + 2d * index / (count - 1);

    private static double Difference(Array a, Array b) =>
        a.Cast<double>().Zip(b.Cast<double>(), (x, y) => Math.Abs(x - y)).Max();

    private static void EqualPixels(double[,] expected, double[,] actual, double tolerance = 1e-12) =>
        Assert.InRange(Difference(expected, actual), 0, tolerance);

    private static double[,] Reconstruct(PsfBasisResult basis, int row)
    {
        var result = (double[,])basis.MeanPsf.Clone();
        for (var component = 0; component < basis.EigenPsfs.Length; component++)
            for (var y = 0; y < result.GetLength(0); y++)
                for (var x = 0; x < result.GetLength(1); x++)
                    result[y, x] += basis.CoefficientGrid[component, row, 0] * basis.EigenPsfs[component][y, x];
        return result;
    }
}
