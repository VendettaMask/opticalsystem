using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class BeamSplitterLayoutWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light", "zh-CN")]
    [InlineData("Dark", "en-US")]
    [InlineData("Isekai", "en-US")]
    [InlineData("Lumen", "ja-JP")]
    [InlineData("Pixel", "zh-CN")]
    public async Task MaterialMatrixAndBothRenderedBranchesUseRealImportedConfigurations(string theme, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(BeamSplitterLayoutWindowTests));
        await session.Dispatch(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            var previousSettings = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            using var files = new CameraSensorFileTests.TemporaryFiles();
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", files.Path("settings"));
            Window? window = null;
            try
            {
                ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language);
                using var app = WorkbenchApplication.Create("blank");
                await app.Documents.OpenAsync(BeamSplitterConfigurationRepairTests.SourcePath);
                using var matrix = new MultiConfigurationPanel(app.Prescription, app.MultiConfiguration, app.Events);
                window = new Window { Width = 1160, Height = 920, Content = matrix };
                window.Show(); Tick(window);
                var grid = Find<DataGrid>(matrix, "MultiConfigurationOperands");
                Assert.Equal(15, grid.ItemsSource!.Cast<object>().Count());
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "GLSS");
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "5/3");
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "6/2");
                Capture(window, theme + "-matrix");
                grid.SelectedIndex = 1; Tick(window);
                Find<Button>(matrix, "ToggleMultiConfigurationProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                var config = Input(matrix, UiLocalization.Text("编辑配置号（从 1 开始）"));
                config.Text = "2"; Tick(window);
                var value = Input(matrix, UiLocalization.Text("多配置单元格值"));
                Assert.Equal("MIRROR", value.Text);
                Assert.False(Find<Button>(matrix, "SetMultiConfigurationVariable").IsEnabled);
                Find<Expander>(matrix, "MultiConfigurationPickupEditor").IsExpanded = true; Tick(window);
                Assert.False(Find<Button>(matrix, "ApplyMultiConfigurationPickup").IsEnabled);
                value.Text = "BK7";
                Find<Button>(matrix, "ApplyMultiConfigurationValue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal("BK7", app.MultiConfiguration.GetOperandRows()[0].TextValues![1]);
                Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                Assert.True(app.Documents.Undo()); Tick(window);
                Assert.Equal("MIRROR", app.MultiConfiguration.GetOperandRows()[0].TextValues![1]);
                var scene = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional,
                    RayCount: 21, IncludeAllConfigurations: true));
                Assert.Equal(126, scene.ThreeDimensional!.Rays.Count);
                var drawing = new OpticSceneControl { Scene = scene, ViewMode = OpticSceneViewMode.ThreeDimensional,
                    VisualStyle = OpticSceneVisualStyle.SolidModel, RenderMode = OpticSceneRenderMode.Solid,
                    RayColorMode = OpticSceneRayColorMode.Field, ShowScaleBar = true };
                window.Content = drawing; Tick(window);
                foreach (var preset in new[] { OpticSceneViewPreset.Front, OpticSceneViewPreset.Isometric })
                {
                    drawing.SetViewPreset(preset); Tick(window);
                    var fit = drawing.SequentialFit(scene.ThreeDimensional, window.Width - 68, window.Height - 68)!;
                    Assert.True(fit.Scale > 3, $"{preset}: {fit.Scale}");
                    var positions = scene.ThreeDimensional.Rays.SelectMany(r => r.Points).ToArray();
                    Assert.True(fit.Center.Y > 20);
                    Assert.Contains(positions, p => p.Y > 100);
                    Assert.Contains(positions, p => p.Z > 139);
                    Capture(window, theme + "-" + preset);
                }
                var firstFit = drawing.SequentialFit(scene.ThreeDimensional, 1092, 852);
                var pointer = new Point(580, 460);
                window.MouseMove(pointer); window.MouseDown(pointer, MouseButton.Left);
                window.MouseMove(pointer + new Vector(24, 12)); window.MouseUp(pointer + new Vector(24, 12), MouseButton.Left); Tick(window);
                Assert.Null(drawing.ActiveViewPreset);
                Assert.Same(firstFit, drawing.SequentialFit(scene.ThreeDimensional, 1092, 852));
                var restyled = scene.ThreeDimensional with
                {
                    Surfaces = scene.ThreeDimensional.Surfaces.Select(s => s with { Opacity = .35, HideEdges = true }).ToArray()
                };
                Assert.Same(firstFit, drawing.SequentialFit(restyled, 1092, 852));
                drawing.FitView();
                Assert.NotSame(firstFit, drawing.SequentialFit(scene.ThreeDimensional, 1092, 852));
                drawing.SetViewPreset(OpticSceneViewPreset.Isometric); drawing.RenderMode = OpticSceneRenderMode.Transparent; Tick(window);
                Capture(window, theme + "-Isometric-transparent");
                Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
            }
            finally
            {
                window?.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
                UiLocalization.Apply(previousLanguage);
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previousSettings);
            }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static TextBox Input(Control root, string name) => root.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == name);
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Capture(Window window, string name)
    {
        var directory = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), ".tmp/beamsplitter-diagnosis/images");
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame(); image!.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
