using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MultiConfigurationSystemPanelTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData("Light", "zh-CN", 1080)]
    [InlineData("Dark", "en-US", 680)]
    [InlineData("Pixel", "ja-JP", 1080)]
    [InlineData("Lumen", "en-US", 1080)]
    [InlineData("Isekai", "en-US", 680)]
    public async Task NativeEditorAddsEditsAndMarksAllFiveTypesWithTheCorrectUnits(string theme, string language, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MultiConfigurationSystemPanelTests));
        await session.Dispatch(() =>
        {
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add();
            using var panel = new MultiConfigurationPanel(app.MultiConfiguration, app.Events);
            var window = new Window { Content = panel, Width = width, Height = 700 };
            try
            {
                window.Show(); Tick(); Click("ToggleMultiConfigurationProperties");
                var kind = panel.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "MultiConfigurationOperandKind");
                TextBox Input(string name) => panel.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == name);
                var codes = new[] { "XFIE", "YFIE", "FLWT", "WAVE", "WLWT" };
                for (var i = 0; i < codes.Length; i++)
                {
                    kind.SelectedIndex = i + 5; Input("MultiConfigurationOperandTarget").Text = "1";
                    Click("AddMultiConfigurationOperand");
                    Assert.Equal(i + 1, app.MultiConfiguration.GetOperandRows().Count);
                }
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "MultiConfigurationOperands");
                Assert.Equal(language switch { "en-US" => "WLWT · Wavelength weight", "ja-JP" => "WLWT · 波長の重み", _ => "WLWT · 波长权重" }, kind.SelectedItem!.ToString());
                for (var i = 0; i < codes.Length; i++)
                {
                    grid.SelectedIndex = i + 1; Tick();
                    Input("MultiConfigurationOperandConfiguration").Text = "2"; Tick();
                    Input("MultiConfigurationOperandValue").Text = i == 3 ? ".67" : i < 2 ? "4" : ".5";
                    Click("ApplyMultiConfigurationValue");
                    Assert.Equal(i == 3 ? .67 : i < 2 ? 4 : .5, app.MultiConfiguration.GetOperandRows()[i].Values[1]);
                    Click("SetMultiConfigurationVariable"); Assert.True(app.MultiConfiguration.GetOperandRows()[i].Variables[1]);
                }
                Assert.Equal(5, app.Optimization.GetMarkedVariables().Count);
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "F1");
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "λ1");
                grid.SelectedIndex = 4; Tick();
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "WAVE (µm)");
                var revision = app.Events.Revision;
                Input("MultiConfigurationOperandValue").Text = "0"; Click("ApplyMultiConfigurationValue");
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal("0", Input("MultiConfigurationOperandValue").Text);
                Click("ReloadMultiConfigurationValue");
                Assert.True(app.Documents.Undo()); Tick(); Assert.False(app.MultiConfiguration.GetOperandRows()[4].Variables[1]);
                Assert.True(app.Documents.Redo()); Tick(); Assert.True(app.MultiConfiguration.GetOperandRows()[4].Variables[1]);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MCE_SYSTEM_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var image = new RenderTargetBitmap(new PixelSize(width, 700), new Vector(96, 96));
                    image.Render(window); image.Save(Path.Combine(directory, $"system-operands-{theme}-{language}-{width}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); UiLocalization.Apply("zh-CN"); }
            return Task.CompletedTask;

            void Click(string name)
            {
                panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick();
            }
            void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        }, CancellationToken.None);
    }
}
