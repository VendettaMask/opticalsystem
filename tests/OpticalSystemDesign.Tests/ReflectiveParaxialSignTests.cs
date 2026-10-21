using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Tests;

public sealed class ReflectiveParaxialSignTests
{
    [Theory]
    [InlineData(-200, false, 100)]
    [InlineData(200, false, -100)]
    [InlineData(-200, true, 100)]
    [InlineData(200, true, -100)]
    public void MirrorFocalLengthFollowsPropagationAndRetainsDivergingPower(double radius, bool secondMirror, double expected)
    {
        var optic = Mirror(radius, secondMirror);
        Assert.Equal(expected, optic.Paraxial.EstimateEffectiveFocalLength(), 12);
        Assert.Equal(1 / expected, optic.Paraxial.EstimateOpticalPower(), 14);
        Assert.Equal(expected, optic.Paraxial.EstimateEffectiveFocalLengthBetweenSurfaces(1, secondMirror ? 2 : 1), 12);
        Assert.Equal(expected, optic.Paraxial.EstimateCardinalPoints().EffectiveFocalLength, 12);
        var marginal = optic.Paraxial.TraceGeneric([1d], [0d], 0, .55);
        var slope = marginal.Slopes[^1][0];
        // The second plane mirror reverses the axial slope without adding power.
        Assert.Equal(secondMirror ? 2 / radius : -2 / radius, slope, 12);
    }

    [Theory]
    [InlineData(-200)]
    [InlineData(200)]
    public void MirrorPrincipalAndNodalPlanesStayAtTheVertex(double radius)
    {
        var cardinal = Mirror(radius, false).Paraxial.EstimateCardinalPoints();
        Assert.Equal(radius / 2, cardinal.FrontFocalPosition, 12);
        Assert.Equal(radius / 2, cardinal.BackFocalPosition, 12);
        Assert.Equal(0, cardinal.FrontPrincipalPlanePosition, 12);
        Assert.Equal(0, cardinal.BackPrincipalPlanePosition, 12);
        Assert.Equal(0, cardinal.FrontNodalPlanePosition, 12);
        Assert.Equal(0, cardinal.BackNodalPlanePosition, 12);
    }

    [Theory]
    [InlineData(50, 1)]
    [InlineData(-50, 1)]
    [InlineData(50, 1.5)]
    public void ReflectiveThinLensMatrixMatchesItsIndependentParaxialInteraction(double focalLength, double index)
    {
        var optic = Mirror(-200, false);
        var lens = new ThinLensInteractionModel(focalLength, isReflective: true);
        optic.SurfaceGroup.Items[0].MaterialAfter = new ConstantIndexMaterial("Incident", index);
        optic.SurfaceGroup.Items[1].InteractionModel = lens;
        optic.SurfaceGroup.Items[1].Thickness = 0;
        optic.SurfaceGroup.Items[2].MaterialAfter = new ConstantIndexMaterial("Incident", index);
        var trace = optic.Paraxial.TraceGeneric([2d], [.03], 0, .55);
        var interaction = lens.Interact(new ParaxialRay(2, .03, 0, 550),
            new SurfaceInteractionContext(new(0, 0, 1), index, index, 550, true));
        Assert.Equal(interaction.Angle, trace.Slopes[1][0], 12);
        Assert.Equal(-.03 + 2 / (index * focalLength), trace.Slopes[1][0], 12);
        Assert.Equal(index * focalLength, optic.Paraxial.EstimateEffectiveFocalLength(), 12);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ImmersedSphericalInterfaceKeepsPrincipalAndNodalPlanesDistinct(double imageIndex)
    {
        var optic = Optic.CreateBlank();
        var glass = new ConstantIndexMaterial("ImageMedium", imageIndex);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Geometry = new StandardGeometry(100), MaterialAfter = glass },
            new OpticalSurface { Geometry = new PlaneGeometry(), MaterialAfter = glass }
        ]);
        var points = optic.Paraxial.EstimateCardinalPoints();
        Assert.Equal(imageIndex * 100 / (imageIndex - 1), points.EffectiveFocalLength, 10);
        Assert.Equal(0, points.FrontPrincipalPlanePosition, 10);
        Assert.Equal(0, points.BackPrincipalPlanePosition, 10);
        Assert.Equal(100, points.FrontNodalPlanePosition, 10);
        Assert.Equal(100, points.BackNodalPlanePosition, 10);
    }

    [Theory]
    [InlineData(0, .001, false)]
    [InlineData(100, .001, false)]
    [InlineData(-100, -.001, false)]
    [InlineData(-200, .001, true)]
    [InlineData(200, -.001, true)]
    [InlineData(100, -.005, false)]
    public void QuadraticAspherePowerMatchesTheVertexNormalDerivative(double baseRadius, double a2, bool reflective)
    {
        var optic = Mirror(-200, false);
        var geometry = new EvenAsphereGeometry(baseRadius, -1.5, [a2, 1e-6]);
        var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = geometry;
        surface.IsReflective = reflective;
        surface.MaterialAfter = new ConstantIndexMaterial("After", reflective ? 1 : 1.5);
        optic.SurfaceGroup.Items[2].MaterialAfter = surface.MaterialAfter;
        var c = (baseRadius == 0 ? 0 : 1 / baseRadius) + 2 * a2;
        var power = reflective ? -2 * c : c / 3;
        Assert.Equal(power, optic.Paraxial.EstimateOpticalPower(), 13);
        Assert.Equal(power == 0 ? 0 : 1 / power, optic.Paraxial.EstimateEffectiveFocalLength(), 10);
        var h = 1e-5;
        var normal = geometry.SurfaceNormal(new(h, 0, geometry.Sag(h, 0)));
        Assert.InRange(Math.Abs(-normal.X / normal.Z / h - c), 0, 1e-14);
        var trace = optic.Paraxial.TraceGeneric([h], [0d], 0, .55);
        Assert.Equal((reflective ? 1 : -1) * h * power, trace.Slopes[1][0], 14);
    }

    private static Optic Mirror(double radius, bool secondMirror)
    {
        var optic = Optic.CreateBlank();
        var surfaces = new List<OpticalSurface>
        {
            new() { Thickness = double.PositiveInfinity },
            new() { Geometry = new StandardGeometry(radius), IsReflective = true, IsStop = true }
        };
        if (secondMirror) surfaces.Add(new OpticalSurface { Geometry = new PlaneGeometry(), IsReflective = true });
        surfaces.Add(new OpticalSurface { Geometry = new PlaneGeometry() });
        optic.SurfaceGroup.Replace(surfaces);
        return optic;
    }
}
