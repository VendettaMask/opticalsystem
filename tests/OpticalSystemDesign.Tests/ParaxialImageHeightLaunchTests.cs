using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

public sealed class ParaxialImageHeightLaunchTests
{
    [Theory]
    [InlineData(1, 1.5, 40, .2, 0)]
    [InlineData(1, 1.5, 40, -.2, 7)]
    [InlineData(1, 2, 40, .2, 0)]
    [InlineData(1, 2, -40, -.2, 7)]
    [InlineData(1.33, 1.5, 40, .2, 7)]
    [InlineData(1.5, 1, 40, -.2, 7)]
    public void ImmersedImageHeightUsesThePrimaryFocalPlane(double objectIndex, double imageIndex, double radius,
        double fieldHeight, double defocus)
    {
        var glass = new ConstantIndexMaterial("ImageMedium", imageIndex);
        // One sphere has f' = n' R / (n' - n). A vertex ray leaves with
        // slope n*u/n', hence its focal-plane height is n*R*u/(n'-n).
        var focus = imageIndex * radius / (imageIndex - objectIndex);
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(),
                MaterialAfter = new ConstantIndexMaterial("ObjectMedium", objectIndex), SemiDiameter = 5 },
            new OpticalSurface { Geometry = new StandardGeometry(radius), Thickness = focus + defocus,
                MaterialAfter = glass, SemiDiameter = 5, IsStop = true },
            new OpticalSurface { Geometry = new PlaneGeometry(), MaterialAfter = glass, SemiDiameter = 5 }
        ]);
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 1;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Y = fieldHeight });
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });

        var expectedSlope = fieldHeight * (imageIndex - objectIndex) / (objectIndex * radius);
        var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, Math.Sign(fieldHeight), 0, 0, .55).Rays[0];
        Assert.Equal(expectedSlope, ray.Direction.Y / ray.Direction.Z, 12);
        var paraxial = optic.Paraxial.ChiefRay(.55);
        Assert.Equal(expectedSlope, paraxial.Slopes[0][0], 12);
        Assert.Equal(fieldHeight, optic.Paraxial.ParaxialImageHeight(.55), 10);
        Assert.Equal(fieldHeight + defocus * objectIndex * expectedSlope / imageIndex, paraxial.Heights[^1][0], 10);
    }

    [Theory]
    [InlineData(-40, .2, 0)]
    [InlineData(-40, -.2, 7)]
    [InlineData(40, .2, -7)]
    public void ReflectedImageHeightKeepsTheFocalPlaneSign(double radius, double fieldHeight, double defocus)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Geometry = new StandardGeometry(radius), Thickness = radius / 2 + defocus,
                IsReflective = true, SemiDiameter = 5, IsStop = true },
            new OpticalSurface { Geometry = new PlaneGeometry(), Thickness = 0 }
        ]);
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Y = fieldHeight });
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        // Reflection changes the vertex-ray slope to -u. At z=R/2,
        // h=-R*u/2, independently of the actual image-plane position.
        var expectedSlope = -2 * fieldHeight / radius;
        var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, Math.Sign(fieldHeight), 0, 0, .55).Rays[0];
        Assert.Equal(expectedSlope, ray.Direction.Y / ray.Direction.Z, 12);
        Assert.Equal(fieldHeight, optic.Paraxial.ParaxialImageHeight(.55), 10);
    }

    [Theory]
    [InlineData(480)]
    [InlineData(650)]
    public void EveryWavelengthUsesTheSelectedPrimaryConjugate(double primaryNanometers)
    {
        var glass = new CauchyMaterial("DispersiveImageMedium", 1.45, .015);
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Geometry = new StandardGeometry(40), Thickness = 80,
                MaterialAfter = glass, IsStop = true },
            new OpticalSurface { Geometry = new PlaneGeometry(), MaterialAfter = glass, Thickness = 0 }
        ]);
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Y = .2 });
        optic.Wavelengths.Clear();
        foreach (var wavelength in new[] { 480d, 550, 650 })
            optic.Wavelengths.Add(new Wavelength { Nanometers = wavelength, IsPrimary = wavelength == primaryNanometers });
        var expectedSlope = .2 * (glass.RefractiveIndex(primaryNanometers) - 1) / 40;
        foreach (var wavelength in optic.Wavelengths)
        {
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, 0, 0, wavelength.Micrometers).Rays[0];
            Assert.Equal(expectedSlope, ray.Direction.Y / ray.Direction.Z, 12);
            Assert.Equal(expectedSlope, optic.Paraxial.ChiefRay(wavelength.Micrometers).Slopes[0][0], 12);
        }
    }
}
