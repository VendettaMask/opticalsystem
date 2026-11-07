using OpticalSystemDesign.Validation;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class HuygensGeometricCenterTests
{
    [Theory]
    [InlineData(false, .27624278)]
    [InlineData(true, .27631068)]
    public void SegmentedPlanarMirrorKeepsNativePeakAtBothCenters(bool useCentroid, double nativePeak)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var source = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/MEMS DLL Sample.ZMX");
        var optic = OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(source), ".zmx");
        var data = new HuygensPsfAnalysis(optic, numRays: 32, imageSize: 32, pixelPitchMillimeters: .00025,
            wavelengthNumber: 1, fieldNumber: 1, useCentroid: useCentroid).GenerateData();
        Assert.InRange(Math.Abs(Convert.ToDouble(data.Values["PeakStrehlRatio"]) - nativePeak), 0, 1e-6);
        if (useCentroid)
            Assert.InRange(Convert.ToDouble(data.Values["GeometricCenterOffsetXMicrometers"]), -2690, -2688);
    }

    [Fact]
    public void CentroidOptionCentersTheSampledImageOnTheGeometricBeam()
    {
        var optic = Optic.CreateCookeTriplet();
        var wavelength = optic.Wavelengths[0];
        var geometric = SpotAnalysisEngine.Generate(optic, [(0, 1)], [wavelength], 13, "uniform",
            reference: "absolute", includeSurfaceTransmission: false);
        var centroid = SpotAnalysisEngine.Centroid(Assert.Single(geometric.Fields).WeightedRays);
        var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 1), wavelength);
        var chief = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(frame.Center);
        var centered = Build(optic, 32, true);
        var chiefCentered = Build(optic, 32, false);
        var points = Assert.Single(centered.PlotSeries).Points;
        Assert.Equal(0, points[16 * 32 + 16].X);
        Assert.Equal(0, points[16 * 32 + 16].Y);
        Assert.Equal((centroid.X - chief.X) * 1000,
            Convert.ToDouble(centered.Values["GeometricCenterOffsetXMicrometers"]), 7);
        Assert.Equal((centroid.Y - chief.Y) * 1000,
            Convert.ToDouble(centered.Values["GeometricCenterOffsetYMicrometers"]), 7);
        Assert.True(points.Zip(Assert.Single(chiefCentered.PlotSeries).Points)
            .Any(pair => Math.Abs(pair.First.Value!.Value - pair.Second.Value!.Value) > 1e-9),
            "Changing the center must recalculate intensity, not merely relabel the old grid.");
    }

    [Fact]
    public void GeometricCenterDoesNotDependOnThePsfWindowOrPeakNormalization()
    {
        var optic = Optic.CreateCookeTriplet();
        var small = Build(optic, 16, true);
        var large = Build(optic, 32, true, normalize: true);
        foreach (var key in new[] { "GeometricCenterOffsetXMicrometers", "GeometricCenterOffsetYMicrometers" })
            Assert.Equal(Convert.ToDouble(small.Values[key]), Convert.ToDouble(large.Values[key]), 12);
        Assert.Equal(1, Assert.Single(large.PlotSeries).Points.Max(point => point.Value!.Value), 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputeHuygensPsf(
            optic, (0, 1), optic.Wavelengths[0], 13, 16, .003,
            imageCenterOffset: (double.NaN, 0)));
    }

    [Fact]
    public void AfocalGeometricCenterUsesAngularUnitsAndAllSelectedSpectralWeights()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.ImageSpaceAfocal = true;
        var wavelengths = optic.Wavelengths.ToArray();
        var primary = wavelengths.First(wavelength => wavelength.IsPrimary);
        var spot = SpotAnalysisEngine.Generate(optic, [(0, 1)], wavelengths, 13, "uniform",
            reference: "absolute", includeSurfaceTransmission: false);
        var expected = SpotAnalysisEngine.Centroid(Assert.Single(spot.Fields).WeightedRays);
        var chief = optic.TraceGenericFinalSample(0, 1, 0, 0, primary.Micrometers)!;
        var chiefAngle = ImageSpaceAnalysisSupport.DirectionAnglesMilliradians(
            optic.SurfaceGroup.Items[^1], chief.Direction);
        var actual = new HuygensPsfAnalysis(optic, numRays: 13, imageSize: 16,
            pixelPitchMillimeters: .1, wavelengthNumber: 0, fieldNumber: optic.Fields.Count,
            useCentroid: true).GenerateData();
        Assert.Equal(expected.X - chiefAngle.X,
            Convert.ToDouble(actual.Values["GeometricCenterOffsetXMilliradians"]), 10);
        Assert.Equal(expected.Y - chiefAngle.Y,
            Convert.ToDouble(actual.Values["GeometricCenterOffsetYMilliradians"]), 10);
        Assert.Equal(0, Convert.ToDouble(actual.Values["GeometricCenterOffsetYMicrometers"]));
        var center = Assert.Single(actual.PlotSeries).Points[8 * 16 + 8];
        Assert.Equal(0, center.X);
        Assert.Equal(0, center.Y);
    }

    private static AnalysisData Build(Optic optic, int imageSize, bool useCentroid, bool normalize = false) =>
        new HuygensPsfAnalysis(optic, numRays: 13, imageSize: imageSize,
            pixelPitchMillimeters: .003, wavelengthNumber: 1, fieldNumber: optic.Fields.Count,
            useCentroid: useCentroid, normalize: normalize).GenerateData();
}
