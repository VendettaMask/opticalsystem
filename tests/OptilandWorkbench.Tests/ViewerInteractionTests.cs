using Avalonia;
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
}
