using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MaterialLibraryPanelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light", "zh-CN", 1280, 960)]
    [InlineData("Light", "zh-CN", 720, 1100)]
    [InlineData("Dark", "en-US", 1280, 960)]
    [InlineData("Lumen", "ja-JP", 1280, 960)]
    [InlineData("Isekai", "en-US", 1280, 960)]
    public async Task RealCatalogDetailsShowTheRequestedSourceValuesAndRemainReachable(
        string theme, string language, int width, int height)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MaterialLibraryPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.Apply(language);
            DisplayTypography.Configure(new() { Theme = theme });
            using var application = WorkbenchApplication.Create("cooke");
            var panel = new MaterialLibraryPanel(application.Materials);
            var window = new Window { Width = width, Height = height, Content = panel };
            try
            {
                window.Show(); Tick(window); UiLocalization.Attach(window);
                Family(panel).SelectedItem = "CDGM";
                Glasses(panel).SelectedItem = "D-ZLAF81-25"; Tick(window);
                var fields = Fields(panel);
                Assert.Equal("4", fields["熔融频率"].Text);
                Assert.Equal("anneal rate:25℃/h", fields["标注"].Text);
                Assert.Equal("13.7", fields["参考成本"].Text);
                foreach (var key in new[] { "CR", "FR", "SR", "AR", "PR" }) Assert.Equal("?", fields[key].Text);
                var caption = panel.GetVisualDescendants().OfType<TextBlock>().Single(t =>
                    t.Text == UiLocalization.Text("目录资料"));
                Assert.NotEmpty(caption.Text!);
                Save(window, $"material-{theme}-{language}-{width}.png");
                foreach (var key in new[] { "熔融频率", "标注", "参考成本", "CR", "FR", "SR", "AR", "PR" })
                {
                    var field = fields[key];
                    Assert.True(field.IsReadOnly);
                    field.BringIntoView(); Tick(window);
                    var bounds = new Rect(field.Bounds.Size).TransformToAABB(field.TransformToVisual(window)!.Value);
                    Assert.True(bounds.Width > 24 && bounds.Height >= 30);
                    Assert.InRange(bounds.Left, 0, window.ClientSize.Width);
                    Assert.InRange(bounds.Right, 0, window.ClientSize.Width + 1);
                    Assert.True(bounds.Bottom > 0 && bounds.Top < window.ClientSize.Height);
                }
                var glass = application.Materials.FindGlass("CDGM:D-ZLAF81-25")!;
                var report = MaterialLibraryPanel.BuildGlassReport(glass);
                Assert.Contains("熔融频率：4", report);
                Assert.Contains("标注：anneal rate:25℃/h", report);
                Assert.Contains("参考成本：13.7", report);
                Assert.Contains("CR：?", report);
            }
            finally { window.Close(); UiLocalization.Apply("zh-CN"); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CdgmUsesOneCurrentCatalogAndBrowsingDoesNotEditTheExistingProject()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MaterialLibraryPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            var before = application.Documents.GetRecoveryState();
            var revision = application.Events.Revision;
            var glasses = application.Materials.GetGlasses();
            var catalogJson = JsonSerializer.Serialize(glasses, JsonOptions);
            var rawCatalogs = new[] { "CDGM", "CDGM-H", "CDGM-ZEMAX202309", "CDGM20113-P" };
            var cdgm = glasses.Where(g => g.Manufacturer == "CDGM").ToArray();
            var panel = new MaterialLibraryPanel(application.Materials);
            var window = new Window { Width = 1280, Height = 960, Content = panel };
            try
            {
                window.Show(); Tick(window);
                var familyNames = Family(panel).Items.Cast<string>().ToArray();
                Assert.Single(familyNames, name => name == "CDGM");
                Assert.DoesNotContain("CDGM-H", familyNames);
                Assert.DoesNotContain("CDGM-ZEMAX202309", familyNames);
                Assert.DoesNotContain("CDGM20113-P", familyNames);
                Family(panel).SelectedItem = "CDGM"; Tick(window);
                var list = Glasses(panel).Items.Cast<string>().ToArray();
                Assert.Equal(cdgm.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n), list.OrderBy(n => n));
                Assert.Equal(list.Length, list.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Assert.Equal(352, list.Length);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "MaterialSourceCatalog");
                foreach (var name in new[] { "BAF4", "H-K9L", "D-ZLAF81-25" })
                {
                    Glasses(panel).SelectedItem = name; Tick(window);
                    var original = Assert.Single(cdgm, g => g.Name == name);
                    Assert.Equal(JsonSerializer.Serialize(original, JsonOptions), JsonSerializer.Serialize(Selected(panel), JsonOptions));
                    Assert.Equal(original.Comment.Length == 0 ? "—" : original.Comment, Fields(panel)["标注"].Text);
                    var displayedCoefficients = Private<TextBox[]>(panel, "_coefficientValues");
                    Assert.Equal(OpticalSystemDesign.Application.Formatting.NumericDisplayFormatter.Format(
                        original.DispersionCoefficients[0], System.Globalization.CultureInfo.InvariantCulture), displayedCoefficients[0].Text);
                    Assert.Contains($"材料库：{original.Manufacturer}", MaterialLibraryPanel.BuildGlassReport(original));
                }
                Assert.All(rawCatalogs.Skip(1), name => Assert.DoesNotContain(name, application.Materials.GetCatalogNames()));
                Assert.Equal(catalogJson, JsonSerializer.Serialize(application.Materials.GetGlasses(), JsonOptions));
                Assert.Equal(before, application.Documents.GetRecoveryState());
                Assert.Equal(revision, application.Events.Revision);
                Console.WriteLine($"CDGM September 2026 materials: {cdgm.Length}; unique names: {list.Length}; active catalog: CDGM");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("nonfinite")]
    public async Task CostAndResistanceKeepTheirPositionsAndDistinguishMissingFromZero(string kind)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MaterialLibraryPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            var basis = application.Materials.FindGlass("CDGM:BAF4")!;
            var values = kind switch
            {
                "valid" => new[] { 12.3, 0, 1.5, 2, 3, 4 },
                "missing" => new[] { -1.0, -1 },
                _ => new[] { double.NaN, double.PositiveInfinity, -1, 0, 2, double.NegativeInfinity }
            };
            var expected = kind switch
            {
                "valid" => new[] { "12.3", "0", "1.5", "2", "3", "4" },
                "missing" => new[] { "?", "?", "?", "?", "?", "?" },
                _ => new[] { "?", "?", "?", "0", "2", "?" }
            };
            var fixture = basis with { Manufacturer = "CDGM-CUSTOM", Name = "UI-FIXTURE", MeltFrequency = 0, Comment = "", OtherData = values };
            var panel = new MaterialLibraryPanel(new StaticMaterials([fixture]));
            var window = new Window { Width = 1280, Height = 960, Content = panel };
            try
            {
                window.Show(); Tick(window);
                Assert.Equal("CDGM-CUSTOM", Family(panel).SelectedItem);
                var fields = Fields(panel);
                Assert.Equal("0", fields["熔融频率"].Text);
                Assert.Equal("—", fields["标注"].Text);
                var keys = new[] { "参考成本", "CR", "FR", "SR", "AR", "PR" };
                var report = MaterialLibraryPanel.BuildGlassReport(fixture);
                for (var i = 0; i < keys.Length; i++)
                {
                    Assert.Equal(expected[i], fields[keys[i]].Text);
                    Assert.Contains($"{keys[i]}：{expected[i]}", report);
                }
                var search = Private<TextBox>(panel, "_search");
                search.Text = "no-such-material"; Tick(window);
                Assert.All(fields.Values, field => Assert.Equal("—", field.Text));
                search.Text = ""; Tick(window);
                Assert.Equal(expected[0], fields["参考成本"].Text);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static GlassMaterialDto Selected(MaterialLibraryPanel panel) =>
        (GlassMaterialDto)typeof(MaterialLibraryPanel).GetMethod("SelectedGlass", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null)!;
    private static T Private<T>(MaterialLibraryPanel panel, string name) =>
        (T)typeof(MaterialLibraryPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
    private static ComboBox Family(MaterialLibraryPanel panel) => Private<ComboBox>(panel, "_catalog");
    private static ListBox Glasses(MaterialLibraryPanel panel) => Private<ListBox>(panel, "_glassList");
    private static Dictionary<string, TextBox> Fields(MaterialLibraryPanel panel) => Private<Dictionary<string, TextBox>>(panel, "_properties");
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Save(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MATERIAL_LIBRARY_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
    private sealed class StaticMaterials(IReadOnlyList<GlassMaterialDto> glasses) : IMaterialCatalogService
    {
        public IReadOnlyList<MaterialCatalogDto> GetCatalogs() => glasses.Select(g => new MaterialCatalogDto(g.Manufacturer, 1)).ToArray();
        public IReadOnlyList<string> GetCatalogNames() => glasses.Select(g => g.Manufacturer).ToArray();
        public IReadOnlyList<GlassMaterialDto> GetGlasses() => glasses;
        public GlassMaterialDto? FindGlass(string materialName) => glasses.FirstOrDefault(g => g.Name == materialName);
        public AnalysisViewDto Analyze(MaterialAnalysisRequestDto request) => throw new NotSupportedException();
        public Task<MaterialCatalogImportResultDto> ImportZemaxCatalogAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
