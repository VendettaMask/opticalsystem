using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ExtendedSourceAreaTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(11, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(11, true)]
    public void UniformAreaMatchesTheAnalyticalSquareImage(int strata, bool aiming)
    {
        var optic = Relay(); optic.RayAimingEnabled = aiming;
        var data = new ExtendedSourceEncircledEnergyAnalysis(optic, fieldSize: 2,
            sourceSampling: strata, numRays: 12000, numPoints: 241, maximumDistanceMicrometers: 1500).GenerateData();
        var points = Assert.Single(data.PlotSeries).Points;
        foreach (var radius in new[] { 250.0, 500, 750, 1000 })
        {
            var expected = Math.PI * Math.Pow(radius / 1000, 2) / 4;
            Assert.InRange(Math.Abs(At(points, radius) - expected), 0, 0.012);
        }
        Assert.Equal(1, points[^1].Y, 12);
        Assert.InRange((int)data.Values["RayCount"], 12000 - strata * strata, 12000);
    }

    [Fact]
    public void OneUniformImaPixelAndBuiltInSquareAreTheSameEmittingArea()
    {
        var optic = Relay();
        AnalysisData Run(ExtendedSourceImage? image) => new ExtendedSourceEncircledEnergyAnalysis(
            optic, fieldSize: 2, sourceSampling: 5, numRays: 3000, numPoints: 101, sourceImage: image).GenerateData();
        Assert.Equal(Run(null).PlotSeries[0].Points, Run(new ExtendedSourceImage(1, 1, [1d])).PlotSeries[0].Points);
    }

    [Fact]
    public void ExplicitSourceWidthWorksWithOnlyAnAxisFieldAndScalesThePhysicalImage()
    {
        var optic = Relay();
        AnalysisData Run(double width) => new ExtendedSourceEncircledEnergyAnalysis(
            optic, fieldSize: width, numRays: 5000, numPoints: 101).GenerateData();
        var narrow = Run(1); var wide = Run(2);
        Assert.Equal(2 * narrow.PlotOptions!.XMaximum!.Value, wide.PlotOptions!.XMaximum!.Value, 8);
        Assert.All(wide.PlotSeries[0].Points.Zip(narrow.PlotSeries[0].Points), pair =>
            Assert.Equal(pair.Second.Y, pair.First.Y, 10));
        Assert.InRange(wide.PlotOptions.XMaximum.Value, 1400, 1500);
    }

    [Fact]
    public void SourceCanExtendBeyondTheLastDeclaredFieldWithoutChangingTheEditor()
    {
        var optic = Relay(); optic.Fields[0].Y = 3;
        var before = optic.ToSnapshot();
        var data = new ExtendedSourceEncircledEnergyAnalysis(optic, fieldSize: 2, numRays: 5000).GenerateData();
        Assert.InRange(Convert.ToDouble(data.Values["ReferenceYMicrometers"]), -3010, -2990);
        Assert.InRange(data.PlotOptions!.XMaximum!.Value, 1400, 1500);
        Assert.Equal(before.Fields, optic.ToSnapshot().Fields);
    }

    [Fact]
    public void WeightedImaPixelsRetainTheirAreasAndRelativeBrightness()
    {
        var source = new ExtendedSourceImage(2, 1, [1d, 3d]);
        var data = new ExtendedSourceEncircledEnergyAnalysis(Relay(), fieldSize: 2,
            numRays: 16000, sourceImage: source, sourceName: "weighted.ima").GenerateData();
        // An ideal -1 relay maps the weighted source centroid +0.25 mm to -0.25 mm.
        Assert.InRange(Convert.ToDouble(data.Values["ReferenceXMicrometers"]), -256, -244);
        Assert.InRange(Convert.ToDouble(data.Values["ReferenceYMicrometers"]), -6, 6);
        Assert.Equal(2, data.Values["ActiveSourcePixelCount"]);
        Assert.Equal("weighted.ima", data.Values["Source"]);
    }

    [Fact]
    public void RealImageApertureStillRejectsRaysAndAcceptedEnergyIsNormalized()
    {
        var optic = Relay(); optic.SurfaceGroup.Items[^1].PhysicalAperture = new CircularAperture(0.4);
        var data = new ExtendedSourceEncircledEnergyAnalysis(optic, fieldSize: 2, numRays: 8000).GenerateData();
        Assert.True((int)data.Values["VignettedRayCount"] > 6000);
        Assert.InRange(data.PlotOptions!.XMaximum!.Value, 400, 425);
        Assert.Equal(1, data.PlotSeries[0].Points[^1].Y, 12);
    }

    [Fact]
    public void AfocalCoordinatesKeepAngularUnitsAndAvoidAnExtraThousandFoldScale()
    {
        var optic = Relay(); optic.ImageSpaceAfocal = true;
        var data = new ExtendedSourceEncircledEnergyAnalysis(optic, fieldSize: 0.2, numRays: 1000).GenerateData();
        Assert.Equal(AnalysisAxisUnit.Milliradian, data.PlotSeries[0].XUnit);
        Assert.Equal("mrad", data.Values["DistanceUnit"]);
        Assert.False(data.Values.ContainsKey("MaximumDistanceMicrometers"));
        Assert.InRange(data.PlotOptions!.XMaximum!.Value, 1, 100);
    }

    [Fact]
    public void ZeroBrightnessAndCancellationRemainExplicit()
    {
        var analysis = new ExtendedSourceEncircledEnergyAnalysis(Relay(), sourceImage: new ExtendedSourceImage(1, 1, [0d]));
        Assert.Equal(AnalysisOutcome.Unavailable, analysis.GenerateData().Outcome);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => new ExtendedSourceEncircledEnergyAnalysis(Relay()).GenerateData());
    }

    private static double At(IReadOnlyList<AnalysisPoint> points, double x) => points.MinBy(point => Math.Abs(point.X - x))!.Y;

    private static Optic Relay()
    {
        var optic = new Optic { FieldDefinition = FieldDefinitionKind.ObjectHeight };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 20 },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 20,
                InteractionModel = new ParaxialLensInteractionModel(.1, .1) },
            new OpticalSurface { Thickness = 0, SemiDiameter = 10 }
        ]);
        return optic;
    }
}
