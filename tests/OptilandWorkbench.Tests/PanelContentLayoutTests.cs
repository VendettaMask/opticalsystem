using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PanelContentLayoutTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_PANEL_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(1180, 660, 1)]
    [InlineData(720, 500, 2)]
    public async Task StockResultHeaderDoesNotOverlapTable(double width, double height, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PanelContentLayoutTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("tessar");
            using var panel = new StockLensMatchingPanel(application.Documents, application.Lenses, application.Events);
            var window = new Window { Width = width, Height = height, Content = panel };
            try
            {
                window.Show();
                window.SetRenderScaling(scaling);
                Render(window);
                for (var i = 0; i < 3; i++)
                {
                    AssertStockLayout(panel, window);
                    window.Content = null;
                    Render(window);
                    window.Content = panel;
                    window.Hide();
                    window.Show();
                    Render(window);
                }
                AssertStockLayout(panel, window);
                Capture(window, $"stock-{width}.png");
                var headerScroll = ((ScrollableHeaderGrid)panel.Content!).Children.OfType<ScrollViewer>().Single();
                if (width < 800)
                {
                    Assert.True(headerScroll.Extent.Height > headerScroll.Viewport.Height);
                    headerScroll.Offset = new Vector(0, headerScroll.Extent.Height);
                    Render(window);
                    AssertStockLayout(panel, window);
                    Capture(window, "stock-narrow-settings-scrolled.png");
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StockMatchingDocumentCanBeClosedReopenedAndTiledWithoutOverlap()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PanelContentLayoutTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("tessar");
            using var manager = new PanelManager(application, new AppSettings(),
                new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "panel-layout", Guid.NewGuid().ToString("N"))));
            var window = new Window { Width = 1500, Height = 900, Content = manager.WorkspaceControl };
            try
            {
                window.Show();
                StockLensMatchingPanel? previous = null;
                for (var i = 0; i < 3; i++)
                {
                    manager.ShowStockLensMatching();
                    Render(window);
                    var document = manager.Factory.OpenDocuments().Single(d => d.Context is StockLensMatchingPanel);
                    var panel = Assert.IsType<StockLensMatchingPanel>(document.Context);
                    Assert.NotSame(previous, panel);
                    AssertStockLayout(panel, window);
                    manager.TileAllWindows();
                    Render(window);
                    AssertStockLayout(panel, window);
                    manager.DockToSinglePane();
                    Render(window);
                    AssertStockLayout(panel, window);
                    if (i == 2) Capture(window, "stock-third-opening.png");
                    previous = panel;
                    manager.Factory.CloseDockable(document);
                    Render(window);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(OpticalDrawingStandard.Iso10110)]
    [InlineData(OpticalDrawingStandard.GbT13323_2009)]
    [InlineData(OpticalDrawingStandard.GbT13323_1991)]
    public async Task DrawingLabelsKeepFullTooltipsAndVisibleUnitsInExistingPanes(OpticalDrawingStandard standard)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PanelContentLayoutTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("tessar");
            using var panel = new OpticalDrawingPanel(application.Prescription, application.Materials,
                application.Events, application.Visualization, standard);
            var window = new Window { Width = 1200, Height = 860, Content = panel };
            try
            {
                window.Show();
                Render(window);
                var layout = panel.GetVisualDescendants().OfType<ResponsiveTwoPaneGrid>().Single();
                foreach (var width in new[] { 1200, 720 })
                {
                    window.Width = width;
                    window.SetRenderScaling(width == 720 ? 2 : 1);
                    Render(window);
                    Assert.Equal(width < 900, layout.IsNarrow);
                    if (width >= 900) Assert.Equal(340, layout.ColumnDefinitions[0].Width.Value);
                    var labels = panel.GetVisualDescendants().OfType<TextBlock>()
                        .Where(t => ToolTip.GetTip(t) is string full && full.Contains(" ("))
                        .Where(t => t.Text?.Contains('\n') == true).ToArray();
                    Assert.True(labels.Length >= 9);
                    foreach (var label in labels)
                    {
                        Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
                        Assert.Equal(TextTrimming.None, label.TextTrimming);
                        Assert.True(label.TextLayout.Height <= label.Bounds.Height + 1);
                        Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1);
                        var grid = Assert.IsType<Grid>(label.Parent);
                        var editor = grid.Children.Single(c => Grid.GetRow(c) == Grid.GetRow(label) && Grid.GetColumn(c) == 1);
                        Assert.Equal(ToolTip.GetTip(label), ToolTip.GetTip(editor));
                        Assert.True(label.Bounds.Right <= editor.Bounds.Left);
                        var scroll = label.FindAncestorOfType<ScrollViewer>()!;
                        var editorRight = editor.TranslatePoint(new Point(editor.Bounds.Width, 0), scroll)!.Value.X;
                        Assert.True(editorRight <= scroll.Viewport.Width, $"{label.Text}: editor right {editorRight} exceeds viewport {scroll.Viewport.Width}; grid {grid.Bounds}, editor {editor.Bounds}");
                        var viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                        var visualRight = editor.TranslatePoint(new Point(editor.Bounds.Width, 0), viewport)!.Value.X;
                        Assert.True(visualRight <= viewport.Bounds.Width, $"{label.Text}: visual right {visualRight} exceeds presenter {viewport.Bounds.Width}; scroll {scroll.Bounds}, viewport {scroll.Viewport}, grid {grid.Bounds}");
                        var scrollbar = scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                            .Single(b => b.Name == "PART_VerticalScrollBar" && b.FindAncestorOfType<ScrollViewer>() == scroll);
                        if (scrollbar.IsVisible)
                            Assert.True(editorRight <= scrollbar.TranslatePoint(default, scroll)!.Value.X,
                                $"{label.Text}: scrollbar overlays the editor's right edge");
                        foreach (var border in editor.GetVisualDescendants().OfType<Border>().Where(b => b.BorderThickness.Right > 0))
                        {
                            var borderRight = border.TranslatePoint(new Point(border.Bounds.Width, 0), editor)!.Value.X;
                            Assert.True(borderRight <= editor.Bounds.Width + 1, $"{label.Text}: {border.Name} right {borderRight} exceeds editor {editor.Bounds.Width}; {border.Bounds}");
                        }
                        Assert.Contains(label.Text!.Split('\n')[1], (string)ToolTip.GetTip(label)!);
                    }
                    var thickness = labels.Single(t => t.Text == "厚度上偏差\n(mm)");
                    thickness.BringIntoView();
                    Render(window);
                    Capture(window, $"drawing-{standard}-{width}.png");
                    var thicknessEditor = ((Grid)thickness.Parent!).Children.OfType<NumericUpDown>()
                        .Single(c => Grid.GetRow(c) == Grid.GetRow(thickness));
                    Assert.Equal(0.02m, thicknessEditor.Value);
                    Click(window, thicknessEditor);
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                    Render(window);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task TableHeadersUseSortSpaceOnlyWhenNeededAndKeepUnitsAndTooltips(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PanelContentLayoutTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            var table = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { new HeaderRow("完整的长表格内容", 12.3), new HeaderRow("第二项", 8.4) } };
            var shortColumn = new DataGridTextColumn { Header = "启用", Binding = new Binding(nameof(HeaderRow.Label)), Width = new DataGridLength(56) };
            var unitColumn = new DataGridTextColumn { Header = "EFL (mm)", Binding = new Binding(nameof(HeaderRow.Value)), Width = new DataGridLength(80) };
            CompactLabel.SetColumnLabel(unitColumn, "EFL", "有效焦距 (mm)", "mm");
            table.Columns.Add(shortColumn);
            table.Columns.Add(unitColumn);
            var window = new Window { Width = 500, Height = 300, Content = table };
            try
            {
                window.Show();
                Render(window);
                var header = table.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, "启用"));
                var label = header.GetVisualDescendants().OfType<TextBlock>().Single();
                var natural = new TextBlock { Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize };
                natural.Measure(Size.Infinity);
                Assert.True(label.Bounds.Width >= natural.DesiredSize.Width, $"Short header is clipped: {label.Bounds.Width} < {natural.DesiredSize.Width}");
                Assert.Equal("启用", ToolTip.GetTip(label));
                var units = table.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, "EFL (mm)"));
                var suffix = units.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == " (mm)");
                var initialWidth = units.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter").Bounds.Width;
                Click(window, units);
                var icon = units.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "SortIcon");
                Assert.True(icon.IsVisible);
                Assert.True(icon.Bounds.Width > 0);
                var sortedWidth = units.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter").Bounds.Width;
                Assert.True(sortedWidth < initialWidth);
                Assert.True(initialWidth - sortedWidth <= 16);
                Assert.True(suffix.TextLayout.Width <= suffix.Bounds.Width + 1);
                Assert.True(suffix.TranslatePoint(new Point(suffix.Bounds.Width, 0), units)!.Value.X <= units.Bounds.Width);
                Assert.Equal("有效焦距 (mm)", ToolTip.GetTip(suffix));
                var cell = table.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CellTextBlock" && t.Text == "完整的长表格内容");
                Assert.Equal(TextTrimming.CharacterEllipsis, cell.TextTrimming);
                Assert.Equal(cell.Text, ToolTip.GetTip(cell));
                Capture(window, $"table-{theme}.png");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertStockLayout(StockLensMatchingPanel panel, Window window)
    {
        var table = panel.GetVisualDescendants().OfType<DataGrid>().Single();
        var product = panel.GetVisualDescendants().OfType<Button>().Single(b =>
            b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "厂商页面"));
        var status = ((Grid)product.Parent!).Children.OfType<TextBlock>().Single();
        var tableTop = table.TranslatePoint(default, window)!.Value.Y;
        var productBottom = product.TranslatePoint(new Point(0, product.Bounds.Height), window)!.Value.Y;
        var statusBottom = status.TranslatePoint(new Point(0, status.Bounds.Height), window)!.Value.Y;
        Assert.True(tableTop >= Math.Max(productBottom, statusBottom), $"Table {tableTop} overlaps result header {productBottom}/{statusBottom}");
        Assert.True(status.Bounds.Right <= product.Bounds.Left);
        Assert.True(table.Bounds.Height > 34);
        Assert.True(table.TranslatePoint(new Point(0, table.Bounds.Height), panel)!.Value.Y <= panel.Bounds.Height);
        Assert.True(((Border)table.Parent!).ClipToBounds);
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
    }

    private sealed record HeaderRow(string Label, double Value);

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTILAND_PANEL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width * 2, (int)window.Height * 2), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
