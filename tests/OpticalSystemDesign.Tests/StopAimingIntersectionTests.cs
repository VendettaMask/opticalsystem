using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class StopAimingIntersectionTests
{
    [Fact]
    public void FarFromConvergedStationaryRayDoesNotSearchThousandsOfAdjacentLaunches()
    {
        var map = new StationaryStopMap(); var x = 0d; var y = 0d;
        Assert.False(StopAimingSolver.Solve(ref map, 0, 0, 1e-12, ref x, ref y, out var residual));
        Assert.Equal(1, residual);
        Assert.InRange(map.Samples, 1, 100);
    }

    private struct StationaryStopMap : IStopMap
    {
        public int Samples;
        public bool Sample(double x, double y, out Vector3D point)
        {
            Samples++;
            point = new(1 + Math.Abs(x), y, 0);
            return true;
        }
    }

    [Theory]
    [InlineData(90, 5)]
    [InlineData(-90, -5)]
    public void MissingStopCannotBeAcceptedAtThePreviousRayPosition(double tilt, double targetX)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Geometry = new PlaneGeometry(), Thickness = 5 },
            new OpticalSurface { Geometry = new PlaneGeometry(), Thickness = 10, IsStop = true,
                SemiDiameter = 10, CoordinateSystem = new CoordinateSystem(Vector3D.Zero, RotationYDegrees: tilt) },
            new OpticalSurface { Geometry = new PlaneGeometry() }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 2;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint());
        // The chief is parallel to this stop. A miss record retains the object
        // position, whose stop-local X happens to equal the requested target.
        // It is still a failed intersection, not a converged aiming solution.
        var ray = Assert.Single(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            0, 0, .55, [new PupilSample(0, 0, 1)], aimAtStop: true,
            stopTargets: [(targetX, 0)]).Rays);
        Assert.Equal(0, ray.Intensity);
    }
}
