using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PrimaryActionPresentationTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_ACTION_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("wizard", "生成评价函数")]
    [InlineData("optimization", "执行优化")]
    [InlineData("tolerance-run", "运行")]
    [InlineData("tolerance-wizard", "确定")]
    [InlineData("slider", "应用")]
    [InlineData("matching", "开始匹配")]
    [InlineData("catalog", "搜索")]
    public Task PagesUseOnePrimaryActionWithoutPromotingSecondaryCommands(string page, string action) => Run(app =>
    {
        Control content = page switch
        {
            "wizard" => new OptimizationWizardWindow(app.Prescription, app.Optimization),
            "optimization" => new OptimizationPanel(app.Prescription, app.Optimization, app.Events),
            "tolerance-run" => new TolerancingRunWindow(TolerancingRunWindow.DefaultOptions()),
            "tolerance-wizard" => new ToleranceWizardWindow(app.Prescription),
            "slider" => new OptimizationVariableSliderWindow(app.Prescription),
            "matching" => new StockLensMatchingPanel(app.Documents, app.Lenses, app.Events),
            _ => new CommercialLensCatalogPanel(app.Lenses)
        };
        var window = content as Window ?? new Window { Width = 1200, Height = 800, Content = content };
        try
        {
            window.Show(); Tick(window);
            if (content is CommercialLensCatalogPanel catalog)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!catalog.CatalogLoadTask.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Tick(window);
                    Thread.Sleep(1);
                }
                Assert.True(catalog.CatalogLoadTask.IsCompleted);
            }
            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("accent"));
            Assert.Equal(action, Caption(button));
            Equal("#2F65CC", Presenter(button).Background);
            Equal("#FFFFFF", Presenter(button).Foreground);
            Assert.All(button.GetVisualDescendants().OfType<TextBlock>(), t => Equal("#FFFFFF", t.Foreground));
            Assert.All(button.GetVisualDescendants().OfType<LocalIcon>(), i => Equal("#FFFFFF", i.Stroke));
            foreach (var secondary in window.GetVisualDescendants().OfType<Button>()
                         .Where(b => new[] { "取消", "重置", "恢复初值", "厂商页面", "导出报告" }.Contains(Caption(b))))
            {
                Assert.DoesNotContain("accent", secondary.Classes);
                var presenter = secondary.GetVisualDescendants().OfType<ContentPresenter>()
                    .FirstOrDefault(p => p.Name == "PART_ContentPresenter");
                if (presenter is not null)
                    Equal(secondary.IsEnabled ? "#FFFFFF" : "#F0F3F8", presenter.Background);
            }
            button.BringIntoView(); Tick(window);
            Capture(window, page);
            // Exercise the production button's state rules without triggering its business operation.
            var point = Position(button, window);
            window.MouseMove(point); Tick(window); Equal("#2554AF", Presenter(button).Background);
            window.MouseDown(point, MouseButton.Left); Tick(window); Equal("#1D448F", Presenter(button).Background);
            window.MouseMove(default); window.MouseUp(default, MouseButton.Left); Tick(window);
            Equal("#2F65CC", Presenter(button).Background);
            window.MouseMove(point); Tick(window); Equal("#2554AF", Presenter(button).Background);
            button.Focus(NavigationMethod.Tab); Tick(window);
            Assert.True(button.IsFocused); Assert.NotNull(button.FocusAdorner);
            button.IsEnabled = false; Tick(window);
            Equal("#F0F3F8", Presenter(button).Background);
            Equal("#9BA9BF", Presenter(button).Foreground);
            Assert.All(button.GetVisualDescendants().OfType<TextBlock>(), t => Equal("#9BA9BF", t.Foreground));
            Assert.All(button.GetVisualDescendants().OfType<LocalIcon>(), i => Equal("#9BA9BF", i.Stroke));
        }
        finally { window.Close(); (content as IDisposable)?.Dispose(); }
    });

    [Fact]
    public Task InvalidWizardExplainsConditionsAndRestoresPrimaryWhenCorrected() => Run(app =>
    {
        var window = new OptimizationWizardWindow(app.Prescription, app.Optimization);
        try
        {
            window.Show(); Tick(window);
            var generate = Field<Button>(window, "_generate");
            Field<ComboBox>(window, "_quality").SelectedIndex = 1;
            Field<NumericUpDown>(window, "_spatialFrequency").Value = 0;
            Tick(window);
            AssertUnavailable(generate, "空间频率");
            var preview = Field<TextBlock>(window, "_preview");
            Assert.Contains("生成条件", preview.Text);
            Equal("#5C6F8A", preview.Foreground);
            Assert.Equal(1, preview.Opacity);
            var before = app.Optimization.GetMeritFunction().Count;
            Click(window, generate);
            Assert.True(window.IsVisible);
            Assert.Equal(before, app.Optimization.GetMeritFunction().Count);
            Capture(window, "wizard-disabled");
            Field<NumericUpDown>(window, "_spatialFrequency").Value = 30;
            Tick(window);
            Assert.True(generate.IsEnabled);
            Assert.Null(ToolTip.GetTip(generate));
            Assert.DoesNotContain("生成条件", preview.Text);
            Field<NumericUpDown>(window, "_xWeight").Value = 0;
            Field<NumericUpDown>(window, "_yWeight").Value = 0;
            AssertUnavailable(generate, "权重");
            Field<NumericUpDown>(window, "_yWeight").Value = 1;
            Assert.True(generate.IsEnabled);
            Field<ComboBox>(window, "_sampling").SelectedIndex = 1;
            AssertUnavailable(Field<NumericUpDown>(window, "_arms"), "臂数");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EmptySliderExplainsAvailabilityWithoutDimmingNormalText() => Run(app =>
    {
        app.Documents.NewBlank();
        var window = new OptimizationVariableSliderWindow(app.Prescription);
        try
        {
            window.Show(); Tick(window);
            var apply = window.GetVisualDescendants().OfType<Button>().Single(b => Caption(b) == "应用");
            AssertUnavailable(apply, "内部表面");
            var status = Field<TextBlock>(window, "_statusText");
            Assert.Contains("内部表面", status.Text);
            Equal("#22334C", status.Foreground);
            Assert.Equal(1, status.Opacity);
            Assert.True(status.IsEffectivelyEnabled);
            var close = window.GetVisualDescendants().OfType<Button>().Single(b => Caption(b) == "关闭");
            Equal("#22334C", Presenter(close).Foreground);
            Click(window, apply); Assert.True(window.IsVisible);
            Capture(window, "slider-disabled");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SliderApplyMouseAndKeyboardRetainSecondaryReset() => Run(app =>
    {
        var window = new OptimizationVariableSliderWindow(app.Prescription);
        try
        {
            window.Show(); Tick(window);
            var apply = window.GetVisualDescendants().OfType<Button>().Single(b => Caption(b) == "应用");
            Click(window, apply);
            Equal("#2554AF", Presenter(apply).Background);
            Assert.Contains("已更新", Field<TextBlock>(window, "_statusText").Text);
            window.MouseMove(default); Tick(window); Equal("#2F65CC", Presenter(apply).Background);
            apply.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Tick(window); Assert.True(apply.IsFocused);
            Assert.Contains("已更新", Field<TextBlock>(window, "_statusText").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ToleranceModeAndMatchingSelectionExplainExistingDisabledControls() => Run(app =>
    {
        var window = new TolerancingRunWindow(TolerancingRunWindow.DefaultOptions());
        try
        {
            window.Show(); Tick(window);
            var inverse = Field<NumericUpDown>(window, "_inverseValue");
            AssertUnavailable(inverse, "反向");
            Field<ComboBox>(window, "_mode").SelectedIndex = 1;
            Assert.True(inverse.IsEnabled); Assert.Null(ToolTip.GetTip(inverse));
            Field<ComboBox>(window, "_compensation").SelectedIndex = 0;
            AssertUnavailable(Field<NumericUpDown>(window, "_cycles"), "补偿");
            Field<ComboBox>(window, "_compensation").SelectedIndex = 1;
            Assert.True(Field<NumericUpDown>(window, "_cycles").IsEnabled);
        }
        finally { window.Close(); }
        using var panel = new StockLensMatchingPanel(app.Documents, app.Lenses, app.Events);
        AssertUnavailable(Field<Button>(panel, "_productPage"), "匹配");
        AssertUnavailable(Field<CheckBox>(panel, "_matchShape"), "完整光学系统");
    });

    private static Task Run(Action<WorkbenchApplication> action) => RunSession(action);
    private static async Task RunSession(Action<WorkbenchApplication> action)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PrimaryActionPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("tessar");
            action(app);
        }, CancellationToken.None);
    }
    private static void AssertUnavailable(Control control, string reason)
    {
        Assert.False(control.IsEnabled);
        Assert.Contains(reason, Assert.IsType<string>(ToolTip.GetTip(control)));
        Assert.True(ToolTip.GetShowOnDisabled(control));
        Assert.Equal(ToolTip.GetTip(control), AutomationProperties.GetHelpText(control));
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static string? Caption(Button button) => button.Content is LocalIconLabel label ? label.Children.OfType<TextBlock>().Single().Text : button.Content as string;
    private static ContentPresenter Presenter(Button button) => button.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
    private static void Equal(string expected, IBrush? actual) => Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    private static Point Position(Control control, Window window) => control.TranslatePoint(new Point(12, control.Bounds.Height / 2), window)!.Value;
    private static void Click(Window window, Button button)
    {
        var point = Position(button, window);
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Tick(window);
    }
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTILAND_ACTION_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width * 2, (int)window.Height * 2), new Vector(192, 192));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
