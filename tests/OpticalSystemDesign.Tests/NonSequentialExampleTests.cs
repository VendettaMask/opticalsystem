using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialExampleTests
{
    [Fact]
    public void PackagedCatalogIncludesAllSourceCategoriesWithOriginalBytesAndUniqueActions()
    {
        var expected = new (string, int)[]
        {
            ("Birefringence", 4), ("Coherence Interference and Diffraction", 4), ("Colorimetry", 4),
            ("Diffractives", 6), ("Fibers", 7), ("Fresnel Lenses", 6), ("Geometry Creation", 12),
            ("GRINs", 2), ("LightningTrace", 3), ("Miscellaneous", 19), ("Phosphors and Fluorescence", 2),
            ("Prisms", 12), ("Ray splitting", 4), ("Reflectors", 7), ("Scattering", 7), ("Sources", 7)
        };
        Assert.Equal(expected, NonSequentialExampleCatalog.Groups.Select(group => (group.Name, group.Examples.Count)));
        Assert.Equal(106, NonSequentialExampleCatalog.Examples.Count);
        var actions = SequentialExampleCatalog.Examples.Select(e => e.ActionId)
            .Concat(NonSequentialExampleCatalog.Examples.Select(e => e.ActionId)).ToArray();
        Assert.Equal(201, actions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var example in NonSequentialExampleCatalog.Examples)
            Assert.Equal(example.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(example.FilePath))));
        Assert.Equal(106, Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Examples", "NonSequential"),
            "*", SearchOption.AllDirectories).Count(path => Path.GetExtension(path).Equals(".zmx", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void EveryBundledFileUsesLosslessNscInspectionAndReportsItsActualImportBoundary()
    {
        var audit = NonSequentialExampleCatalog.Examples.Select(example =>
        {
            var result = ZemaxNonSequentialReader.Read(example.FilePath);
            Assert.Equal(example.Sha256, result.SourceSha256.ToLowerInvariant());
            Assert.Equal(File.ReadAllBytes(example.FilePath), result.OriginalBytes());
            Assert.NotEmpty(result.Objects);
            Assert.Equal(result.CanApply, result.Document is not null && !result.Diagnostics.Any(d => d.Blocking));
            if (!result.CanApply) Assert.Contains(result.Diagnostics, d => d.Blocking);
            return new
            {
                example.RelativePath,
                result.CanApply,
                ObjectCount = result.Objects.Count,
                Blocking = result.Diagnostics.Where(d => d.Blocking).GroupBy(d => d.Code)
                    .ToDictionary(g => g.Key, g => g.Count()),
                MissingAssets = result.Assets.Where(a => !a.Exists).Select(a => a.Reference).ToArray()
            };
        }).ToArray();
        var directory = Environment.GetEnvironmentVariable("NSC_EXAMPLES_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "import-audit.json"), JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task EveryCategoryEntrySelectsItsOwnOriginalFile()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var selected = new List<NonSequentialExample>();
            var button = MainWindow.CreateNonSequentialExamplesButton(example => { selected.Add(example); return Task.CompletedTask; });
            var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
            Assert.Contains("ribbon-dropdown", button.Classes);
            Assert.Equal(16, flyout.Items.Count);
            foreach (var (category, group) in flyout.Items.Cast<MenuItem>().Zip(NonSequentialExampleCatalog.Groups))
            {
                Assert.Equal(group.DisplayName, category.Header);
                Assert.NotEqual(group.DisplayName, UiLocalization.Text(group.DisplayName, UiLocalization.English));
                Assert.False(string.IsNullOrWhiteSpace(UiLocalization.Text(group.DisplayName, UiLocalization.Japanese)));
                foreach (var (item, example) in category.Items.Cast<MenuItem>().Zip(group.Examples))
                {
                    Assert.Equal(example.RelativePath, ToolTip.GetTip(item));
                    Assert.Null(AutomationProperties.GetHelpText(item));
                    Assert.Equal("ribbon-" + example.ActionId, AutomationProperties.GetAutomationId(item));
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.Same(example, selected[^1]);
                }
            }
            Assert.Equal(106, selected.Count);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", "zh-CN", 1, false)]
    [InlineData("Dark", "ja-JP", 1.5, false)]
    [InlineData("Isekai", "en-US", 2, false)]
    [InlineData("Light", "zh-CN", 1, true)]
    [InlineData("Dark", "ja-JP", 1.5, true)]
    [InlineData("Isekai", "en-US", 2, true)]
    public async Task NonSequentialFileRibbonDirectlyImportsSupportedSamplesAndOnlyReportsUnavailableFiles(
        string theme, string language, double scaling, bool unavailable)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var files = new TemporaryFiles();
            using var application = WorkbenchApplication.Create();
            application.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
            var settings = new AppSettings
            {
                Theme = theme,
                Language = language,
                WorkbenchMode = "NonSequential",
                WindowWidth = 1600,
                WindowHeight = 1000
            };
            var window = new MainWindow(settings, application, new WorkspaceSessionStore(files.Path("workspace")));
            var original = application.Documents.GetRecoveryState();
            var originalScene = application.NonSequential.GetDesignSceneIdentity();
            try
            {
                var ready = false; window.StartupCompleted += (_, _) => ready = true;
                window.Show(); window.SetRenderScaling(scaling);
                for (var n = 0; n < 250 && !ready; n++) { await Task.Delay(10); Tick(window); }
                Assert.True(ready);
                var ribbon = window.GetVisualDescendants().OfType<TabControl>().Single(c => c.Classes.Contains("ribbon-tabs"));
                ribbon.SelectedIndex = 0; Tick(window);
                var button = window.GetVisualDescendants().OfType<DropDownButton>()
                    .Single(b => AutomationProperties.GetAutomationId(b) == "ribbon-menu-examples");
                Assert.True(button.IsEffectivelyVisible);
                var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
                Assert.Equal(16, flyout.Items.Count);
                flyout.ShowAt(button); Tick(window);
                var category = flyout.Items.Cast<MenuItem>().First();
                category.IsSubMenuOpen = true; Tick(window);
                var index = unavailable ? 1 : 0;
                var item = category.Items.Cast<MenuItem>().ElementAt(index);
                var example = NonSequentialExampleCatalog.Examples[index];
                var categoryRoot = TopLevel.GetTopLevel(category)!;
                var exampleRoot = TopLevel.GetTopLevel(item)!;
                categoryRoot.SetRenderScaling(scaling); exampleRoot.SetRenderScaling(scaling); Tick(window);
                Assert.Equal(UiLocalization.Text(NonSequentialExampleCatalog.Groups.First().DisplayName), category.Header);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                flyout.Hide();
                Assert.Equal(OpticalWorkbenchMode.NonSequential, application.Modes.CurrentMode);
                if (unavailable)
                {
                    for (var n = 0; n < 250 && !window.OwnedWindows.Any(); n++) { await Task.Delay(10); Tick(window); }
                    var failure = Assert.Single(window.OwnedWindows);
                    Assert.Equal("nsc-import-unavailable", AutomationProperties.GetAutomationId(failure));
                    failure.SetRenderScaling(scaling); Tick(failure);
                    Assert.Contains(failure.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == UiLocalization.Text("无法导入此文件。"));
                    Assert.Empty(failure.GetVisualDescendants().OfType<TextBox>());
                    Assert.Equal(original, application.Documents.GetRecoveryState());
                    Capture(failure, $"{theme}-unavailable.png");
                    var close = failure.GetVisualDescendants().OfType<Button>().Single();
                    Assert.True(close.IsEffectivelyVisible); Assert.True(close.Bounds.Width > 0);
                    close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else
                {
                    for (var n = 0; n < 250 && application.NonSequential.GetDocument().Objects.Count != 5; n++) { await Task.Delay(10); Tick(window); }
                    Assert.Equal(5, application.NonSequential.GetDocument().Objects.Count);
                    Assert.Empty(window.OwnedWindows);
                    Capture(window, $"{theme}-imported.png");
                    Assert.True(application.Documents.Undo());
                    Assert.Equal(originalScene, application.NonSequential.GetDesignSceneIdentity());
                }
            }
            finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); }
        }, CancellationToken.None);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(TopLevel root, string name)
    {
        var directory = Environment.GetEnvironmentVariable("NSC_EXAMPLES_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = root.CaptureRenderedFrame(); Assert.NotNull(image);
        using var stream = File.Create(Path.Combine(directory, name));
        image.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("nsc-examples-");
        public string Path(string name) => System.IO.Path.Combine(_directory.FullName, name);
        public void Dispose()
        {
            var root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            var target = System.IO.Path.GetFullPath(_directory.FullName);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || !System.IO.Path.GetFileName(target).StartsWith("nsc-examples-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test directory.");
            Directory.Delete(target, true);
        }
    }
}
