using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ParaxialSurfacePresentationTests
{
    internal static string OriginalFile => SequentialExampleCatalog.Examples.Single(e =>
        e.RelativePath == "Birefringent prisms/Birefringent cube.zmx").FilePath;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ActualCrystalExampleKeepsParaxialIdentityThroughEditsUndoAndNativeReopen(int configuration)
    {
        using var app = WorkbenchApplication.Create();
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(OriginalFile));
        await app.Documents.OpenAsync(OriginalFile);
        Assert.Contains("Crystal axis 45 degrees in YZ plane", app.Documents.GetSnapshot().Name);
        Assert.Equal(2, app.MultiConfiguration.GetRows().Count);
        app.MultiConfiguration.Activate(configuration);
        var before = app.Prescription.GetSurfaces()[4];
        AssertParaxial(before);
        Assert.Equal(100, before.Thickness);
        Assert.Equal("近轴面", new SurfaceEditorRow(before).SurfaceType);
        var update = new SurfaceComponentUpdateDto("近轴面", "圆形", 1, 1, 0, 50);
        app.Prescription.UpdateSurfaceComponents(4, update);
        AssertParaxial(app.Prescription.GetSurfaces()[4]);
        Assert.True(app.Documents.Undo());
        AssertParaxial(app.Prescription.GetSurfaces()[4]);
        Assert.True(app.Documents.Redo());
        var row = app.Prescription.GetSurfaces()[4];
        app.Prescription.UpdateSurface(row with { Label = "renamed powered plane" });
        AssertParaxial(app.Prescription.GetSurfaces()[4]);
        row = app.Prescription.GetSurfaces()[4];
        app.Prescription.UpdateSurface(row with { Material = "N-BK7" });
        AssertParaxial(app.Prescription.GetSurfaces()[4]);
        Assert.True(app.Documents.Undo());
        AssertParaxial(app.Prescription.GetSurfaces()[4]);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            await app.Documents.OpenAsync(path);
            AssertParaxial(app.Prescription.GetSurfaces()[4]);
            Assert.Equal(2, app.MultiConfiguration.GetRows().Count);
        }
        finally { File.Delete(path); }
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(OriginalFile)));
    }

    [Fact]
    public async Task UnsupportedConversionsAreAtomicAndLabelsCannotInventAnIdealLens()
    {
        using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(OriginalFile);
        var before = app.Prescription.GetSurfaces()[4];
        var revision = app.Documents.GetSnapshot().Revision;
        Assert.Throws<NotSupportedException>(() => app.Prescription.UpdateSurfaceComponents(4,
            new("偶次非球面", "无", 1, 1, 0, 50)));
        Assert.Equal(revision, app.Documents.GetSnapshot().Revision);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(app.Prescription.GetSurfaces()[4]));
        Assert.Throws<NotSupportedException>(() => app.Prescription.UpdateSurface(before with { Material = "MIRROR" }));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(app.Prescription.GetSurfaces()[4]));
        var plane = app.Prescription.GetSurfaces()[1];
        app.Prescription.UpdateSurface(plane with { Label = "PARAXIAL 近轴面" });
        Assert.Null(app.Prescription.GetSurfaces()[1].ParaxialLens);
        Assert.Equal("标准面", new SurfaceEditorRow(app.Prescription.GetSurfaces()[1]).SurfaceType);
    }

    [Fact]
    public async Task UnequalXYPowersAndZeroPowerUseTypedModelRatherThanPlaneShape()
    {
        using var app = WorkbenchApplication.Create();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zmx");
        try
        {
            foreach (var (kind, p1, p2, type) in new[] { ("PARAX_XY", .01, .02, "近轴 XY 面"),
                ("PARAXIAL", 0d, 1d, "近轴面") })
            {
                await File.WriteAllTextAsync(path, FormattableString.Invariant($"MODE SEQ\nUNIT MM\nENPD 10\nWAVM 1 .55 1\nSURF 0\nTYPE STANDARD\nDISZ INFINITY\nSURF 1\nSTOP\nTYPE {kind}\nPARM 1 {p1}\nPARM 2 {p2}\nDISZ 100\nDIAM 10 1\nSURF 2\nTYPE STANDARD\n"));
                await app.Documents.OpenAsync(path);
                var row = new SurfaceEditorRow(app.Prescription.GetSurfaces()[1]);
                Assert.Equal(type, row.SurfaceType);
                Assert.NotNull(row.ParaxialLens);
                Assert.Equal(row.ParaxialLens, row.ToDto().ParaxialLens);
                Assert.Equal(kind == "PARAX_XY" ? .02 : 0, row.ParaxialLens.PowerYPerMillimeter);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Light", "zh-CN", "近轴面", "焦距")]
    [InlineData("Dark", "en-US", "Paraxial", "Focal length")]
    [InlineData("Light", "ja-JP", "近軸面", "焦点距離")]
    [InlineData("Lumen", "zh-CN", "近轴面", "焦距")]
    [InlineData("Isekai", "en-US", "Paraxial", "Focal length")]
    [InlineData("Pixel", "zh-CN", "近轴面", "焦距")]
    public async Task ActualGridAndPropertiesShowFocalLengthAndOpdWithoutReplacingTheLens(string theme, string language,
        string typeCaption, string focalCaption)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ParaxialSurfacePresentationTests));
        await session.Dispatch(async () =>
        {
            var previous = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language); ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(OriginalFile);
            UiLocalization.Apply(language);
            using var editor = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService(), app.MultiConfiguration);
            var window = new Window { Width = 1400, Height = 900, Content = editor };
            try
            {
                window.Show(); Tick(window);
                var grid = Find<DataGrid>(editor, "LensSurfaceGrid");
                SurfaceEditorRow Item(int number) => grid.ItemsSource.Cast<SurfaceEditorRow>().Single(r => r.Number == number);
                DataGridRow Row(int number) => grid.GetVisualDescendants().OfType<DataGridRow>().Single(r =>
                    r.DataContext is SurfaceEditorRow item && item.Number == number);
                void Away() { window.MouseMove(new Point(window.Width - 8, window.Height - 8)); Tick(window); }
                void CheckColor(Color expected)
                {
                    var row = Row(4);
                    Assert.Contains(ParaxialRowStyles.RowClass, row.Classes);
                    Assert.DoesNotContain("glass-material-row", row.Classes);
                    Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(row.GetVisualDescendants()
                        .OfType<Avalonia.Controls.Shapes.Rectangle>().Single(r => r.Name == "BackgroundRectangle").Fill).Color);
                    Assert.All(row.GetVisualDescendants().OfType<DataGridCell>(), cell =>
                        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(cell.Background).Color));
                    Assert.All(row.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)), text =>
                    {
                        var a = CustomSurfaceRowStyles.Luminance(expected);
                        var b = CustomSurfaceRowStyles.Luminance(Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
                        Assert.True((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 4.5);
                    });
                }
                var colors = ParaxialRowStyles.ColorsFor(ThemeRegistry.FromSettings(theme));
                grid.SelectedItem = Item(0); Away(); CheckColor(colors.Normal);
                var point = Row(4).TranslatePoint(new Point(18, 18), window)!.Value;
                window.MouseMove(point); Tick(window); CheckColor(colors.Hover);
                grid.SelectedItem = Item(4); Away(); CheckColor(colors.Selected);
                window.MouseMove(point); Tick(window); CheckColor(colors.SelectedHover);
                app.Prescription.UpdateSurfaceDrawingSettings(4, new(RowColor: "#204080")); Tick(window); Away();
                Assert.DoesNotContain(ParaxialRowStyles.RowClass, Row(4).Classes);
                Assert.Contains(CustomSurfaceRowStyles.RowClass, Row(4).Classes);
                app.Prescription.UpdateSurfaceDrawingSettings(4, new()); Tick(window);
                grid.SelectedItem = Item(4); Away(); CheckColor(colors.Selected);
                ThemeApplicationService.Apply(Avalonia.Application.Current!, theme == "Light" ? "Dark" : "Light"); Tick(window);
                ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language); Tick(window); Away(); CheckColor(colors.Selected);
                grid.SelectedItem = grid.ItemsSource.Cast<SurfaceEditorRow>().Single(r => r.Number == 4);
                Find<Button>(editor, "SurfacePropertiesToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                var geometry = Find<ComboBox>(editor, "SurfaceGeometry");
                Assert.Equal("近轴面", geometry.SelectedItem);
                Assert.False(geometry.IsEnabled);
                Assert.True(Find<StackPanel>(editor, "SurfaceParaxialProperties").IsVisible);
                var summary = Find<TextBlock>(editor, "SurfaceParaxialSummary");
                Assert.Contains(focalCaption, summary.Text);
                Assert.Contains("100", summary.Text); Assert.Contains("0", summary.Text);
                Assert.Contains(editor.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == typeCaption);
                var navigation = Find<ListBox>(editor, "SurfacePropertyNavigation");
                navigation.SelectedIndex = 2; Tick(window);
                Find<ComboBox>(editor, "SurfaceAperture").SelectedItem = "圆形";
                Find<Button>(editor, "ApplySurfaceProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                AssertParaxial(app.Prescription.GetSurfaces()[4]);
                navigation.SelectedIndex = 0; Tick(window);
                var evidence = Environment.GetEnvironmentVariable("PARAXIAL_SURFACE_EVIDENCE");
                if (!string.IsNullOrEmpty(evidence))
                {
                    Directory.CreateDirectory(evidence);
                    using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(evidence, theme + "-" + language + ".png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); UiLocalization.Apply(previous); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); }
        }, CancellationToken.None);
    }

    private static void AssertParaxial(SurfaceRowDto row)
    {
        Assert.Equal("近轴透镜", row.InteractionKind);
        var lens = Assert.IsType<ParaxialLensSurfaceDto>(row.ParaxialLens);
        Assert.Equal(.01, lens.PowerXPerMillimeter); Assert.Equal(.01, lens.PowerYPerMillimeter); Assert.Equal(0, lens.OpdMode);
    }
    private static T Find<T>(Control owner, string name) where T : Control => owner.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
