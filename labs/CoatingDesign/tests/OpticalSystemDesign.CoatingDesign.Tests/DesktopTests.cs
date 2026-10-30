using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.CoatingDesign.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopCollection { public const string Name = "Coating desktop"; }

[Collection(DesktopCollection.Name)]
public sealed partial class DesktopTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(1200, 900)]
    [InlineData(820, 900)]
    [InlineData(820, 640)]
    public async Task RealMouseWorkflowEditsLayersAndClearsStaleSpectrum(int width, int height)
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow { Width = width, Height = height, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); Tick(window);
                Click(window, "1 生成膜系"); await window.PendingOperation; Tick(window);
                Assert.NotNull(window.CurrentExperiment.Result);
                var table = window.GetLogicalDescendants().OfType<DataGrid>().Single();
                var rows = (IEnumerable<LayerRow>)table.ItemsSource;
                var originalCount = rows.Count();
                table.SelectedItem = rows.First();
                Click(window, "复制"); await window.PendingOperation;
                Assert.Null(window.CurrentExperiment.Result);
                Assert.Equal(originalCount + 1, rows.Count());
                Click(window, "删除"); await window.PendingOperation;
                Assert.Equal(originalCount, rows.Count());
                Click(window, "计算 / 复验"); await window.PendingOperation;
                Assert.NotNull(window.CurrentExperiment.Result);
                Click(window, "1 生成膜系"); await window.PendingOperation;
                Click(window, "2 优化"); await window.PendingOperation;
                Assert.True(window.CurrentExperiment.Result!.Passed);
                Capture(window, $"ar-{width}-{height}");
                Find<TextBox>(window, "中心波长 (nm)").Text = "not-a-number";
                Assert.Null(window.CurrentExperiment.Result);
                Click(window, "计算 / 复验"); await window.PendingOperation;
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("请输入有效数值", StringComparison.Ordinal) == true);
                Assert.Null(window.CurrentExperiment.Result);
                Click(window, "示例：窄带"); await window.PendingOperation;
                Assert.Equal(DesignKind.NarrowBand, window.CurrentExperiment.Target.Kind);
                Assert.Null(window.CurrentExperiment.Result);
                Click(window, "1 生成膜系"); await window.PendingOperation;
                Assert.NotNull(window.CurrentExperiment.Result);
                Capture(window, $"narrow-{width}-{height}");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ThicknessFormattingDoesNotRoundStoredValuesAndInvalidEditsRemainInvalid()
    {
        var layer = new FilmLayer("test", 91.123456789);
        var row = new LayerRow(layer);
        Assert.Equal(layer, row.ToLayer());
        row.ThicknessText = "无效";
        Assert.Throws<ArgumentException>(() => row.ToLayer());
    }

    [Theory]
    [InlineData("clean", true)]
    [InlineData("cancel", false)]
    [InlineData("discard", true)]
    [InlineData("save-cancel", false)]
    [InlineData("save-failed", false)]
    [InlineData("save-ok", true)]
    [InlineData("changed-during-save", false)]
    public async Task HostClosePreservesUnsavedExperimentUnlessExplicitlyResolved(string scenario, bool canClose)
    {
        var directory = Path.Combine(Path.GetTempPath(), "coating-host-close-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "saved.coating.json");
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                MainWindow? window = null;
                var prompts = 0;
                window = new MainWindow(new LaboratoryFileDialogs(() =>
                {
                    if (scenario == "changed-during-save") Find<TextBox>(window!, "实验名称").Text = "保存期间的新修改";
                    return Task.FromResult<string?>(scenario == "save-cancel" ? null : scenario == "save-failed" ? directory : path);
                }, () => Task.FromResult<string?>(null), () => Task.FromResult<string?>(null)))
                {
                    ChooseUnsavedChanges = _ =>
                    {
                        prompts++;
                        return Task.FromResult(scenario == "cancel" ? global::OpticalSystemDesign.App.UnsavedChangesChoice.Cancel
                            : scenario == "discard" ? global::OpticalSystemDesign.App.UnsavedChangesChoice.Discard
                            : global::OpticalSystemDesign.App.UnsavedChangesChoice.Save);
                    }
                };
                try
                {
                    window.Show(); Tick(window);
                    Assert.False(window.HasUnsavedChanges);
                    if (scenario != "clean") Find<TextBox>(window, "实验名称").Text = "未保存的镀膜设计";
                    Assert.Equal(canClose, await window.PrepareForHostCloseAsync());
                    Assert.Equal(scenario == "clean" ? 0 : 1, prompts);
                    Assert.Equal(scenario is not ("clean" or "save-ok"), window.HasUnsavedChanges);
                    if (scenario is "save-ok" or "changed-during-save")
                        Assert.Equal("未保存的镀膜设计", (await ExperimentStore.OpenAsync(path)).Name);
                    else Assert.False(File.Exists(path));
                }
                finally { window.CloseAfterHostApproval(); }
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SaveOpenExportButtonsPreserveSnapshotAndBrokenFileClearsOldContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coating-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "example.coating.json");
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(path),
                    () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(directory)))
                { ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window);
                    Click(window, "1 生成膜系"); await window.PendingOperation;
                    var before = window.CurrentExperiment;
                    Click(window, "保存实验"); await window.PendingOperation;
                    Assert.True(File.Exists(path));
                    Click(window, "示例：高反"); await window.PendingOperation;
                    Click(window, "打开实验"); await window.PendingOperation;
                    Assert.Equal(before.Fingerprint(), window.CurrentExperiment.Fingerprint());
                    Assert.Equal(before.Result!.Spectrum, window.CurrentExperiment.Result!.Spectrum);
                    Click(window, "导出结果"); await window.PendingOperation;
                    Assert.NotEmpty(Directory.GetFiles(directory, "spectrum.csv", SearchOption.AllDirectories));
                    await File.WriteAllTextAsync(path, "{ damaged");
                    Click(window, "打开实验"); await window.PendingOperation;
                    Assert.Null(window.CurrentExperiment.Result);
                    Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("未完成", StringComparison.Ordinal) == true);
                }
                finally { window.Close(); }
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task SharedThemesSwitchWithoutChangingExperimentAndSidebarStaysCompact(int theme)
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow { Width = 820, Height = 900, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); Tick(window);
                Click(window, "1 生成膜系"); await window.PendingOperation;
                var fingerprint = window.CurrentExperiment.Fingerprint();
                var product = Assert.IsType<global::OpticalSystemDesign.App.App>(Avalonia.Application.Current);
                product.ApplyTheme(global::OpticalSystemDesign.App.Theming.ThemeRegistry.SelectableThemes[theme].SettingsValue);
                Tick(window);
                var plot = window.GetLogicalDescendants().OfType<SpectrumPlot>().Single();
                if (global::OpticalSystemDesign.App.Theming.ThemeRegistry.SelectableThemes[theme].FollowsSystem
                    && !plot.TryFindResource(global::OpticalSystemDesign.App.Services.ThemeResourceBindings.PlotText, plot.ActualThemeVariant, out _))
                {
                    // System mode need not resolve a platform palette in Headless; fallback must stay visible.
                    Assert.Equal(global::OpticalSystemDesign.App.Theming.ThemePalette.Light.PlotText,
                        Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(plot.PlotTextBrush).Color);
                    Assert.Equal(global::OpticalSystemDesign.App.Theming.ThemePalette.Light.PlotBackground,
                        Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(plot.PlotBackgroundBrush).Color);
                }
                else
                {
                    Assert.True(plot.TryFindResource(global::OpticalSystemDesign.App.Services.ThemeResourceBindings.PlotText, plot.ActualThemeVariant, out var plotText));
                    Assert.True(plot.TryFindResource(global::OpticalSystemDesign.App.Services.ThemeResourceBindings.PlotBackground, plot.ActualThemeVariant, out var plotBackground));
                    Assert.Same(plotText, plot.PlotTextBrush);
                    Assert.Same(plotBackground, plot.PlotBackgroundBrush);
                }
                Assert.True(Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(plot.PlotTextBrush).Color.A > 0);
                Assert.NotEqual(Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(plot.PlotBackgroundBrush).Color,
                    Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(plot.PlotTextBrush).Color);
                Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint());
                var sidebar = window.GetLogicalDescendants().OfType<Border>().Single(x => x.Name == "CoatingSidebar");
                Assert.InRange(sidebar.Bounds.Width, 240, 280);
                var scroll = Assert.IsType<ScrollViewer>(sidebar.Child);
                Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.All(window.GetLogicalDescendants().OfType<Button>().Where(x => x.Content?.ToString() is "1 生成膜系" or "2 优化" or "结构搜索"), b => Assert.Contains("accent", b.Classes));
                Assert.False(Find<Button>(window, "取消").IsEnabled);
                Assert.NotEmpty(AutomationProperties.GetHelpText(Find<Button>(window, "取消"))!);
                Capture(window, $"theme-{theme}-820");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task MaterialDialogCreatesIndependentSnapshotAndInvalidatesOldResult()
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow { ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); Tick(window); Click(window, "1 生成膜系"); await window.PendingOperation;
                var original = window.CurrentExperiment.Materials;
                window.FontSize = 16;
                Click(window, "材料管理"); Tick(window);
                var editor = window.OwnedWindows.OfType<MaterialEditorWindow>().Single();
                Tick(editor); Assert.Equal(window.FontSize, editor.FontSize); Assert.Equal(window.FontFamily, editor.FontFamily);
                Click(editor, "新建 / 示例格式");
                Find<TextBox>(editor, "材料名称").Text = "实测材料测试";
                Find<TextBox>(editor, "数据来源").Text = "测试夹具，非实测产品数据";
                Find<TextBox>(editor, "n/k 表格").Text = "wavelength_nm,n,k\n400,1.8,0.01\n700,1.75,0.02\n";
                Capture(editor, "material-editor");
                Click(editor, "保存到实验"); await window.PendingOperation;
                Assert.Null(window.CurrentExperiment.Result);
                Assert.Equal(original.Length + 1, window.CurrentExperiment.Materials.Length);
                Assert.All(original, m => Assert.Contains(window.CurrentExperiment.Materials, x => x.Id == m.Id));
                Click(window, "添加层"); await window.PendingOperation;
                Click(window, "计算 / 复验"); await window.PendingOperation;
                Assert.NotNull(window.CurrentExperiment.Result);
                Assert.StartsWith("custom:", window.CurrentExperiment.Layers.Last().MaterialId);
            }
            finally { window.Close(); }
        });
    }

    private static Task<global::OpticalSystemDesign.App.UnsavedChangesChoice> DiscardChanges(string operation) =>
        Task.FromResult(global::OpticalSystemDesign.App.UnsavedChangesChoice.Discard);

    private static T Find<T>(Window window, string label) where T : Control => window.GetLogicalDescendants()
        .OfType<T>().Single(c => AutomationProperties.GetName(c) == label);
    private static void Tick(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Window window, string label)
    {
        var button = Find<Button>(window, label); button.BringIntoView(); Tick(window);
        Assert.True(button.IsEffectivelyEnabled, label);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        Assert.InRange(point.X, 0, window.Bounds.Width); Assert.InRange(point.Y, 0, window.Bounds.Height);
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("COATING_UI_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); Tick(window);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap); bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private sealed class Host : IDisposable
    {
        private readonly SafeHeadlessUnitTestSession _session = SafeHeadlessUnitTestSession.StartNew(typeof(DesktopTests));
        public Task Run(Func<Task> test) => _session.Dispatch(async () => { await test(); return true; }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        public void Dispose()
        {
            try { _session.Dispose(); }
            catch (NullReferenceException exception) when (exception.StackTrace?.Contains("Avalonia.Headless.HeadlessUnitTestSession.Dispose", StringComparison.Ordinal) == true) { }
        }
    }
}
