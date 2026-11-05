using OpticalSystemDesign.Validation;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class CommercialBsdfNativeTests
{
    [Fact]
    public void NativeMeasuredInputDirectionsMatchWithWhiteSpecularDisabled()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var data = BsdfFileImporter.Read(Path.Combine(fixtures, "speos-native-v11-no-white.anisotropicbsdf"), new(BsdfImportSides.Front));
        using var reference = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(fixtures, "speos-native-v11-no-white-reference.json")));
        var identity = reference.RootElement.GetProperty("Identity").Deserialize<BsdfReferenceIdentity>()!;
        var tolerance = reference.RootElement.GetProperty("Tolerance").Deserialize<BsdfReferenceTolerance>()!;
        var points = reference.RootElement.GetProperty("Values").Deserialize<BsdfReferenceValue[]>()!;
        var result = BsdfReferenceComparer.Compare(data, identity, points, tolerance);
        Assert.Equal(603, result.Points.Count); Assert.True(result.CommercialComparisonPassed);
        Record("native-no-white-regression", new { result.CommercialComparisonPassed, Count = result.Points.Count, MaximumValueError = result.Points.Max(p => p.ValueError), Scope = "400 nm, measured 0/30/60 deg inputs, White Specular disabled only" });
    }
    [Fact]
    public void LegacyGridFiveViewerCoefficientsMatchAtMeasuredAndInterpolatedIncidences()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("control.anisotropicbsdf");
        var source = SpeosV11BinaryTests.Binary(true, true); File.WriteAllBytes(path, source);
        using var reference = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "speos-native-v11-energy.json")));
        var root = reference.RootElement;
        Assert.Equal("2026.1.0.37094", root.GetProperty("Version").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), root.GetProperty("SourceSha256").GetString());
        var data = SpeosV11XyzTests.LegacyWhite(BsdfFileImporter.Read(path, BinaryZemaxBsdfTests.Options)); var model = new MeasuredBsdf(data); double maximum = 0;
        foreach (var point in root.GetProperty("Values").EnumerateArray())
        {
            var c = SpeosV11XyzTests.Context(point.GetProperty("IncidenceInput").GetDouble(), point.GetProperty("WavelengthInput").GetDouble());
            var energy = model.Integrate(c);
            var error = Math.Abs(energy.Reflection - point.GetProperty("Reflection").GetDouble() / 100);
            maximum = Math.Max(maximum, error);
            // Numerical regression for this exact native control; not a universal commercial tolerance.
            Assert.InRange(error, 0, 1e-10); Assert.Equal(0, energy.Transmission);
        }
        Record("native-v11-coefficient-comparison", new { data.SourceSha256, data.DataSha256, MaximumAbsoluteError = maximum, Samples = 15, DirectionalCertification = false });
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ThetaSamplingMatchesIndependentHemisphereCdfAndMoments(bool projected)
    {
        var points = new List<MeasuredBsdfPoint>();
        foreach (var input in new[] { 0d, 60 }) foreach (var theta in new[] { 0d, 60, 90 }) foreach (var phi in new[] { 0d, 90, 180, 270, 360 })
            points.Add(new(BsdfSide.Front, 550, input, theta, phi, theta == 0 ? .1 : theta == 60 ? .05 : 0, theta == 0 ? .05 : theta == 60 ? .025 : 0));
        var data = new MeasuredBsdfData("analytic theta control", "synthetic", new string('0', 64), points, sourceFormat: "ThetaLinearControl/1", storedQuantity: projected ? BsdfStoredQuantity.ProjectedBsdf : BsdfStoredQuantity.Bsdf,
            speosSpectral: new([new(false, new[] { 400d, 700 }, new[] { .4, .4 }, .4), new(true, new[] { 400d, 700 }, new[] { .4, .4 }, .4)], true));
        var model = new MeasuredBsdf(data); var context = SpeosV11XyzTests.Context(19);
        const int steps = 131072; double mass = 0, first = 0, second = 0, fourth = 0, cap = 0;
        for (var i = 0; i < steps; i++)
        {
            var theta = (i + .5) * Math.PI / (2 * steps); var degrees = theta * 180 / Math.PI;
            var q = degrees <= 60 ? .1 - .05 * degrees / 60 : .05 * (90 - degrees) / 30;
            var density = q * Math.Sin(theta) * (projected ? 1 : Math.Cos(theta));
            mass += density; first += density * Math.Cos(theta); second += density * Math.Pow(Math.Cos(theta), 2); fourth += density * Math.Pow(Math.Cos(theta), 4); if (degrees < 60) cap += density;
        }
        first /= mass; second /= mass; fourth /= mass; cap /= mass; mass *= Math.PI * Math.PI / steps;
        Assert.Equal(mass, model.Integrate(context).Reflection, 9); Assert.Equal(mass / 2, model.Integrate(context).Transmission, 9);
        var random = new Random(712390); const int count = 16384; double a = 0, b = 0; var hit = 0; var reflected = 0;
        for (var i = 0; i < count; i++)
        {
            var sample = model.Sample(context, random.NextDouble(), random.NextDouble()); var mu = Math.Abs(sample.Direction.Z);
            a += mu; b += mu * mu; if (mu > .5) hit++; if (sample.Lobe == BsdfLobe.DiffuseReflection) reflected++;
            Assert.Equal(model.Integrate(context).Total, sample.Value * mu / sample.Pdf, 11);
            var angle = Math.Acos(mu) * 180 / Math.PI; var expected = angle <= 60 ? .1 - .05 * angle / 60 : .05 * (90 - angle) / 30;
            if (sample.Direction.Z < 0) expected /= 2;
            if (projected) expected /= mu;
            Assert.InRange(Math.Abs(sample.Value - expected), 0, 1e-11);
        }
        Assert.InRange(Math.Abs(a / count - first), 0, 6 * Math.Sqrt((second - first * first) / count));
        Assert.InRange(Math.Abs(b / count - second), 0, 6 * Math.Sqrt((fourth - second * second) / count));
        Assert.InRange(Math.Abs(hit / (double)count - cap), 0, 6 * Math.Sqrt(cap * (1 - cap) / count));
        Assert.InRange(Math.Abs(reflected / (double)count - 2d / 3), 0, 6 * Math.Sqrt(2d / 9 / count));
        Record("theta-sampling-" + projected, new { Count = count, ExpectedFirst = first, ObservedFirst = a / count, ExpectedSecond = second, ObservedSecond = b / count, ExpectedCap = cap, ObservedCap = hit / (double)count, Reflected = reflected });
    }
    [Fact]
    public async Task PreviousSpeosGridFourExperimentKeepsOriginalAlgorithmAndRawIsp()
    {
        using var app = WorkbenchApplication.Create();
        var opened = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-speos-grid4-frozen.zip"));
        Assert.Equal("MeasuredBsdfGrid/4", opened.B.Request.NscDetector!.SurfaceBsdfProtocol);
        var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(opened.B.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(opened.B.RawDn, replay.RawDn); Assert.Equal(opened.B.Developed.Rgb24, replay.Developed.Rgb24);
    }
    [Fact]
    public void ThetaPayloadRejectsInvalidAxesAndExcessPower()
    {
        var data = SpeosV11XyzTests.Read(SpeosV11XyzTests.SpeosText());
        MeasuredBsdfData Replace(IEnumerable<MeasuredBsdfPoint> nodes) => new(data.Name, data.SourceFileName, data.SourceSha256, nodes.ToArray(), coordinates: data.Coordinates, sourceFormat: data.SourceFormat, storedQuantity: data.StoredQuantity, speosSpectral: data.SpeosSpectral);
        Assert.Throws<InvalidDataException>(() => Replace(data.Points.Select((p, i) => i == 0 ? p with { OutgoingThetaDegrees = double.NaN } : p)));
        Assert.Throws<InvalidDataException>(() => Replace(data.Points.Select(p => p with { BrdfPerSteradian = p.BrdfPerSteradian * 100 })));
        Assert.Throws<InvalidDataException>(() => Replace(data.Points.Select(p => p.IncidentThetaDegrees == 0 && p.OutgoingThetaDegrees == 45 && p.OutgoingPhiDegrees == 90 ? p with { BrdfPerSteradian = p.BrdfPerSteradian * 2 } : p)));
        var fixedPoints = data.Points.SelectMany(p => new[] { 0d, 90, 180, 270, 360 }.Select(a => p with { IncidentPhiDegrees = a })).ToArray();
        _ = new MeasuredBsdfData(data.Name, data.SourceFileName, data.SourceSha256, fixedPoints, coordinates: BsdfCoordinates.FixedMaterialAxes, sourceFormat: data.SourceFormat, storedQuantity: data.StoredQuantity, speosSpectral: data.SpeosSpectral);
        Assert.Throws<InvalidDataException>(() => new MeasuredBsdfData(data.Name, data.SourceFileName, data.SourceSha256,
            fixedPoints.Select(p => p.IncidentThetaDegrees == 0 && p.IncidentPhiDegrees == 90 && p.OutgoingThetaDegrees == 45 && p.OutgoingPhiDegrees == 90 ? p with { BrdfPerSteradian = p.BrdfPerSteradian * 2 } : p).ToArray(),
            coordinates: BsdfCoordinates.FixedMaterialAxes, sourceFormat: data.SourceFormat, storedQuantity: data.StoredQuantity, speosSpectral: data.SpeosSpectral));
    }
    private static void Record(string name, object value)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_COMMERCIAL_CALIBRATION_EVIDENCE_DIR"); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
