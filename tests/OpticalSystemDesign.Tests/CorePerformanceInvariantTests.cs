using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

public sealed class CorePerformanceInvariantTests
{
    [Fact]
    public void SupportedPreflightDoesNotAllocatePerSurfaceAndStillSeesMaterialEdits()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        for (var i = 0; i < 100; i++)
        {
            OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.RayTrace);
            OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.RayTrace);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.RayTrace);
            OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.RayTrace);
        }
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 160_000);
        var material = surface.MaterialAfter;
        surface.MaterialAfter = new UnresolvedMaterial("missing-one");
        var inspected = OpticCapabilityPreflight.Inspect(optic);
        var error = Assert.Throws<OpticCapabilityException>(() =>
            OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.RayTrace));
        Assert.Equal(inspected, error.Issues);
        Assert.Throws<OpticCapabilityException>(() =>
            OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.RayTrace));
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Visualization);
        surface.MaterialAfter = material;
        OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.RayTrace);
    }

    [Fact]
    public void CoordinateReusePreservesValueIdentitySerializationAndModifiedCopies()
    {
        var pose = new CoordinateSystem(new(3, -4, 9), 13, -27, 41);
        var equal = new CoordinateSystem(pose.Origin, 13, -27, 41);
        var json = JsonSerializer.Serialize(pose);
        var hash = pose.GetHashCode();
        var vector = new Vector3D(.1, -.7, 1.2);
        var expected = pose.ToGlobalDirection(vector);
        Parallel.For(0, 512, _ => Assert.Equal(expected, pose.ToGlobalDirection(vector)));
        Assert.Equal(equal, pose);
        Assert.Equal(hash, pose.GetHashCode());
        Assert.Equal(json, JsonSerializer.Serialize(pose));
        var changed = pose with { RotationYDegrees = 62, Origin = new(7, 8, -2) };
        var fresh = new CoordinateSystem(new(7, 8, -2), 13, 62, 41);
        Assert.Equal(fresh.ToGlobalDirection(vector), changed.ToGlobalDirection(vector));
        Assert.NotEqual(expected, changed.ToGlobalDirection(vector));
        var roundTrip = changed.ToLocalPoint(changed.ToGlobalPoint(vector));
        Assert.InRange((roundTrip - vector).Length, 0, 1e-14);
        Assert.Equal(fresh, JsonSerializer.Deserialize<CoordinateSystem>(JsonSerializer.Serialize(changed)));
    }
}
