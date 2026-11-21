using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class ThroughFocusAimingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NominalFocusHasTheSameAcceptedRaysAsStandardSpot(bool aiming, bool polarized)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.RayAimingEnabled = aiming;
        var standard = new SpotDiagramAnalysis(optic, new SpotDiagramSettings(
            RayDensity: 4, Reference: "chief", UsePolarization: polarized)).GenerateData();
        var through = new ThroughFocusAnalysis(optic, new ThroughFocusSpotSettings(
            RayDensity: 4, Reference: "chief", UsePolarization: polarized)).GenerateData();

        Assert.Equal(aiming, through.Values["UseRayAiming"]);
        Assert.Equal(standard.PlotPanes!.Count * 5, through.PlotPanes!.Count);
        for (var field = 0; field < standard.PlotPanes.Count; field++)
        {
            var focused = through.PlotPanes[field * 5 + 2];
            Assert.Equal(standard.PlotPanes[field].Series.Count, focused.Series.Count);
            for (var wavelength = 0; wavelength < focused.Series.Count; wavelength++)
                Assert.Equal(standard.PlotPanes[field].Series[wavelength].Points,
                    focused.Series[wavelength].Points);
            for (var plane = 0; plane < 5; plane++)
                Assert.Equal(focused.Series.Select(series => series.Points.Count),
                    through.PlotPanes[field * 5 + plane].Series.Select(series => series.Points.Count));
        }
        Assert.Equal(aiming, optic.RayAimingEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusMetricUsesTheSameNominalPupilAsSpotMetric(bool aiming)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.RayAimingEnabled = aiming;
        var spot = SpotMetricEvaluator.Evaluate(optic, rayDensity: 4, reference: "chief");
        var focus = FocusMetricEvaluator.Evaluate(optic, focusStep: .05,
            focusPlaneCount: 3, rayDensity: 4, reference: "chief");
        Assert.Equal(spot.RmsSpotRadius, focus.Points[1].RmsSpotRadius, 12);
        Assert.Equal(spot.Radius80, focus.Points[1].Radius80, 12);
    }

    [Fact]
    public void PhysicalObscurationStillRemovesRaysInEveryDefocusPlane()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.RayAimingEnabled = true;
        var stop = optic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        var obscuration = new AnnularAperture(stop.SemiDiameter, stop.SemiDiameter * .3);
        stop.PhysicalAperture = obscuration;
        var attempted = ApertureSampler.GenerateHexapolarRings(4).Count;
        var through = new ThroughFocusAnalysis(optic, new ThroughFocusSpotSettings(
            RayDensity: 4, FieldNumber: 1, WavelengthNumber: 1, Reference: "chief")).GenerateData();
        Assert.All(through.PlotPanes!, pane =>
            Assert.InRange(Assert.Single(pane.Series).Points.Count, 1, attempted - 1));
        Assert.Same(obscuration, stop.PhysicalAperture);
    }
}
