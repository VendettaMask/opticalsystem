using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class MeasuredBsdfTests
{
    internal static MeasuredBsdfData Data(Func<double, double, double, double, (double R, double T)> function,
        double[]? wavelengths = null, double[]? incidences = null)
    {
        var points = new List<MeasuredBsdfPoint>();
        foreach (var side in Enum.GetValues<BsdfSide>()) foreach (var w in wavelengths ?? [400d, 700d])
        foreach (var i in incidences ?? [0d, 60d, 90d]) foreach (var o in new[] { 0d, 45d, 90d })
        foreach (var phi in new[] { 0d, 90d, 180d, 270d, 360d })
        {
            var muI = i == 90 ? 0 : Math.Cos(i * Math.PI / 180); var mu = o == 90 ? 0 : Math.Cos(o * Math.PI / 180);
            var values = function(w, muI, mu, phi); points.Add(new(side, w, i, o, phi, values.R, values.T));
        }
        return new("Analytical synthetic control", "control.csv", new string('a', 64), points);
    }
    internal static MeasuredBsdfData Constant(double r, double t) => Data((_, _, _, _) => (r / Math.PI, t / Math.PI));
    internal static string Csv(MeasuredBsdfData data)
    {
        var text = new StringBuilder("# OpticalSystemDesign.MeasuredBsdfGrid/1\n# name=Analytical synthetic control\n" + MeasuredBsdfCsv.Header + "\n");
        foreach (var p in data.Points)
            text.AppendLine(string.Join(',', p.Side.ToString(), p.WavelengthNanometers.ToString("G17", CultureInfo.InvariantCulture),
                p.IncidentThetaDegrees.ToString("G17", CultureInfo.InvariantCulture), p.OutgoingThetaDegrees.ToString("G17", CultureInfo.InvariantCulture),
                p.OutgoingPhiDegrees.ToString("G17", CultureInfo.InvariantCulture), p.BrdfPerSteradian.ToString("G17", CultureInfo.InvariantCulture),
                p.BtdfPerSteradian.ToString("G17", CultureInfo.InvariantCulture)));
        return text.ToString();
    }
    internal static Vector3D Direction(double mu, double phi = 0) => new(Math.Sqrt(1 - mu * mu) * Math.Cos(phi), Math.Sqrt(1 - mu * mu) * Math.Sin(phi), mu);
    private static MeasuredBsdfData Directional() => Data((w, mi, mu, phi) =>
    {
        var g = phi switch { 0 or 360 => 1d, 180 => -1d, _ => 0d };
        var a = .06 + .02 * ((w - 400) / 300) + .01 * mi;
        return ((a + .09 * mu + .02 * (1 - mi) * (1 - mu) * g) / Math.PI, (.25 + .1 * mi + .15 * mu) / Math.PI);
    });

    [Fact]
    public void IndependentOffGridValuesEnergyAndSolidAnglePdfMatchAnalyticalIntegrals()
    {
        var model = new MeasuredBsdf(Directional()); var c = new BsdfContext(Direction(.5), new(0, 0, 1), 550);
        Assert.Equal(.1055 / Math.PI, model.Evaluate(c, Direction(.3, Math.PI / 4)), 13);
        Assert.Equal(.135, model.Integrate(c).Reflection, 13); Assert.Equal(.4, model.Integrate(c).Transmission, 13);
        double integral = 0, density = 0;
        // Independent two-node Gauss integration in mu, midpoint integration in each piecewise-linear azimuth interval.
        foreach (var sign in new[] { -1, 1 }) foreach (var mu in new[] { .5 - .5 / Math.Sqrt(3), .5 + .5 / Math.Sqrt(3) })
        for (var a = 0; a < 4; a++)
        {
            var direction = Direction(sign * mu, (a + .5) * Math.PI / 2);
            integral += model.Evaluate(c, direction) * mu * .5 * Math.PI / 2;
            density += model.Pdf(c, direction) * .5 * Math.PI / 2;
        }
        Assert.Equal(.535, integral, 13); Assert.Equal(1, density, 13);
        Assert.Equal(model.Evaluate(c, Direction(.3, Math.PI / 4)), model.Evaluate(c, Direction(.3, Math.PI / 4 + 2 * Math.PI)), 13);
        var original = Directional();
        var asymmetric = new MeasuredBsdf(new MeasuredBsdfData(original.Name, original.SourceFileName, original.SourceSha256,
            original.Points.Select(p => p.Side == BsdfSide.Back ? p with { BrdfPerSteradian = p.BrdfPerSteradian / 2,
                BtdfPerSteradian = p.BtdfPerSteradian / 4 } : p).ToArray()));
        var back = new BsdfContext(c.IncomingDirection, c.SurfaceNormal, 550, BsdfSide.Back);
        Assert.Equal(.0675, asymmetric.Integrate(back).Reflection, 13); Assert.Equal(.1, asymmetric.Integrate(back).Transmission, 13);
        Assert.Equal(.135, asymmetric.Integrate(c).Reflection, 13); Assert.Equal(.4, asymmetric.Integrate(c).Transmission, 13);
    }

    [Fact]
    public void JointImportanceSamplesMatchReflectionTransmissionCdfMomentsAndAzimuthMass()
    {
        var model = new MeasuredBsdf(Directional()); var c = new BsdfContext(Direction(.5), new(0, 0, 1), 550);
        var random = new Random(3081); const int n = 32768; int reflected = 0, positiveXReflection = 0; var cdf = new int[9]; double first = 0, second = 0;
        for (var i = 0; i < n; i++)
        {
            var sample = model.Sample(c, random.NextDouble(), random.NextDouble()); var mu = Math.Abs(sample.Direction.Z);
            Assert.True(sample.Pdf > 0); Assert.Equal(model.Pdf(c, sample.Direction), sample.Pdf);
            Assert.InRange(Math.Abs(sample.Value * mu / sample.Pdf - .535), 0, 3e-14);
            Assert.Equal(sample.Lobe == BsdfLobe.DiffuseTransmission, sample.Direction.Z < 0);
            if (sample.Lobe == BsdfLobe.DiffuseReflection) { reflected++; if (sample.Direction.X > 0) positiveXReflection++; }
            first += mu; second += mu * mu;
            for (var k = 1; k < 9; k++) if (mu <= k / 10d) cdf[k]++;
        }
        var pR = .135 / .535;
        Assert.InRange(Math.Abs(reflected / (double)n - pR), 0, 6 * Math.Sqrt(pR * (1 - pR) / n));
        var expectedMean = .37 / .535; var expectedSecond = .2835 / .535;
        var expectedFourth = (.75 / 6 + .48 / 7) / .535;
        Assert.InRange(Math.Abs(first / n - expectedMean), 0, 6 * Math.Sqrt((expectedSecond - expectedMean * expectedMean) / n));
        Assert.InRange(Math.Abs(second / n - expectedSecond), 0, 6 * Math.Sqrt((expectedFourth - expectedSecond * expectedSecond) / n));
        for (var k = 1; k < 9; k++)
        {
            var z = k / 10d; var p = (.375 * z * z + .16 * z * z * z) / .535;
            Assert.InRange(Math.Abs(cdf[k] / (double)n - p), 0, 6 * Math.Sqrt(p * (1 - p) / n));
        }
        // Integral of triangular cos-azimuth over the positive-X half-plane is pi/2; radial integral mu(1-mu)=1/6.
        var expectedPositiveX = (.135 / 2 + .01 / 12) / .535;
        Assert.InRange(Math.Abs(positiveXReflection / (double)n - expectedPositiveX), 0, 6 * Math.Sqrt(expectedPositiveX * (1 - expectedPositiveX) / n));
        Record("distribution", new { Count = n, Seed = 3081, Reflected = reflected, Transmitted = n - reflected,
            MeanAbsCosine = first / n, CosineSecondMoment = second / n, CdfCounts = cdf, PositiveXReflection = positiveXReflection });
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(.2, 0)] [InlineData(0, 1)] [InlineData(.2, .5)]
    public void PureAndMixedLobesEndpointsAndRotatedFramesHaveConsistentWeights(double r, double t)
    {
        var model = new MeasuredBsdf(Constant(r, t)); var normal = new Vector3D(.3, -.4, .8660254037844386);
        var context = new BsdfContext(normal, normal, 550, BsdfSide.Back);
        foreach (var u in new[] { 0d, .1, .5, Math.BitDecrement(1d) })
        {
            var sample = model.Sample(context, u, u); var unit = sample.Direction / sample.Direction.Length;
            var mu = Math.Abs(unit.X * context.SurfaceNormal.X + unit.Y * context.SurfaceNormal.Y + unit.Z * context.SurfaceNormal.Z);
            Assert.True(double.IsFinite(sample.Pdf) && sample.Pdf > 0);
            Assert.InRange(Math.Abs(sample.Value * mu / sample.Pdf - r - t), 0, 3e-14);
        }
    }

    [Fact]
    public void CsvImportFreezesSortedDataAndBindsOriginalBytesAndCanonicalValues()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("measured.csv");
        var original = Directional(); File.WriteAllText(path, Csv(original)); var imported = MeasuredBsdfCsv.Read(path);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), imported.SourceSha256);
        Assert.Equal(original.DataSha256, imported.DataSha256); Assert.Equal(original.Points, imported.Points);
        Assert.Throws<NotSupportedException>(() => ((IList<MeasuredBsdfPoint>)imported.Points)[0] = imported.Points[0]);
        Assert.Throws<InvalidDataException>(() => new MeasuredBsdfData(imported.Name, imported.SourceFileName, imported.SourceSha256, imported.Points, new string('0', 64)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => MeasuredBsdfCsv.Read(path, cancel.Token));
    }

    [Fact]
    public void NonuniformNarrowTransmissionLobeAllowsLargeSrValuesAndSamplesOnlyItsMeasuredSupport()
    {
        var points = new List<MeasuredBsdfPoint>();
        foreach (var side in Enum.GetValues<BsdfSide>()) foreach (var theta in new[] { 0d, 1d, 2d, 90d })
        foreach (var phi in new[] { 0d, 40d, 180d, 360d })
            points.Add(new(side, 550, 0, theta, phi, 0, theta == 1 ? 100 : 0));
        var data = new MeasuredBsdfData("Synthetic narrow lobe", "narrow.csv", new string('b', 64), points);
        var model = new MeasuredBsdf(data); var context = new BsdfContext(new(0, 0, 1), new(0, 0, 1), 550);
        var lo = Math.Cos(2 * Math.PI / 180); var mid = Math.Cos(Math.PI / 180);
        var exact = 2 * Math.PI * 100 * (1 - lo) * (lo + mid + 1) / 6;
        Assert.Equal(exact, model.Integrate(context).Transmission, 12);
        var random = new Random(104); double minimum = 1, maximumPdf = 0;
        for (var i = 0; i < 4096; i++)
        {
            var sample = model.Sample(context, random.NextDouble(), random.NextDouble()); var mu = -sample.Direction.Z;
            Assert.Equal(BsdfLobe.DiffuseTransmission, sample.Lobe); Assert.InRange(mu, lo - 1e-14, 1);
            Assert.InRange(Math.Abs(sample.Value * mu / sample.Pdf - exact), 0, 1e-12);
            minimum = Math.Min(minimum, mu); maximumPdf = Math.Max(maximumPdf, sample.Pdf);
        }
        Assert.True(maximumPdf > 100);
        Record("narrow-lobe", new { Count = 4096, IntegratedTransmission = exact, MinimumAbsCosine = minimum, MaximumPdf = maximumPdf, PeakBtdfPerSteradian = 100 });
    }

    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("negative")] [InlineData("active")] [InlineData("seam")] [InlineData("pole")]
    public void InvalidMeasuredGridsAreRejectedWithoutRepairOrNormalization(string defect)
    {
        var data = Constant(.2, .5); var points = data.Points.ToList();
        if (defect == "missing") points.RemoveAt(0);
        if (defect == "duplicate") points[0] = points[1];
        if (defect == "negative") points[0] = points[0] with { BrdfPerSteradian = -.1 };
        if (defect == "active") points = points.Select(p => p with { BrdfPerSteradian = 2 / Math.PI }).ToList();
        if (defect == "seam") points[0] = points[0] with { BrdfPerSteradian = .1 };
        if (defect == "pole") points[1] = points[1] with { BrdfPerSteradian = .1 };
        Assert.Throws<InvalidDataException>(() => new MeasuredBsdfData(data.Name, data.SourceFileName, data.SourceSha256, points));
    }

    [Fact]
    public void UnsupportedFilesRangesAndSidesFailExplicitly()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("bad.csv"); File.WriteAllText(path, "unknown format");
        Assert.Throws<InvalidDataException>(() => MeasuredBsdfCsv.Read(path));
        File.WriteAllText(path, "# OpticalSystemDesign.MeasuredBsdfGrid/1\n" + MeasuredBsdfCsv.Header + "\n" + new string(',', 100000));
        Assert.Throws<InvalidDataException>(() => MeasuredBsdfCsv.Read(path));
        var data = Data((_, _, _, _) => (.2 / Math.PI, 0), [550], [0]); var model = new MeasuredBsdf(data);
        Assert.Throws<InvalidDataException>(() => model.Integrate(new(new(0, 0, 1), new(0, 0, 1), 600)));
        Assert.Throws<InvalidDataException>(() => model.Integrate(new(Direction(.5), new(0, 0, 1), 550)));
        var front = new MeasuredBsdfData(data.Name, data.SourceFileName, data.SourceSha256, data.Points.Where(p => p.Side == BsdfSide.Front).ToArray());
        Assert.Throws<InvalidDataException>(() => new MeasuredBsdf(front).Integrate(new(new(0, 0, 1), new(0, 0, 1), 550, BsdfSide.Back)));
    }
    internal static void Record(string name, object data)
    {
        var dir = Environment.GetEnvironmentVariable("OPTICAL_MEASURED_BSDF_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, name + ".json"), System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
