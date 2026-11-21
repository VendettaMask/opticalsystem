using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Visualization;

namespace OpticalSystemDesign.Tests;

public sealed class SurfaceDrawingSettingsTests
{
    [Fact]
    public void DefaultSnapshotsRemainCompatibleAndMalformedDrawingDataIsRejected()
    {
        var snapshot = Optic.CreateDemo().ToSnapshot();
        Assert.DoesNotContain("\"Drawing\"", JsonSerializer.Serialize(snapshot));
        Assert.All(Optic.FromSnapshot(snapshot).SurfaceGroup.Items, s => Assert.Equal(SurfaceDrawingOptions.Default, s.Drawing));
        snapshot.Surfaces[1] = snapshot.Surfaces[1] with { Drawing = new(Opacity: -0.1) };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
        snapshot.Surfaces[1] = snapshot.Surfaces[1] with { Drawing = new(RowColor: "#NOTRGB") };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
        snapshot.Surfaces[1] = snapshot.Surfaces[1] with { Drawing = new(Precision: (SurfaceDrawingPrecision)999) };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
    }

    [Fact]
    public async Task SettingsSurviveUndoRedoAndProjectReopenWithoutChangingPhysicalRays()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = await app.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        var options = new SurfaceDrawingOptionsDto(Opacity: 0.375, HideSurface: true, HideEdges: true,
            DrawLocalAxes: true, DrawLocalOrigin: true, EdgeMode: SurfaceEdgeModeDto.DirectToNext,
            Precision: SurfaceDrawingPrecisionDto.High, RowColor: "#aBc123");
        app.Prescription.UpdateSurfaceDrawingSettings(1, options);
        var expected = options with { RowColor = "#ABC123" };
        Assert.Equal(expected, app.Prescription.GetSurfaces()[1].Drawing);
        var after = await app.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        Assert.Equal(JsonSerializer.Serialize(before.ThreeDimensional!.Rays), JsonSerializer.Serialize(after.ThreeDimensional!.Rays));
        var surface = after.ThreeDimensional.Surfaces.Single(s => s.SurfaceNumber == 1);
        Assert.Equal(0.375, surface.Opacity); Assert.True(surface.HideSurface && surface.HideEdges);
        Assert.True(surface.LocalFrame is { DrawAxes: true, DrawOrigin: true });
        Assert.True(app.Documents.Undo());
        Assert.Equal(new SurfaceDrawingOptionsDto(), app.Prescription.GetSurfaces()[1].Drawing);
        Assert.True(app.Documents.Redo());
        Assert.Equal(expected, app.Prescription.GetSurfaces()[1].Drawing);
        var saved = Path.Combine(Path.GetTempPath(), $"surface-drawing-{Guid.NewGuid():N}.staropt");
        try
        {
            await app.Documents.SaveAsync(saved); app.Documents.NewBlank(); await app.Documents.OpenAsync(saved);
            Assert.Equal(expected, app.Prescription.GetSurfaces()[1].Drawing);
        }
        finally { if (File.Exists(saved)) File.Delete(saved); }
    }

    [Fact]
    public void HideAndSkipRaysAffectOnlyDrawingSegmentsAndPreserveTraceIntensity()
    {
        var optic = Optic.CreateDemo();
        var builder = new Layout2DBuilder(optic);
        var baseline = builder.Build3D();
        Assert.NotEmpty(baseline.Rays);
        var target = baseline.Rays[0].Segments.First(s => s.SourceSurfaceNumber is > 0 && s.TargetSurfaceNumber is > 1).TargetSurfaceNumber!.Value;
        var selected = optic.SurfaceGroup.Items.Single(s => s.Number == target);
        selected.Drawing = new(HideRays: true);
        var hidden = builder.Build3D();
        Assert.Equal(baseline.Rays.Count, hidden.Rays.Count);
        Assert.All(hidden.Rays, ray => Assert.DoesNotContain(ray.Segments, segment => segment.TargetSurfaceNumber == target));
        Assert.Equal(baseline.Rays.Select(r => r.FinalIntensity), hidden.Rays.Select(r => r.FinalIntensity));
        selected.Drawing = new(SkipRayDrawing: true);
        var skipped = builder.Build3D();
        Assert.All(skipped.Rays, ray =>
        {
            Assert.DoesNotContain(ray.Segments, segment => segment.TargetSurfaceNumber == target || segment.SourceSurfaceNumber == target);
            Assert.Contains(ray.Segments, segment => segment.SourceSurfaceNumber == target - 1 && segment.TargetSurfaceNumber == target + 1);
        });
        Assert.Equal(baseline.Rays.Select(r => r.FinalIntensity), skipped.Rays.Select(r => r.FinalIntensity));
        foreach (var surface in optic.SurfaceGroup.Items) surface.Drawing = new(HideRays: true);
        Assert.Empty(builder.Build3D().Rays);
        Assert.Empty(builder.Build().Rays);
    }

    [Fact]
    public void ResolutionAndEdgeStyleChangeActualGeometryAndCanBeReset()
    {
        var optic = Optic.CreateDemo(); var builder = new Layout2DBuilder(optic);
        var baseline = builder.Build();
        var firstEdge = baseline.LensEdges.First();
        var front = optic.SurfaceGroup.Items.Single(s => s.Number == firstEdge.FrontSurfaceNumber);
        var back = optic.SurfaceGroup.Items.Single(s => s.Number == firstEdge.BackSurfaceNumber);
        front.SemiDiameter = 5; back.SemiDiameter = 3;
        var square = builder.Build();
        front.Drawing = new(EdgeMode: SurfaceEdgeMode.DirectToNext);
        var tapered = builder.Build();
        var taperedEdge = tapered.LensEdges.First(e => e.FrontSurfaceNumber == front.Number);
        Assert.NotEqual(Math.Abs(taperedEdge.Start.Y), Math.Abs(taperedEdge.End.Y));
        front.Drawing = new(EdgeMode: SurfaceEdgeMode.None);
        Assert.DoesNotContain(builder.Build().LensEdges, e => e.FrontSurfaceNumber == front.Number);
        front.Drawing = new(HideEdges: true);
        Assert.DoesNotContain(builder.Build().LensEdges, e => e.FrontSurfaceNumber == front.Number);
        var withoutShoulders = builder.Build3D(options: new(RayCount: 0)).LensElements.First(e => e.FrontSurfaceNumber == front.Number);
        static double Radius(Layout3DPoint point) => Math.Sqrt(point.X * point.X + point.Y * point.Y);
        Assert.Equal(5, withoutShoulders.FrontRim.Max(Radius), 8);
        Assert.Equal(3, withoutShoulders.BackRim.Max(Radius), 8);
        front.Drawing = new(EdgeMode: SurfaceEdgeMode.FlatToNext);
        var flat = builder.Build().LensEdges.First(e => e.FrontSurfaceNumber == front.Number);
        Assert.Equal(Math.Abs(flat.Start.Y), Math.Abs(flat.End.Y), 10);
        var counts = new List<int>();
        foreach (var precision in Enum.GetValues<SurfaceDrawingPrecision>())
        {
            front.Drawing = new(Precision: precision);
            counts.Add(builder.Build().Surfaces.Single(s => s.SurfaceNumber == front.Number).Points.Count);
        }
        Assert.True(counts.Zip(counts.Skip(1), (a, b) => b > a).All(value => value));
        front.Drawing = SurfaceDrawingOptions.Default;
        Assert.Equal(square.Surfaces.Single(s => s.SurfaceNumber == front.Number).Points.Count,
            builder.Build().Surfaces.Single(s => s.SurfaceNumber == front.Number).Points.Count);
    }

    [Fact]
    public void LocalFrameUsesActualDecenteredRotatedSurfaceCoordinates()
    {
        var optic = Optic.CreateDemo(); var surface = optic.SurfaceGroup.Items[1];
        surface.CoordinateSystem = new CoordinateSystem(new Vector3D(3, 4, 5), 19, 27, 43);
        surface.Drawing = new(DrawLocalAxes: true, DrawLocalOrigin: true);
        var scene = new Layout2DBuilder(optic).Build3D(options: new(RayCount: 0));
        var frame = scene.Surfaces.Single(s => s.SurfaceNumber == 1).LocalFrame!;
        Assert.Equal(new Layout3DPoint(3, 4, 5), frame.Origin);
        var direction = surface.CoordinateSystem.ToGlobalDirection(new Vector3D(0, 0, 1));
        var delta = new Vector3D(frame.ZAxisEnd.X - 3, frame.ZAxisEnd.Y - 4, frame.ZAxisEnd.Z - 5);
        var actual = delta / delta.Length;
        Assert.Equal(direction.X, actual.X, 10); Assert.Equal(direction.Y, actual.Y, 10); Assert.Equal(direction.Z, actual.Z, 10);
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-0.01)] [InlineData(1.01)]
    public void InvalidDrawingOptionsAreRejectedBeforeMutatingTheDocument(double opacity)
    {
        using var app = WorkbenchApplication.Create("cooke"); var revision = app.Events.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => app.Prescription.UpdateSurfaceDrawingSettings(1, new(Opacity: opacity)));
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(new SurfaceDrawingOptionsDto(), app.Prescription.GetSurfaces()[1].Drawing);
    }
}
