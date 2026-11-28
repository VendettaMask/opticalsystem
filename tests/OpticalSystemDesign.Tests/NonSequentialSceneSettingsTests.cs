using System.Collections;
using System.Text.Json;
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
using OpticalSystemDesign.Core.NonSequential;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialSceneSettingsServiceTests
{
    [Fact]
    public async Task GlobalSettingsApplyInOneUndoAndRoundTripWithOrganizationAndSequentialPrescriptionIntact()
    {
        using var app = WorkbenchApplication.Create();
        var source = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.SourceRay);
        var folder = Guid.NewGuid(); var set = Guid.NewGuid();
        app.NonSequential.UpdateOrganization(new([new(folder, "Sources", null, [source])], [new(set, "Selection", [source])]));
        var sequential = JsonSerializer.Serialize(app.Prescription.GetWavelengths());
        var before = app.NonSequential.GetDocument(); var identity = app.NonSequential.GetDesignSceneIdentity();
        var settings = Settings(before) with
        {
            Name = "Illumination",
            AmbientMaterial = "Air",
            Wavelengths = [new(0, "Red", 620, .25, false), new(1, "Near IR", 780, 1, true)],
            TraceSettings = before.TraceSettings with
            {
                RandomSeed = 73,
                MaximumSegmentsPerRay = 77,
                MaximumActiveBranches = 9000,
                MaximumTotalSourceRays = 50000,
                MinimumRelativeIntensity = .00001,
                SplitFresnelRays = false,
                UsePolarization = true,
                CoherentDetection = true
            }
        };
        app.NonSequential.UpdateSceneSettings(settings, identity, app.Events.Revision);
        var changed = app.NonSequential.GetDocument(); Assert.Equal("Illumination", changed.Name);
        Assert.Equal(780, changed.Wavelengths.Single(w => w.IsPrimary).Nanometers);
        Assert.Equal(folder, changed.Organization!.Folders.Single().Id);
        Assert.True(app.Documents.Undo()); Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity());
        Assert.True(app.Documents.Redo()); Assert.Equal(73, app.NonSequential.GetDocument().TraceSettings.RandomSeed);
        // The existing trace-dialog physics path must also preserve organization.
        app.NonSequential.UpdatePhysicsSettings(false, false);
        Assert.Equal(set, app.NonSequential.GetDocument().Organization!.SelectionSets.Single().Id);
        Assert.Equal(sequential, JsonSerializer.Serialize(app.Prescription.GetWavelengths()));
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("scene-settings.staropt");
        await app.Documents.SaveAsync(path);
        using var reopened = WorkbenchApplication.Create(); await reopened.Documents.OpenAsync(path);
        var loaded = reopened.NonSequential.GetDocument(); Assert.Equal("Illumination", loaded.Name);
        Assert.Equal(77, loaded.TraceSettings.MaximumSegmentsPerRay); Assert.Equal(2, loaded.Wavelengths.Count);
        Assert.Equal(source, loaded.Organization!.Folders.Single().ObjectIds.Single());
    }
    [Fact]
    public void InvalidOrStaleDraftCannotPartiallyChangeTheScene()
    {
        using var app = WorkbenchApplication.Create(); var doc = app.NonSequential.GetDocument(); var identity = app.NonSequential.GetDesignSceneIdentity();
        foreach (var invalid in new[] { Settings(doc) with { Name = "" }, Settings(doc) with { AmbientMaterial = "missing-medium" },
            Settings(doc) with { TraceSettings = doc.TraceSettings with { MaximumSegmentsPerRay = 0 } },
            Settings(doc) with { Wavelengths = [new(0, "invalid", double.NaN, 1, true)] } })
        {
            Assert.ThrowsAny<Exception>(() => app.NonSequential.UpdateSceneSettings(invalid, identity, app.Events.Revision));
            Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity());
        }
        var revision = app.Events.Revision; app.Documents.NewBlank();
        Assert.Throws<InvalidOperationException>(() => app.NonSequential.UpdateSceneSettings(Settings(doc), identity, revision));
    }
    internal static Dto.NonSequentialSceneSettingsDto Settings(Dto.NonSequentialDocumentDto d) => new(d.Name, d.AmbientMaterial, d.Wavelengths, d.TraceSettings);
}

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialSceneSettingsTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light", "zh-CN", 360, 1080, 1)]
    [InlineData("Light", "zh-CN", 256, 1080, 1)]
    [InlineData("Dark", "ja-JP", 360, 950, 1.5)]
    [InlineData("Isekai", "en-US", 340, 1080, 2)]
    [InlineData("Lumen", "en-US", 360, 950, 1)]
    [InlineData("Pixel", "zh-CN", 360, 950, 1)]
    public async Task ExistingSidebarNowShowsRealSceneSettingsAndAll44Wavelengths(string theme, string language, int width, int height, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(NonSequentialSceneSettingsTests));
        await session.Dispatch(async () =>
        {
            var previous = UiLocalization.CurrentLanguage;
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create();
            var import = await app.NonSequential.InspectZemaxNscAsync(NonSequentialColorFringesTests.Source);
            app.NonSequential.ApplyZemaxNscImport(import, app.NonSequential.GetDesignSceneIdentity());
            app.Modes.SwitchTo(Dto.OpticalWorkbenchMode.NonSequential);
            var factory = new WorkspaceDockFactory(app, new global::OpticalSystemDesign.App.Services.AppSettings());
            factory.CreateLayout();
            var panel = Assert.IsType<NonSequentialModePanel>(factory.DocumentContent<Control>(WorkspaceDockFactory.SystemToolId));
            var window = new Window { Title = "场景设置", Width = width, Height = height, Content = panel };
            ThemeChrome.ApplyDialogDecoration(window); DisplayTypography.Apply(panel);
            try
            {
                window.Show(); window.SetRenderScaling(scale); Tick(window);
                T Find<T>(string name) where T : Control => panel.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                Assert.Equal(5, app.NonSequential.GetDocument().Objects.Count);
                var grid = Find<DataGrid>("nsc-scene-wavelengths");
                var rows = ((IEnumerable)grid.ItemsSource!).Cast<object>().ToArray(); Assert.Equal(44, rows.Length);
                Assert.False(Find<Button>("nsc-scene-apply").IsEnabled);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("切换模式不会修改顺序处方") == true);
                foreach (var name in new[] { "nsc-scene-name", "nsc-scene-ambient", "nsc-scene-wavelengths", "nsc-scene-segments", "nsc-scene-polarized" })
                    Assert.True(Find<Control>(name).IsEffectivelyVisible, name);
                foreach (var name in new[] { "nsc-scene-name", "nsc-scene-ambient", "nsc-scene-segments" })
                {
                    var input = Find<Control>(name); var location = input.TranslatePoint(default, window)!.Value;
                    Assert.InRange(location.X + input.Bounds.Width, 0, window.Bounds.Width);
                    Assert.True(input.Bounds.Width >= 70, name);
                }
                var apply = Find<Button>("nsc-scene-apply"); var at = apply.TranslatePoint(default, window)!.Value;
                Assert.InRange(at.Y + apply.Bounds.Height, 0, window.Bounds.Height);
                Capture(window, theme + (width < 300 ? "-narrow" : "") + "-sidebar", scale);
                grid.ScrollIntoView(rows[^1], null); Tick(window);
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "44");
                Capture(window, theme + (width < 300 ? "-narrow" : "") + "-wavelength-end", scale);
            }
            finally { window.Close(); factory.DisposeContent(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); UiLocalization.Apply(previous); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditingIsAtomicKeepsInvalidInputsAndReloadsOnFileSwitch()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(NonSequentialSceneSettingsTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create(); app.NonSequential.AddObject(Dto.NonSequentialObjectKind.SourceRay);
            using var panel = new NonSequentialModePanel(app.NonSequential, app.Events);
            var window = new Window { Content = panel, Width = 370, Height = 1000 };
            try
            {
                window.Show(); Tick(window);
                T Find<T>(string name) where T : Control => panel.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                var identity = app.NonSequential.GetDesignSceneIdentity();
                Find<TextBox>("nsc-scene-name").Text = "Editable scene";
                Find<TextBox>("nsc-scene-segments").Text = "bad"; Tick(window);
                Assert.False(panel.ApplySettings()); Assert.Equal("bad", Find<TextBox>("nsc-scene-segments").Text);
                Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity());
                Find<TextBox>("nsc-scene-segments").Text = "77";
                Find<TextBox>("nsc-scene-seed").Text = "73"; Tick(window);
                var grid = Find<DataGrid>("nsc-scene-wavelengths");
                grid.SelectedIndex = 0; grid.CurrentColumn = grid.Columns[2]; grid.Focus(); Tick(window);
                Assert.True(grid.BeginEdit()); Tick(window);
                var waveEditor = grid.GetVisualDescendants().OfType<TextBox>().Single();
                waveEditor.Text = "632.8"; Tick(window);
                Assert.True(panel.ApplySettings()); Tick(window);
                Assert.Equal(632.8, app.NonSequential.GetDocument().Wavelengths[0].Nanometers);
                Assert.True(app.Documents.Undo()); Tick(window); Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity());
                var settings = NonSequentialSceneSettingsServiceTests.Settings(app.NonSequential.GetDocument());
                app.NonSequential.UpdateSceneSettings(settings with { TraceSettings = settings.TraceSettings with { MaximumSegmentsPerRay = 77, RandomSeed = 73 } },
                    app.NonSequential.GetDesignSceneIdentity(), app.Events.Revision); Tick(window);
                var trace = new NonSequentialTraceControlWindow(app.NonSequential, app.NonSequentialAnalysis);
                trace.Show(); Tick(trace);
                Assert.Equal("77", trace.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "nsc-trace-segments").Text);
                Assert.Equal("73", trace.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "nsc-trace-seed").Text);
                trace.Close();
                Find<TextBox>("nsc-scene-name").Text = "Draft for the old file"; Tick(window);
                app.Documents.NewBlank(); Tick(window);
                Assert.Equal(app.NonSequential.GetDocument().Name, Find<TextBox>("nsc-scene-name").Text);
                Assert.False(Find<Button>("nsc-scene-apply").IsEnabled);
                // An unrelated scene edit preserves the draft and rejects its stale apply.
                Find<TextBox>("nsc-scene-name").Text = "Draft"; Tick(window);
                app.NonSequential.AddObject(Dto.NonSequentialObjectKind.Null); Tick(window);
                Assert.Equal("Draft", Find<TextBox>("nsc-scene-name").Text);
                Assert.False(panel.ApplySettings()); panel.Reload(); Tick(window);
                Assert.False(Find<Button>("nsc-scene-apply").IsEnabled);
                await Task.CompletedTask;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task WorkspaceCommandOpensTheSameEditablePanelWithoutAnExtraPermanentSidebar()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(NonSequentialSceneSettingsTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create(); app.Modes.SwitchTo(Dto.OpticalWorkbenchMode.NonSequential);
            using var editor = new NonSequentialObjectEditorPanel(app.NonSequential, app.Events, app.Visualization, app.NonSequentialAnalysis, app.Documents);
            var owner = new Window { Content = editor, Width = 1600, Height = 1000 };
            try
            {
                owner.Show(); await editor.SceneRefresh; Tick(owner);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "nsc-open-scene-settings")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(owner);
                var dialog = Assert.Single(owner.OwnedWindows); Tick(dialog);
                var panel = Assert.Single(dialog.GetVisualDescendants().OfType<NonSequentialModePanel>());
                Assert.InRange(panel.GetVisualDescendants().OfType<DataGrid>().Single().Bounds.Height, 70, 90);
                panel.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "nsc-scene-name").Text = "From workspace"; Tick(dialog);
                panel.GetVisualDescendants().OfType<Button>().Single(t => t.Name == "nsc-scene-apply")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(dialog);
                Assert.Equal("From workspace", app.NonSequential.GetDocument().Name);
                Capture(dialog, "Light-workspace-settings", 1); dialog.Close();
                Assert.DoesNotContain(editor.GetVisualDescendants(), c => c is NonSequentialModePanel);
            }
            finally { foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close(); owner.Close(); }
        }, CancellationToken.None);
    }
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Capture(Window window, string name, double scale)
    {
        var output = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), ".tmp", "nsc-scene-settings"); Directory.CreateDirectory(output);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(window); bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
