using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalMtfFieldParityTests
{
    [Fact]
    public void BlockingHalfThePupilDoesNotHalveTheNormalizedDcOtf()
    {
        static (double T, double S) Dc(bool clipped)
        {
            var optic = new Optic { ImageSpaceAfocal = true };
            optic.Aperture.Value = 10;
            optic.Solves.KeepImageAtBackFocus = false;
            optic.Fields.Add(new FieldPoint());
            var wave = new Wavelength { Nanometers = 550, IsPrimary = true };
            optic.Wavelengths.Add(wave);
            optic.SurfaceGroup.ImportLegacySurfaces([
                new() { Thickness = double.PositiveInfinity, SemiDiameter = 50 },
                new() { Thickness = 10, SemiDiameter = 5, IsStop = true },
                new() { Thickness = 10, SemiDiameter = 50 },
                new() { SemiDiameter = 50 }
            ]);
            if (clipped) optic.SurfaceGroup.Items[2].PhysicalAperture = new RectangularAperture(5, 10, 5);
            var pupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32,
                cellCentered: true, referenceWavelength: wave);
            if (clipped) Assert.Contains(pupil.Samples, sample => sample.Intensity == 0);
            var method = typeof(DiffractionEngine).GetMethod("ComputeFastFftMtfAtFrequency",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var otf = ((System.Numerics.Complex T, System.Numerics.Complex S))method.Invoke(null,
                new object?[] { optic, (0d, 0d), wave, 32, 0d, 0d, false, pupil, null, wave })!;
            return (otf.T.Real, otf.S.Real);
        }
        var full = Dc(false);
        var half = Dc(true);
        // The symmetric quadrature error cancels in this aperture invariant.
        Assert.Equal(full.T, half.T, 11);
        Assert.Equal(full.S, half.S, 11);
        Assert.InRange(half.T, .99, 1.01);
    }

    [Theory]
    [InlineData("m1-geometric")]
    [InlineData("m2-geometric")]
    [InlineData("m3-geometric")]
    [InlineData("m4-geometric")]
    [InlineData("m5-geometric")]
    public void GeometricFieldCurvesMeetUnchangedCapturedAnalysisGates(string id)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-mtf-field");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "manifest.json")));
        var archive = Path.Combine(fixture, "reference.zip");
        Assert.Equal(manifest.RootElement.GetProperty("sha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))));
        var temp = Path.Combine(Path.GetTempPath(), "afocal-mtf-field-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(archive, temp);
            using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp, "cases.json")));
            var row = cases.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
            var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var snapshot = Path.Combine(temp, row.GetProperty("snapshot").GetString()!);
            Assert.Equal(row.GetProperty("snapshotSha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(snapshot))));
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot));
            var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
            Assert.Equal(AnalysisOutcome.Success, data.Outcome);
            Assert.Equal(10, data.Values["FieldPointCount"]);
            Assert.Equal(300, data.Values["PlotPointCount"]);
            Assert.Equal(12, data.PlotSeries.Count);
            Assert.All(data.PlotSeries, series =>
            {
                Assert.Equal(AnalysisAxisQuantity.NormalizedField, series.XQuantity);
                Assert.Equal(AnalysisAxisUnit.Dimensionless, series.XUnit);
                Assert.Equal(AnalysisAxisQuantity.Modulation, series.YQuantity);
                Assert.Equal(AnalysisAxisUnit.Dimensionless, series.YUnit);
            });
            var actual = Path.Combine(temp, "actual.json");
            JsonFiles.Write(actual, data);
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var w = ResultNormalizer.Workbench(actual, entry, request);
            var z = ResultNormalizer.Zemax(Path.Combine(temp, "native", id, "data.json"), entry, request);
            var gates = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
                .Analyses[request.CanonicalAnalysisKey].Quantities;
            var capturedGates = row.GetProperty("gates").Deserialize<Dictionary<string, Tolerances>>(JsonFiles.Options)!;
            Assert.Equal(capturedGates["Modulation"], gates["Modulation"]);
            Assert.Equal(12, z.Series.Count);
            foreach (var expected in z.Series)
            {
                var values = ComparisonMetrics.Align(w.Series.Single(s => s.Id == expected.Id), expected, []);
                var metric = ComparisonMetrics.Calculate(expected.Id, expected.YAxis.Unit, values, gates[expected.YAxis.Quantity]);
                Assert.True(metric.Conclusion == Conclusion.Pass, $"{id}/{expected.Id}: {metric}");
            }
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
