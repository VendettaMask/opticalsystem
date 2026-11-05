using OpticalSystemDesign.Validation;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class OffAxisCollimationReferenceTests
{
    [Theory]
    [InlineData(1, 0)] [InlineData(2, 0)] [InlineData(3, 0)]
    [InlineData(1, 3)] [InlineData(2, 3)] [InlineData(3, 3)]
    public void CompleteOffAxisOpdFansMatchIndependentConjugatePhaseControls(int wave, int mode)
    {
        var optic = Example();
        using var fixture = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "perfect-collimation-opd-native-v1.json")));
        var reference = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(c =>
            c.GetProperty("wave").GetInt32() == wave && c.GetProperty("nativeMode").GetInt32() == mode);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Optical Path Difference", new Dictionary<string, string>
        { ["NumberOfRays"] = "20", ["WavelengthNumber"] = wave.ToString(), ["FieldNumber"] = "3", ["UseRayAiming"] = "True" });
        var series = data.PlotPanes!.SelectMany(p => p.Series).ToArray();
        Assert.Equal(2, series.Length);
        foreach (var (curve, k) in reference.GetProperty("curves").EnumerateArray().Select((c, k) => (c, k)))
        {
            var x = curve.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = curve.GetProperty("y").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            Assert.Equal(41, series[k].Points.Count);
            var errors = series[k].Points.Select((p, i) =>
            {
                Assert.Equal(x[i], p.X, 12); Assert.True(double.IsFinite(p.Y));
                return Math.Abs(p.Y - y[i]);
            }).ToArray();
            // The existing OPD combined acceptance gates, applied to every original node.
            Assert.True(errors.Select((e, i) => e <= 1e-5 + .001 * Math.Abs(y[i])).All(v => v)
                || Math.Sqrt(errors.Average(e => e * e)) / Math.Max(1e-5, y.Max(Math.Abs)) <= .003);
        }
    }

    [Theory]
    [InlineData(1, 0)] [InlineData(2, 0)] [InlineData(3, 0)]
    [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 1)]
    public void FocalPointCollimatorHasOnePlanePhaseForAllParallelOutgoingRays(int wave, double hy)
    {
        var optic = Example(); var wavelength = optic.Wavelengths[wave - 1];
        var generator = optic.SequentialRayTracer.RayGenerator;
        var chief = optic.SequentialRayTracer.Trace(generator.GenerateGeneric(0, hy, 0, 0,
            wavelength.Micrometers, optic.RayAimingEnabled)).RayHistories[0].Single(r => r.SurfaceNumber == 1);
        foreach (var (px, py) in new[] { (.5, 0d), (1d, 0d), (0d, .5), (0d, 1d) })
        {
            var ray = optic.SequentialRayTracer.Trace(generator.GenerateGeneric(0, hy, px, py,
                wavelength.Micrometers, optic.RayAimingEnabled)).RayHistories[0].Single(r => r.SurfaceNumber == 1);
            Assert.InRange((ray.Direction - chief.Direction).Length, 0, 1e-12);
            var delta = ray.Position - chief.Position;
            var plane = delta.X * chief.Direction.X + delta.Y * chief.Direction.Y + delta.Z * chief.Direction.Z;
            var actual = ray.PhaseInclusiveOpticalPathLength!.Value - chief.PhaseInclusiveOpticalPathLength!.Value;
            Assert.InRange(Math.Abs(actual - plane), 0, 1e-9);
        }
    }

    private static Optic Example() => OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "definition-settings", "example2.zmx")), ".zmx");
}
