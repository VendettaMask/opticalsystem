using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

// Independent scalar, paraxial, monochromatic disk references. No native baseline replacement.
public sealed class AfocalIndependentPhysicsTests(ITestOutputHelper output)
{
    private static Optic Disk()
    {
        var optic = OpticalFormatCatalog.Import("MODE SEQ\nENPD 2\nWAVM 1 0.55 1\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 1\nDISZ 10\nSURF 2\nDIAM 20", ".zmx");
        optic.ImageSpaceAfocal = true;
        return optic;
    }

    private void Record(object value) => output.WriteLine(JsonSerializer.Serialize(value));

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void IdealDiskHasAnalyticAiryMtfAndDiscreteParseval(int n)
    {
        var optic = Disk();
        var g = 4 * n;
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], n, g);
        // Independent prescription: diameter 2 mm, wavelength .55 um, n-1 intervals.
        var pitch = .55 * (n - 1) / (2 * g);
        Assert.Equal(AnalysisAxisUnit.Milliradian, psf.SampleSpacingUnit);
        Assert.InRange(Math.Abs(psf.SampleSpacingMicrometers - pitch), 0, 1e-12);
        var occupied = 0;
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
        {
            var px = -1 + 2d * x / (n - 1); var py = -1 + 2d * y / (n - 1);
            if (px * px + py * py <= 1) occupied++;
        }
        var sum = psf.Values.Cast<double>().Sum() / 100;
        var parsevalExpected = (double)g * g / occupied;
        Assert.InRange(Math.Abs(sum / parsevalExpected - 1), 0, 2e-12);
        var airyErrors = Enumerable.Range(0, 17).Select(k =>
        {
            var z = Math.PI * 2 * (k * pitch) / .55;
            var expected = z == 0 ? 1 : Math.Pow(2 * BesselJ1(z) / z, 2);
            return Math.Abs(psf.Values[g / 2, g / 2 + k] / 100 - expected);
        }).ToArray();
        // Conservative O(h) raster-boundary budget, declared before running.
        // Not the unchanged native comparison tolerance, and not a continuum precision claim.
        Assert.InRange(airyErrors.Max(), 0, 8d / (n - 1));
        var mtfErrors = new List<double>();
        foreach (var shift in new[] { 0, n / 4, n / 2, 3 * n / 4, n - 1 })
        {
            var nu = shift / (n - 1d);
            var expected = 2 / Math.PI * (Math.Acos(nu) - nu * Math.Sqrt(1 - nu * nu));
            var otf = Complex.Zero;
            for (var y = 0; y < g; y++) for (var x = 0; x < g; x++)
                otf += psf.Values[y, x] / 100 * Complex.FromPolarCoordinates(1, 2 * Math.PI * shift * (x - g / 2) / g);
            mtfErrors.Add(Math.Abs(otf.Magnitude / sum - expected));
        }
        Assert.InRange(mtfErrors.Max(), 0, 8d / (n - 1));
        var windows = new List<object>();
        foreach (var radius in new[] { .2, .4, .8 })
        {
            var cropped = 0d;
            for (var y = 0; y < g; y++) for (var x = 0; x < g; x++)
                if (Math.Pow((x - g / 2) * pitch, 2) + Math.Pow((y - g / 2) * pitch, 2) <= radius * radius)
                    cropped += psf.Values[y, x] / 100;
            var z = Math.PI * 2 * radius / .55;
            var continuum = 1 - Math.Pow(BesselJ0(z), 2) - Math.Pow(BesselJ1(z), 2);
            windows.Add(new
            {
                radiusMrad = radius,
                discreteFractionOfPeriod = cropped / sum,
                continuumFractionOfInfinitePlane = continuum
            });
        }
        Record(new
        {
            kind = "ideal-disk",
            n,
            g,
            occupied,
            pitchMrad = pitch,
            windows,
            airyMaximumAbsolute = airyErrors.Max(),
            mtfMaximumAbsolute = mtfErrors.Max(),
            parsevalRelative = sum / parsevalExpected - 1,
            rasterBudget = 8d / (n - 1)
        });
    }

    // J1 power series is independent of the FFT implementation; evaluated only |z| <= 13.
    private static double BesselJ1(double z)
    {
        var term = z / 2; var sum = term;
        for (var k = 1; k < 100; k++)
        {
            term *= -z * z / (4 * k * (k + 1)); sum += term;
            if (Math.Abs(term) < 1e-17) break;
        }
        return sum;
    }

    private static double BesselJ0(double z)
    {
        var term = 1d; var sum = term;
        for (var k = 1; k < 100; k++)
        {
            term *= -z * z / (4 * k * k); sum += term;
            if (Math.Abs(term) < 1e-17) break;
        }
        return sum;
    }

    [Fact]
    public void ZeroPaddingChangesAngularPitchButNotMatchedSamplesOrIntegratedEnergy()
    {
        var optic = Disk(); const int n = 64;
        var small = DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], n, 128);
        var large = DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], n, 256);
        var maximum = 0d;
        for (var y = 0; y < 128; y++) for (var x = 0; x < 128; x++)
            maximum = Math.Max(maximum, Math.Abs(small.Values[y, x] - large.Values[2 * y, 2 * x]));
        Assert.InRange(maximum, 0, 1e-10);
        double Energy(PsfResult p) => p.Values.Cast<double>().Sum() * Math.Pow(p.SampleSpacingMicrometers, 2);
        Assert.InRange(Math.Abs(Energy(small) / Energy(large) - 1), 0, 2e-12);
        Assert.Equal(small.SampleSpacingMicrometers / 2, large.SampleSpacingMicrometers);
        Record(new
        {
            kind = "padding",
            maximum,
            energyRelative = Energy(small) / Energy(large) - 1,
            note = "Same Fourier period; this is zero padding, not an enlarged physical window"
        });
    }

    [Fact]
    public void RotatingAnAsymmetricPhasePupilRotatesTheFullPsf()
    {
        var optic = Disk(); const int n = 64; const int g = 128;
        var wave = optic.Wavelengths[0];
        var pupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, n, referenceWavelength: wave);
        double Phase(double x, double y) => .21 * x + .13 * x * x + .09 * x * y;
        WavefrontResult Prepared(bool rotate) => pupil with
        {
            Samples = pupil.Samples.Select(s => s with
            { OpdWaves = rotate ? Phase(s.NormalizedPupilY, -s.NormalizedPupilX) : Phase(s.NormalizedPupilX, s.NormalizedPupilY) }).ToArray()
        };
        var first = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, n, g, preparedWavefront: Prepared(false), referenceWavelength: wave);
        var rotated = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, n, g, preparedWavefront: Prepared(true), referenceWavelength: wave);
        var maximum = 0d;
        for (var y = 0; y < g; y++) for (var x = 0; x < g; x++)
            maximum = Math.Max(maximum, Math.Abs(rotated.Values[y, x] - first.Values[(g - x) % g, y]));
        Assert.InRange(maximum, 0, 1e-10);
        Record(new { kind = "rotation90", maximum, scope = "prepared scalar pupil, not a folded-train global-frame certification" });
    }

    [Theory]
    [InlineData("Afocal/Binocular_System.ZMX", 1)]
    [InlineData("Afocal/Binocular_System.ZMX", 2)]
    [InlineData("Miscellaneous/Apochromat_prism.zmx", 1)]
    public async Task BoundedPupilProbeSweepRecordsScaleAndFitResidual(string lens, int configuration)
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/afocal-repair/sources", lens))).Configurations[configuration - 1];
        var wave = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        foreach (var h in new[] { .01, .003, .001, .0003, .0001 })
        {
            var wf = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave,
                new[] { (-h, 0d), (h, 0d), (0d, -h), (0d, h) },
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: wave);
            Assert.Equal(4, wf.Samples.Count); Assert.All(wf.Samples, s => Assert.True(s.Intensity > 0));
            Vector3D P(int i) => new(wf.Samples[i].PupilX, wf.Samples[i].PupilY, wf.Samples[i].PupilZ);
            var dx = (P(1) - P(0)).Length / h; var dy = (P(3) - P(2)).Length / h;
            Assert.True(double.IsFinite(dx) && dx > 0 && double.IsFinite(dy) && dy > 0);
            Record(new
            {
                kind = "pencil",
                lens,
                configuration,
                h,
                diameterX = dx,
                diameterY = dy,
                aspect = dx / dy,
                productionScalarDiameter = wf.AfocalPupilDiameterMillimeters,
                aimAtStop = optic.RayAimingEnabled,
                points = wf.Samples.Select(s => new { s.PupilX, s.PupilY, s.PupilZ }).ToArray()
            });
            if (lens.Contains("Binocular"))
                foreach (var angle in new[] { 3e-4, 1e-4, 3e-5 })
                {
                    var result = ExitPupilGeometryEngine.Compute(optic, (0, 0), wave,
                        new[] { (0d, 0d), (h, 0d), (0d, h) }, angle);
                    var diameter = 2 * result.DisplayRadiusMillimeters / h;
                    Assert.True(double.IsFinite(diameter) && diameter > 0);
                    Record(new
                    {
                        kind = "compound",
                        lens,
                        configuration,
                        h,
                        angle,
                        diameter,
                        centerZ = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(result.Center).Z,
                        residualMm = result.MaximumFitResidualMillimeters
                    });
                }
        }
        // Finite/accepted data is a test assertion; stability is reported, not silently certified.
    }
}
