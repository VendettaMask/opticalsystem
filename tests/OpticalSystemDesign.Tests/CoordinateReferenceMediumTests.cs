using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class CoordinateReferenceMediumTests
{
    [Theory]
    [InlineData("bire-tilted-0", false)] [InlineData("bire-tilted-0", true)]
    [InlineData("bire-tilted-1", false)] [InlineData("bire-tilted-1", true)]
    [InlineData("bire-axial-0", false)] [InlineData("bire-axial-0", true)]
    [InlineData("bire-axial-1", false)] [InlineData("bire-axial-1", true)]
    [InlineData("bire-glass-0", false)] [InlineData("bire-glass-0", true)]
    [InlineData("bire-glass-1", false)] [InlineData("bire-glass-1", true)]
    public void RotatedReferencePlanesPreserveCrystalMomentumAndNativeOpticalPath(string id, bool batched)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "birefringent-native-v1.json")));
        var data = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        var original = OpticalFormatCatalog.Import(data.GetProperty("source").GetString()!, ".zmx");
        var modified = InsertReferencePlanes(original, 2);
        foreach (var optic in new[] { modified, modified.Clone(), Optic.FromSnapshot(modified.ToSnapshot()) })
        {
            Assert.Equal(2, optic.SurfaceGroup.Items.Count(s => s.InteractionModel is CoordinateReferenceInteractionModel));
            foreach (var group in data.GetProperty("rows").EnumerateArray().GroupBy(r =>
                (Wave: r.GetProperty("input").GetProperty("int2").GetInt32(),
                 Hy: r.GetProperty("input").GetProperty("data2").GetDouble(),
                 Px: r.GetProperty("input").GetProperty("data3").GetDouble(),
                 Py: r.GetProperty("input").GetProperty("data4").GetDouble())))
            {
                var rays = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, group.Key.Hy, group.Key.Px, group.Key.Py,
                    optic.Wavelengths[group.Key.Wave - 1].Micrometers);
                using var trace = optic.SequentialRayTracer.Trace(rays, TraceRequest.FullHistory() with { UseBatchedBackend = batched });
                Assert.True(trace.TryGetSample(0, 1, out var first));
                var nativeFirst = group.Single(r => r.GetProperty("input").GetProperty("int1").GetInt32() == 1
                    && r.GetProperty("input").GetProperty("code").GetString() == "OPTH").GetProperty("value").GetDouble();
                foreach (var row in group)
                {
                    var p = row.GetProperty("input"); var oldSurface = p.GetProperty("int1").GetInt32();
                    var index = oldSurface < 3 ? oldSurface : oldSurface + 2;
                    Assert.True(trace.TryGetSample(0, index, out var sample)); Assert.False(sample.Vignetted);
                    var frame = optic.SurfaceGroup.Items[index].CoordinateSystem;
                    var position = frame.ToLocalPoint(sample.Position); var direction = frame.ToLocalDirection(sample.Direction);
                    var code = p.GetProperty("code").GetString();
                    var actual = code switch
                    {
                        "REAX" => position.X, "REAY" => position.Y, "REAZ" => position.Z,
                        "REAA" => direction.X, "REAB" => direction.Y, "REAC" => direction.Z,
                        "OPTH" => sample.PhaseInclusiveOpticalPathLength!.Value - first.PhaseInclusiveOpticalPathLength!.Value,
                        _ => throw new InvalidDataException(code)
                    };
                    var expected = row.GetProperty("value").GetDouble() - (code == "OPTH" ? nativeFirst : 0);
                    Assert.InRange(Math.Abs(actual - expected), 0, code == "OPTH" ? 1e-7 : code is "REAA" or "REAB" or "REAC" ? 1e-11 : 1e-9);
                }
            }
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReferencePlanesRetainTheGradientVolumeFrameAndLocalIntegrationPosition(bool batched)
    {
        var original = OpticalFormatCatalog.Import("MODE SEQ\nENPD .2\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE GRINSUR1\nPARM 1 .05\nPARM 2 1.5\nPARM 3 -.083\nPARM 4 0\nDISZ 4\nSURF 2\nDISZ 1\nSURF 3", ".zmx");
        var modified = InsertReferencePlanes(original, 1);
        foreach (var optic in new[] { modified, modified.Clone() })
        {
            var rays = new RealRayBundle([new RealRay(new(.1, 0, -1), new(0, 0, 1), 550)]);
            using var before = original.SequentialRayTracer.Trace(rays, TraceRequest.FinalOnly(false) with { UseBatchedBackend = batched });
            using var after = optic.SequentialRayTracer.Trace(rays, TraceRequest.FinalOnly(false) with { UseBatchedBackend = batched });
            Assert.True(before.TryGetSample(0, original.SurfaceGroup.Items.Count - 1, out var a));
            Assert.True(after.TryGetSample(0, optic.SurfaceGroup.Items.Count - 1, out var b));
            Assert.False(b.Vignetted);
            Assert.InRange((a.Position - b.Position).Length, 0, 1e-7);
            Assert.InRange((a.Direction - b.Direction).Length, 0, 1e-7);
            Assert.InRange(Math.Abs(a.PhaseInclusiveOpticalPathLength!.Value - b.PhaseInclusiveOpticalPathLength!.Value), 0, 1e-7);
        }
    }

    [Fact]
    public void CrystalFramesAndExplicitInterfacesRemainRequiredAtPhysicalBoundaries()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "birefringent-native-v1.json")));
        var source = fixture.RootElement.GetProperty("cases")[0].GetProperty("source").GetString()!;
        var optic = OpticalFormatCatalog.Import(source, ".zmx"); var entrance = optic.SurfaceGroup.Items[2];
        var exit = optic.SurfaceGroup.Items[3].Clone();
        var ray = new RealRay(entrance.CoordinateSystem.Origin, new(0, 0, 1), 550);
        Assert.Throws<NotSupportedException>(() => exit.TraceRay(ray, entrance.MaterialAfter, exit.MaterialAfter, 0, 0));
        exit.InteractionModel = new RefractiveReflectiveInteractionModel();
        Assert.Throws<NotSupportedException>(() => exit.TraceRay(ray, entrance.MaterialAfter, exit.MaterialAfter, 0, 0, entrance.CoordinateSystem));
    }

    private static Optic InsertReferencePlanes(Optic original, int entranceIndex)
    {
        var snapshot = original.ToSnapshot(); var entrance = snapshot.Surfaces[entranceIndex];
        var length = entrance.Thickness / 3; var coordinates = entrance.CoordinateSystem!;
        var surfaces = snapshot.Surfaces.ToList(); surfaces[entranceIndex] = entrance with { Thickness = length };
        for (var i = 1; i <= 2; i++)
        {
            var component = entrance.Components!;
            var reference = entrance with
            {
                Label = "Rotated volume reference", Radius = 0, Conic = 0, Thickness = length, IsStop = false,
                CoordinateSystem = new(coordinates.OriginX + .03, coordinates.OriginY - .02, coordinates.OriginZ + i * length, 13 * i, -7 * i, 19 * i),
                SemiDiameterDefinesPhysicalAperture = false,
                Components = component with
                {
                    GeometryKind = "plane", Geometry = ComponentSnapshot.Empty("plane"),
                    MaterialBefore = component.MaterialAfter, MaterialBeforeComponent = component.MaterialAfterComponent,
                    InteractionKind = "coordinate_reference", Interaction = ComponentSnapshot.Empty("coordinate_reference"),
                    PhysicalApertureKind = null, PhysicalAperture = null
                }
            };
            surfaces.Insert(entranceIndex + i, reference);
        }
        return Optic.FromSnapshot(snapshot with { Surfaces = surfaces.Select((s, i) => s with { Number = i }).ToList(), MeritOperands = [] });
    }
}
