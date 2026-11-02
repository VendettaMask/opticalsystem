using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

public sealed class ParaxialImageHeightWavefrontTests
{
    [Theory]
    [InlineData(false, false, 480, 0)]
    [InlineData(false, true, 650, 7)]
    [InlineData(true, false, 650, 0)]
    [InlineData(true, true, 480, 7)]
    public void EquivalentIncidentPlaneWavesKeepPhaseAndPsfAcrossFieldDefinitions(
        bool afocal, bool aiming, double primary, double defocus)
    {
        var optic = Optic.CreateBlank();
        var glass = new CauchyMaterial("DispersiveImageMedium", 1.45, .015);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Geometry = new StandardGeometry(40), Thickness = 80 + defocus,
                MaterialAfter = glass, IsStop = true, SemiDiameter = 5 },
            new OpticalSurface { Geometry = new PlaneGeometry(), MaterialAfter = glass }
        ]);
        optic.Aperture.Value = 2;
        optic.ImageSpaceAfocal = afocal;
        optic.RayAimingEnabled = aiming;
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = .2 });
        optic.Wavelengths.Clear();
        foreach (var nanometers in new[] { 480d, 550, 650 })
            optic.Wavelengths.Add(new Wavelength { Nanometers = nanometers, IsPrimary = nanometers == primary });

        // Independent one-sphere paraxial slope n*u=(n'-n)*h/R.
        var slope = .2 * (glass.RefractiveIndex(primary) - 1) / 40;
        var angle = Optic.FromSnapshot(optic.ToSnapshot());
        angle.FieldDefinition = FieldDefinitionKind.Angle;
        angle.Fields[0].Y = Math.Atan(slope) * 180 / Math.PI;
        (double X, double Y)[] pupils = [(0, 0), (0, -.7), (0, .7), (-.4, 0), (.3, .5)];
        foreach (var wavelength in optic.Wavelengths)
        {
            var a = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wavelength, pupils, aiming);
            var b = WavefrontEngine.GenerateChiefRaySamples(angle, (0, 1), wavelength, pupils, aiming);
            for (var i = 0; i < pupils.Length; i++)
            {
                Assert.True(a.Samples[i].Intensity > 0);
                Assert.InRange(Math.Abs(a.Samples[i].OpdWaves - b.Samples[i].OpdWaves), 0, 2e-8);
            }
            var pitch = afocal ? .002 : .0005;
            var psfA = DiffractionEngine.ComputeHuygensPsf(optic, (0, 1), wavelength, 16, 16, pitch, aimAtStop: aiming);
            var psfB = DiffractionEngine.ComputeHuygensPsf(angle, (0, 1), wavelength, 16, 16, pitch, aimAtStop: aiming);
            for (var row = 0; row < 16; row++) for (var col = 0; col < 16; col++)
                Assert.InRange(Math.Abs(psfA.Values[row, col] - psfB.Values[row, col]), 0, 2e-8);
        }
    }
}
