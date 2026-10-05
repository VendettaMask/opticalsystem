using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MultiConfigurationPanelTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_MCE_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>(); if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", 1000)]
    [InlineData("Dark", 1000)]
    [InlineData("Light", 680)]
    public async Task TableShowsRealValuesAndPreservesInvalidAndStaleDrafts(string theme, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MultiConfigurationPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add();
            using var panel = new MultiConfigurationPanel(app.Prescription, app.MultiConfiguration, app.Events);
            var window = new Window { Content = panel, Width = width, Height = 760, Title = "多配置操作数" };
            try
            {
                window.Show(); Tick();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex = 1; Tick();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "MultiConfigurationOperands");
                TextBox Input(string name) => window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == name);
                Button Button(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                void Click(string name) { Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Tick(); }
                var surface = Input("多配置行表面号"); var config = Input("编辑配置号（从 1 开始）"); var value = Input("多配置单元格值");
                surface.Text = "4"; Click("AddMultiConfigurationOperand"); Assert.Single(app.MultiConfiguration.GetOperandRows());
                Assert.Equal(new[] { "Op#", "类型", "表面", "配置 1", "配置 2" }, grid.Columns.Select(column => column.Header));
                config.Text = "2"; Tick(); value.Text = "-2"; Click("ApplyMultiConfigurationValue");
                Assert.Equal(-2, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
                Assert.Contains("accent", Button("ApplyMultiConfigurationValue").Classes);
                Assert.DoesNotContain("accent", Button("ReloadMultiConfigurationValue").Classes);
                Click("SetMultiConfigurationVariable"); Assert.True(app.MultiConfiguration.GetOperandRows()[0].Variables[1]);
                Assert.Single(app.Optimization.GetMarkedVariables());
                Click("ClearMultiConfigurationVariable"); Assert.False(app.MultiConfiguration.GetOperandRows()[0].Variables[1]);
                Assert.True(app.Documents.Undo()); Tick(); Assert.True(app.MultiConfiguration.GetOperandRows()[0].Variables[1]);
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("V") == true);
                var wizard = new OptimizationWizardWindow(app.Prescription, app.Optimization);
                try
                {
                    wizard.Show(); Tick();
                    Assert.Contains(wizard.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.StartsWith("当前优化变量：1。") == true);
                }
                finally { wizard.Close(); Tick(); }
                Capture("table");
                var revision = app.Events.Revision; value.Text = "abc"; Click("ApplyMultiConfigurationValue");
                Assert.Equal("abc", value.Text); Assert.Equal(revision, app.Events.Revision);
                Click("ClearMultiConfigurationVariable"); Assert.True(app.MultiConfiguration.GetOperandRows()[0].Variables[1]);
                Assert.Equal(revision, app.Events.Revision); Click("ApplyMultiConfigurationValue");
                var error = panel.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("请输入有限数值") == true);
                var position = error.TranslatePoint(default, window); Assert.NotNull(position);
                Assert.True(position.Value.Y >= 0 && position.Value.Y + error.Bounds.Height < window.Bounds.Height);
                Capture("invalid");
                value.Text = "5"; config.Text = "1"; Click("ApplyMultiConfigurationValue");
                Assert.Equal(revision, app.Events.Revision); Assert.Equal(-2, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
                Click("ReloadMultiConfigurationValue"); value.Text = "8";
                app.MultiConfiguration.SetOperandValue(1, 1, -3, app.Events.Revision); Tick();
                revision = app.Events.Revision; Click("ApplyMultiConfigurationValue"); Assert.Equal(revision, app.Events.Revision); Assert.Equal("8", value.Text);
                Click("ReloadMultiConfigurationValue"); value.Text = "6"; Click("ApplyMultiConfigurationValue");
                Assert.Equal(6, app.MultiConfiguration.GetOperandRows()[0].Values[0]);
                surface.Text = "3"; Click("AddMultiConfigurationOperand"); Assert.Equal(2, app.MultiConfiguration.GetOperandRows().Count);
                grid.SelectedIndex = 1; Tick(); Click("MoveMultiConfigurationOperandUp"); Assert.Equal(3, app.MultiConfiguration.GetOperandRows()[0].SurfaceNumber);
                Click("RemoveMultiConfigurationOperand"); Assert.Single(app.MultiConfiguration.GetOperandRows());
                Assert.True(app.Documents.Undo()); Tick(); Assert.Equal(2, grid.ItemsSource!.Cast<object>().Count());

                void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                void Capture(string state)
                {
                    var directory = Environment.GetEnvironmentVariable("OPTILAND_MCE_CAPTURE_DIR");
                    if (string.IsNullOrWhiteSpace(directory)) return;
                    Directory.CreateDirectory(directory); using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(directory, $"mce-{theme.ToLowerInvariant()}-{width}-{state}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
