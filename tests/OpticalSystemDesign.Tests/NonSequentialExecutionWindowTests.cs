using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.NonSequential;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialExecutionWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light", "zh-CN", 96)]
    [InlineData("Dark", "ja-JP", 144)]
    [InlineData("Isekai", "en-US", 192)]
    public async Task DatabaseParentNavigationAndWrappedTraceCommandsWorkOffscreen(string theme, string language, double dpi)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(NonSequentialExecutionWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.Apply(language);
            using var files = new CameraSensorFileTests.TemporaryFiles();
            var path = files.Path("family.starrdb");
            using (var stream = File.Create(path))
                NonSequentialExecutionTests.Write(stream,
                    NonSequentialRayDatabaseHeader.Create(NonSequentialExecutionTests.Scene()), [1, 3, 2]);
            using var app = WorkbenchApplication.Create();
            app.Modes.SwitchTo(Dto.OpticalWorkbenchMode.NonSequential);
            app.NonSequential.AddObject(Dto.NonSequentialObjectKind.SourcePoint);
            var database = new NonSequentialRayDatabaseWindow(app.NonSequentialAnalysis, path);
            var trace = new NonSequentialTraceControlWindow(app.NonSequential, app.NonSequentialAnalysis)
                { Width = 600, Height = 540 };
            try
            {
                database.Show();
                await database.InitialLoad;
                database.UpdateLayout();
                T Find<T>(string name) where T : Control
                {
                    var controls = database.GetLogicalDescendants().Concat(database.GetVisualDescendants())
                        .Distinct().OfType<Control>().ToArray();
                    var found = controls.OfType<T>().Where(control => control.Name == name).ToArray();
                    Assert.True(found.Length == 1, $"{name}: {string.Join(", ", controls.Select(control => control.GetType().Name + ":" + control.Name))}");
                    return found[0];
                }
                var grid = Find<DataGrid>("nsc-branch-grid");
                var input = Find<TextBox>("nsc-branch-id");
                Assert.True(grid.IsEnabled);
                Assert.True(input.IsEffectivelyEnabled);
                await NonSequentialExecutionTests.UntilAsync(() => grid.ItemsSource?.Cast<object>().Count() == 3);
                input.Text = "3";
                Find<Button>("nsc-branch-locate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await NonSequentialExecutionTests.UntilAsync(() => grid.SelectedItem is not null
                    && grid.ItemsSource!.Cast<object>().Count() == 2);
                Assert.Equal("3", input.Text);
                Find<Button>("nsc-branch-parent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await NonSequentialExecutionTests.UntilAsync(() => grid.ItemsSource!.Cast<object>().Count() == 3);
                Assert.Equal("1", input.Text);
                Assert.NotNull(grid.SelectedItem);

                trace.Show();
                trace.UpdateLayout();
                var pause = trace.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "nsc-trace-pause");
                Assert.False(pause.IsEnabled);
                var start = trace.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "nsc-trace-start");
                var cancel = trace.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "nsc-trace-cancel");
                trace.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "nsc-trace-count").Text = "1000000";
                start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await NonSequentialExecutionTests.UntilAsync(() => app.NonSequentialAnalysis.GetTraceProgress() is not null);
                pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await NonSequentialExecutionTests.UntilAsync(() => pause.Content?.ToString() == UiLocalization.Text("继续"));
                Assert.True(app.NonSequentialAnalysis.GetTraceProgress()!.IsPaused);
                var progress = trace.GetLogicalDescendants().OfType<ProgressBar>().Single(control => control.Name == "nsc-trace-progress");
                Assert.True(progress.IsVisible);
                trace.UpdateLayout();
                Assert.All(((Panel)pause.Parent!).Children.OfType<Button>(), button =>
                {
                    var point = button.TranslatePoint(default, trace)!.Value;
                    Assert.True(button.Bounds.Width > 0);
                    Assert.InRange(point.X, 0, trace.Bounds.Width - button.Bounds.Width);
                    Assert.InRange(point.Y, 0, trace.Bounds.Height - button.Bounds.Height);
                });
                using var bitmap = new RenderTargetBitmap(
                    new PixelSize((int)(trace.Bounds.Width * dpi / 96), (int)(trace.Bounds.Height * dpi / 96)), new Vector(dpi, dpi));
                bitmap.Render(trace);
                var output = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), ".tmp", "nsc-repair");
                Directory.CreateDirectory(output);
                bitmap.Save(Path.Combine(output, $"trace-controls-{theme}.png"), PngBitmapEncoderOptions.Default);
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await NonSequentialExecutionTests.UntilAsync(() => start.IsEnabled);
                Assert.False(progress.IsVisible);
                Assert.Null(app.NonSequentialAnalysis.GetTraceProgress());
                Assert.Equal(path, app.NonSequentialAnalysis.GetCurrentSession()!.RayDatabasePath);
                return true;
            }
            finally
            {
                trace.Close(); database.Close(); await database.InitialLoad;
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
                UiLocalization.Apply(previousLanguage);
            }
        }, CancellationToken.None);
    }
}
