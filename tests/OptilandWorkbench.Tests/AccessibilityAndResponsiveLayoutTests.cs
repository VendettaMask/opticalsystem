using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using OptilandWorkbench.App;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AccessibilityAndResponsiveLayoutTests
{
    [Fact]
    public async Task InteractiveCanvasesExposeNamedAutomationPeers()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            Control[] controls =
            {
                new AnalysisPlotControl(),
                new OpticSceneControl(),
                new WavefrontSurfaceControl(),
                new DrawingPreviewControl()
            };

            foreach (var control in controls)
            {
                Assert.True(control.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(control)));
                var peer = Assert.IsType<InteractiveCanvasAutomationPeer>(
                    ControlAutomationPeer.CreatePeerForElement(control));
                var value = Assert.IsAssignableFrom<IValueProvider>(peer);
                Assert.True(value.IsReadOnly);
                Assert.False(string.IsNullOrWhiteSpace(value.Value));
                Assert.IsAssignableFrom<IInvokeProvider>(peer).Invoke();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TwoPaneLayoutReflowsBelowItsBreakpoint()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var first = new Border { MinHeight = 100 };
            var second = new Border { MinHeight = 100 };
            var layout = new ResponsiveTwoPaneGrid(
                first,
                second,
                "3*,16,2*",
                "Auto,16,Auto",
                breakpoint: 800);

            layout.Measure(new Size(600, 800));
            Assert.True(layout.IsNarrow);
            Assert.Equal(2, Grid.GetRow(second));
            Assert.Equal(0, Grid.GetColumn(second));

            layout.Measure(new Size(1000, 800));
            Assert.False(layout.IsNarrow);
            Assert.Equal(0, Grid.GetRow(second));
            Assert.Equal(2, Grid.GetColumn(second));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SettingsGridReflowsWithoutFixedItemWidths()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var first = new TextBox();
            var second = new ComboBox();
            var third = new NumericUpDown();
            var layout = new ResponsiveSettingsGrid([first, second, third], breakpoint: 400);

            layout.Measure(new Size(320, 600));
            Assert.True(layout.IsNarrow);
            Assert.Equal(1, Grid.GetRow(second));
            Assert.Equal(0, Grid.GetColumn(second));

            layout.Measure(new Size(520, 600));
            Assert.False(layout.IsNarrow);
            Assert.Equal(0, Grid.GetRow(second));
            Assert.Equal(2, Grid.GetColumn(second));
            Assert.Equal(1, Grid.GetRow(third));
        }, CancellationToken.None);
    }

    [Fact]
    public void CanvasKeyboardCommandsAreStable()
    {
        var cases = new[]
        {
            (Key.Home, InteractiveCanvasCommand.Reset),
            (Key.OemPlus, InteractiveCanvasCommand.ZoomIn),
            (Key.OemMinus, InteractiveCanvasCommand.ZoomOut),
            (Key.Left, InteractiveCanvasCommand.Left),
            (Key.Right, InteractiveCanvasCommand.Right),
            (Key.Up, InteractiveCanvasCommand.Up),
            (Key.Down, InteractiveCanvasCommand.Down)
        };
        foreach (var (key, expected) in cases)
        {
            Assert.True(InteractiveCanvasKeyboard.TryGetCommand(key, out var actual));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task OpticalDrawingIconButtonsExposeTheirCommandName()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var button = OpticalDrawingPanel.IconButton("zoom-in", "放大");

            Assert.Equal("放大", AutomationProperties.GetName(button));
            Assert.Equal("放大", ToolTip.GetTip(button));
        }, CancellationToken.None);
    }

    [Fact]
    public void CorruptSettingsAreQuarantinedAndReported()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"settings-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, "{ invalid json");

            var settings = AppSettings.Load(path);

            Assert.NotNull(settings.LoadWarning);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(directory, "settings.json.invalid-*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OversizedSettingsAreQuarantinedAndReported()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"settings-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            using (var stream = File.Create(path))
            {
                stream.SetLength(BoundedFile.MaximumSettingsBytes + 1);
            }

            var settings = AppSettings.Load(path);

            Assert.NotNull(settings.LoadWarning);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(directory, "settings.json.invalid-*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DisplaySettingsRemainReachableInShortWindows()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var window = new DisplaySettingsWindow(new AppSettings());

            Assert.True(window.CanResize);
            Assert.True(window.MinHeight <= 320);
            var scroll = Assert.IsType<ScrollViewer>(window.Content);
            Assert.NotNull(scroll.Content);
            window.Close();
        }, CancellationToken.None);
    }
}
