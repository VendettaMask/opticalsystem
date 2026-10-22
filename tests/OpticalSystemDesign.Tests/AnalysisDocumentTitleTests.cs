using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

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
    public void OpeningImageAnalysisUsesDisplayNameWithoutChangingAnalysisIdentity(string key, string title)
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
