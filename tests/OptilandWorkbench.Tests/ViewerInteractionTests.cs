using Avalonia;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.Tests;

public sealed class ViewerInteractionTests
{
    [Fact]
    public void SceneViewportMovesAxisAndContentTogether()
    {
        var viewport = new SceneViewport();
        var size = new Size(800, 500);
        var axisPoint = new Point(200, 250);
        var lensPoint = new Point(420, 180);
        var delta = new Vector(37, -24);

        var axisBefore = viewport.Apply(axisPoint, size);
        var lensBefore = viewport.Apply(lensPoint, size);
        viewport.PanBy(delta);

        Assert.Equal(axisBefore + delta, viewport.Apply(axisPoint, size));
        Assert.Equal(lensBefore + delta, viewport.Apply(lensPoint, size));
    }

    [Fact]
    public void SceneViewportZoomKeepsPointerAnchorStable()
    {
        var viewport = new SceneViewport();
        var size = new Size(800, 500);
        var scenePoint = new Point(620, 140);
        viewport.PanBy(new Vector(31, -17));
        var anchor = viewport.Apply(scenePoint, size);

        viewport.ZoomAt(1.8, anchor, size);

        Assert.Equal(anchor.X, viewport.Apply(scenePoint, size).X, precision: 10);
        Assert.Equal(anchor.Y, viewport.Apply(scenePoint, size).Y, precision: 10);
    }

    [Fact]
    public void DrawingPreviewZoomButtonsAndResetUpdateZoom()
    {
        var preview = new DrawingPreviewControl
        {
            Width = 800,
            Height = 600
        };

        preview.ZoomIn();
        Assert.Equal(1.25, preview.Zoom, precision: 10);

        preview.ZoomOut();
        Assert.Equal(1, preview.Zoom, precision: 10);

        preview.ZoomIn();
        preview.ResetView();
        Assert.Equal(1, preview.Zoom, precision: 10);
    }

    [Fact]
    public void CutawayClipsEveryRaySegmentToTheRetainedHalfSpace()
    {
        var paths = OpticSceneControl.ClipPolylineToCutaway(new[]
        {
            new ScenePoint3Dto(2, 0, 0),
            new ScenePoint3Dto(-2, 4, 4),
            new ScenePoint3Dto(2, 8, 8),
            new ScenePoint3Dto(-2, 12, 12)
        });

        Assert.Equal(2, paths.Count);
        Assert.Equal(3, paths[0].Count);
        Assert.Equal(2, paths[1].Count);
        Assert.All(paths.SelectMany(path => path), point => Assert.True(point.X <= 1e-9));
        Assert.Equal(0, paths[0][0].X, precision: 12);
        Assert.Equal(0, paths[0][^1].X, precision: 12);
        Assert.Equal(0, paths[1][0].X, precision: 12);
    }

    [Fact]
    public void SolidRenderingKeepsCurvedSurfaceTessellation()
    {
        var center = new ScenePoint3Dto(0, 0, 1);
        var rim = new[]
        {
            new ScenePoint3Dto(-1, -1, 0),
            new ScenePoint3Dto(1, -1, 0),
            new ScenePoint3Dto(1, 1, 0),
            new ScenePoint3Dto(-1, 1, 0)
        };
        var surface = new SceneSurface3Dto(
            1,
            "S1",
            false,
            false,
            "N-BK7",
            rim,
            Array.Empty<ScenePoint3Dto>(),
            Array.Empty<ScenePoint3Dto>(),
            new[]
            {
                new SceneSurfaceFace3Dto(new[] { center, rim[0], rim[1] }),
                new SceneSurfaceFace3Dto(new[] { center, rim[1], rim[2] })
            });

        var faces = OpticSceneControl.SurfaceFacesForRendering(surface, cutawayEnabled: false);

        Assert.Equal(2, faces.Count);
        Assert.All(faces, face => Assert.Equal(3, face.Count));
        Assert.All(faces, face => Assert.Contains(center, face));
    }
}
