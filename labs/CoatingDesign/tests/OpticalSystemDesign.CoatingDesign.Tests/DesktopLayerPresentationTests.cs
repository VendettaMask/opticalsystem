using Avalonia;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Theory]
    [InlineData("Light", 1200, 13)]
    [InlineData("Dark", 1200, 13)]
    [InlineData("Light", 820, 18)]
    public async Task LayerCellsRetainMaterialIdentityAndEditableValuesWhenRecycled(string theme, int width, double fontSize)
    {
        var directory = Path.Combine(Path.GetTempPath(), "coating-layer-presentation-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var experiment = Examples.Create(DesignKind.HighReflector);
            experiment = experiment with
            {
                Layers = Enumerable.Range(0, 32).Select(i => new FilmLayer(i % 2 == 0 ? experiment.HighId : experiment.LowId,
                    64.989123456789 + i * 3.125, i % 3 != 0)).ToArray()
            };
            var path = Path.Combine(directory, "table.coating.json");
            await ExperimentStore.SaveAsync(experiment, path);
            using var host = new Host();
            await host.Run(async () =>
            {
                var previousLanguage = UiLocalization.CurrentLanguage;
                DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
                Assert.IsType<global::OpticalSystemDesign.App.App>(Avalonia.Application.Current).ApplyTheme(theme);
                UiLocalization.Apply(UiLocalization.Chinese);
                var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(null),
                    () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null)))
                { Width = width, Height = 960, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    var header = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "LaboratoryHeader");
                    Assert.True(header.Bounds.Height <= Math.Max(50, fontSize * 3), $"Command bar consumed too much height: {header.Bounds}");
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), c => c.Name == "CoatingDocumentName");
                    Assert.Equal(experiment.Name, Find<TextBox>(window, "实验名称").Text);
                    var left = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "CoatingLayerCard");
                    var right = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "CoatingSpectrumCard");
                    Assert.True(left.Bounds.Width >= 300 && right.Bounds.Width >= 300);
                    Assert.True(left.TranslatePoint(new Point(left.Bounds.Width, 0), window)!.Value.X
                        < right.TranslatePoint(new Point(0, 0), window)!.Value.X);
                    Click(window, "设计目标设置"); Tick(window);
                    var settings = window.OwnedWindows.Single(w => w.Name == "CoatingSettingsWindow"); Tick(settings);
                    Assert.Equal(0, window.SettingsContent.SelectedIndex);
                    var nameEditor = Find<TextBox>(settings, "实验名称");
                    Assert.True(nameEditor.Bounds.Width > 150);
                    var originalName = nameEditor.Text;
                    nameEditor.Text = "界面设置检查";
                    Click(settings, "完成"); Tick(window);
                    Assert.Equal("界面设置检查", Find<TextBox>(window, "实验名称").Text);
                    Find<TextBox>(window, "实验名称").Text = originalName;
                    var fingerprint = window.CurrentExperiment.Fingerprint();
                    var table = WindowControls(window).OfType<DataGrid>().Single(g => g.Name == "CoatingLayerTable");
                    Assert.Equal(7, table.Columns.Count);
                    Assert.Equal("物理厚度 (nm)", Assert.IsType<TextBlock>(table.Columns[2].Header).Text);
                    Assert.Equal("光学厚度 (nm)", Assert.IsType<TextBlock>(table.Columns[3].Header).Text);
                    Assert.False(table.Columns[2].IsReadOnly); Assert.False(table.Columns[3].IsReadOnly);
                    Assert.False(table.Columns[4].IsReadOnly); Assert.False(table.Columns[5].IsReadOnly);
                    var rows = ((IEnumerable<LayerRow>)table.ItemsSource).ToArray();
                    Assert.All(rows, row => Assert.NotNull(row.OpticalThicknessNm));
                    table.ScrollIntoView(rows[^1], table.Columns[1]); Tick(window);
                    table.ScrollIntoView(rows[0], table.Columns[1]); Tick(window);
                    var firstIndex = table.GetVisualDescendants().OfType<TextBlock>().Single(c =>
                        c.Name == "CellTextBlock" && ReferenceEquals(c.DataContext, rows[0]) && c.Text == "1"
                        && c.GetVisualAncestors().OfType<DataGridCell>().Any(cell => cell.Name == "CoatingLayerNumberCell"));
                    Assert.True(firstIndex.Bounds.Width >= fontSize * .6, $"Layer number clipped: {firstIndex.Bounds}");
                    foreach (var name in table.GetVisualDescendants().OfType<TextBlock>().Where(c => c.Name == "CoatingMaterialLabel"))
                    {
                        var row = Assert.IsType<LayerRow>(name.DataContext);
                        Assert.Equal(row.MaterialLabel, name.Text);
                        Assert.Equal(experiment.Materials.Single(m => m.Id == row.MaterialId).Name, row.MaterialName);
                        Assert.Contains(row.MaterialLabel, row.MaterialName);
                    }
                    Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint());
                    Assert.Same(rows[0].MaterialBrush, rows[2].MaterialBrush);
                    Assert.NotEqual(rows[0].MaterialBrush, rows[1].MaterialBrush);
                    Assert.Equal(experiment.Layers[0], rows[0].ToLayer());
                    table.ScrollIntoView(rows[0], table.Columns[6]); Tick(window);
                    var variable = table.GetVisualDescendants().OfType<CheckBox>()
                        .Single(c => c.Name == "CoatingLayerVariable" && ReferenceEquals(c.DataContext, rows[0]));
                    variable.BringIntoView(); Tick(window);
                    var point = variable.TranslatePoint(new Point(variable.Bounds.Width / 2, variable.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Tick(window);
                    Assert.Equal(!experiment.Layers[0].Variable, rows[0].Variable);
                    Assert.Equal(experiment.Layers[0].ThicknessNm, rows[0].ToLayer().ThicknessNm);
                    table.ScrollIntoView(rows[0], table.Columns[1]); Tick(window);
                    rows[0].MaterialId = experiment.LowId; Tick(window);
                    Assert.Equal(experiment.Materials.Single(m => m.Id == experiment.LowId).Name, rows[0].MaterialName);
                    Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), c => c.Name == "CoatingMaterialLabel"
                        && ReferenceEquals(c.DataContext, rows[0]) && c.Text == rows[0].MaterialLabel);
                    var marker = table.GetVisualDescendants().OfType<Ellipse>()
                        .Single(c => c.Name == "CoatingMaterialMarker" && ReferenceEquals(c.DataContext, rows[0]));
                    Assert.Same(rows[1].MaterialBrush, marker.Fill);
                    rows[0].MaterialId = experiment.HighId; rows[0].Variable = experiment.Layers[0].Variable;
                    table.ScrollIntoView(rows[0], table.Columns[0]); Tick(window);
                    Click(window, "计算 / 复验"); await window.PendingOperation; Tick(window);
                    Assert.NotNull(window.CurrentExperiment.Result);
                    Capture(window, $"layers-{theme}-{width}-{fontSize}");
                    var evidence = Environment.GetEnvironmentVariable("COATING_UI_EVIDENCE");
                    if (!string.IsNullOrWhiteSpace(evidence))
                    {
                        var card = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "CoatingLayerCard");
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(card.Bounds.Width * 2),
                            (int)Math.Ceiling(card.Bounds.Height * 2)), new Vector(192, 192));
                        bitmap.Render(card); bitmap.Save(Path.Combine(evidence, $"layer-card-{theme}-{width}-{fontSize}.png"), PngBitmapEncoderOptions.Default);
                        using var barBitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(header.Bounds.Width * 2),
                            (int)Math.Ceiling(header.Bounds.Height * 2)), new Vector(192, 192));
                        barBitmap.Render(header);
                        barBitmap.Save(Path.Combine(evidence, $"command-bar-{theme}-{width}-{fontSize}.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                finally
                {
                    window.CloseAfterHostApproval();
                    DisplayTypography.Configure(new AppSettings());
                    Assert.IsType<global::OpticalSystemDesign.App.App>(Avalonia.Application.Current).ApplyTheme("Light");
                    UiLocalization.Apply(previousLanguage);
                }
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
