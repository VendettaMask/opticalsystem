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
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MultiConfigurationPickupPanelTests
{
    public static AppBuilder BuildAvaloniaApp() => MultiConfigurationPanelTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light", 1000)]
    [InlineData("Dark", 1000)]
    [InlineData("Light", 680)]
    public async Task PickupEditorPreservesDraftsAndShowsDependentValues(string theme, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MultiConfigurationPickupPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add();
            app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.Thickness, 3)], app.Events.Revision);
            app.MultiConfiguration.SetOperandValue(1, 0, 4, app.Events.Revision);
            using var panel = new MultiConfigurationPanel(app.Prescription, app.MultiConfiguration, app.Events);
            var window = new Window { Content = panel, Width = width, Height = 760, Title = "多配置拾取" };
            try
            {
                window.Show(); Tick(); window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1; Tick();
                var expander = window.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "MultiConfigurationPickupEditor");
                expander.IsExpanded = true; Tick();
                TextBox Input(string name) => window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == name);
                Button Button(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                void Click(string name) { Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Tick(); }
                var configuration = Input("编辑配置号（从 1 开始）"); var value = Input("多配置单元格值");
                var scale = Input("拾取比例"); var offset = Input("拾取偏移");
                configuration.Text = "2"; Tick(); scale.Text = "2"; offset.Text = "1"; Click("ApplyMultiConfigurationPickup");
                Assert.Equal(9, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("9  P") == true);
                Assert.Contains("accent", Button("ApplyMultiConfigurationPickup").Classes);
                Assert.DoesNotContain("accent", Button("ClearMultiConfigurationPickup").Classes);
                Capture("pickup");
                var revision = app.Events.Revision; scale.Text = "abc"; Click("ApplyMultiConfigurationPickup");
                Assert.Equal("abc", scale.Text); Assert.Equal(revision, app.Events.Revision);
                var error = panel.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("拾取比例必须") == true);
                var position = error.TranslatePoint(default, window); Assert.NotNull(position);
                Assert.True(position.Value.Y >= 0 && position.Value.Y + error.Bounds.Height < window.Bounds.Height);
                Capture("invalid");
                Click("SetMultiConfigurationVariable"); Assert.Equal(revision, app.Events.Revision);
                configuration.Text = "1"; Tick(); scale.Text = "2"; Click("ApplyMultiConfigurationPickup"); Assert.Equal(revision, app.Events.Revision);
                configuration.Text = "2"; Tick(); Click("ReloadMultiConfigurationValue"); scale.Text = "3";
                app.MultiConfiguration.SetOperandValue(1, 0, 5, app.Events.Revision); Tick(); revision = app.Events.Revision;
                Click("ApplyMultiConfigurationPickup"); Assert.Equal(revision, app.Events.Revision); Assert.Equal("3", scale.Text);
                Click("ReloadMultiConfigurationValue"); Assert.Equal("2", scale.Text); Assert.Equal("11", value.Text);
                value.Text = "17"; Click("ApplyMultiConfigurationValue"); Assert.Equal(revision, app.Events.Revision); Assert.Equal("17", value.Text);
                Click("ReloadMultiConfigurationValue"); Click("ClearMultiConfigurationPickup");
                Assert.Null(app.MultiConfiguration.GetOperandRows()[0].Pickups[1]);
                app.MultiConfiguration.SetOperandValue(1, 0, 6, app.Events.Revision); Tick(); Assert.Equal(11, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
                Assert.True(app.Documents.Undo()); Assert.True(app.Documents.Undo()); Tick(); Assert.NotNull(app.MultiConfiguration.GetOperandRows()[0].Pickups[1]);

                void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                void Capture(string state)
                {
                    var directory = Environment.GetEnvironmentVariable("OPTILAND_MCE_CAPTURE_DIR");
                    if (string.IsNullOrWhiteSpace(directory)) return;
                    Directory.CreateDirectory(directory); using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(directory, $"mce-pickup-{theme.ToLowerInvariant()}-{width}-{state}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
