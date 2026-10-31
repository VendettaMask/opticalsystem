using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class SpeosV11XyzTests
{
    internal static string SpeosText(bool transmit = false, bool both = false, bool fixedAxes = false, double[]? incidenceAngles = null, double ratio = 1)
    {
        var text = new StringBuilder("OPTIS - Anisotropic BSDF surface file v11\n0\nSynthetic format/physics control, not measured\n0\n\n1 0 0\n" + (both ? "1 1" : transmit ? "0 1" : "1 0") + "\n0\n");
        var active = both ? new[] { false, true } : new[] { transmit }; var rotations = fixedAxes ? new[] { 0d, 90d, 180d, 270d, 360d } : [0d]; var incs = incidenceAngles ?? new[] { 0d, 30d, 60d };
        string N(double x) => x.ToString("G17", CultureInfo.InvariantCulture);
        foreach (var lobe in active) { text.AppendLine(rotations.Length.ToString()); text.AppendLine(string.Join(" ", rotations.Select(N))); foreach (var a in rotations) text.AppendLine(incs.Length + "\n" + string.Join(" ", incs.Select(N))); }
        foreach (var lobe in active) text.AppendLine("0 0\nSynthetic spectra\n2\n400 " + (both ? lobe ? "20" : "10" : "20") + "\n700 " + (both ? lobe ? "30" : "20" : "40"));
        var phis = Enumerable.Range(0, 37).Select(i => i * 10d).ToArray();
        foreach (var lobe in active) foreach (var a in rotations) foreach (var inc in incs)
        {
            var theta = Enumerable.Range(0, 21).Select(i => i * 4.5d).ToArray(); if (lobe) theta = theta.Select(t => 180 - t).Reverse().ToArray();
            text.AppendLine("21 37"); text.AppendLine(string.Join(" ", phis.Select(N)));
            foreach (var t in theta)
            {
                var to = lobe ? 180 - t : t; var mu = to == 90 ? 0 : Math.Cos(to * Math.PI / 180);
                var values = phis.Select(p => { var center = lobe ? Math.Asin(Math.Sin(inc * Math.PI / 180) * ratio) : inc * Math.PI / 180; var dot = mu * Math.Cos(center) + Math.Sqrt(Math.Max(0, 1 - mu * mu)) * Math.Sin(center) * Math.Cos(p * Math.PI / 180); return mu * (.01 + (1 + (fixedAxes ? .2 * Math.Cos(a * 2 * Math.PI / 180) : 0)) * Math.Exp(150 * (dot - 1))); });
                text.AppendLine(N(t) + " " + string.Join(" ", values.Select(N)));
            }
        }
        text.AppendLine("End of file"); return text.ToString();
    }
    internal static string XyzText(bool transmit = false, bool fixedAxes = false, double[]? incidences = null)
    {
        var mono = BinaryZemaxBsdfTests.Zemax(transmit, fixedAxes, incidences); var begin = mono.IndexOf("\nMonochrome\n", StringComparison.Ordinal);
        var header = mono[..begin].Replace("SpectralContent Monochrome", "SpectralContent XYZ"); var body = mono[(begin + "\nMonochrome\n".Length)..];
        return header + "\nTristimulusX\n" + body.Replace("TIS 0.59999999999999998", "TIS 0.2") + "TristimulusY\n" + body + "TristimulusZ\n" + body.Replace("TIS 0.59999999999999998", "TIS 0.4");
    }
    internal static string Calibration(double scale = 1) => JsonSerializer.Serialize(new XyzRadiometricCalibration("Synthetic laboratory calibration", "Declared synthetic 3-channel observer", "Declared synthetic equal-energy input", XyzRadiometricCalibration.UnitsContract,
        [new(400, .2 * scale, .3 * scale, .1 * scale), new(700, .1 * scale, .2 * scale, .5 * scale)]));
    internal static MeasuredBsdfData Read(string text, SpeosPeakImportOptions? peak = null, bool xyz = false)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("input.bsdf"); File.WriteAllText(path, text); var calibration = files.Path("calibration.json"); File.WriteAllText(calibration, Calibration());
        return BsdfFileImporter.Read(path, new(BsdfImportSides.BothDeclaredIdentical, 550, true, ZemaxXyzCalibrationPath: xyz ? calibration : null, SpeosPeak: peak));
    }
    internal static BsdfContext Context(double theta = 45, double wave = 550, double phi = 0, Vector3D? axis = null, double ni = 1, double nt = 1) => new(
        new(Math.Sin(theta * Math.PI / 180) * Math.Cos(phi * Math.PI / 180), Math.Sin(theta * Math.PI / 180) * Math.Sin(phi * Math.PI / 180), Math.Cos(theta * Math.PI / 180)), new(0, 0, 1), wave, materialXAxis: axis ?? new(1, 0, 0), incidentRefractiveIndex: ni, transmittedRefractiveIndex: nt);
    private static Vector3D Direction(double theta, bool trans = false) => new(-Math.Sin(theta * Math.PI / 180), 0, (trans ? -1 : 1) * Math.Cos(theta * Math.PI / 180));
    internal static void Record(string name, object value)
    { var dir = Environment.GetEnvironmentVariable("OPTICAL_SPEOS_V11_XYZ_EVIDENCE_DIR"); if (string.IsNullOrWhiteSpace(dir)) return; Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void V11TextBothQuantitiesAndTerminalMarkerAreExplicit(bool projected)
    {
        var text = SpeosText(both: true); if (!projected) text = text.Replace("1 1\n0\n", "1 1\n1\n");
        var data = Read(text); Assert.Equal("SpeosAnisotropicBsdfText11/1", data.SourceFormat);
        var e = new MeasuredBsdf(data).Integrate(Context(0)); Assert.Equal(.15, e.Reflection, 12); Assert.Equal(.25, e.Transmission, 12);
        Assert.Equal(projected ? BsdfStoredQuantity.ProjectedBsdf : BsdfStoredQuantity.Bsdf, data.StoredQuantity);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void RegisteredPeakFollowsIntermediateSpecularDirectionAndConservesPower(bool transmission)
    {
        var plain = Read(SpeosText(transmission)); var data = Read(SpeosText(transmission), new(14, .01)); var model = new MeasuredBsdf(data); var c = Context();
        Assert.Equal("MeasuredBsdfGrid/4", data.ComputationProtocol); Assert.Equal(new MeasuredBsdf(plain).Integrate(c).Total, model.Integrate(c).Total, 12);
        Assert.True(model.Evaluate(c, Direction(45, transmission)) > model.Evaluate(c, Direction(30, transmission)) * 2);
        Assert.True(model.Evaluate(c, Direction(45, transmission)) > model.Evaluate(c, Direction(60, transmission)) * 2);
        var rng = new Random(31827); double sx = 0, sz = 0, maxWeight = 0; int near = 0; const int count = 16384;
        for (var i = 0; i < count; i++) { var s = model.Sample(c, rng.NextDouble(), rng.NextDouble()); var mu = Math.Abs(s.Direction.Z); var w = s.Value * mu / s.Pdf; Assert.Equal(model.Integrate(c).Total, w, 11); maxWeight = Math.Max(maxWeight, w); sx += s.Direction.X; sz += mu; if ((s.Direction.X * Direction(45, transmission).X + s.Direction.Z * Direction(45, transmission).Z) > Math.Cos(15 * Math.PI / 180)) near++; }
        Assert.InRange(near / (double)count, .35, .65); Record("registered-" + transmission, new { Energy = model.Integrate(c), Count = count, Near15Degrees = near, MeanX = sx / count, MeanAbsZ = sz / count, MaxWeight = maxWeight });
    }
    [Fact]
    public void WhitePeakIsAchromaticAndBackgroundKeepsItsSpectrum()
    {
        var data = Read(SpeosText(), new(14, .01, 550)); var peak = data.SpeosPeak!;
        var zero = data.Points.Select(p => p with { BrdfPerSteradian = 0, BtdfPerSteradian = 0 }).ToArray();
        var isolatedPeak = new MeasuredBsdf(new(data.Name, data.SourceFileName, data.SourceSha256, zero, coordinates: data.Coordinates, sourceFormat: data.SourceFormat, storedQuantity: data.StoredQuantity, speosPeak: peak, speosSpectral: data.SpeosSpectral));
        var background = new MeasuredBsdf(new(data.Name, data.SourceFileName, data.SourceSha256, data.Points, coordinates: data.Coordinates, sourceFormat: data.SourceFormat, storedQuantity: data.StoredQuantity, speosSpectral: data.SpeosSpectral));
        Assert.Equal(isolatedPeak.Integrate(Context(wave: 400)).Total, isolatedPeak.Integrate(Context(wave: 700)).Total, 12);
        Assert.Equal(isolatedPeak.Evaluate(Context(wave: 400), Direction(45)), isolatedPeak.Evaluate(Context(wave: 700), Direction(45)), 12);
        Assert.Equal(background.Integrate(Context(wave: 400)).Total * 2, background.Integrate(Context(wave: 700)).Total, 12);
        var model = new MeasuredBsdf(data); foreach (var w in new[] { 400d, 550, 700 }) { var c = Context(wave: w); Assert.InRange(model.Integrate(c).Total, 0, 1); Assert.Equal(background.Integrate(c).Total + isolatedPeak.Integrate(c).Total, model.Integrate(c).Total, 12); }        Record("white-specular", new { peak.WhiteReferenceNanometers, data.DataSha256, Low = model.Integrate(Context(wave: 400)), Middle = model.Integrate(Context()), High = model.Integrate(Context(wave: 700)) });
    }
    [Theory] [InlineData(false)] [InlineData(true)] [InlineData(false, true)]
    public void XyzUsesIndependentTisAndCalibratedSpectralMixture(bool transmission, bool fixedAxes = false)
    {
        var data = Read(XyzText(transmission, fixedAxes), xyz: true); var model = new MeasuredBsdf(data); Assert.NotNull(data.ZemaxXyz); Assert.Equal("MeasuredBsdfGrid/4", data.ComputationProtocol);
        foreach (var wave in new[] { 400d, 550, 700 })
        {
            var c = Context(wave: wave); var expected = wave == 400 ? .26 : wave == 700 ? .34 : .30;
            Assert.Equal(expected, model.Integrate(c).Total, 12); var rng = new Random(2817); double first = 0, second = 0; const int count = 8192;
            for (var i = 0; i < count; i++) { var s = model.Sample(c, rng.NextDouble(), rng.NextDouble()); var mu = Math.Abs(s.Direction.Z); Assert.Equal(expected, s.Value * mu / s.Pdf, 11); first += mu; second += mu * mu; }
            // Constant raw XYZ controls give a Lambertian distribution independently of the spectral mixture.
            Assert.InRange(Math.Abs(first / count - 2d / 3), 0, 6 * Math.Sqrt((.5 - 4d / 9) / count)); Assert.InRange(Math.Abs(second / count - .5), 0, 6 * Math.Sqrt((1d / 3 - .25) / count));
        }
        Record("xyz-" + transmission + "-" + fixedAxes, new { data.SourceSha256, data.DataSha256, CalibrationSha = data.ZemaxXyz.Calibration.SourceSha256, data.PayloadPointCount, SamplesPerBand = 8192, ThreeBands = true });
    }
    [Fact]
    public void XyzRequiresCalibrationAndRejectsNegativeOrNonpassiveMappings()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("input.BSDF"); File.WriteAllText(path, XyzText());
        Assert.Throws<InvalidDataException>(() => BsdfFileImporter.Read(path, BinaryZemaxBsdfTests.Options));
        var calibration = files.Path("bad.json"); File.WriteAllText(calibration, Calibration(20)); Assert.Throws<InvalidDataException>(() => BsdfFileImporter.Read(path, BinaryZemaxBsdfTests.Options with { ZemaxXyzCalibrationPath = calibration }));
        File.WriteAllText(calibration, Calibration(-1)); Assert.Throws<InvalidDataException>(() => BsdfFileImporter.Read(path, BinaryZemaxBsdfTests.Options with { ZemaxXyzCalibrationPath = calibration }));
        var valid = Read(XyzText(), xyz: true); Assert.Throws<InvalidDataException>(() => new MeasuredBsdf(valid).Integrate(Context(wave: 750)));
    }
    [Fact]
    public void EnhancedAndXyzPayloadsFreezeAndDetectTampering()
    {
        foreach (var data in new[] { Read(SpeosText(), new(14, .01, 550)), Read(XyzText(), xyz: true) })
        {
            var json = JsonSerializer.Serialize(data); var restored = JsonSerializer.Deserialize<MeasuredBsdfData>(json)!; Assert.Equal(data.DataSha256, restored.DataSha256); Assert.Equal(data.ComputationProtocol, restored.ComputationProtocol);
            var bad = json.Replace(data.DataSha256, new string('0', 64)); Assert.Throws<InvalidDataException>(() => JsonSerializer.Deserialize<MeasuredBsdfData>(bad));
        }
    }
    [Fact]
    public void PublicGaussianV11TextIsDecodedOrRejectedOnlyForDeclaredBounds()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "speos-gaussian-v11.anisotropicbsdf");
        var data = BsdfFileImporter.Read(path, BinaryZemaxBsdfTests.Options with { Sides = BsdfImportSides.Front }); Assert.Equal("SpeosAnisotropicBsdfText11/1", data.SourceFormat); Assert.True(data.HasTransmission);
        Record("official-v11-text", new { data.SourceSha256, data.DataSha256, data.PayloadPointCount });
    }
}
