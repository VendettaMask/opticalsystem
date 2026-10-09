using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class FftSamplingReferenceRepairTests
{
    [Fact]
    public void VertexPlotMatchesImageLocalReferenceUsedByEnergyOperand()
    {
        var optic = Fixture();
        var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = image.CoordinateSystem with
        { Origin = image.CoordinateSystem.Origin + new Vector3D(.005, 0, 0) };
        var distribution = DiffractionEnergyMetrics.Create(optic, 1, 1, 1, EnergyRegion.Circle, 2);
        var data = Energy(optic, 1, "vertex", 1);
        foreach (var point in data.PlotSeries[^1].Points)
            Assert.Equal(distribution.FractionAtDistance(point.X), point.Y, 10);
        Assert.NotEqual(Energy(optic, 1, "chief", 1).PlotSeries[^1].Points[^1].Y,
            data.PlotSeries[^1].Points[^1].Y);
    }

    [Fact]
    public void OffAxisVertexIsNotAnAliasForChiefRay()
    {
        var optic = Fixture();
        var chief = Energy(optic, 1, "chief", optic.Fields.Count);
        var vertex = Energy(optic, 1, "vertex", optic.Fields.Count);
        Assert.True(chief.PlotSeries[^1].Points[^1].Y > .01);
        Assert.Equal(0, vertex.PlotSeries[^1].Points[^1].Y);
    }

    [Fact]
    public void VertexUsesImageLocalCoordinatesAfterRigidTranslation()
    {
        var optic = Fixture();
        var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = image.CoordinateSystem with
        { Origin = image.CoordinateSystem.Origin + new Vector3D(.005, 0, 0) };
        var moved = Optic.FromSnapshot(optic.ToSnapshot());
        moved.InvalidateRayTraceCache();
        foreach (var surface in moved.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with
            { Origin = surface.CoordinateSystem.Origin + new Vector3D(10, 10, 0) };
        var original = Energy(optic, 1, "vertex", 1);
        var translated = Energy(moved, 1, "vertex", 1);
        foreach (var pair in original.PlotSeries[^1].Points.Zip(translated.PlotSeries[^1].Points))
            Assert.Equal(pair.First.Y, pair.Second.Y, 10);
    }

    [Theory]
    [InlineData(.00001)]
    [InlineData(1000)]
    public void CoreRejectsExplicitPitchWithDegenerateOrIncompletePupil(double pitch)
    {
        var optic = Fixture();
        Assert.Throws<InvalidOperationException>(() => DiffractionEngine.ComputeFftPsf(optic,
            (0, 0), optic.Wavelengths[0], 32, 64, cellCenteredPupil: true,
            zemaxFftSampling: true, aimAtStop: true, imageDelta: pitch));
    }

    [Theory]
    [InlineData(.00001)]
    [InlineData(1000)]
    public void PublicPsfDoesNotTurnInvalidPitchIntoAFlatSuccessfulImage(double pitch)
        => Assert.Throws<InvalidOperationException>(() => Psf(Fixture(), 32, 1, pitch).GenerateData());

    [Theory]
    [InlineData(32, false)]
    [InlineData(64, false)]
    [InlineData(32, true)]
    public void AutomaticAndExplicitSamePitchUseTheSamePolychromaticPupils(int sampling, bool afocal)
    {
        var optic = Fixture(); optic.ImageSpaceAfocal = afocal;
        var automatic = Psf(optic, sampling, 0, 0).GenerateData();
        var pitch = (double)automatic.Values[afocal ? "ImageDeltaMilliradians" : "ImageDeltaMicrometers"];
        var explicitGrid = Psf(optic, sampling, 0, pitch).GenerateData();
        foreach (var pair in automatic.PlotSeries.Single().Points.Zip(explicitGrid.PlotSeries.Single().Points))
            Assert.Equal(pair.First.Value!.Value, pair.Second.Value!.Value, 11);
        foreach (var pair in ((double[])automatic.Values["PupilGridStretch"])
            .Zip((double[])explicitGrid.Values["PupilGridStretch"]))
            Assert.Equal(pair.First, pair.Second, 12);
        Assert.True(((double[])automatic.Values["PupilGridStretch"]).Max() >
            ((double[])automatic.Values["PupilGridStretch"]).Min());
    }

    [Fact]
    public void PolychromaticEnergyPlotAndOperandShareTheSameSampling()
    {
        var optic = Fixture();
        var distribution = DiffractionEnergyMetrics.Create(optic, 1, 0, 1, EnergyRegion.Circle, 1);
        var data = Energy(optic, 0, "centroid", 1);
        foreach (var point in data.PlotSeries[^1].Points)
            Assert.Equal(distribution.FractionAtDistance(point.X), point.Y, 10);
    }

    [Fact]
    public void NegativePolychromaticPitchRetainsFullUnstretchedPupil()
    {
        var data = Psf(Fixture(), 64, 0, -1).GenerateData();
        Assert.All((double[])data.Values["PupilGridStretch"], stretch => Assert.Equal(1, stretch));
    }

    [Fact]
    public void SmallGeneralPurposeGridDoesNotCompressItsCoverageBelowThePupil()
    {
        var optic = Fixture();
        var result = DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0],
            8, 16, cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: true);
        var grid = new PupilGridSpecification(8, true, true, result.PupilGridStretch);
        Assert.True(grid.Coordinate(0) <= -1 && grid.Coordinate(7) >= 1);
    }

    private static Optic Fixture()
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = true; return optic;
    }

    private static PsfAnalysis Psf(Optic optic, int sampling, int wave, double pitch)
        => new(optic, sampling, 2 * sampling, wavelengthNumber: wave, fieldNumber: 1,
            imageDeltaMicrometers: pitch, type: "linear", displayAs: "heatmap", zemaxCompatible: true);

    private static AnalysisData Energy(Optic optic, int wave, string reference, int field)
        => new DiffractionEncircledEnergyAnalysis(optic, 32, 64, numPoints: 17,
            wavelengthNumber: wave, fieldNumber: field, reference: reference,
            maximumDistanceMicrometers: 10).GenerateData();
}
