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
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LensMaterialRowThemeTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_MATERIAL_ROWS_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task EveryMaterialRowKeepsBlueThroughSelectionEditsRecyclingAndFileSwitches()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensMaterialRowThemeTests));
        await session.Dispatch(() =>
        {
            var app = global::Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            using var application = WorkbenchApplication.Create("tessar");
            using var panel = new LensEditorPanel(application.Prescription, application.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1400, Height = 520, Content = panel };
            try
            {
                window.Show();
                Render(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                TextBox? materialInput = null;
                grid.PreparingCellForEdit += (_, args) =>
                {
                    if (Equals(args.Column.Header, "材料")) materialInput = Assert.IsType<TextBox>(args.EditingElement);
                };
                SurfaceEditorRow Item(int number) => grid.ItemsSource!.Cast<SurfaceEditorRow>().Single(r => r.Number == number);
                DataGridRow Row(int number) => grid.GetVisualDescendants().OfType<DataGridRow>()
                    .Single(r => r.DataContext is SurfaceEditorRow item && item.Number == number);
                void Away()
                {
                    window.MouseMove(new Point(1390, 510));
                    Render(window);
                }
                void AssertMaterialRows()
                {
                    foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
                    {
                        if (row.DataContext is not SurfaceEditorRow item) continue;
                        var hasMaterial = !string.IsNullOrWhiteSpace(item.MaterialDisplay);
                        var typeCell = grid.Columns[1].GetCellContent(row)!;
                        var labels = typeCell.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                        Assert.Equal(new[] { item.SurfaceRole, item.SurfaceType }, labels);
                        Assert.Equal(hasMaterial, row.Classes.Contains("glass-material-row"));
                        if (!row.IsSelected)
                            Equal(hasMaterial ? "#EAF1FF" : "#FFFFFF", Background(row).Fill);
                    }
                }
                void EditMaterial(int number, string value, bool commit)
                {
                    grid.SelectedItem = Item(number);
                    grid.CurrentColumn = grid.Columns.Single(c => Equals(c.Header, "材料"));
                    grid.ScrollIntoView(grid.SelectedItem, grid.CurrentColumn);
                    grid.Focus();
                    Render(window);
                    materialInput = null;
                    Assert.True(grid.BeginEdit());
                    Render(window);
                    var input = Assert.IsType<TextBox>(materialInput);
                    input.Text = value;
                    if (commit) Assert.True(grid.CommitEdit(DataGridEditingUnit.Row, true));
                    else Assert.True(grid.CancelEdit(DataGridEditingUnit.Row));
                    Render(window);
                    grid.SelectedItem = Item(0);
                    Away();
                    AssertMaterialRows();
                }

                grid.SelectedItem = Item(0);
                Away();
                Assert.Equal(new[] { 1, 3, 6, 7 }, grid.ItemsSource!.Cast<SurfaceEditorRow>()
                    .Where(r => r.HasOpticalMaterial).Select(r => r.Number));
                AssertMaterialRows();
                Capture(window, "material-rows-tessar.png");

                var propertiesToggle = panel.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Name == "SurfacePropertiesToggle");
                propertiesToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Render(window);
                foreach (var number in new[] { 9, 5, 0, 7, 2, 9, 0 })
                {
                    grid.ScrollIntoView(Item(number), grid.Columns[0]);
                    Away();
                    AssertMaterialRows();
                    if (number == 5) Capture(window, "material-rows-recycled-stop.png");
                    if (number == 9) Capture(window, "material-rows-recycled-image.png");
                }
                Capture(window, "material-rows-recycled-roles.png");
                propertiesToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Render(window);

                var materialRow = Row(3);
                var point = materialRow.TranslatePoint(new Point(18, 18), window)!.Value;
                window.MouseMove(point);
                Render(window);
                Equal("#D5E3FC", Background(materialRow).Fill);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Render(window);
                Assert.True(materialRow.IsSelected);
                Away();
                Equal("#DFEAFE", Background(materialRow).Fill);
                var currency = materialRow.GetVisualDescendants().OfType<Rectangle>()
                    .Single(r => r.Name == "CurrencyVisual" && r.IsVisible);
                Equal("#2F65CC", currency.Stroke);
                Assert.Equal(2, currency.StrokeThickness);
                window.MouseMove(point);
                Render(window);
                Equal("#D5E3FC", Background(materialRow).Fill);
                materialRow.IsEnabled = false;
                Render(window);
                Equal("#F0F3F8", Background(materialRow).Fill);
                materialRow.IsEnabled = true;
                grid.SelectedItem = Item(0);
                Away();
                AssertMaterialRows();

                var original = Item(3).Material;
                EditMaterial(3, "", commit: false);
                Assert.Equal(original, application.Prescription.GetSurfaces()[3].Material);
                EditMaterial(3, "", commit: true);
                Assert.False(Item(3).HasOpticalMaterial);
                Assert.True(string.IsNullOrEmpty(Item(3).MaterialDisplay));
                EditMaterial(3, original, commit: true);
                Assert.True(Item(3).HasOpticalMaterial);

                var changed = application.Prescription.GetSurfaces()[6] with { Material = "Air" };
                application.Prescription.UpdateSurface(changed);
                Render(window);
                Away();
                Assert.False(Item(6).HasOpticalMaterial);
                AssertMaterialRows();
                Assert.True(application.Documents.Undo());
                Render(window);
                Away();
                Assert.True(Item(6).HasOpticalMaterial);
                AssertMaterialRows();

                window.Height = 180;
                Render(window);
                grid.ScrollIntoView(Item(9), grid.Columns[0]);
                Away();
                AssertMaterialRows();
                grid.ScrollIntoView(Item(0), grid.Columns[0]);
                Away();
                AssertMaterialRows();
                window.Height = 520;
                Render(window);
                foreach (var theme in new[] { "Dark", "Pixel", "Light" })
                {
                    ThemeApplicationService.Apply(app, theme);
                    Render(window);
                }
                Away();
                AssertMaterialRows();
                application.Documents.NewBlank();
                Render(window);
                Away();
                Assert.All(grid.ItemsSource!.Cast<SurfaceEditorRow>(), r => Assert.False(r.HasOpticalMaterial));
                AssertMaterialRows();
                application.Documents.NewTessar();
                Render(window);
                Away();
                AssertMaterialRows();
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static Rectangle Background(DataGridRow row) => row.GetVisualDescendants().OfType<Rectangle>()
        .Single(r => r.Name == "BackgroundRectangle" && !r.GetVisualAncestors().OfType<DataGridRowHeader>().Any());

    private static void Equal(string expected, IBrush? brush) =>
        Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTILAND_MATERIAL_ROWS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize(2800, 1040), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
