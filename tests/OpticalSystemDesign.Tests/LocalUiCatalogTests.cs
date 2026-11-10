using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Localization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LocalUiCatalogTests
{
    [Fact]
    public void BundledCatalogHasCompleteLanguagesAndPreservesSlotsNumbersUnitsLinksAndFormulas()
    {
        var catalog = LocalUiCatalog.Load();
        Assert.True(catalog.Entries.Count > 3000);
        Assert.All(catalog.Entries, entry => Assert.Empty(TranslationValidation.Validate(
            new(entry.Source, entry.Chinese, entry.English, entry.Japanese))));
        Assert.Contains(catalog.Entries, entry => entry.Source == "导入 Zemax NSC" && entry.English == "Import Zemax NSC");
        Assert.Contains(catalog.Entries, entry => entry.Source == "辐照度" && entry.Japanese == "放射照度");
        Assert.Contains(catalog.Entries, entry => entry.Source == "照度" && entry.English == "Illuminance");
    }

    [Theory]
    [InlineData("保存 {0}", "Save {1}", "保存 {0}", "placeholder")]
    [InlineData("曝光 10 ms", "Exposure 11 ms", "露光 10 ms", "numeric")]
    [InlineData("厚度 1.25 nm", "Thickness 1.25 mm", "膜厚 1.25 nm", "unit")]
    [InlineData("来源 https://example.test/a", "Source https://example.test/b", "出典 https://example.test/a", "URL")]
    [InlineData("n = n0 + nr2·r²；说明", "n = n0 + nr3·r³; note", "n = n0 + nr2·r²。説明", "formula")]
    [InlineData("输入 {材料ID}", "Enter {材料ID}", "入力 {材料ID}", "untranslated")]
    public void InvalidTranslationsAreRejected(string source, string english, string japanese, string reason) =>
        Assert.Contains(TranslationValidation.Validate(new(source, source, english, japanese)), error => error.Contains(reason, StringComparison.Ordinal));

    [Theory]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("placeholder")]
    public void RuntimeRejectsDamagedCatalogsInsteadOfSilentlyDroppingText(string damage)
    {
        var entries = new[] { new LocalUiTranslation("保存 {0}", "保存 {0}", "Save {0}", "保存 {0}") };
        if (damage == "duplicate") entries = [entries[0], entries[0]];
        if (damage == "empty") entries[0] = entries[0] with { Japanese = "" };
        if (damage == "placeholder") entries[0] = entries[0] with { English = "Save {1}" };
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(entries));
        Assert.Throws<InvalidDataException>(() => LocalUiCatalog.Read(stream));
    }

    [Fact]
    public void ExtractorFindsActualLabelsChoicesAndTemplatesWithoutTreatingIdsAndDataAsCaptions()
    {
        var source = """"
            class View {
              void Label(string id, string caption) { var b = new Button { Content = caption, Name = id }; }
              void Build() {
                // Content = "注释不是界面";
                var secret = "不是界面文字";
                Label("material-id", "净口径");
                var input = new TextBox { Text = model.MaterialName, PlaceholderText = "输入材料名称" };
                var label = new TextBlock { Text = """含有\反斜杠""" };
                label.Text = $"面 {surface}" + $" · 厚度 {thickness} mm";
                label.Text = "打开失败：" + path;
                label.Text = enabled ? "启用" : "关闭";
                label.Text = kind switch { 1 => "球面", _ => "非球面" };
                var choice = new ComboBox { ItemsSource = new[] { "光程差", "保存" } };
                var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header = "真实标题", Name = "tab-id" } } };
              }
            }
            """";
        var entries = LocalizationExtractor.Extract(source, "View.cs");
        foreach (var text in new[] { "净口径", "输入材料名称", "含有\\反斜杠", "面 {0} · 厚度 {1} mm", "打开失败：{0}", "启用", "关闭", "球面", "非球面", "光程差", "保存", "真实标题" })
            Assert.Contains(entries, item => item.Source == text);
        foreach (var text in new[] { "注释不是界面", "不是界面文字", "material-id", "tab-id" }) Assert.DoesNotContain(entries, item => item.Source == text);
        Assert.All(entries, item => Assert.True(item.Line > 0));
    }

    [Fact]
    public void ExtractorFollowsHelpersAcrossPartialFilesWithoutBorrowingUnrelatedClasses()
    {
        var sources = new (string Path, string Text)[]
        {
            ("View.cs", "namespace App; partial class View { object Field(string caption, object input) => new TextBlock { Text = caption }; }"),
            ("View.Layout.cs", "namespace App; partial class View { void Build() { Field(\"测量模式\", control); } }"),
            ("Other.cs", "namespace Other; partial class View { void Build() { Field(\"用户数据\", control); } }"),
            ("Data.cs", "namespace App; partial class Data { void Build() { Field(\"材料标识\", control); } }")
        };
        var entries = LocalizationExtractor.Extract(sources);
        Assert.Contains(entries, entry => entry.Source == "测量模式" && entry.Path == "View.Layout.cs" && entry.Line == 1);
        Assert.DoesNotContain(entries, entry => entry.Source is "用户数据" or "材料标识");
    }

    [Theory]
    [InlineData(UiLocalization.English, "Saved: ", "Save")]
    [InlineData(UiLocalization.Japanese, "保存完了：", "保存")]
    public void DynamicValuesRemainOpaqueIncludingNamesThatAreTranslatedUiWords(string language, string prefix, string save)
    {
        var data = @"C:\材料\保存 001.2300 nm {0}.staropt";
        var catalog = LocalUiCatalog.Load();
        Assert.True(catalog.TryTemplate("已保存：" + data, language, out var translated));
        Assert.Equal(prefix + data, translated);
        Assert.True(catalog.TryTemplate(translated, UiLocalization.Chinese, out var restored));
        Assert.Equal("已保存：" + data, restored);
        Assert.Equal(save, UiLocalization.Text("保存", language));
        Assert.True(catalog.TryGet("nm", language, out var unit)); Assert.Equal("nm", unit);
        Assert.True(catalog.TryGet("N-BK7", language, out var material)); Assert.Equal("N-BK7", material);
    }

    [Fact]
    public async Task LanguageRoundTripsUpdateExistingLateAndPrelocalizedControlsButPreserveActualData()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.Chinese);
            var label = new TextBlock { Text = "导入 Zemax NSC" };
            var material = UiLocalization.Preserve(new TextBlock { Text = "保存" });
            var filename = UiLocalization.Preserve(new TextBlock { Text = @"D:\保存\实验 001.2300 nm.staropt" });
            var formula = UiLocalization.Preserve(new TextBlock { Text = "n = n0 + nr2·r²" });
            var numeric = new TextBlock { Text = "001.2300E-05 W/m²", Classes = { ProductFonts.NumericClass } };
            var input = new TextBox { Text = "保存", PlaceholderText = "搜索" };
            var bound = new TextBlock { DataContext = new NameData("保存") };
            bound.Bind(TextBlock.TextProperty, new Binding(nameof(NameData.Name)));
            var materialChoice = UiLocalization.Preserve(new ComboBox { ItemsSource = new[] { "保存", "N-BK7" }, SelectedIndex = 0 });
            var content = new StackPanel { Children = { label, material, filename, formula, numeric, input, bound, materialChoice } };
            var window = new Window { Width = 700, Height = 600, Content = content };
            try
            {
                window.Show(); Tick(window); UiLocalization.Apply(UiLocalization.English); Tick(window);
                var later = new Button { Content = UiLocalization.Text("导入 Zemax NSC") };
                content.Children.Add(later); Tick(window);
                foreach (var language in new[] { UiLocalization.Japanese, UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese, UiLocalization.Chinese })
                {
                    UiLocalization.Apply(language); Tick(window);
                    Assert.Equal(UiLocalization.Text("导入 Zemax NSC"), label.Text);
                    Assert.Equal(UiLocalization.Text("导入 Zemax NSC"), later.Content);
                    Assert.Equal("保存", material.Text); Assert.Equal("保存", input.Text); Assert.Equal("保存", bound.Text);
                    Assert.Equal(@"D:\保存\实验 001.2300 nm.staropt", filename.Text);
                    Assert.Equal("n = n0 + nr2·r²", formula.Text);
                    Assert.Equal("001.2300E-05 W/m²", numeric.Text);
                    Assert.Equal("保存", materialChoice.SelectedItem);
                    Assert.Contains(materialChoice.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "保存");
                }
                UiLocalization.Apply(UiLocalization.English);
                var lateData = new TextBlock { Text = "保存" }; content.Children.Add(lateData); Tick(window);
                Assert.Equal("Save", lateData.Text);
                UiLocalization.Preserve(lateData); Assert.Equal("保存", lateData.Text);
                Assert.Equal("Saved: 保存", UiLocalization.Format("已保存：{0}", "保存"));
            }
            finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RealMainWindowLanguageMenuKeepsCommandAndPrescriptionIdentitiesAcrossThreeLanguages()
    {
        var folder = Path.Combine(Path.GetTempPath(), "optical-local-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var previous = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", Path.Combine(folder, "settings"));
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            var settings = new AppSettings { Language = UiLocalization.Chinese };
            var main = new global::OpticalSystemDesign.App.MainWindow(settings, application, new WorkspaceSessionStore(Path.Combine(folder, "workspace")));
            try
            {
                main.Show(); Tick(main);
                var revision = application.Events.Revision;
                var surfaces = application.Documents.GetSnapshot().SurfaceCount;
                var ribbon = main.GetVisualDescendants().OfType<TabControl>().Single(item => item.Classes.Contains("ribbon-tabs"));
                ribbon.SelectedItem = ribbon.Items.Cast<TabItem>().Single(item => AutomationProperties.GetName(item) == UiLocalization.Text("设置"));
                Tick(main);
                var languageButton = main.GetVisualDescendants().OfType<DropDownButton>().Single(item => AutomationProperties.GetAutomationId(item) == "ribbon-language");
                var flyout = Assert.IsType<MenuFlyout>(languageButton.Flyout);
                foreach (var language in new[] { UiLocalization.English, UiLocalization.Japanese, UiLocalization.Chinese })
                {
                    var item = flyout.Items.OfType<MenuItem>().Single(menu => AutomationProperties.GetAutomationId(menu) == "language-" + language);
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Tick(main);
                    Assert.Equal(language, UiLocalization.CurrentLanguage); Assert.Equal(language, settings.Language); Assert.True(item.IsChecked);
                    Assert.Equal(UiLocalization.Text("语言"), AutomationProperties.GetName(languageButton));
                    Assert.Equal(revision, application.Events.Revision); Assert.Equal(surfaces, application.Documents.GetSnapshot().SurfaceCount);
                }
            }
            finally { main.Close(); UiLocalization.Apply(UiLocalization.Chinese); Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previous); }
        }, CancellationToken.None);
    }

    private sealed record NameData(string Name);
    private static void Tick(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); }
}
