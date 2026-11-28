using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisDocumentTitleTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [Theory]
    [InlineData("Image Simulation", "图像模拟")]
    [InlineData("Geometric Image Analysis", "几何图像分析")]
    [InlineData("Geometric Bitmap Image Analysis", "几何位图图像分析")]
    [InlineData("Light Source Analysis", "光源分析")]
    [InlineData("Partially Coherent Image Analysis", "部分相干图像分析")]
    [InlineData("Extended Diffraction Image Analysis", "扩展图像分析")]
    [InlineData("Relative Illumination", "相对照度")]
    [InlineData("扩展图像分析", "扩展图像分析")]
    public async Task OpeningImageAnalysisUsesDisplayNameWithoutChangingAnalysisIdentity(string key, string title)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            panels.ShowAnalysis(key);
            var canonical = application.Analyses.CanonicalKey(key);
            var document = Assert.Single(panels.Factory.OpenDocuments(), item => item.Id == $"analysis:{canonical}");
            Assert.Equal(title, document.Title);
            var descriptor = panels.Factory.Descriptor(document.Id)!;
            Assert.Equal(title, descriptor.Title);
            Assert.Equal(key, descriptor.AnalysisName);
            var content = Assert.IsType<AnalysisPanel>(document.Context);
            Assert.Equal(canonical, content.AnalysisKey);
            var instance = content.InstanceId;
            panels.ShowAnalysis(title);
            Assert.Same(document, Assert.Single(panels.Factory.OpenDocuments(), item => item.Id == document.Id));
            Assert.Same(content, document.Context);
            Assert.Equal(instance, content.InstanceId);
            var second = Assert.Single(panels.Factory.OpenDocuments(), item => item.Title == $"{title} 2");
            var secondContent = Assert.IsType<AnalysisPanel>(second.Context);
            Assert.Equal(canonical, secondContent.AnalysisKey);
            Assert.NotEqual(instance, secondContent.InstanceId);
            Assert.NotSame(content.Settings, secondContent.Settings);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(OpticalWorkbenchMode.Sequential)]
    [InlineData(OpticalWorkbenchMode.NonSequential)]
    public async Task EveryStandardAnalyzeMenuCommandOpensTwoIndependentPanels(OpticalWorkbenchMode mode)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisDocumentTitleTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            application.Modes.SwitchTo(mode);
            using var panels = CreateManager(application);
            var commands = mode == OpticalWorkbenchMode.Sequential
                ? WorkbenchAnalysisCatalog.RibbonCommands : WorkbenchAnalysisCatalog.NonSequentialRibbonCommands;
            foreach (var command in commands.Where(
                         command => command.Kind == AnalysisRibbonCommandKind.Analysis))
            {
                panels.ShowAnalysis(command.Name);
                panels.ShowAnalysis(command.Name);
                var documents = panels.Factory.OpenDocuments().Where(document => document.Context is AnalysisPanel).ToArray();
                Assert.True(documents.Length == 2, command.Name);
                var pair = documents.Select(document => (AnalysisPanel)document.Context!).ToArray();
                Assert.Equal(pair[0].AnalysisKey, pair[1].AnalysisKey);
                Assert.NotEqual(pair[0].InstanceId, pair[1].InstanceId);
                Assert.NotEqual(documents[0].Title, documents[1].Title);
                pair[0].IsLocked = true;
                Assert.False(pair[1].IsLocked);
                panels.Factory.CloseDockable(documents[0]);
                Assert.Contains(documents[1], panels.Factory.OpenDocuments());
                panels.Factory.CloseDockable(documents[1]);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CloningAndSavingSpotDiagramsCaptureEachWindowsVisibleSettings()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisDocumentTitleTests));
        await session.Dispatch(async () =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            using var panels = CreateManager(application);
            panels.ShowAnalysis("Spot Diagram");
            panels.ShowAnalysis("Spot Diagram");
            var originals = panels.Factory.OpenDocuments().Where(d => d.Context is AnalysisPanel).ToArray();
            var first = Assert.IsType<AnalysisPanel>(originals[0].Context);
            var second = Assert.IsType<AnalysisPanel>(originals[1].Context);
            var pattern = second.GetLogicalDescendants().OfType<ComboBox>()
                .Single(picker => picker.Items.Contains("矩形"));
            pattern.SelectedItem = "矩形";
            panels.CloneActiveAnalysis();
            var clone = Assert.Single(panels.Factory.OpenDocuments(), d => d.Title.EndsWith("（副本）", StringComparison.Ordinal));
            Assert.Equal("矩形", Assert.IsType<AnalysisPanel>(clone.Context).Settings["Pattern"]);
            Assert.Equal("六边", first.Settings["Pattern"]);
            await panels.SaveLayoutSlotAsync(1);
            await panels.LoadLayoutSlotAsync(1);
            Assert.Equal("六边", panels.Factory.DocumentContent<AnalysisPanel>(originals[0].Id)!.Settings["Pattern"]);
            Assert.Equal("矩形", panels.Factory.DocumentContent<AnalysisPanel>(originals[1].Id)!.Settings["Pattern"]);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MaterialAnalysesOpenIndependentPanelsForEachKind()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisDocumentTitleTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            foreach (var kind in Enum.GetValues<MaterialAnalysisKind>())
            {
                panels.ShowMaterialAnalysis(kind);
                panels.ShowMaterialAnalysis(kind);
                var documents = panels.Factory.OpenDocuments().Where(d => d.Context is MaterialAnalysisPanel).ToArray();
                Assert.Equal(2, documents.Length);
                Assert.NotSame(documents[0].Context, documents[1].Context);
                Assert.NotEqual(documents[0].Title, documents[1].Title);
                foreach (var document in documents) panels.Factory.CloseDockable(document);
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(UiLocalization.Chinese)]
    [InlineData(UiLocalization.English)]
    [InlineData(UiLocalization.Japanese)]
    public async Task DockedAndIndependentImageTitlesFollowLanguageChanges(string initialLanguage)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisDocumentTitleTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.InstallGlobalHooks();
            UiLocalization.Apply(initialLanguage);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            panels.ShowAnalysis("Extended Diffraction Image Analysis");
            var document = panels.Factory.OpenDocuments().Single(item => item.Id == "analysis:Extended Diffraction Image Analysis");
            var content = Assert.IsType<AnalysisPanel>(document.Context);
            var instance = content.InstanceId;
            panels.ShowAnalysis("Extended Diffraction Image Analysis");
            var second = panels.Factory.OpenDocuments().Single(item => item.Context is AnalysisPanel && item.Id != document.Id);
            second.Context = new Border();
            // Keep the real Dock controls/title bindings; do not run an optical
            // image computation merely to exercise caption localization.
            document.Context = new Border();
            HostWindow? floating = null;
            panels.Factory.HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
            {
                [nameof(IDockWindow)] = () => floating = new HostWindow()
            };
            var main = new Window { Width = 1200, Height = 800, Content = panels.WorkspaceControl };
            try
            {
                main.Show();
                Render(main);
                Assert.Contains(main.GetVisualDescendants().OfType<TextBlock>(), block =>
                    block.Text == UiLocalization.Text("扩展图像分析", initialLanguage));
                foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(language);
                    Render(main);
                    Assert.Equal(UiLocalization.Text("扩展图像分析", language) + " 2", second.Title);
                    Assert.Contains(main.GetVisualDescendants().OfType<TextBlock>(), block =>
                        block.Text == UiLocalization.Text("扩展图像分析", language));
                }

                panels.Factory.FloatDockable(document);
                Assert.NotNull(floating);
                foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese, UiLocalization.Chinese })
                {
                    UiLocalization.Apply(language);
                    Render(floating);
                    Assert.Equal(UiLocalization.Text("扩展图像分析", language), floating.Title);
                }
                Assert.Equal("Extended Diffraction Image Analysis", content.AnalysisKey);
                Assert.Equal(instance, content.InstanceId);
                Assert.Equal("Extended Diffraction Image Analysis", panels.Factory.Descriptor(document.Id)!.AnalysisName);
            }
            finally
            {
                floating?.Close();
                main.Close();
                UiLocalization.Apply(previousLanguage);
            }
        }, CancellationToken.None);
    }

    private static PanelManager CreateManager(WorkbenchApplication application) => new(application, new AppSettings(),
        new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "analysis-title-" + Guid.NewGuid().ToString("N"))));

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
