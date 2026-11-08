using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MeritOperandRowThemeTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_OPERAND_COLORS_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task NativeRowPaintSurvivesThemeStatesEditingRecyclingAndDisposal()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MeritOperandRowThemeTests));
        await session.Dispatch(() =>
        {
            var app = global::Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            var codes = new[] { "AXCL", "AXCL", "REAY", "REAY", "EFFL", "REAY", "DIFF", "BLNK", "DMFS", "MNCA", "MXCA", "MNEA", "OPDX", "MNCG", "INDX", "RSCE", "DIVI" };
            // Disabled numerical operands isolate display verification from optical evaluation.
            application.Optimization.SetMeritFunction(Enumerable.Range(0, 85).Select(i =>
                new MeritOperandRowDto(i + 1, false, codes[i % codes.Length], 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, "",
                    ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 0, ZemaxData2: 0, ZemaxData3: 0, ZemaxData4: 0, ZemaxData5: 0)).ToArray());
            using var panel = new OptimizationPanel(application.Prescription, application.Optimization, application.Events);
            var window = new Window { Width = 1600, Height = 780, Content = panel };
            try
            {
                window.Show();
                Render(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                var items = grid.ItemsSource!.Cast<MeritOperandEditorRow>().ToArray();
                DataGridRow Row(int index) => grid.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, items[index]));
                void Away() { window.MouseMove(new Point(1595, 775)); Render(window); }
                void AssertPaint()
                {
                    foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
                    {
                        if (row.DataContext is not MeritOperandEditorRow item) continue;
                        var expected = MeritOperandRowPalette.ResolveVisual(item.Type,
                            ThemeRegistry.FromActualVariant(row.ActualThemeVariant), !string.IsNullOrWhiteSpace(item.Error),
                            row.IsPointerOver, row.IsSelected, row.IsEnabled);
                        Assert.Equal(expected.Background, Solid(Background(row).Fill));
                        Assert.Equal(1, Background(row).Opacity);
                        Assert.Equal(expected.Foreground, Solid(row.Foreground));
                        var text = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CellTextBlock");
                        Assert.Equal(expected.Foreground, Solid(text.Foreground));
                    }
                }
                foreach (var theme in new[] { "Light", "Dark", "Isekai", "Pixel", "Lumen", "Light" })
                {
                    ThemeApplicationService.Apply(app, theme);
                    grid.SelectedItem = null;
                    Away();
                    AssertPaint();
                    AssertPixels(window, grid);
                    Capture(window, $"merit-rows-{theme}.png");
                    if (theme == "Isekai")
                    {
                        Capture(window, "merit-rows-Isekai-150.png", 1.5);
                        Capture(window, "merit-rows-Isekai-200.png", 2);
                    }
                    var row = Row(2);
                    var point = row.TranslatePoint(new Point(220, row.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point); Render(window);
                    Assert.True(row.IsPointerOver);
                    AssertPaint();
                    grid.SelectedItem = items[2];
                    grid.Focus(); Render(window); AssertPaint();
                    AssertPixels(window, grid);
                    Assert.Equal(1, Background(Row(2)).StrokeThickness);
                    Away(); AssertPaint();
                    items[2].Type = "OPDX"; Render(window); AssertPaint();
                    Assert.Equal("OPDX", Row(2).GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem);
                    items[2].Error = "Display test error"; Render(window); AssertPaint();
                    AssertPixels(window, grid);
                    if (theme == "Isekai") Capture(window, "merit-rows-Isekai-error-selected.png");
                    items[2].Error = ""; items[2].Type = "REAY";
                    grid.SelectedItem = null; Away(); AssertPaint();
                }
                // The native type picker persists/rebuilds rows; the new code must repaint too.
                var picker = Row(4).GetVisualDescendants().OfType<ComboBox>().Single();
                picker.SelectedItem = "REAY";
                Render(window); Away(); AssertPaint();
                Assert.Equal("REAY", grid.ItemsSource!.Cast<MeritOperandEditorRow>().ElementAt(4).Type);
                window.Height = 270; Render(window);
                grid.ScrollIntoView(grid.ItemsSource!.Cast<MeritOperandEditorRow>().Last(), grid.Columns[0]);
                Away(); AssertPaint();
                grid.ScrollIntoView(grid.ItemsSource!.Cast<MeritOperandEditorRow>().First(), grid.Columns[0]);
                Away(); AssertPaint();
                window.Width = 880; Render(window); AssertPaint();
                Capture(window, "merit-rows-Light-narrow.png");
                var liveRow = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext is MeritOperandEditorRow);
                var liveItem = (MeritOperandEditorRow)liveRow.DataContext!;
                var colour = liveRow.Background;
                panel.Dispose();
                liveItem.Type = "DIVI";
                liveItem.Error = "after disposal";
                Render(window);
                Assert.Same(colour, liveRow.Background);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeToleranceRowsKeepTheirColorsDuringEditingThemeSwitchAndRecycling()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MeritOperandRowThemeTests));
        await session.Dispatch(() =>
        {
            var app = global::Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            using var panel = new TolerancingPanel(application.Documents, application.Prescription,
                application.Tolerancing, application.Events);
            var window = new Window { Width = 1550, Height = 690, Content = panel };
            try
            {
                window.Show();
                Render(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single(g =>
                    g.ItemsSource is IEnumerable<ToleranceOperandEditorRow>);
                var items = Assert.IsAssignableFrom<IList<ToleranceOperandEditorRow>>(grid.ItemsSource);
                items.Clear();
                var kinds = Enum.GetValues<ToleranceOperandKind>();
                for (var i = 0; i < 64; i++)
                    items.Add(new(new(i + 1, i != 1, kinds[i % kinds.Length], 2, -.1, .1,
                        ToleranceDistribution.Normal, "")));

                DataGridRow Row(int index) => grid.GetVisualDescendants().OfType<DataGridRow>()
                    .Single(r => ReferenceEquals(r.DataContext, items[index]));
                void RenderAway()
                {
                    window.MouseMove(new Point(window.Width - 5, window.Height - 5));
                    Render(window);
                }
                void AssertPaint()
                {
                    foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
                    {
                        if (row.DataContext is not ToleranceOperandEditorRow item) continue;
                        var visual = MeritOperandRowPalette.ResolveToleranceVisual(item.Kind,
                            ThemeRegistry.FromActualVariant(row.ActualThemeVariant), row.IsPointerOver, row.IsSelected, row.IsEnabled);
                        Assert.Equal(visual.Background, Solid(Background(row).Fill));
                        Assert.Equal(1, Background(row).Opacity);
                        Assert.Equal(visual.Foreground, Solid(row.Foreground));
                        var text = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CellTextBlock");
                        Assert.Equal(visual.Foreground, Solid(text.Foreground));
                    }
                }

                foreach (var theme in new[] { "Light", "Dark", "Isekai", "Pixel", "Lumen", "Light" })
                {
                    ThemeApplicationService.Apply(app, theme);
                    grid.SelectedItem = null;
                    RenderAway();
                    AssertPaint();
                    Capture(window, $"tolerance-rows-{theme}.png");
                    AssertPixels(window, grid, [0, 2, 9]);
                    grid.SelectedItem = items[3];
                    Render(window);
                    AssertPaint();
                    Assert.Equal(1, Background(Row(3)).StrokeThickness);
                    var point = Row(3).TranslatePoint(new Point(125, Row(3).Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point);
                    Render(window);
                    Assert.True(Row(3).IsPointerOver);
                    AssertPaint();
                }

                var original = Solid(Row(0).Background);
                items[0].Code = "TIND";
                RenderAway();
                AssertPaint();
                Assert.NotEqual(original, Solid(Row(0).Background));
                grid.ScrollIntoView(items[^1], grid.Columns[0]);
                RenderAway();
                AssertPaint();
                Assert.Same(items[^1], Row(items.Count - 1).DataContext);
                grid.ScrollIntoView(items[0], grid.Columns[0]);
                RenderAway();
                AssertPaint();

                var liveRow = Row(0);
                var beforeDispose = liveRow.Background;
                panel.Dispose();
                items[0].Code = "TRAD";
                Render(window);
                Assert.Same(beforeDispose, liveRow.Background);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WizardGeneratedRowsKeepTheirClassificationColorsInTheDockedEditor()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MeritOperandRowThemeTests));
        await session.Dispatch(async () =>
        {
            using var workbench = WorkbenchApplication.Create();
            await workbench.Documents.OpenAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-ms-l7-high-na.ZMX"));
            using var directory = new ReportTestDirectory();
            var window = new MainWindow(new AppSettings { Theme = "Light", WindowWidth = 1600, WindowHeight = 980 },
                workbench, new WorkspaceSessionStore(directory.Path));
            var ready = false;
            window.StartupCompleted += (_, _) => ready = true;
            try
            {
                window.Show();
                for (var attempt = 0; attempt < 200 && !ready; attempt++)
                {
                    await Task.Delay(10);
                    Render(window);
                }
                Assert.True(ready);
                var actions = Assert.IsType<ActionManager>(typeof(MainWindow)
                    .GetField("_actions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
                Assert.True(await actions.ExecuteAsync(actions.Find("show-tolerancing")));
                Render(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<TolerancingPanel>());
                var wizardButton = Assert.Single(panel.GetVisualDescendants().OfType<Button>(),
                    button => button.Content is LocalIconLabel label
                        && label.Children.OfType<TextBlock>().Any(text => text.Text == "公差向导"));
                wizardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Render(window);
                var wizard = Assert.Single(window.OwnedWindows.OfType<ToleranceWizardWindow>());
                Render(wizard);
                Assert.Single(wizard.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "确定"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Render(window);
                Assert.Contains("公差向导已生成", panel.ResultMessage);
                var grid = Assert.Single(panel.GetVisualDescendants().OfType<DataGrid>());
                var expected = new[] { "TRAD", "TTHI", "TIND", "TABB" };
                var colors = new HashSet<Color>();
                foreach (var code in expected)
                {
                    var row = grid.GetVisualDescendants().OfType<DataGridRow>()
                        .First(row => row.DataContext is ToleranceOperandEditorRow item && item.Code == code);
                    var operand = Assert.IsType<ToleranceOperandEditorRow>(row.DataContext);
                    var visual = MeritOperandRowPalette.ResolveToleranceVisual(operand.Kind,
                        ThemeRegistry.FromActualVariant(row.ActualThemeVariant), row.IsPointerOver, row.IsSelected, row.IsEnabled);
                    Assert.Equal(visual.Background, Solid(Background(row).Fill));
                    Assert.True(colors.Add(visual.Background), code);
                }
                AssertPixels(window, grid, [0, 2, 9]);
                Capture(window, "tolerance-wizard-docked-light.png");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static Rectangle Background(DataGridRow row) => row.GetVisualDescendants().OfType<Rectangle>()
        .Single(r => r.Name == "BackgroundRectangle" && !r.GetVisualAncestors().OfType<DataGridRowHeader>().Any());
    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static void AssertPixels(Window window, DataGrid grid, int[]? columns = null)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        image.Render(window);
        using var stream = new MemoryStream();
        image.Save(stream, PngBitmapEncoderOptions.Default);
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
        var gridOrigin = grid.TranslatePoint(default, window)!.Value;
        foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
        {
            var code = row.DataContext switch
            {
                MeritOperandEditorRow item => item.Type,
                ToleranceOperandEditorRow item => item.Code,
                _ => null
            };
            if (code is null) continue;
            foreach (var column in columns ?? [0, 2, 14]) // Index, parameter/type and empty comment cells.
            {
                var content = grid.Columns[column].GetCellContent(row);
                var cell = content?.GetVisualAncestors().OfType<DataGridCell>().FirstOrDefault();
                if (cell is null) continue;
                // The last tolerance column stretches past the vertical scrollbar.
                var sampleX = columns is not null && column == 9 ? 28 : cell.Bounds.Width - 8;
                var point = cell.TranslatePoint(new Point(sampleX, 4), window)!.Value;
                if (point.Y < 145 || point.Y >= window.Bounds.Height - 45 || point.X < gridOrigin.X
                    || point.X >= gridOrigin.X + grid.Bounds.Width - 20) continue;
                var actual = pixels.GetPixel((int)point.X, (int)point.Y);
                Assert.True(Solid(row.Background) == Color.FromRgb(actual.Red, actual.Green, actual.Blue),
                    $"{row.ActualThemeVariant}/{code}/column {column}: rendered {actual}, row {row.Background}");
            }
        }
    }
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    private static void Capture(Window window, string name, double scale = 1)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_OPERAND_COLORS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
