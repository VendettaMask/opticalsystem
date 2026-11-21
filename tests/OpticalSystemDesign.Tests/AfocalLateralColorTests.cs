using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class AfocalLateralColorTests
{
    public static string Source => Path.GetFullPath(Path.Combine(
        FindRepository(), "src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AfocalColorUsesChiefDirectionDifferencesInBothRayModes(bool real)
    {
        var optic = Load();
        var data = new LateralColorAnalysis(optic, useRealRays: real, showAiryDisk: false).GenerateData();
        var wavelengths = optic.Wavelengths.OrderBy(w => w.Micrometers).ToArray();
        var curve = Assert.Single(data.PlotSeries);
        Assert.Equal(AnalysisAxisUnit.Milliradian, curve.XUnit);
        Assert.Equal(AnalysisAxisQuantity.Angle, curve.XQuantity);
        Assert.Equal("mrad", curve.XAxisLabel);
        for (var index = 0; index < curve.Points.Count; index++)
        {
            var fraction = index / (double)(curve.Points.Count - 1);
            var expected = 1000 * (Angle(wavelengths[^1].Micrometers) - Angle(wavelengths[0].Micrometers));
            Assert.Equal(expected, curve.Points[index].X, 9);

            double Angle(double wavelength)
            {
                if (!real)
                {
                    var trace = optic.Paraxial.TraceNormalizedPupil(fraction, [0d], wavelength);
                    return Math.Atan(trace.Slopes[^1][0]);
                }
                var rays = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                    0, fraction, 0, 0, wavelength, aimAtStop: optic.RayAimingEnabled);
                var sample = optic.SequentialRayTracer.TraceFinalSamples(rays).Single()!;
                var direction = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalDirection(sample.Direction);
                return Math.Atan2(direction.Y, direction.Z);
            }
        }
    }

    [Fact]
    public void RiflescopeCurveAndAiryScaleAgreeWithTheUserAngularReference()
    {
        var data = new LateralColorAnalysis(Load()).GenerateData();
        var minimum = data.PlotSeries[0].Points.MinBy(point => point.X)!;
        var arcminutesPerMilliradian = 180 * 60 / (Math.PI * 1000);
        Assert.InRange(minimum.Y, 1.275, 1.325);
        Assert.InRange(minimum.X * arcminutesPerMilliradian, -0.966, -0.963);
        var airy = Convert.ToDouble(data.Values["AiryRadiusMilliradians"]);
        Assert.InRange(airy * arcminutesPerMilliradian, 0.60, 0.63);
        Assert.Equal("mrad", data.Values["LateralColorUnit"]);
        Assert.False(data.Values.ContainsKey("AiryRadiusMicrometers"));
        Assert.All(data.PlotSeries.Skip(1), series => Assert.All(series.Points,
            point => Assert.Equal(airy, Math.Abs(point.X), 12)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TranslatingOnlyTheAfocalReadoutPlaneDoesNotChangeAngularColor(bool real)
    {
        var optic = Load();
        var original = new LateralColorAnalysis(optic, useRealRays: real).GenerateData();
        optic.SurfaceGroup.Items[^2].Thickness += 100;
        optic.SurfaceGroup.Renumber();
        var shifted = new LateralColorAnalysis(optic, useRealRays: real).GenerateData();
        Assert.Equal(original.PlotSeries.Count, shifted.PlotSeries.Count);
        for (var index = 0; index < original.PlotSeries.Count; index++)
        {
            var first = original.PlotSeries[index].Points;
            var second = shifted.PlotSeries[index].Points;
            Assert.Equal(first.Count, second.Count);
            Assert.All(first.Zip(second), pair => Assert.Equal(pair.First.X, pair.Second.X, 9));
        }
    }

    [Fact]
    public void ManualScaleAndAllWavelengthsUseThePublishedAngularUnit()
    {
        var optic = Load();
        var data = new LateralColorAnalysis(optic, graphScaleMicrometers: .29,
            allWavelengths: true, showAiryDisk: false).GenerateData();
        Assert.Equal(.29, data.PlotOptions!.XMaximum);
        Assert.Equal(-.29, data.PlotOptions.XMinimum);
        Assert.Equal(.29, data.Values["GraphScaleMilliradians"]);
        Assert.Equal(optic.Wavelengths.Count - 1, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, series => Assert.Equal(AnalysisAxisUnit.Milliradian, series.XUnit));
    }

    [Fact]
    public void FocalCurvesKeepTheirOriginalMicrometerInterceptDefinition()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new LateralColorAnalysis(optic, showAiryDisk: false).GenerateData();
        var waves = optic.Wavelengths.OrderBy(w => w.Micrometers).ToArray();
        var points = data.PlotSeries[0].Points;
        for (var index = 0; index < points.Count; index++)
        {
            var normalized = index / (double)(points.Count - 1);
            double Height(double wave)
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                    0, normalized, 0, 0, wave, aimAtStop: optic.RayAimingEnabled);
                return optic.SequentialRayTracer.TraceFinalSamples(bundle).Single()!.Position.Y;
            }
            Assert.Equal((Height(waves[^1].Micrometers) - Height(waves[0].Micrometers)) * 1000,
                points[index].X, 8);
        }
        Assert.Equal(AnalysisAxisUnit.Micrometer, data.PlotSeries[0].XUnit);
        Assert.True(data.Values.ContainsKey("AiryRadiusMicrometers"));
    }

    public static Optic Load() => OpticalFormatCatalog.Import(File.ReadAllText(Source), ".zmx");
    private static string FindRepository()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OpticalSystemDesign.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
