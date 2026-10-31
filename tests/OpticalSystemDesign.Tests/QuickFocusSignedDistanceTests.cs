using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class QuickFocusSignedDistanceTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    public async Task QuickFocusPreservesSignedDistancesAndReportsTheSavedPrescriptionRms(bool reflected, double defocus)
    {
        var optic = Optic.CreateBlank("Signed focus regression");
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Geometry = reflected ? new StandardGeometry(-100) : new PlaneGeometry(),
                Thickness = (reflected ? -50 : 50) + defocus,
                InteractionModel = reflected ? new RefractiveReflectiveInteractionModel() : new ThinLensInteractionModel(50),
                IsReflective = reflected, SemiDiameter = 6, IsStop = true },
            new OpticalSurface { Geometry = new PlaneGeometry() }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 2;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 0 });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        Assert.Equal(reflected, optic.SurfaceGroup.Items[1].IsReflective);
        var summary = FocusMetricEvaluator.Evaluate(optic);
        var input = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".optic.json");
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".optic.json");
        try
        {
            await OpticJsonStore.SaveAsync(optic, input);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(input);
            var original = app.Prescription.GetSurfaces()[^2].Thickness;
            var result = await app.Optimization.QuickFocusAsync();
            Assert.Equal(original + summary.BestFocusShift, result.FinalThickness, 12);
            Assert.Equal(reflected ? -1 : 1, Math.Sign(result.FinalThickness));
            Assert.Equal(result.FinalThickness, app.Prescription.GetSurfaces()[^2].Thickness, 12);
            Assert.Equal(result.FinalThickness - original, result.AppliedShift, 12);
            if (defocus == 0) Assert.Equal(0, result.AppliedShift, 12);
            await app.Documents.SaveAsync(output);
            var saved = await OpticJsonStore.LoadAsync(output);
            var actual = FocusMetricEvaluator.Evaluate(saved, focusStep: 0, focusPlaneCount: 1);
            Assert.Equal(actual.BestRmsSpotRadius, result.RmsSpotRadius, 12);
            Assert.InRange(result.RmsSpotRadius, 0, .01);
            Assert.Equal(2, saved.Aperture.Value); Assert.Equal(550, saved.Wavelengths[0].Nanometers);
            if (defocus != 0)
            {
                Assert.True(app.Documents.Undo());
                Assert.Equal(original, app.Prescription.GetSurfaces()[^2].Thickness);
            }
        }
        finally { File.Delete(input); File.Delete(output); }
    }
}
