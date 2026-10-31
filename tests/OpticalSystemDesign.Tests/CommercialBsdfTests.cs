using System.Globalization;
using System.Text;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class CommercialBsdfTests
{
    internal static double G(double phi) { phi = (phi % 360 + 360) % 360; return phi switch { 0 => 1, 90 or 270 => 0, 180 => -1, _ => throw new ArgumentException() }; }
    internal static MeasuredBsdfData FixedData()
    {
        var points = new List<MeasuredBsdfPoint>();
        foreach (var side in Enum.GetValues<BsdfSide>()) foreach (var w in new[] { 400d, 700d }) foreach (var ti in new[] { 0d, 60d, 90d })
        foreach (var pi in new[] { 0d, 90d, 180d, 270d, 360d }) foreach (var to in new[] { 0d, 60d, 90d }) foreach (var po in new[] { 0d, 90d, 180d, 270d, 360d })
        {
            var mi = ti == 90 ? 0 : Math.Cos(ti * Math.PI / 180); var mu = to == 90 ? 0 : Math.Cos(to * Math.PI / 180);
            var factor = (1 + .1 * (1 - mi) * Math.Cos(2 * pi * Math.PI / 180)) * (1 + .8 * (1 - mu) * G(po));
            var r = .2 + .1 * (w - 400) / 300; var t = .5 - .1 * (w - 400) / 300;
            points.Add(new(side, w, ti, to, po, r * factor / Math.PI, t * factor / Math.PI, pi));
        }
        return new("Synthetic fixed material axes", "fixed.csv", new string('d', 64), points, coordinates: BsdfCoordinates.FixedMaterialAxes, sourceFormat: "NativeCsvFixedAxes/2");
    }
    internal static string Speos(bool anisotropic, double[]? materialRotations = null, bool constantAzimuth = false, double referenceTheta = 0)
    {
        var text = new StringBuilder(); var description = "Synthetic Speos v8 format control";
        void Line(string s) => text.AppendLine(s);
        string N(double d) => d.ToString("G17", CultureInfo.InvariantCulture);
        Line(anisotropic ? "OPTIS - Anisotropic BSDF surface file v8.0" : "OPTIS - BRDF surface file v8.0");
        Line("0"); Line("# Synthetic analytical data, not measured"); Line(description.Length.ToString(CultureInfo.InvariantCulture)); Line(description);
        var rotations = materialRotations ?? [0d, 90d, 180d, 270d, 360d]; var incidences = new[] { 0d, 60d, 90d }; var wavelengths = new[] { 400d, 700d };
        if (anisotropic) Line("1 0 0"); Line("1 1"); Line("1");
        if (anisotropic)
        {
            for (var lobe = 0; lobe < 2; lobe++) { Line(rotations.Length.ToString()); Line(string.Join(" ", rotations.Select(N))); foreach (var a in rotations) { Line("3"); Line("0 60 90"); } }
            for (var lobe = 0; lobe < 2; lobe++) { Line(N(referenceTheta) + " 0"); Line("Wavelength (nm), coefficient (%)"); Line("2"); Line(lobe == 0 ? "400 20" : "400 50"); Line(lobe == 0 ? "700 30" : "700 40"); }
        }
        else { Line("3 2"); Line("0 60 90"); Line("400 700"); }
        void Profile(bool trans, double a, double ti)
        {
            Line("3 5"); Line("0 90 180 270 360");
            var angles = trans ? new[] { 90d, 120d, 180d } : new[] { 0d, 60d, 90d };
            foreach (var theta in angles)
            {
                var to = trans ? 180 - theta : theta; var mu = to == 90 ? 0 : Math.Cos(to * Math.PI / 180); var mi = ti == 90 ? 0 : Math.Cos(ti * Math.PI / 180);
                var values = new[] { 0d, 90d, 180d, 270d, 360d }.Select(p =>
                    3 * (1 + (anisotropic ? .1 * (1 - mi) * Math.Cos(2 * a * Math.PI / 180) : 0))
                    * (1 + (constantAzimuth ? 0 : .8 * (1 - mu) * (anisotropic ? G(p - a) : Math.Sin(ti * Math.PI / 180) * G(p)))));
                Line(N(theta) + " " + string.Join(" ", values.Select(N)));
            }
        }
        for (var lobe = 0; lobe < 2; lobe++)
        {
            if (anisotropic) foreach (var a in rotations) foreach (var ti in incidences) Profile(lobe == 1, a, ti);
            else foreach (var ti in incidences) foreach (var w in wavelengths)
            { Line(N(lobe == 0 ? (w == 400 ? 20 : 30) : (w == 400 ? 50 : 40))); Profile(lobe == 1, 0, ti); }
        }
        return text.ToString();
    }
    internal static BsdfContext Context(double theta = 0, double phi = 0, Vector3D? axis = null) => new(
        new(Math.Sin(theta * Math.PI / 180) * Math.Cos(phi * Math.PI / 180), Math.Sin(theta * Math.PI / 180) * Math.Sin(phi * Math.PI / 180), Math.Cos(theta * Math.PI / 180)),
        new(0, 0, 1), 550, materialXAxis: axis ?? new(1, 0, 0));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpeosTextProfilesUseFileEnergyAndCorrectAngularConventions(bool anisotropic)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path(anisotropic ? "synthetic.anisotropicbsdf" : "synthetic.brdf");
        File.WriteAllText(path, Speos(anisotropic));
        var data = BsdfFileImporter.Read(path, new(BsdfImportSides.BothDeclaredIdentical)); var model = new MeasuredBsdf(data);
        Assert.Equal(MeasuredBsdfData.FixedAxisProtocol, model.Kind); Assert.True(data.HasBothSides); Assert.True(data.HasTransmission);
        Assert.Equal(.25, model.Integrate(Context()).Reflection, 12); Assert.Equal(.45, model.Integrate(Context()).Transmission, 12);
        Assert.Equal(anisotropic ? BsdfCoordinates.FixedMaterialAxes : BsdfCoordinates.IncidentPlane, data.Coordinates);
        if (anisotropic)
        {
            Assert.Equal(.7 * 1.05, model.Integrate(Context(60, 0)).Total, 12);
            Assert.Equal(.7 * .95, model.Integrate(Context(60, 90)).Total, 12);
            Assert.Contains("reference theta=0", data.ImportNotes);
            Assert.True(model.Evaluate(Context(), new(.8, 0, .6)) > model.Evaluate(Context(), new(-.8, 0, .6)));
        }
        else
        {
            Assert.Equal(.7, model.Integrate(Context(60)).Total, 12);
            Assert.True(model.Evaluate(Context(60), new(-.8, 0, .6)) > model.Evaluate(Context(60), new(.8, 0, .6)));
        }
        Record(anisotropic ? "speos-anisotropic" : "speos-brdf", new { data.SourceFormat, data.SourceSha256, data.DataSha256, Nodes = data.Points.Count, R = model.Integrate(Context()).Reflection, T = model.Integrate(Context()).Transmission });
    }

    [Fact]
    public void FixedAxesInterpolateIncidenceAzimuthAndSampleTheJointDistribution()
    {
        var model = new MeasuredBsdf(FixedData()); var c = Context(60, 45);
        Assert.Equal(.7, model.Integrate(c).Total, 12);
        var direction = new Vector3D(Math.Sqrt(.91) / Math.Sqrt(2), Math.Sqrt(.91) / Math.Sqrt(2), .3);
        Assert.Equal(.25 / Math.PI * 1.28, model.Evaluate(c, direction), 12);
        var random = new Random(4901); const int count = 32768; double sx = 0, sy = 0, sz = 0; int trans = 0;
        var normal = Context();
        for (var k = 0; k < count; k++)
        {
            var sample = model.Sample(normal, random.NextDouble(), random.NextDouble()); var u = sample.Direction / sample.Direction.Length;
            Assert.InRange(Math.Abs(sample.Value * Math.Abs(u.Z) / sample.Pdf - .7), 0, 3e-14);
            sx += u.X; sy += u.Y; sz += Math.Abs(u.Z); if (sample.Lobe == BsdfLobe.DiffuseTransmission) trans++;
        }
        var expectedX = 8 * .8 / (Math.PI * Math.PI) * (1d / 3 - Math.PI / 16);
        Assert.InRange(Math.Abs(sx / count - expectedX), 0, 6 * Math.Sqrt((.25 - expectedX * expectedX) / count));
        Assert.InRange(Math.Abs(sy / count), 0, 6 * Math.Sqrt(.25 / count));
        Assert.InRange(Math.Abs(sz / count - 2d / 3), 0, 6 * Math.Sqrt(1d / (18 * count)));
        var pt = .45 / .7; Assert.InRange(Math.Abs(trans / (double)count - pt), 0, 6 * Math.Sqrt(pt * (1 - pt) / count));
        Record("fixed-axis-statistics", new { Count = count, Seed = 4901, MeanMaterialX = sx / count, ExpectedMaterialX = expectedX, MeanMaterialY = sy / count, MeanAbsCosine = sz / count, TransmissionCount = trans });
    }

    [Fact]
    public void RotatingOnlyTheMaterialAxisRotatesTheDistributionWithoutChangingTotalEnergy()
    {
        var model = new MeasuredBsdf(FixedData()); var x = Context(); var y = Context(axis: new(0, 1, 0));
        Assert.Equal(model.Integrate(x), model.Integrate(y));
        Assert.Equal(model.Evaluate(x, new(.8, 0, .6)), model.Evaluate(y, new(0, .8, .6)), 13);
        Assert.NotEqual(model.Evaluate(x, new(.8, 0, .6)), model.Evaluate(y, new(.8, 0, .6)));
        Assert.Throws<ArgumentException>(() => model.Integrate(new(new(0, 0, 1), new(0, 0, 1), 550)));
    }

    [Theory]
    [InlineData(90)] [InlineData(180)] [InlineData(360)]
    public void CommercialSymmetryAndNonuniformRotationGridsKeepThePeriodicCell(int extent)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("symmetry.anisotropicbsdf");
        double[] rotations = extent == 90 ? [0, 90] : extent == 180 ? [0, 90, 180] : [0, 120, 240, 360];
        File.WriteAllText(path, Speos(true, rotations, constantAzimuth: true));
        var model = new MeasuredBsdf(BsdfFileImporter.Read(path, new(BsdfImportSides.BothDeclaredIdentical)));
        var expected = .7 * (extent == 360 ? .975 : 1.05);
        Assert.Equal(expected, model.Integrate(Context(60, 0)).Total, 12);
        Assert.Equal(model.Evaluate(Context(60, .000001), new(.8, 0, .6)), model.Evaluate(Context(60, 359.999999), new(.8, 0, .6)), 10);
    }

    [Fact]
    public void SpectrumReferenceIncidenceControlsScaleRatherThanRenormalizingEveryInputSlice()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("reference.anisotropicbsdf");
        File.WriteAllText(path, Speos(true, referenceTheta: 30));
        var model = new MeasuredBsdf(BsdfFileImporter.Read(path, new(BsdfImportSides.BothDeclaredIdentical)));
        var referenceIntegralFactor = 1 + .1 * (1 - Math.Cos(Math.PI / 6));
        Assert.Equal(.7 / referenceIntegralFactor, model.Integrate(Context()).Total, 12);
        Assert.Equal(.7 * 1.05 / referenceIntegralFactor, model.Integrate(Context(60)).Total, 12);
        Assert.Equal(.7, model.Integrate(Context(30)).Total, 12);
    }

    [Theory]
    [InlineData("binary")] [InlineData("intensity")] [InlineData("sides")] [InlineData("negative")] [InlineData("active")] [InlineData("short")]
    [InlineData("combined-energy")] [InlineData("header")]
    public void UnsupportedAndMalformedCommercialFilesFailExplicitly(string defect)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("bad.brdf"); var text = Speos(false);
        if (defect == "binary") text = text.Replace("v8.0\r\n0", "v8.0\r\n1").Replace("v8.0\n0", "v8.0\n1");
        if (defect == "intensity") text = text.Replace("1 1" + Environment.NewLine + "1", "1 1" + Environment.NewLine + "0");
        if (defect == "negative") text = text.Replace("400 700", "-400 700");
        if (defect == "active") text = text.Replace(Environment.NewLine + "20" + Environment.NewLine, Environment.NewLine + "101" + Environment.NewLine);
        if (defect == "combined-energy") text = text.Replace(Environment.NewLine + "20" + Environment.NewLine, Environment.NewLine + "70" + Environment.NewLine);
        if (defect == "header") text = text.Replace("OPTIS - BRDF", "OPTIS - Unknown");
        if (defect == "short") text = text[..(text.Length / 2)];
        File.WriteAllText(path, text);
        Assert.Throws<InvalidDataException>(() => BsdfFileImporter.Read(path, defect == "sides" ? null : new(BsdfImportSides.BothDeclaredIdentical)));
    }

    [Fact]
    public void FixedCsvAndCanonicalDigestFreezeTheAxisConventionAndRejectBadSeams()
    {
        var data = FixedData(); using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("fixed.csv");
        var text = new StringBuilder("# OpticalSystemDesign.MeasuredBsdfGrid/2\n" + BsdfFileImporter.FixedCsvHeader + "\n");
        foreach (var p in data.Points) text.AppendLine(string.Join(',', new[] { p.Side.ToString() }.Concat(new[] { p.WavelengthNanometers, p.IncidentThetaDegrees, p.IncidentPhiDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees, p.BrdfPerSteradian, p.BtdfPerSteradian }.Select(v => v.ToString("G17", CultureInfo.InvariantCulture)))));
        File.WriteAllText(path, text.ToString()); var read = BsdfFileImporter.Read(path);
        Assert.Equal(data.DataSha256, read.DataSha256);
        var bad = data.Points.Select(p => p.IncidentPhiDegrees == 360 ? p with { BrdfPerSteradian = p.BrdfPerSteradian * 1.1 } : p).ToArray();
        Assert.Throws<InvalidDataException>(() => new MeasuredBsdfData(data.Name, data.SourceFileName, data.SourceSha256, bad, coordinates: BsdfCoordinates.FixedMaterialAxes));
    }
    internal static void Record(string name, object value)
    {
        var dir = Environment.GetEnvironmentVariable("OPTICAL_COMMERCIAL_BSDF_EVIDENCE_DIR"); if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, name + ".json"), System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
