using System.Globalization;
using System.IO.Compression;
using System.Text;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class BinaryZemaxBsdfTests
{
    internal static byte[] BinarySpeos()
    {
        using var stream = new MemoryStream(); stream.Write(Encoding.ASCII.GetBytes("OPTIS - brdf surface file v8.0\n1"));
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true); writer.Write(3); writer.Write(1); writer.Write((byte)0);
        void Packed(byte[] values)
        { using var compressed = new MemoryStream(); using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) z.Write(values); writer.Write(values.Length); writer.Write(checked((int)compressed.Length)); writer.Write(compressed.ToArray()); }
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); Packed(bytes); }
        void Values(double[] values) { using var b = new MemoryStream(); using (var w = new BinaryWriter(b, Encoding.UTF8, true)) foreach (var v in values) w.Write(v); Packed(b.ToArray()); }
        Text("Synthetic archive-3 binary control"); Text("Synthetic R/T, not measured"); writer.Write((byte)1); writer.Write((byte)1); writer.Write((byte)0); writer.Write((byte)0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(3); writer.Write(2); Values([0, 60, 90]); Values([400, 700]); Values([.2, .3, .2, .3, .2, .3]); Values([.5, .4, .5, .4, .5, .4]);
        for (var lobe = 0; lobe < 2; lobe++) for (var i = 0; i < 3; i++) for (var w = 0; w < 2; w++)
        { writer.Write(3); writer.Write(5); Values(lobe == 0 ? [0, 60, 90] : [90, 120, 180]); Values([0, 90, 180, 270, 360]); Values((lobe == 0 ? new[] { 3d, 1.5, 0 } : new[] { 0d, 1.5, 3 }).SelectMany(v => Enumerable.Repeat(v, 5)).ToArray()); }
        for (var lobe = 0; lobe < 2; lobe++) for (var aux = 0; aux < 2; aux++) Values(Enumerable.Repeat(1d, 6).ToArray());
        writer.Write(Encoding.ASCII.GetBytes("End of file\n"));
        return stream.ToArray();
    }
    internal static string Zemax(bool transmission = false, bool anisotropic = false, double[]? incidences = null, double radialMaximum = 180, double fraction = .6)
    {
        string N(double x) => x.ToString("G17", CultureInfo.InvariantCulture);
        var text = new StringBuilder("# Synthetic radiometric control, not measured\nSource Measured\nSymmetry " + (anisotropic ? "Asymmetrical4D" : "PlaneSymmetrical") + "\nSpectralContent Monochrome\nScatterType " + (transmission ? "BTDF" : "BRDF") + "\n");
        var rotations = anisotropic ? new[] { 0d, 90d, 180d, 270d, 360d } : [0d]; incidences ??= [0, 30, 60, 90];
        var azimuths = anisotropic ? new[] { 0d, 90d, 180d, 270d, 360d } : [0d, 90d, 180d];
        var radial = radialMaximum == 180 ? new[] { 0d, 60d, 90d, 120d, 180d } : [0d, radialMaximum];
        void Axis(string key, double[] a) { text.AppendLine(key + " " + a.Length); text.AppendLine(string.Join(" ", a.Select(N))); }
        Axis("SampleRotation", rotations); Axis("AngleOfIncidence", incidences); Axis("ScatterAzimuth", azimuths); Axis("ScatterRadial", radial);
        text.AppendLine("\nMonochrome\nDataBegin");
        foreach (var rotation in rotations) foreach (var incidence in incidences)
        {
            text.AppendLine("TIS " + N(fraction));
            foreach (var azimuth in azimuths) text.AppendLine(string.Join(" ", radial.Select(_ => "1")));
        }
        text.AppendLine("DataEnd"); return text.ToString();
    }
    internal static byte[] BinaryAnisotropic(int version)
    {
        using var stream = new MemoryStream(); stream.Write(Encoding.ASCII.GetBytes($"OPTIS - Anisotropic BSDF surface file v{version}.0\n1")); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(3); writer.Write((byte)1); writer.Write(0);
        void Packed(byte[] raw)
        {
            using var b = new MemoryStream(); using (var w = new BinaryWriter(b, Encoding.UTF8, true))
            { w.Write((byte)(Math.Min(15, raw.Length) << 4)); if (raw.Length >= 15) { var rest = raw.Length - 15; while (rest >= 255) { w.Write((byte)255); rest -= 255; } w.Write((byte)rest); } w.Write(raw); }
            writer.Write((long)raw.Length); if (raw.Length > 0) { writer.Write(raw.Length); writer.Write(checked((int)b.Length)); writer.Write(b.ToArray()); }
        }
        void Values(double[] values) { using var b = new MemoryStream(); using (var w = new BinaryWriter(b, Encoding.UTF8, true)) foreach (var v in values) w.Write(v); Packed(b.ToArray()); }
        void Text(string value) { var b = Encoding.UTF8.GetBytes(value); writer.Write(b.Length); if (b.Length > 0) Packed(b); }
        Text("Synthetic archive-3 LZ4"); Text("Synthetic R/T"); writer.Write(1d); writer.Write(0d); writer.Write(0d); writer.Write((byte)1); writer.Write((byte)1);
        if (version >= 9) { writer.Write(1d); writer.Write(1d); } writer.Write((byte)0); writer.Write((byte)0);
        for (var lobe = 0; lobe < 2; lobe++) { writer.Write(1); Values([0]); writer.Write(3); Values([0, 60, 90]); }
        for (var lobe = 0; lobe < 2; lobe++) { writer.Write(0d); writer.Write(0d); Text("R/T spectrum"); writer.Write(2); writer.Write(400d); writer.Write(700d); writer.Write(lobe == 0 ? 20d : 50d); writer.Write(lobe == 0 ? 30d : 40d); }
        for (var lobe = 0; lobe < 2; lobe++) for (var i = 0; i < 3; i++)
        { writer.Write(3); writer.Write(5); Values(lobe == 0 ? [0, 60, 90] : [90, 120, 180]); Values([0, 90, 180, 270, 360]); Values((lobe == 0 ? new[] { 3d, 1.5, 0 } : new[] { 0d, 1.5, 3 }).SelectMany(v => Enumerable.Repeat(v, 5)).ToArray()); }
        for (var lobe = 0; lobe < 2; lobe++) for (var aux = 0; aux < (version >= 9 ? 4 : 2); aux++) for (var i = 0; i < 3; i++) writer.Write(1d);
        writer.Write((byte)0); writer.Write(632d); return stream.ToArray();
    }
    internal static BsdfFileImportOptions Options => new(BsdfImportSides.BothDeclaredIdentical, 550, true);
    internal static MeasuredBsdfData Read(string text, BsdfFileImportOptions? options = null)
    { using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("synthetic.BSDF"); File.WriteAllText(path, text); return BsdfFileImporter.Read(path, options ?? Options); }

    [Fact]
    public void BinaryArchiveDecodesBothLobesAndKeepsTheOriginalByteDigest()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("binary.brdf"); var raw = BinarySpeos(); File.WriteAllBytes(path, raw);
        var data = BsdfFileImporter.Read(path, new(BsdfImportSides.BothDeclaredIdentical)); var model = new MeasuredBsdf(data);
        Assert.Equal("SpeosBrdfBinary8Archive3/1", data.SourceFormat); Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)).ToLowerInvariant(), data.SourceSha256);
        Assert.Equal(.25, model.Integrate(CommercialBsdfTests.Context()).Reflection, 12); Assert.Equal(.45, model.Integrate(CommercialBsdfTests.Context()).Transmission, 12);
        Record("binary-import", new { data.SourceFormat, data.SourceSha256, data.DataSha256, Nodes = data.Points.Count });
    }
    [Fact]
    public void OfficialBinaryAnisotropicFixtureDecodesLz4AndItsDeclaredSpectrumReference()
    {
        var data = BsdfFileImporter.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "speos-r-test-v9.anisotropicbsdf"), new(BsdfImportSides.BothDeclaredIdentical));
        Assert.Equal("SpeosAnisotropicBsdfBinary9Archive3/1", data.SourceFormat); Assert.Equal(BsdfStoredQuantity.ProjectedBsdf, data.StoredQuantity);
        var model = new MeasuredBsdf(data); Assert.Equal(.6227, model.Integrate(CommercialBsdfTests.Context(6)).Reflection, 10);
        Record("official-speos-v9", new { data.SourceSha256, data.DataSha256, Nodes = data.Points.Count, AtReference = model.Integrate(CommercialBsdfTests.Context(6)) });
    }
    [Theory]
    [InlineData(7)] [InlineData(9)]
    public void Lz4AnisotropicVersionsDecodeBothSpectraAndTransmissionHemisphere(int version)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("synthetic.anisotropicbsdf"); File.WriteAllBytes(path, BinaryAnisotropic(version));
        var data = BsdfFileImporter.Read(path, new(BsdfImportSides.BothDeclaredIdentical)); var model = new MeasuredBsdf(data);
        Assert.Equal($"SpeosAnisotropicBsdfBinary{version}Archive3/1", data.SourceFormat); Assert.True(data.HasTransmission);
        Assert.Equal(.25, model.Integrate(CommercialBsdfTests.Context()).Reflection, 12); Assert.Equal(.45, model.Integrate(CommercialBsdfTests.Context()).Transmission, 12);
        Assert.Equal(.25 / Math.PI, model.Evaluate(CommercialBsdfTests.Context(30), new(.8, 0, .6)), 12);
        Assert.Equal(.45 / Math.PI, model.Evaluate(CommercialBsdfTests.Context(30), new(.8, 0, -.6)), 12);
    }
    [Theory]
    [InlineData("truncate")] [InlineData("checksum")] [InlineData("size")] [InlineData("version")]
    public void CorruptBinaryFilesFailWithoutAllocatingFromUntrustedSizes(string defect)
    {
        var raw = BinarySpeos();
        if (defect == "truncate") raw = raw[..(raw.Length / 2)];
        if (defect == "version") raw[32] = 77;
        if (defect == "size") { var at = Array.IndexOf(raw, (byte)0x78); System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(at - 8, 4), int.MaxValue); }
        if (defect == "checksum") { var at = Array.IndexOf(raw, (byte)0x78); raw[at + 6] ^= 0x77; }
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("bad.brdf"); File.WriteAllBytes(path, raw);
        Assert.ThrowsAny<InvalidDataException>(() => BsdfFileImporter.Read(path, new(BsdfImportSides.BothDeclaredIdentical)));
    }
    [Theory]
    [InlineData("lz4-offset")] [InlineData("enhancement")] [InlineData("white-specular")]
    public void UnsupportedBinaryPhysicsAndMalformedLz4AreNotSilentlyIgnored(string defect)
    {
        var raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "speos-r-test-v9.anisotropicbsdf"));
        if (defect == "lz4-offset") raw[125] = 0; if (defect == "enhancement") raw[104] = 1; if (defect == "white-specular") raw[^9] = 1;
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("bad.anisotropicbsdf"); File.WriteAllBytes(path, raw);
        Assert.Throws<InvalidDataException>(() => BsdfFileImporter.Read(path, new(BsdfImportSides.BothDeclaredIdentical)));
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void SpecularFramesIntegrateAConstantPhysicalBsdfAtObliqueAndGrazingIncidence(bool transmission, bool anisotropic)
    {
        var data = Read(Zemax(transmission, anisotropic)); var model = new MeasuredBsdf(data);
        Assert.Equal(MeasuredBsdfData.SpecularProtocol, model.Kind);
        foreach (var theta in new[] { 0d, 17d, 30d, 59d, 89d, 90d })
        {
            var c = CommercialBsdfTests.Context(theta, 35); var energy = model.Integrate(c); Assert.Equal(.6, energy.Total, 12);
            var sign = transmission ? -1 : 1;
            foreach (var x in new[] { -.8, 0, .8 }) Assert.Equal(.6 / Math.PI, model.Evaluate(c, new(x, 0, sign * Math.Sqrt(1 - x * x))), 8);
            Assert.Equal(0, model.Evaluate(c, new(0, 0, -sign)));
        }
    }
    [Fact]
    public void NarrowSpecularCapRetainsItsWidthAndMatchesIndependentProjectedMoments()
    {
        var model = new MeasuredBsdf(Read(Zemax(incidences: [30], radialMaximum: 5))); var c = CommercialBsdfTests.Context(30); var s = new Vector3D(-.5, 0, Math.Sqrt(.75)); var e = new Vector3D(Math.Sqrt(.75), 0, .5);
        var cosine = Math.Cos(Math.PI / 36); var integral = Math.PI * Math.Sqrt(.75) * (1 - cosine * cosine);
        Assert.Equal(.6 / integral, model.Evaluate(c, s), 7); Assert.Equal(0, model.Evaluate(c, new(0, 0, 1)));
        var random = new Random(8317); const int count = 8192; double meanAlong = 0, meanTowardNormal = 0;
        for (var k = 0; k < count; k++)
        {
            var sample = model.Sample(c, random.NextDouble(), random.NextDouble()); var along = sample.Direction.X * s.X + sample.Direction.Z * s.Z;
            Assert.InRange(along, cosine - 1e-13, 1); Assert.InRange(Math.Abs(sample.Value * Math.Abs(sample.Direction.Z) / sample.Pdf - .6), 0, 1e-13);
            meanAlong += along; meanTowardNormal += sample.Direction.X * e.X + sample.Direction.Z * e.Z;
        }
        var expectedAlong = 2 * (1 - cosine * cosine * cosine) / (3 * (1 - cosine * cosine));
        var expectedToward = Math.Tan(Math.PI / 6) * (1 - cosine) * (cosine + 2) / (3 * (1 + cosine));
        Assert.InRange(Math.Abs(meanAlong / count - expectedAlong), 0, 6 * (1 - cosine) / Math.Sqrt(count));
        Assert.InRange(Math.Abs(meanTowardNormal / count - expectedToward), 0, 6 * Math.Sin(Math.PI / 36) / Math.Sqrt(count));
        Record("narrow-specular-statistics", new { Count = count, Seed = 8317, MeanAlong = meanAlong / count, ExpectedAlong = expectedAlong, MeanTowardNormal = meanTowardNormal / count, ExpectedTowardNormal = expectedToward, CapDegrees = 5 });
    }
    [Fact]
    public void BtdfUsesPostRefractionAnglesAndRejectsIncompatibleSceneIndices()
    {
        var data = Read(Zemax(true, incidences: [0, 30]), Options with { IncidentIndex = 1, TransmittedIndex = 1.5 }); var model = new MeasuredBsdf(data);
        var thetaI = Math.Asin(.75); var c = new BsdfContext(new(Math.Sin(thetaI), 0, Math.Cos(thetaI)), new(0, 0, 1), 550, materialXAxis: new(1, 0, 0), incidentRefractiveIndex: 1, transmittedRefractiveIndex: 1.5);
        Assert.Equal(.6, model.Integrate(c).Transmission, 12); Assert.Equal(.6 / Math.PI, model.Evaluate(c, new(0, 0, -1)), 8);
        Assert.Throws<InvalidDataException>(() => model.Integrate(CommercialBsdfTests.Context()));
        Assert.Throws<InvalidDataException>(() => Read(Zemax(true), Options with { IncidentIndex = 1, TransmittedIndex = 1.5 }));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GrazingProjectionSamplesThePhysicalHemisphereAndAnalyticalMoments(bool transmission)
    {
        var model = new MeasuredBsdf(Read(Zemax(transmission, incidences: [85]))); var context = CommercialBsdfTests.Context(85); var random = new Random(9107); const int count = 8192; double cosine = 0, x = 0, y = 0;
        for (var k = 0; k < count; k++)
        {
            var s = model.Sample(context, random.NextDouble(), random.NextDouble()); var mu = Math.Abs(s.Direction.Z);
            Assert.True(transmission ? s.Direction.Z < 0 : s.Direction.Z > 0); Assert.InRange(Math.Abs(s.Value * mu / s.Pdf - .6), 0, 1e-13);
            cosine += mu; x += s.Direction.X; y += s.Direction.Y;
        }
        Assert.InRange(Math.Abs(cosine / count - 2d / 3), 0, 6 * Math.Sqrt(1d / (18 * count)));
        Assert.InRange(Math.Abs(x / count), 0, 6 * Math.Sqrt(.25 / count)); Assert.InRange(Math.Abs(y / count), 0, 6 * Math.Sqrt(.25 / count));
        Record("grazing-statistics-" + transmission, new { Count = count, Seed = 9107, MeanAbsCosine = cosine / count, ExpectedAbsCosine = 2d / 3, MeanX = x / count, MeanY = y / count });
    }
    [Fact]
    public void AnisotropicSpecularCapsRotateWithTheMaterialFrameWithoutRasterization()
    {
        var model = new MeasuredBsdf(Read(Zemax(anisotropic: true, incidences: [30], radialMaximum: 5)));
        var a = CommercialBsdfTests.Context(30, 90); var b = CommercialBsdfTests.Context(30, 180, new(0, 1, 0)); var random = new Random(721);
        for (var k = 0; k < 256; k++)
        {
            var u = random.NextDouble(); var v = random.NextDouble(); var x = model.Sample(a, u, v); var y = model.Sample(b, u, v);
            Assert.InRange((new Vector3D(-x.Direction.Y, x.Direction.X, x.Direction.Z) - y.Direction).Length, 0, 2e-12); Assert.Equal(x.Value, y.Value, 8);
        }
    }
    [Theory]
    [InlineData("wavelength")] [InlineData("photometric")] [InlineData("xyz")] [InlineData("negative")] [InlineData("active")] [InlineData("short")] [InlineData("sides")] [InlineData("normal-gap")]
    public void MissingDeclarationsAndMalformedZemaxDataFailExplicitly(string defect)
    {
        var text = Zemax(); var options = Options;
        if (defect == "wavelength") options = options with { ZemaxWavelengthNanometers = null };
        if (defect == "photometric") options = options with { ZemaxRadiometric = false };
        if (defect == "xyz") text = text.Replace("SpectralContent Monochrome", "SpectralContent XYZ");
        if (defect == "normal-gap") text = text.Replace("Symmetry PlaneSymmetrical", "Symmetry Asymmetrical").Replace("0 90 180" + Environment.NewLine, "0 90 350" + Environment.NewLine);
        if (defect == "negative") text = text.Replace("1 1 1 1 1", "-1 1 1 1 1");
        if (defect == "active") text = text.Replace("TIS 0.59999999999999998", "TIS 1.1");
        if (defect == "short") text = text[..(text.Length / 2)]; if (defect == "sides") options = options with { Sides = BsdfImportSides.FileDefined };
        Assert.Throws<InvalidDataException>(() => Read(text, options));
    }
    internal static void Record(string name, object value)
    { var dir = Environment.GetEnvironmentVariable("OPTICAL_BINARY_ZEMAX_BSDF_EVIDENCE_DIR"); if (string.IsNullOrWhiteSpace(dir)) return; Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, name + ".json"), System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); }
}
