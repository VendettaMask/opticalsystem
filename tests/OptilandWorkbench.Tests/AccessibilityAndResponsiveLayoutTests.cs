using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
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
    public async Task LensLibraryKeepsSelectedDetailsInFiniteWideAndNarrowViewports()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var lenses = Enumerable.Range(1, 925)
                .Select(LensEntry)
                .ToArray();
            var panel = new LensLibraryPanel(new LayoutLensLibraryService(lenses));
            var list = PrivateField<ListBox>(panel, "_list");
            var name = PrivateField<TextBlock>(panel, "_name");
            var preview = PrivateField<OpticSceneControl>(panel, "_preview");
            var root = Assert.IsType<Grid>(panel.Content);
            var body = Assert.IsType<ResponsiveTwoPaneGrid>(root.Children[1]);
            var window = ShowInWindow(panel, new Size(1400, 900));

            try
            {
                Assert.Equal(925, Assert.IsAssignableFrom<IEnumerable<string>>(list.ItemsSource).Count());
                Assert.Equal("测试镜头 1", name.Text);
                Assert.False(body.IsNarrow);
                Assert.InRange(body.Bounds.Height, 1, 900);
                Assert.InRange(list.Bounds.Height, 1, body.Bounds.Height);
                Assert.InRange(preview.Bounds.Height, 1, body.Bounds.Height);
                Assert.True(list.GetVisualDescendants().OfType<ListBoxItem>().Count() < lenses.Length);
            }
            finally
            {
                window.Close();
            }

            var narrowPanel = new LensLibraryPanel(new LayoutLensLibraryService(lenses));
            var narrowRoot = Assert.IsType<Grid>(narrowPanel.Content);
            var narrowBody = Assert.IsType<ResponsiveTwoPaneGrid>(narrowRoot.Children[1]);
            var narrowList = PrivateField<ListBox>(narrowPanel, "_list");
            var narrowPreview = PrivateField<OpticSceneControl>(narrowPanel, "_preview");
            var narrowWindow = ShowInWindow(narrowPanel, new Size(720, 900));
            try
            {
                Assert.True(narrowBody.IsNarrow);
                Assert.InRange(narrowBody.Bounds.Height, 1, 900);
                Assert.InRange(narrowList.Bounds.Height, 1, narrowBody.Bounds.Height);
                Assert.InRange(narrowPreview.Bounds.Height, 1, narrowBody.Bounds.Height);
            }
            finally
            {
                narrowWindow.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MasterDetailDataPagesPlaceResponsiveBodyDirectlyInFiniteRootRow()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var lenses = new LayoutLensLibraryService([LensEntry(1)]);
            AssertFiniteBody(new LensLibraryPanel(lenses), bodyIndex: 1, width: 1200, expectedNarrow: false);
            AssertFiniteBody(new LensLibraryPanel(lenses), bodyIndex: 1, width: 720, expectedNarrow: true);
            AssertFiniteBody(new CommercialLensCatalogPanel(lenses), bodyIndex: 1, width: 1200, expectedNarrow: false);
            AssertFiniteBody(new CommercialLensCatalogPanel(lenses), bodyIndex: 1, width: 720, expectedNarrow: true);
            AssertFiniteBody(new MaterialLibraryPanel(new EmptyMaterialCatalogService()), bodyIndex: 2, width: 1200, expectedNarrow: false);
            AssertFiniteBody(new MaterialLibraryPanel(new EmptyMaterialCatalogService()), bodyIndex: 2, width: 720, expectedNarrow: true);
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
    public async Task AnalysisSettingsOverlayAnchorsBelowToolbarAndSizesToContent()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(
                application.Analyses,
                application.Visualization,
                application.Documents,
                application.Events,
                new AppSettings(),
                "单光线追迹");

            var settingsHost = PrivateField<Border>(panel, "_settingsHost");
            var parameterPanel = PrivateField<StackPanel>(panel, "_parameterPanel");

            Assert.Equal(HorizontalAlignment.Left, settingsHost.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, settingsHost.VerticalAlignment);
            Assert.Equal(new Thickness(12, 0, 12, 0), settingsHost.Margin);
            Assert.True(double.IsPositiveInfinity(settingsHost.MaxWidth));
            Assert.True(double.IsPositiveInfinity(settingsHost.MaxHeight));
            Assert.Equal(HorizontalAlignment.Left, parameterPanel.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, parameterPanel.VerticalAlignment);

            settingsHost.IsVisible = true;
            var window = ShowInWindow(panel, new Size(1400, 900));
            try
            {
                Assert.Equal(12, settingsHost.Bounds.X);
                Assert.InRange(settingsHost.Bounds.Width, 1, 899);
                var twoColumnParameterGrids = parameterPanel.GetVisualDescendants()
                    .OfType<Grid>()
                    .Where(grid => grid.ColumnDefinitions.Count == 5)
                    .ToArray();
                Assert.NotEmpty(twoColumnParameterGrids);
                Assert.All(
                    twoColumnParameterGrids,
                    grid => Assert.Equal(HorizontalAlignment.Left, grid.HorizontalAlignment));
            }
            finally
            {
                window.Close();
            }
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
            var chrome = Assert.IsType<OptilandWorkbench.App.Theming.ThemeChromeLayer>(window.Content);
            var scroll = Assert.IsType<ScrollViewer>(chrome.Children[0]);
            Assert.NotNull(scroll.Content);
            window.Close();
        }, CancellationToken.None);
    }

    private static void AssertFiniteBody(
        UserControl panel,
        int bodyIndex,
        double width,
        bool expectedNarrow)
    {
        var viewport = new Size(width, 800);
        var window = ShowInWindow(panel, viewport);
        try
        {
            var root = Assert.IsType<Grid>(panel.Content);
            var body = Assert.IsType<ResponsiveTwoPaneGrid>(root.Children[bodyIndex]);

            Assert.Equal(expectedNarrow, body.IsNarrow);
            Assert.InRange(body.Bounds.Height, 1, viewport.Height);
            Assert.All(body.Children, child =>
                Assert.InRange(child.Bounds.Height, 1, body.Bounds.Height));
        }
        finally
        {
            window.Close();
        }
    }

    private static Window ShowInWindow(Control control, Size viewport)
    {
        var window = new Window
        {
            Width = viewport.Width,
            Height = viewport.Height,
            Content = control
        };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

    private static LensLibraryEntryDto LensEntry(int index) => new(
        Id: $"lens-{index}",
        Name: $"测试镜头 {index}",
        Category: "工业镜头",
        SourceName: "布局测试",
        SourceUrl: "https://example.invalid/lens",
        License: "测试",
        SourceFormat: "STAROPT",
        ImportStatus: "可用",
        ImportMessage: null,
        EffectiveFocalLength: 50,
        FNumber: 4,
        ApertureKind: "FNumber",
        ApertureValue: 4,
        TotalTrack: 60,
        SurfaceCount: 6,
        FieldDefinition: "Angle",
        MaximumField: 10,
        FieldCount: 3,
        WavelengthCount: 3,
        MinimumWavelengthNanometers: 486.1,
        MaximumWavelengthNanometers: 656.3,
        NativePath: $"projects/lens-{index}.staropt",
        SourcePath: $"lens-{index}.zmx",
        NumericalAperture: 0.125,
        NumericalApertureBasis: "测试",
        WorkingDistance: 20,
        WorkingDistanceBasis: "测试",
        LensElementCount: 3,
        MaximumClearAperture: 12,
        LensType: "测试镜头",
        Application: "布局测试",
        DesignOrganization: "S.T.A.R. Labs",
        ImportedAt: DateTimeOffset.UnixEpoch,
        ImporterVersion: "test");

    private static T PrivateField<T>(object instance, string name) where T : class =>
        Assert.IsType<T>(instance.GetType().GetField(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(instance));

    private sealed class LayoutLensLibraryService(IReadOnlyList<LensLibraryEntryDto> entries)
        : ILensLibraryService
    {
        public string LibraryDirectory => string.Empty;

        public IReadOnlyList<LensLibraryEntryDto> GetLenses() => entries;

        public IReadOnlyList<CommercialLensEntryDto> GetCommercialLenses() => [];

        public string? GetNativeProjectPath(string lensId) => null;

        public string? GetCommercialNativeProjectPath(string lensId) => null;

        public Task<SceneDto?> BuildPreviewAsync(
            string lensId,
            CancellationToken cancellationToken = default) => Task.FromResult<SceneDto?>(null);

        public Task<SceneDto?> BuildCommercialPreviewAsync(
            string lensId,
            CancellationToken cancellationToken = default) => Task.FromResult<SceneDto?>(null);
    }

    private sealed class EmptyMaterialCatalogService : IMaterialCatalogService
    {
        public IReadOnlyList<MaterialCatalogDto> GetCatalogs() => [];

        public IReadOnlyList<string> GetCatalogNames() => [];

        public IReadOnlyList<GlassMaterialDto> GetGlasses() => [];

        public AnalysisViewDto Analyze(MaterialAnalysisRequestDto request) =>
            throw new NotSupportedException();

        public Task<MaterialCatalogImportResultDto> ImportZemaxCatalogAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
