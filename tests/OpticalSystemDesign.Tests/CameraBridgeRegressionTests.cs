using Avalonia.Controls;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.NonSequential;
using NonSequentialObjectKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;
using PlaneRectangleParameters = OpticalSystemDesign.Core.NonSequential.PlaneRectangleParameters;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CameraBridgeRegressionTests
{
    [Theory]
    [InlineData("scene3d-depth", false)]
    [InlineData("scene3d-power", false)]
    [InlineData("scene3d-guard", true)]
    [InlineData("scene3d-psf-radius", true)]
    public async Task InvalidSceneTextDoesNotEscapeInputEventsAndCanBeCorrected(string key, bool diffraction)
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(async () =>
        {
            using var app = WorkbenchApplication.Create("cooke"); var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); window.UpdateLayout();
                T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == "camera-" + name);
                Find<ComboBox>("source").SelectedIndex = (int)CameraSource.Scene3D;
                Find<ComboBox>("size").SelectedIndex = 4;
                await window.RunAsync(); window.PinComparison(); var a = window.Comparison; Assert.NotNull(a);
                if (diffraction) Find<ComboBox>("aberration").SelectedIndex = 1;
                var input = Find<NumericUpDown>(key); var original = input.Value;
                foreach (var text in new[] { "not-a-number", "", "-" })
                {
                    input.Text = text; window.UpdateLayout();
                    Assert.False(Find<Button>("run").IsEnabled); Assert.Null(window.Result); Assert.Same(a, window.Comparison);
                    input.Value = original; input.Text = original!.Value.ToString(input.FormatString, input.NumberFormat);
                    window.UpdateLayout(); Assert.True(Find<Button>("run").IsEnabled);
                }
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BothSceneRenderersRejectCurvedImageSurfaces(bool diffraction)
    {
        var lens = CameraSamplingTests.Lens(); lens.SurfaceGroup.Items[^1].Geometry = new StandardGeometry(1);
        var plane = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, -100)), Parameters = new PlaneRectangleParameters(100, 100) };
        var scene = new DirectCameraScene(new("control", [new("green", 550, 1, true)], [plane]),
            [new DirectSceneMaterial(plane.Id, [0], [1])], []);
        var error = Assert.Throws<NotSupportedException>(() =>
        {
            if (diffraction) DiffractionCameraRenderer.Render(lens, scene, new(4, 4, 3), new(4, 1, 1, 2));
            else DirectCameraRenderer.Render(lens, scene, new(4, 4, 3));
        });
        Assert.Contains("平面像面", error.Message);
    }
}
