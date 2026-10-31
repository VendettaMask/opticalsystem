using System.Text;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class SpeosV11BinaryTests
{
    internal static byte[] Binary(bool enhanced = false, bool white = false)
    {
        using var output = new MemoryStream(); output.Write(Encoding.ASCII.GetBytes("OPTIS - Anisotropic BSDF surface file v11.0\n1")); using var w = new BinaryWriter(output, Encoding.UTF8, true);
        w.Write(3); w.Write((byte)1); w.Write(0); w.Write((byte)0);
        void Packed(byte[] raw)
        {
            using var buffer = new MemoryStream(); buffer.WriteByte((byte)(Math.Min(raw.Length, 15) << 4));
            if (raw.Length >= 15) { var n = raw.Length - 15; while (n >= 255) { buffer.WriteByte(255); n -= 255; } buffer.WriteByte((byte)n); } buffer.Write(raw);
            w.Write((long)raw.Length); if (raw.Length > 0) { w.Write(raw.Length); w.Write((int)buffer.Length); w.Write(buffer.ToArray()); }
        }
        void Values(params double[] values) { using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, Encoding.UTF8, true)) foreach (var v in values) writer.Write(v); Packed(memory.ToArray()); }
        w.Write(0); w.Write(0); w.Write(1d); w.Write(0d); w.Write(0d); w.Write((byte)1); w.Write((byte)0); w.Write(1d); w.Write(1d); w.Write((byte)0); w.Write((byte)(enhanced ? 1 : 0)); w.Write(1 / Math.PI);
        w.Write(1); Values(0); w.Write(3); Values(0, 30, 60);
        w.Write(0d); w.Write(0d); w.Write(0); w.Write(2); w.Write(400d); w.Write(700d); w.Write(20d); w.Write(40d);
        for (var i = 0; i < 3; i++) { w.Write(3); w.Write(5); Values(0, 60, 90); Values(0, 90, 180, 270, 360); Values(1, 1, 1, 1, 1, .5, .5, .5, .5, .5, 0, 0, 0, 0, 0); }
        for (var i = 0; i < 3; i++) w.Write(.5); for (var i = 0; i < 3; i++) w.Write(.1);
        w.Write((byte)(white ? 1 : 0)); w.Write(550d); return output.ToArray();
    }
    [Theory] [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public void V11ArchiveAndConeTablesRoundTrip(bool enhanced, bool white)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("synthetic-v11.anisotropicbsdf"); var raw = Binary(enhanced, white); File.WriteAllBytes(path, raw);
        var data = BsdfFileImporter.Read(path, BinaryZemaxBsdfTests.Options); Assert.Equal("SpeosAnisotropicBsdfBinary11Archive3/1", data.SourceFormat); Assert.NotNull(data.SpeosSpectral); Assert.Equal(enhanced, data.SpeosPeak is not null);
        var model = new MeasuredBsdf(data); Assert.InRange(model.Integrate(SpeosV11XyzTests.Context()).Total, 0, .5);
        if (!white) Assert.Equal(.3, model.Integrate(SpeosV11XyzTests.Context()).Total, 12);
        SpeosV11XyzTests.Record("v11-binary-" + enhanced + "-" + white, new { data.SourceSha256, data.DataSha256, data.PayloadPointCount, Energy = model.Integrate(SpeosV11XyzTests.Context()) });
    }
    [Fact]
    public void UnknownArchiveVariantAndTruncationAreRejected()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("bad.anisotropicbsdf"); var raw = Binary(true, true);
        foreach (var variant in new[] { raw[..^1], raw.Concat(new byte[] { 0 }).ToArray(), raw.ToArray() })
        { if (variant.Length == raw.Length) variant[Array.IndexOf(variant, (byte)10) + 6] = 0; File.WriteAllBytes(path, variant); Assert.Throws<InvalidDataException>(() => BsdfFileImporter.Read(path, BinaryZemaxBsdfTests.Options)); }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void InstalledV11MaterialRetainsItsFullSpectrumWithoutExpansion(bool withoutAuxiliary)
    {
        const string path = @"D:\Program Files\ANSYS Inc\v261\Optical Products\OpticalLibraries\Optical_Property\volume_optical_properties\plastic\covestro\material_plastic_covestro_makrolon_opaque_reflector\Covestro_Makrolon_RW2407_010226.anisotropicbsdf";
        // Local corpus is optional on other machines; deterministic archive tests above are always run.
        var selected = withoutAuxiliary ? @"D:\Program Files\ANSYS Inc\v261\Optical Products\OpticalLibraries\Optical_Property\surface_optical_properties\plastic\surface_covestro_rw2405\Materials\COVESTRO_RW2405-012797.anisotropicbsdf" : path;
        if (!File.Exists(selected)) return;
        var data = BsdfFileImporter.Read(selected, new(BsdfImportSides.Front)); Assert.Equal(withoutAuxiliary ? 81 : 125, data.SpeosSpectral!.Lobes.Single().Wavelengths.Count); Assert.True(data.Points.Count < 65536);
        var model = new MeasuredBsdf(data); var e = model.Integrate(SpeosV11XyzTests.Context(30)); Assert.InRange(e.Total, 0, 1);
        SpeosV11XyzTests.Record("installed-v11-" + withoutAuxiliary, new { Path = selected, data.SourceSha256, data.DataSha256, data.PayloadPointCount, SpectrumSamples = withoutAuxiliary ? 81 : 125, At30Degrees550Nm = e });
    }
}
