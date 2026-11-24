using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialOrganizationTests
{
    [Fact]
    public async Task NestedFoldersAndStableSelectionsRoundTripWithoutChangingPhysicalIdentity()
    {
        using var app = WorkbenchApplication.Create();
        var parent = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.Null);
        var lens = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.StandardLens);
        var original = app.NonSequential.GetDocument().Objects.Single(o => o.Id == lens);
        app.NonSequential.UpdateObject(Update(original) with { ReferenceObjectId = parent, X = 3, Z = 12 });
        var identity = app.NonSequential.GetDesignSceneIdentity();
        var folder = Guid.NewGuid(); var nested = Guid.NewGuid(); var selection = Guid.NewGuid();
        var members = new[] { lens };
        app.NonSequential.UpdateOrganization(new([new(folder, "Assembly", null, []), new(nested, "Optics", folder, members)], [new(selection, "My lenses", members)]));
        members[0] = Guid.NewGuid(); // Callers cannot mutate stored organization through their arrays.
        Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity());
        Assert.Equal(lens, app.NonSequential.GetDocument().Organization!.Folders[1].ObjectIds.Single());
        Assert.True(app.Documents.Undo()); Assert.Empty(app.NonSequential.GetDocument().Organization!.Folders);
        Assert.True(app.Documents.Redo());
        app.NonSequential.MoveObject(lens, 0);
        Assert.Equal(lens, app.NonSequential.GetDocument().Organization!.SelectionSets.Single().ObjectIds.Single());
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("organization.staropt");
        await app.Documents.SaveAsync(path);
        using var reopened = WorkbenchApplication.Create(); await reopened.Documents.OpenAsync(path);
        var doc = reopened.NonSequential.GetDocument();
        Assert.Equal(folder, doc.Organization!.Folders.Single(f => f.Id == nested).ParentId);
        var row = doc.Objects.Single(o => o.Id == lens); Assert.Equal(parent, row.ReferenceObjectId); Assert.Equal(3, row.X); Assert.Equal(12, row.Z);
        reopened.NonSequential.DeleteObject(lens);
        Assert.Empty(reopened.NonSequential.GetDocument().Organization!.SelectionSets.Single().ObjectIds);
        Assert.True(reopened.Documents.Undo()); Assert.Equal(lens, reopened.NonSequential.GetDocument().Organization!.SelectionSets.Single().ObjectIds.Single());
    }

    [Fact]
    public void BatchEditsAndDeletesAreAtomicAndUndoInOneStep()
    {
        using var app = WorkbenchApplication.Create();
        var source = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.SourceRay);
        var detector = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.DetectorRectangle);
        var before = app.NonSequential.GetDocument();
        var updates = before.Objects.Select(o => Update(o) with { X = 8, Visible = false }).ToArray();
        app.NonSequential.UpdateObjects(updates);
        Assert.All(app.NonSequential.GetDocument().Objects, o => { Assert.Equal(8, o.X); Assert.False(o.Visible); Assert.True(o.Enabled); });
        Assert.True(app.Documents.Undo()); Assert.All(app.NonSequential.GetDocument().Objects, o => { Assert.Equal(0, o.X); Assert.True(o.Visible); });
        Assert.True(app.Documents.Redo());
        var identity = app.NonSequential.GetDesignSceneIdentity();
        Assert.ThrowsAny<Exception>(() => app.NonSequential.UpdateObjects([updates[0] with { X = 22 }, updates[1] with { ReferenceObjectId = detector }]));
        Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity());
        app.NonSequential.DeleteObjects([source, detector]); Assert.Empty(app.NonSequential.GetDocument().Objects);
        Assert.True(app.Documents.Undo()); Assert.Equal(2, app.NonSequential.GetDocument().Objects.Count);
    }

    [Fact]
    public void InvalidOrganizationCannotPublishCyclesUnknownMembersOrDuplicateAssignments()
    {
        using var app = WorkbenchApplication.Create(); var id = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.Box);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        foreach (var organization in new NonSequentialOrganization[]
        {
            new([new(a, "a", b, []), new(b, "b", a, [])], []),
            new([new(a, "a", null, [Guid.NewGuid()])], []),
            new([new(a, "a", null, [id]), new(b, "b", null, [id])], []),
            new([], [new(a, "set", [Guid.NewGuid()])])
        })
        {
            Assert.ThrowsAny<Exception>(() => app.NonSequential.UpdateOrganization(organization));
            Assert.Empty(app.NonSequential.GetDocument().Organization!.Folders);
            Assert.Empty(app.NonSequential.GetDocument().Organization!.SelectionSets);
        }
    }
    internal static Dto.NonSequentialObjectUpdateDto Update(Dto.NonSequentialObjectRowDto o) => new(o.Id, o.Enabled, o.Visible, o.Kind, o.Name,
        o.ReferenceObjectId, o.ContainingObjectId, o.X, o.Y, o.Z, o.TiltXDegrees, o.TiltYDegrees, o.TiltZDegrees, o.Parameters, o.SurfaceBsdf);
}

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialSceneWorkspaceTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Fact]
    public async Task FlatLargeScenesCreateOnlyTheOpenedNavigationPage()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(NonSequentialSceneWorkspaceTests));
        await session.Dispatch(async () =>
        {
            using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("flat.staropt");
            var objects = Enumerable.Range(0, 2000).Select(i => NonSequentialObjectDefinition.Create(NonSequentialObjectKind.Null) with { Name = "Object" + i }).ToArray();
            await StarOptProjectStore.SaveAsync(new([OpticalSystemDesign.Core.Optic.CreateBlank()], 0,
                NonSequentialDocument: new NonSequentialDocument("Flat scene", [new("d", 550, 1, true)], objects)), path);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            using var panel = new NonSequentialObjectEditorPanel(app.NonSequential, app.Events);
            Assert.InRange(panel.MaterializedNodeCount, 1, 160);
            panel.SelectObjects([objects[^1].Id]);
            Assert.Equal(objects[^1].Id, panel.SelectedObjectIds.Single());
            Assert.InRange(panel.MaterializedNodeCount, 1, 290);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", "zh-CN", 1760, 1040, 1)]
    [InlineData("Dark", "ja-JP", 1200, 950, 1.5)]
    [InlineData("Isekai", "en-US", 950, 1100, 2)]
    [InlineData("Lumen", "en-US", 1540, 1000, 1)]
    [InlineData("Pixel", "zh-CN", 1540, 1000, 1)]
    public async Task ActualMainWorkspaceNavigates432ObjectsAndKeepsSearchIsolationAndTableSelectionLinked(string theme, string language, int width, int height, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(NonSequentialSceneWorkspaceTests));
        await session.Dispatch(async () =>
        {
            var previous = UiLocalization.CurrentLanguage;
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "docs/assets/ui-proposals/2026-11-23/non-sequential-scene-tree/v3-complex-demo.staropt"));
            app.Modes.SwitchTo(Dto.OpticalWorkbenchMode.NonSequential);
            var ready = false;
            using var files = new CameraSensorFileTests.TemporaryFiles();
            var window = new MainWindow(new AppSettings
            {
                Theme = theme,
                Language = language,
                WorkbenchMode = "NonSequential",
                WindowWidth = width,
                WindowHeight = height
            }, app, new WorkspaceSessionStore(files.Path("workspace")));
            window.StartupCompleted += (_, _) => ready = true;
            try
            {
                window.Show(); window.SetRenderScaling(scale);
                for (var i = 0; i < 300 && !ready; i++) { await Task.Delay(10); Tick(window); }
                Assert.True(ready);
                var panel = window.GetVisualDescendants().OfType<NonSequentialObjectEditorPanel>().Single();
                await panel.SceneRefresh; Tick(window);
                if (language == "en-US")
                {
                    Assert.Equal("Structure", panel.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "nsc-nav-structure").Content);
                    Assert.Equal("Optics", panel.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "nsc-nav-optical").Content);
                }
                var document = app.NonSequential.GetDocument(); Assert.Equal(432, document.Objects.Count);
                Assert.InRange(panel.MaterializedNodeCount, 1, 160);
                var original = app.NonSequential.GetDesignSceneIdentity();
                var lens = document.Objects.First(o => o.Kind == Dto.NonSequentialObjectKind.StandardLens);
                panel.SelectObjects([lens.Id]); Tick(window);
                var model = panel.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                Assert.Contains(lens.ObjectNumber, model.HighlightedObjectNumbers);
                var fitted = Assert.IsType<Rect>(model.ObjectPickingBounds);
                Assert.InRange(fitted.Left, 0, model.Bounds.Width);
                Assert.InRange(fitted.Top, 0, model.Bounds.Height);
                Assert.InRange(fitted.Right, 0, model.Bounds.Width);
                Assert.InRange(fitted.Bottom, 0, model.Bounds.Height);
                Assert.True(Math.Max(fitted.Width / model.Bounds.Width, fitted.Height / model.Bounds.Height) > .65,
                    "The fitted model should occupy the viewport rather than remain a small thumbnail.");
                Assert.Single(panel.GetVisualDescendants().OfType<Button>(), b => b.Name == "nsc-primary-trace");
                foreach (var editor in panel.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name?.StartsWith("nsc-field-") == true))
                {
                    var origin = editor.TranslatePoint(default, panel)!.Value;
                    Assert.InRange(origin.X, 0, panel.Bounds.Width);
                    Assert.InRange(origin.X + editor.Bounds.Width, 0, panel.Bounds.Width);
                    Assert.True(editor.Bounds.Width >= 36, "Property editors must retain a usable width.");
                }
                Capture(window, theme + "-workspace", scale);
                var settings = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "nsc-view-settings");
                var settingsFlyout = Assert.IsType<Flyout>(settings.Flyout); settingsFlyout.ShowAt(settings); Tick(window);
                var settingsContent = Assert.IsAssignableFrom<Control>(settingsFlyout.Content);
                var rayToggle = settingsContent.GetVisualDescendants().OfType<CheckBox>().Single();
                rayToggle.IsChecked = true; Assert.True(model.ShowRays); rayToggle.IsChecked = false; Assert.False(model.ShowRays);
                settingsFlyout.Hide(); Tick(window);
                var search = panel.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "nsc-search");
                search.Text = lens.Name; Tick(window);
                var tree = panel.GetVisualDescendants().OfType<TreeView>().Single();
                Assert.NotEmpty(tree.Items); Assert.Contains(lens.Id, panel.SelectedObjectIds);
                Capture(window, theme + "-search", scale);
                search.Text = "zzzz-no-match"; Tick(window); Assert.Empty(tree.Items);
                search.Text = ""; Tick(window);
                panel.IsolateSelection(); Tick(window);
                Assert.All(model.Scene!.ThreeDimensional!.Surfaces, s => Assert.Equal(lens.ObjectNumber, s.SurfaceNumber));
                Capture(window, theme + "-isolated", scale);
                var point = model.ObjectPickPoint(lens.ObjectNumber); Assert.NotNull(point);
                Assert.Equal(lens.ObjectNumber, model.PickObjectAt(point.Value)); Assert.Single(panel.SelectedObjectIds);
                panel.ShowAllObjects(); panel.ShowObjectTable(true); Tick(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                Assert.True(grid.IsEffectivelyVisible); Assert.Single(grid.SelectedItems);
                panel.SetNavigation("optical"); Tick(window); Assert.Contains(lens.Id, panel.SelectedObjectIds);
                panel.SetNavigation("simulation"); Tick(window); Assert.NotEmpty(tree.Items);
                panel.SetNavigation("structure"); panel.ShowObjectTable(false); Tick(window);
                Assert.Equal(original, app.NonSequential.GetDesignSceneIdentity());
                if (theme == "Light")
                {
                    var addMenu = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "nsc-add-menu");
                    var addFlyout = Assert.IsType<Flyout>(addMenu.Flyout); addFlyout.ShowAt(addMenu); Tick(window);
                    var addContent = Assert.IsAssignableFrom<Control>(addFlyout.Content);
                    addContent.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "nsc-add-object").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Tick(window); Assert.Equal(433, app.NonSequential.GetDocument().Objects.Count);
                    Assert.True(app.Documents.Undo()); Tick(window); Assert.Equal(original, app.NonSequential.GetDesignSceneIdentity());
                }
                // A file switch clears old selection, isolation and old geometry before the new scene publishes.
                app.Documents.NewBlank(); Tick(window); await panel.SceneRefresh; Tick(window);
                Assert.Empty(panel.SelectedObjectIds); Assert.Empty(model.Scene!.ThreeDimensional!.Surfaces);
            }
            finally { window.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); UiLocalization.Apply(previous); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ActualInspectorAppliesMixedValuesOnceAndOrganizationPersistsThroughUndo()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(NonSequentialSceneWorkspaceTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create(); app.Modes.SwitchTo(Dto.OpticalWorkbenchMode.NonSequential);
            var a = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.StandardLens); var b = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.StandardLens);
            var row = app.NonSequential.GetDocument().Objects.Single(o => o.Id == b);
            app.NonSequential.UpdateObject(NonSequentialOrganizationTests.Update(row) with { X = 4 });
            using var panel = new NonSequentialObjectEditorPanel(app.NonSequential, app.Events, app.Visualization, app.NonSequentialAnalysis, app.Documents);
            var window = new Window { Content = panel, Width = 1500, Height = 1000 }; ThemeChrome.ApplyDialogDecoration(window);
            try
            {
                window.Show(); await panel.SceneRefresh; Tick(window); panel.SelectObjects([a, b]); Tick(window);
                T Find<T>(string name) where T : Control => panel.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                var x = Find<TextBox>("nsc-batch-x"); Assert.Equal("", x.Text); Assert.Equal(UiLocalization.Text("多个值"), x.PlaceholderText);
                Find<TextBox>("nsc-batch-y").Text = "7"; Tick(window);
                Find<Button>("nsc-apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                var changed = app.NonSequential.GetDocument(); Assert.All(changed.Objects, o => Assert.Equal(7, o.Y));
                Assert.Equal(0, changed.Objects.Single(o => o.Id == a).X); Assert.Equal(4, changed.Objects.Single(o => o.Id == b).X);
                Assert.True(app.Documents.Undo()); Tick(window); Assert.All(app.NonSequential.GetDocument().Objects, o => Assert.Equal(0, o.Y));
                panel.SelectObjects([a, b]); Tick(window);
                Find<CheckBox>("nsc-batch-visible").IsChecked = false; Tick(window);
                Find<Button>("nsc-apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.All(app.NonSequential.GetDocument().Objects, o => { Assert.False(o.Visible); Assert.True(o.Enabled); });
                Assert.True(app.Documents.Undo()); Tick(window);
                var identity = app.NonSequential.GetDesignSceneIdentity();
                panel.SelectObjects([a, b]); var folder = panel.AddFolder("User optics"); panel.MoveToFolder([a, b], folder);
                panel.SaveSelection("Pair"); Tick(window);
                Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity()); Assert.Equal(2, app.NonSequential.GetDocument().Organization!.SelectionSets.Single().ObjectIds.Count);
                var tree = Find<TreeView>("nsc-tree");
                tree.SelectedItem = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(i => AutomationProperties.GetAutomationId(i) == "nsc-node-folder-" + folder);
                Tick(window); Assert.Equal(2, panel.SelectedObjectIds.Count);
                var set = app.NonSequential.GetDocument().Organization!.SelectionSets.Single();
                tree.SelectedItem = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(i => AutomationProperties.GetAutomationId(i) == "nsc-node-set-" + set.Id);
                Tick(window); Assert.Equal(2, panel.SelectedObjectIds.Count);
                panel.SelectObjects([a, b]); Tick(window); Capture(window, "Light-batch", 1);
                Assert.True(app.Documents.Undo()); Tick(window); Assert.Empty(app.NonSequential.GetDocument().Organization!.SelectionSets);
                // Invalid batch input is rejected without changing either object.
                panel.SelectObjects([a, b]); Tick(window); Find<TextBox>("nsc-batch-y").Text = "NaN"; Tick(window);
                Find<Button>("nsc-apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal(identity, app.NonSequential.GetDesignSceneIdentity());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Capture(Window window, string name, double scale)
    {
        var output = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), ".tmp", "nsc-tree-product"); Directory.CreateDirectory(output);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(window); bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
