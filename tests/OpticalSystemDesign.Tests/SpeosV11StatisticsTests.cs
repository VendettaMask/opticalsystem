using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class SpeosV11StatisticsTests
{
    [Theory] [InlineData(400)] [InlineData(550)] [InlineData(700)]
    public void JointWhiteReflectionTransmissionMatchesIndependentGridIntegrals(double wave)
    {
        var data = SpeosV11XyzTests.Read(SpeosV11XyzTests.SpeosText(both: true), new(14, .01, 550));
        double Integral(IReadOnlyList<MeasuredBsdfPoint> points, bool transmission)
        {
            var nodes = points.Where(p => p.Side == BsdfSide.Front && p.IncidentThetaDegrees == 0).ToDictionary(p => (p.OutgoingThetaDegrees, p.OutgoingPhiDegrees));
            var theta = nodes.Keys.Select(k => k.OutgoingThetaDegrees).Distinct().Order().ToArray(); var phi = nodes.Keys.Select(k => k.OutgoingPhiDegrees).Distinct().Order().ToArray(); double sum = 0;
            for (var i = 0; i < theta.Length - 1; i++) for (var j = 0; j < phi.Length - 1; j++)
            {
                double Q(int t, int p) { var n = nodes[(theta[t], phi[p])]; return transmission ? n.BtdfPerSteradian : n.BrdfPerSteradian; }
                var dmu = Math.Cos(theta[i] * Math.PI / 180) - Math.Cos(theta[i + 1] * Math.PI / 180);
                sum += dmu * (phi[j + 1] - phi[j]) * Math.PI / 180 * (Q(i, j) + Q(i + 1, j) + Q(i, j + 1) + Q(i + 1, j + 1)) / 4;
            }
            return sum;
        }
        var rScale = (.1 + .1 * (wave - 400) / 300) / .2; var tScale = (.2 + .1 * (wave - 400) / 300) / .3;
        var expectedR = Integral(data.Points, false) * rScale + Integral(data.SpeosPeak!.Points, false) * .75;
        var expectedT = Integral(data.Points, true) * tScale + Integral(data.SpeosPeak.Points, true) * (.25 / .3);
        var model = new MeasuredBsdf(data); var c = SpeosV11XyzTests.Context(0, wave); Assert.Equal(expectedR, model.Integrate(c).Reflection, 12); Assert.Equal(expectedT, model.Integrate(c).Transmission, 12);
        var rng = new Random(7223); const int count = 16384; var reflected = 0;
        for (var i = 0; i < count; i++) { var s = model.Sample(c, rng.NextDouble(), rng.NextDouble()); if (s.Lobe == BsdfLobe.DiffuseReflection) reflected++; Assert.Equal(expectedR + expectedT, s.Value * Math.Abs(s.Direction.Z) / s.Pdf, 11); }
        var p = expectedR / (expectedR + expectedT); Assert.InRange(Math.Abs(reflected / (double)count - p), 0, 6 * Math.Sqrt(p * (1 - p) / count));
        SpeosV11XyzTests.Record("joint-white-" + wave, new { ExpectedR = expectedR, ExpectedT = expectedT, Count = count, Reflected = reflected, ExpectedProbability = p, SigmaLimit = 6 });
    }
    [Fact]
    public void TransmissionPeakUsesDeclaredRefractiveIndicesAndRejectsMismatchedContext()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("snell.anisotropicbsdf"); File.WriteAllText(path, SpeosV11XyzTests.SpeosText(true, ratio: 1 / 1.5));
        var data = BsdfFileImporter.Read(path, new(BsdfImportSides.Front, IncidentIndex: 1, TransmittedIndex: 1.5, SpeosPeak: new(14, .01)));
        var model = new MeasuredBsdf(data); var c = SpeosV11XyzTests.Context(45, ni: 1, nt: 1.5); var theta = Math.Asin(Math.Sin(Math.PI / 4) / 1.5);
        var peak = new Vector3D(-Math.Sin(theta), 0, -Math.Cos(theta)); var unbent = new Vector3D(-Math.Sin(Math.PI / 4), 0, -Math.Cos(Math.PI / 4));
        Assert.True(model.Evaluate(c, peak) > 2 * model.Evaluate(c, unbent));
        Assert.Throws<InvalidDataException>(() => model.Evaluate(SpeosV11XyzTests.Context(), peak));
    }
    private static MeasuredBsdfData PowerControl(bool transmission, bool fixedAxes = false)
    {
        var points = new List<MeasuredBsdfPoint>();
        foreach (var inc in new[] { 0d, 89 }) foreach (var ip in fixedAxes ? new[] { 0d, 90, 180, 270, 360 } : new[] { 0d }) foreach (var theta in new[] { 0d, 60, 90 }) foreach (var phi in new[] { 0d, 90, 180, 270, 360 })
            points.Add(new(BsdfSide.Front, 550, inc, theta, phi, transmission ? 0 : .6 / Math.PI, transmission ? .6 / Math.PI : 0, ip));
        var zero = points.Select(p => p with { BrdfPerSteradian = 0, BtdfPerSteradian = 0 }).ToArray();
        return new("Independent Lambertian peak power control", "synthetic", new string('0', 64), zero, coordinates: fixedAxes ? BsdfCoordinates.FixedMaterialAxes : BsdfCoordinates.IncidentPlane,
            sourceFormat: "AnalyticSlopeTransportControl/1", speosPeak: new(1, 1, null, points));
    }
    [Theory] [InlineData(false, 15)] [InlineData(false, 45)] [InlineData(false, 85)] [InlineData(true, 45)] [InlineData(true, 85)]
    public void SlopeJacobianAndRecoveredSourceCdfMatchIndependentAnalyticLaw(bool transmission, double theta)
    {
        var model = new MeasuredBsdf(PowerControl(transmission)); var c = SpeosV11XyzTests.Context(theta); var rng = new Random(71289); const int count = 16384;
        var lowerWeight = (1 - Math.Cos(theta * Math.PI / 180)) / (1 - Math.Cos(89 * Math.PI / 180)); double first = 0, second = 0, maxPdfError = 0; int aboveHalf = 0;
        double Delta(double source) => -Math.Tan(theta * Math.PI / 180) + Math.Tan(source * Math.PI / 180);
        for (var i = 0; i < count; i++)
        {
            var u = rng.NextDouble(); var s = model.Sample(c, u, rng.NextDouble()); var mu = Math.Abs(s.Direction.Z); var source = u < lowerWeight ? 89d : 0d;
            var slopeX = s.Direction.X / mu - Delta(source); var slopeY = s.Direction.Y / mu; var recoveredMu = 1 / Math.Sqrt(1 + slopeX * slopeX + slopeY * slopeY);
            first += recoveredMu; second += recoveredMu * recoveredMu; if (recoveredMu > .5) aboveHalf++;
            double Component(double angle) { var x = s.Direction.X / mu - Delta(angle); var y = s.Direction.Y / mu; var z = 1 / Math.Sqrt(1 + x * x + y * y); return .6 / Math.PI * Math.Pow(z / mu, 4); }
            var expected = lowerWeight * Component(89) + (1 - lowerWeight) * Component(0); var relative = Math.Abs(s.Value / expected - 1); maxPdfError = Math.Max(maxPdfError, relative);
            Assert.InRange(relative, 0, 2e-10); Assert.Equal(.6, s.Value * mu / s.Pdf, 11);
        }
        Assert.InRange(Math.Abs(first / count - 2d / 3), 0, 6 * Math.Sqrt((.5 - 4d / 9) / count));
        Assert.InRange(Math.Abs(second / count - .5), 0, 6 * Math.Sqrt((1d / 3 - .25) / count));
        Assert.InRange(Math.Abs(aboveHalf / (double)count - .75), 0, 6 * Math.Sqrt(.75 * .25 / count));
        SpeosV11XyzTests.Record("slope-statistics-" + transmission + "-" + theta, new { Count = count, Seed = 71289, ExpectedFirst = 2d / 3, ObservedFirst = first / count, ExpectedSecond = .5, ObservedSecond = second / count, AboveHalf = aboveHalf, MaxDensityRelativeError = maxPdfError, Weight = .6 });
    }
    [Fact]
    public void FixedMaterialAxisRotatesTheCompleteSampleAndPdf()
    {
        var data = PowerControl(false, true); var model = new MeasuredBsdf(data); var a = 37 * Math.PI / 180; var c = SpeosV11XyzTests.Context(45); var rotated = SpeosV11XyzTests.Context(45, phi: 37, axis: new(Math.Cos(a), Math.Sin(a), 0));
        var rng = new Random(3187); for (var i = 0; i < 512; i++) { var u = rng.NextDouble(); var v = rng.NextDouble(); var s = model.Sample(c, u, v); var t = model.Sample(rotated, u, v); Assert.Equal(s.Direction.X * Math.Cos(a) - s.Direction.Y * Math.Sin(a), t.Direction.X, 10); Assert.Equal(s.Direction.X * Math.Sin(a) + s.Direction.Y * Math.Cos(a), t.Direction.Y, 10); Assert.Equal(s.Direction.Z, t.Direction.Z, 10); Assert.InRange(Math.Abs(s.Pdf / t.Pdf - 1), 0, 1e-10); }
    }
    [Fact]
    public void WhiteSpecularRejectsNonpassiveSpectralCombination()
    {
        var prototype = PowerControl(false); var points = prototype.Points.Select(p => p with { BtdfPerSteradian = .8 / Math.PI }).ToArray();
        var peak = prototype.SpeosPeak! with { WhiteReferenceNanometers = 400, Points = prototype.SpeosPeak.Points.Select(p => p with { BrdfPerSteradian = .8 / Math.PI }).ToArray() };
        var spectra = new SpeosSpectralDefinition([new(false, new[] { 400d, 700 }, new[] { .8, .1 }, .8), new(true, new[] { 400d, 700 }, new[] { .1, .8 }, .8)]);
        Assert.Throws<InvalidDataException>(() => new MeasuredBsdfData(prototype.Name, prototype.SourceFileName, prototype.SourceSha256, points, sourceFormat: "AnalyticControl", speosPeak: peak, speosSpectral: spectra));
    }
}
