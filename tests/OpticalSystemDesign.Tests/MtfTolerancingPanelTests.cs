using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using IsoSurfaceFormUnit = OpticalSystemDesign.Core.Tolerancing.IsoSurfaceFormUnit;
using IsoSurfaceFormStatistics = OpticalSystemDesign.Core.Tolerancing.IsoSurfaceFormStatistics;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MtfTolerancingPanelTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MTF_TOLERANCE_CAPTURE_DIR"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_JOINT_TOLERANCE_CAPTURE_DIR"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_STAGE_OBJECTIVE_CAPTURE_DIR"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SYSTEM_MERIT_CAPTURE_DIR"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOUND_MERIT_CAPTURE_DIR"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TSC_CAPTURE_DIR"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOLERANCE_RESULTS_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", 720)]
    [InlineData("Dark", 720)]
    [InlineData("Light", 480)]
    public async Task MtfSettingsFieldsAndCompensatorsRemainEditableAndVisible(string theme, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            var options = TolerancingRunWindow.DefaultOptions() with
            {
                Criterion = ToleranceCriterion.Mtf,
                YieldLimit = .2,
                MtfSettings = new(30, FieldLimits: [new(2, .15)]),
                AdditionalCompensators = [new(1, ToleranceCompensatorKind.ElementTiltY, -.05, .05, 2, new(1, -2, 5), Stage: 2)]
            };
            var window = new TolerancingRunWindow(options, app.Prescription.GetFields(), app.Prescription.GetSurfaces(), app.Prescription.GetWavelengths()) { Width = width, Height = 760 };
            try
            {
                window.Show(); window.SetRenderScaling(width == 480 ? 1.5 : 1); Tick(); var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                tabs.SelectedIndex = 1; Tick(); Capture("criterion");
                var frequency = Field<NumericUpDown>(window, "_mtfFrequency");
                Assert.Equal(30, frequency.Value); Assert.True(frequency.IsEffectivelyVisible);
                var right = frequency.TranslatePoint(default, window)!.Value.X + frequency.Bounds.Width;
                Assert.InRange(right, 0, window.Bounds.Width - 25);
                var secondField = window.GetVisualDescendants().OfType<NumericUpDown>().Single(control => AutomationProperties.GetName(control) == "视场 2 MTF 下限");
                Assert.True(secondField.IsEnabled); Assert.Equal(.15m, secondField.Value);
                Field<CheckBox>(window, "_separateFields").IsChecked = false;
                Assert.False(secondField.IsEnabled); Assert.NotNull(ToolTip.GetTip(secondField)); Assert.NotEmpty(AutomationProperties.GetHelpText(secondField) ?? "");
                Field<CheckBox>(window, "_separateFields").IsChecked = true;
                Assert.True(secondField.IsEnabled); Assert.Null(ToolTip.GetTip(secondField));
                secondField.Value = .18m;
                var scroller = ((TabItem)tabs.Items[1]!).Content as ScrollViewer; scroller!.Offset = new Vector(0, scroller.Extent.Height); Tick(); Capture("fields");
                tabs.SelectedIndex = 2; Tick(); Capture("compensation");
                Field<ComboBox>(window, "_compensation").SelectedIndex = 2;
                Assert.True(Field<NumericUpDown>(window, "_cycles").IsEnabled);
                var draft = (TolerancingRunOptions)window.GetType().GetMethod("BuildOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
                Assert.Equal(ToleranceCompensationAlgorithm.CoordinatePatternSearch, draft.CompensationAlgorithm);
                Assert.Equal(.18, Assert.Single(draft.MtfSettings!.FieldLimits!).Minimum);
                var end = window.GetVisualDescendants().OfType<NumericUpDown>()
                    .Single(control => AutomationProperties.GetName(control) == "镜组终止面");
                Assert.True(end.IsEffectivelyVisible);
                Assert.Equal(2, end.Value);
                Assert.InRange(end.TranslatePoint(default, window)!.Value.X + end.Bounds.Width, 0, window.Bounds.Width - 25);
                var pivotZ = window.GetVisualDescendants().OfType<NumericUpDown>()
                    .Single(control => AutomationProperties.GetName(control) == "支点 Z (mm)");
                Assert.True(pivotZ.IsEffectivelyVisible);
                Assert.Equal(5, pivotZ.Value);
                Assert.InRange(pivotZ.TranslatePoint(default, window)!.Value.X + pivotZ.Bounds.Width, 0, window.Bounds.Width - 25);
                var stage = window.GetVisualDescendants().OfType<NumericUpDown>()
                    .Single(control => AutomationProperties.GetName(control) == "补偿阶段");
                Assert.True(stage.IsEffectivelyVisible);
                Assert.Equal(2, stage.Value);
                Assert.InRange(stage.TranslatePoint(default, window)!.Value.X + stage.Bounds.Width, 0, window.Bounds.Width - 25);
                Assert.Equal(options.AdditionalCompensators, draft.AdditionalCompensators);
                Assert.Contains("accent", window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "运行")).Classes);
                Assert.DoesNotContain("accent", window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "取消")).Classes);

                void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                void Capture(string name)
                {
                    Tick(); var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MTF_TOLERANCE_CAPTURE_DIR");
                    if (string.IsNullOrWhiteSpace(directory)) return;
                    Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"mtf-tolerance-{theme.ToLowerInvariant()}-{width}-{name}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageObjectivesStayEditableAndRoundTripStudy(bool systemMerit)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            if (systemMerit)
            {
                var surfaces = app.Prescription.GetSurfaces();
                var focus = surfaces.Count - 2;
                app.Optimization.SetMeritFunction(SystemMeritTolerancingTests.DifferenceTable(focus, surfaces[focus].Thickness - surfaces[1].Thickness));
            }
            var options = TolerancingRunWindow.DefaultOptions() with
            {
                Criterion = ToleranceCriterion.Mtf, MtfSettings = new(30, FieldLimits: [new(2, .2)]),
                StageObjectives = [new(1, systemMerit ? ToleranceCriterion.SystemMerit : ToleranceCriterion.RmsSpotRadius),
                    new(2, ToleranceCriterion.Mtf, new(25, Method: ToleranceMtfMethod.Geometric))],
                AdditionalCompensators = [new(1, ToleranceCompensatorKind.ElementDecenterX, -.05, .05, 2),
                    new(app.Prescription.GetSurfaces().Count - 2, ToleranceCompensatorKind.Thickness, -.2, .2, Stage: 2)]
            };
            var window = new TolerancingRunWindow(options, app.Prescription.GetFields(), app.Prescription.GetSurfaces(), app.Prescription.GetWavelengths())
                { Width = 600, Height = 820 };
            try
            {
                window.Show();
                window.SetRenderScaling(1.5);
                Dispatcher.UIThread.RunJobs();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                tabs.SelectedIndex = 2;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var frequency = window.GetVisualDescendants().OfType<NumericUpDown>()
                    .Single(control => AutomationProperties.GetName(control) == "阶段 MTF 空间频率"
                        && control.IsEffectivelyVisible);
                Assert.Equal(25, frequency.Value);
                frequency.Value = 18;
                Assert.InRange(frequency.TranslatePoint(default, window)!.Value.X + frequency.Bounds.Width, 0, window.Bounds.Width - 20);
                var directory = Environment.GetEnvironmentVariable(systemMerit
                    ? "OPTICAL_SYSTEM_DESIGN_SYSTEM_MERIT_CAPTURE_DIR" : "OPTICAL_SYSTEM_DESIGN_STAGE_OBJECTIVE_CAPTURE_DIR");
                frequency.BringIntoView();
                CaptureFrame(window, systemMerit ? "system-merit-stage-light-600.png" : "stage-objectives-light-600.png", directory);
                if (systemMerit)
                {
                    tabs.SelectedIndex = 1;
                    Field<ComboBox>(window, "_criterion").SelectedIndex = (int)ToleranceCriterion.SystemMerit;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.True(Field<Control>(window, "_systemMeritNote").IsEffectivelyVisible);
                    CaptureFrame(window, "system-merit-criterion-light-600.png", directory);
                }
                var draft = (TolerancingRunOptions)window.GetType().GetMethod("BuildOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
                Assert.Equal(18, draft.StageObjectives![1].MtfSettings!.Frequency);
                Assert.Equal(30, draft.MtfSettings!.Frequency);
                using var panel = new TolerancingPanel(app.Documents, app.Prescription, app.Tolerancing, app.Events);
                panel.GetType().GetField("_stageObjectives", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(panel, draft.StageObjectives);
                var study = panel.BuildStudyFile() with
                {
                    Operands = [new(1, true, ToleranceOperandKind.Radius, 1, -.01, .01)],
                    Criterion = draft.Criterion, MtfSettings = draft.MtfSettings, AdditionalCompensators = draft.AdditionalCompensators
                };
                Assert.Equal(systemMerit ? 9 : 8, study.SchemaVersion);
                var serializer = new JsonSerializerOptions
                {
                    WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                };
                var json = JsonSerializer.Serialize(study, serializer);
                Assert.Equal(json, JsonSerializer.Serialize(panel.ParseStudyFile(json), serializer));
                Assert.Throws<InvalidDataException>(() => panel.ParseStudyFile(JsonSerializer.Serialize(study with { SchemaVersion = study.SchemaVersion - 1 }, serializer)));
                if (!string.IsNullOrWhiteSpace(directory))
                    File.WriteAllText(Path.Combine(directory, systemMerit ? "system-merit.startol.json" : "stage-objectives.startol.json"), json);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MeritBindingsCanBeCapturedAcrossDialogVisitsAndRoundTripVersionTen()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            var surfaces = app.Prescription.GetSurfaces();
            var focus = surfaces.Count - 2;
            var options = TolerancingRunWindow.DefaultOptions() with
            {
                Criterion = ToleranceCriterion.SystemMerit,
                StageObjectives = [new(1, ToleranceCriterion.SystemMerit), new(2, ToleranceCriterion.SystemMerit)],
                AdditionalCompensators = [new(3, ToleranceCompensatorKind.ElementDecenterX, -.1, .1, 4),
                    new(focus, ToleranceCompensatorKind.Thickness, -.4, .4, Stage: 2)]
            };
            var owner = new Window { Width = 640, Height = 800 };
            owner.Show();
            var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOUND_MERIT_CAPTURE_DIR");
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    var name = $"表 {i + 1}";
                    app.Optimization.SetMeritFunction(i switch
                    {
                        0 => [BoundMeritTolerancingTests.Row(1, "GLCX", 3, .03)],
                        1 => [BoundMeritTolerancingTests.Row(1, "THIC", focus, surfaces[focus].Thickness + .1)],
                        _ => [BoundMeritTolerancingTests.Row(1, "GLCX", 3, .03),
                            BoundMeritTolerancingTests.Row(2, "THIC", focus, surfaces[focus].Thickness + .125)]
                    });
                    var window = new TolerancingRunWindow(options, app.Prescription.GetFields(), surfaces,
                        app.Prescription.GetWavelengths(), captureMeritTable: app.Tolerancing.CaptureMeritTable)
                        { Width = 480, Height = 820 };
                    try
                    {
                        var dialog = window.ShowDialog<TolerancingRunOptions?>(owner);
                        window.SetRenderScaling(1.5);
                        Dispatcher.UIThread.RunJobs();
                        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                        tabs.SelectedIndex = i < 2 ? 2 : 1;
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        var binding = i < 2 ? i : 0;
                        var names = window.GetVisualDescendants().OfType<TextBox>()
                            .Where(box => box.IsEffectivelyVisible && AutomationProperties.GetName(box) == "评价表名称").ToArray();
                        names[binding].Text = name;
                        var bind = window.GetVisualDescendants().OfType<Button>()
                            .Where(button => button.IsEffectivelyVisible && Equals(button.Content, "绑定当前评价表")).ElementAt(binding);
                        bind.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var apply = window.GetVisualDescendants().OfType<Button>()
                            .Single(button => Equals(button.Content, "应用设置"));
                        Assert.InRange(apply.TranslatePoint(default, window)!.Value.X + apply.Bounds.Width, 0, window.Bounds.Width);
                        if (i == 1)
                        {
                            bind.BringIntoView();
                            CaptureFrame(window, "bound-merit-stage-light-480.png", directory);
                        }
                        if (i == 2) CaptureFrame(window, "bound-merit-final-light-480.png", directory);
                        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Dispatcher.UIThread.RunJobs();
                        Assert.True(window.ApplyOnly);
                        Assert.True(dialog.IsCompletedSuccessfully);
                        options = Assert.IsType<TolerancingRunOptions>(dialog.Result);
                    }
                    finally { if (window.IsVisible) window.Close(); }
                }
                Assert.Equal(new[] { "表 1", "表 2" }, options.StageObjectives!.Select(row => row.MeritTable!.Name));
                Assert.Equal(new[] { .03, surfaces[focus].Thickness + .1 }, options.StageObjectives!.Select(row => Assert.Single(row.MeritTable!.Rows).Target));
                Assert.Equal("表 3", options.MeritTable!.Name);
                Assert.Equal(surfaces[focus].Thickness + .125, options.MeritTable.Rows[1].Target, 12);
                app.Optimization.SetMeritFunction([]);
                using var panel = new TolerancingPanel(app.Documents, app.Prescription, app.Tolerancing, app.Events);
                Field<ComboBox>(panel, "_criterion").SelectedIndex = (int)options.Criterion;
                panel.GetType().GetField("_stageObjectives", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, options.StageObjectives);
                panel.GetType().GetField("_meritTable", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, options.MeritTable);
                var study = panel.BuildStudyFile() with
                {
                    Operands = [new(1, true, ToleranceOperandKind.Radius, 1, -.01, .01)],
                    AdditionalCompensators = options.AdditionalCompensators
                };
                Assert.Equal(10, study.SchemaVersion);
                var serializer = new JsonSerializerOptions
                {
                    WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                };
                var json = JsonSerializer.Serialize(study, serializer);
                Assert.Equal(json, JsonSerializer.Serialize(panel.ParseStudyFile(json), serializer));
                Assert.Throws<InvalidDataException>(() => panel.ParseStudyFile(JsonSerializer.Serialize(study with { SchemaVersion = 9 }, serializer)));
                if (!string.IsNullOrWhiteSpace(directory))
                    File.WriteAllText(Path.Combine(directory, "bound-merit.startol.json"), json);
            }
            finally { owner.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ScriptPreviewBlocksUntilTravelAndTablesAreExplicitlyBound()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            var owner = new Window { Width = 620, Height = 680 };
            var window = new ToleranceScriptImportWindow("two-stage.tsc", ToleranceScriptImportTests.Script, 8, [], null, _ => null)
                { Width = 560, Height = 680 };
            owner.Show();
            try
            {
                var dialog = window.ShowDialog<ToleranceScriptPreview?>(owner);
                window.SetRenderScaling(1.5);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var preview = Field<TextBox>(window, "_preview");
                var apply = Field<Button>(window, "_apply");
                Assert.True(preview.IsEffectivelyVisible);
                Assert.False(apply.IsEnabled);
                Assert.Contains("缺少评价表", preview.Text);
                Assert.Contains("缺少行程", preview.Text);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TSC_CAPTURE_DIR");
                CaptureFrame(window, "tsc-preview-blocked-light-560.png", directory);
                foreach (var table in ToleranceScriptImportTests.NamedTables().Values) window.BindMeritTable(table);
                Assert.False(apply.IsEnabled);
                Field<NumericUpDown>(window, "_travel").Value = .2m;
                Assert.True(apply.IsEnabled);
                Assert.Contains("阶段 2", preview.Text);
                Assert.Contains("最终验收使用 Acceptance", preview.Text);
                Assert.InRange(apply.TranslatePoint(default, window)!.Value.X + apply.Bounds.Width, 0, window.Bounds.Width);
                CaptureFrame(window, "tsc-preview-ready-light-560.png", directory);
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(dialog.IsCompletedSuccessfully);
                Assert.Equal(2, dialog.Result!.Stages.Count);
            }
            finally
            {
                if (window.IsVisible) window.Close();
                owner.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", 1080)]
    [InlineData("Dark", 560)]
    public async Task ToleranceDataViewerShowsLiveResultsAndDrillsIntoActualTrials(string theme, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            var focus = app.Prescription.GetSurfaces().Count - 2;
            var originalGap = app.Prescription.GetSurfaces()[focus].Thickness;
            var second = app.MultiConfiguration.Add();
            app.MultiConfiguration.SetThickness(second, focus, originalGap + .4);
            var layoutDirectory = Directory.CreateTempSubdirectory("optical-tolerance-viewer-");
            using var manager = new PanelManager(app, new AppSettings(), new WorkspaceSessionStore(layoutDirectory.FullName));
            manager.ShowTolerancingDataViewer();
            const string id = "document:tolerance-results";
            Assert.Equal(WorkspaceDocumentTypes.ToleranceResults, manager.Factory.Descriptor(id)?.TypeId);
            var panel = Assert.IsType<ToleranceResultsPanel>(manager.Factory.DocumentContent<ToleranceResultsPanel>(id));
            var editor = Assert.IsType<TolerancingPanel>(manager.Factory.DocumentContent<TolerancingPanel>("document:tolerancing"));
            var window = new Window { Width = width, Height = 730, Content = panel };
            try
            {
                window.Show();
                window.SetRenderScaling(1.5);
                Tick();
                Assert.True(Field<TextBlock>(panel, "_empty").IsEffectivelyVisible);
                Assert.False(Field<Button>(panel, "_histogram").IsEnabled);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOLERANCE_RESULTS_CAPTURE_DIR");
                if (theme == "Light") CaptureFrame(window, "tolerance-results-empty-light-1080.png", directory);
                var options = TolerancingRunWindow.DefaultOptions() with
                {
                    MonteCarloRuns = 3, Seed = 431, YieldLimit = .2, CompensationIterations = 3, MaxDegreeOfParallelism = 1,
                    AdditionalCompensators =
                    [
                        new(1, ToleranceCompensatorKind.ElementDecenterX, -.15, .15, 2),
                        new(focus, ToleranceCompensatorKind.Thickness, -.8, .8, Stage: 2, Configuration: 1),
                        new(focus, ToleranceCompensatorKind.Thickness, -.8, .8, Stage: 2, Configuration: 2)
                    ],
                    ConfigurationSettings = new(ToleranceConfigurationScope.All, Limits: [new(1, .2), new(2, 1e-8)])
                };
                Assert.True(await editor.RunAsync(options), editor.ResultMessage);
                Tick();
                var result = Assert.IsType<TolerancingResultDto>(editor.CurrentResult);
                var grid = Field<DataGrid>(panel, "_configurationGrid");
                var overview = grid.ItemsSource.Cast<object>().ToArray();
                Assert.Equal(3, overview.Length);
                Assert.Equal(0, Property(overview[0], "Passed"));
                Assert.Equal(3, Property(overview[1], "Passed"));
                Assert.Equal(0, Property(overview[2], "Passed"));
                Assert.True(Field<Button>(panel, "_histogram").IsEnabled);
                var editorDocument = manager.Factory.OpenDocument(new("document:tolerancing", WorkspaceDocumentTypes.Tolerancing, "公差数据编辑器"));
                manager.Factory.CloseDockable(editorDocument);
                Assert.Same(editor, manager.Factory.DocumentContent<TolerancingPanel>("document:tolerancing"));
                manager.ShowTolerancingHistogram();
                var histogram = Assert.IsType<ToleranceChartDocumentPanel>(
                    manager.Factory.DocumentContent<ToleranceChartDocumentPanel>("document:tolerance-histogram"));
                Assert.NotEmpty(Field<OpticalSystemDesign.App.Controls.AnalysisPlotControl>(histogram, "_plot").Series);
                CaptureFrame(window, $"tolerance-results-overview-{theme.ToLowerInvariant()}-{width}.png", directory);

                var tabs = Field<TabControl>(panel, "_tabs");
                tabs.SelectedItem = Field<TabItem>(panel, "_sensitivityTab");
                Tick();
                Assert.NotEmpty(Field<DataGrid>(panel, "_sensitivityGrid").ItemsSource.Cast<object>());
                Field<Button>(panel, "_worst").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Tick();
                var trials = Field<DataGrid>(panel, "_trialGrid");
                var selected = Assert.IsType<TolerancingTrialRowDto>(Property(trials.SelectedItem!, "Row"));
                Assert.Equal(result.WorstSample!.Recorded.Trial, selected.Trial);
                var detail = Field<ContentControl>(panel, "_trialDetail");
                var phases = detail.GetVisualDescendants().OfType<TabControl>().Single();
                Assert.Equal(new[] { "补偿后", "补偿前", "阶段 1", "阶段 2" }, phases.Items.Cast<TabItem>().Select(tab => tab.Header));
                phases.SelectedIndex = 3;
                Tick();
                var stageGrid = Assert.IsType<DataGrid>(Assert.IsType<TabItem>(phases.SelectedItem).Content);
                Assert.Contains(stageGrid.ItemsSource.Cast<object>(), row => Property(row, "Name") as string == "配置 2");
                Assert.Contains(stageGrid.ItemsSource.Cast<object>(), row =>
                    (Property(row, "Note") as string)?.Contains("行程") == true);
                Assert.DoesNotContain(stageGrid.ItemsSource.Cast<object>(), row =>
                    (Property(row, "Name") as string)?.Contains("ElementDecenterX") == true);
                Assert.Equal(width < 850, panel.GetVisualDescendants()
                    .OfType<OpticalSystemDesign.App.Controls.ResponsiveTwoPaneGrid>().Single().IsNarrow);
                CaptureFrame(window, $"tolerance-results-trial-{theme.ToLowerInvariant()}-{width}.png", directory);
                Field<ComboBox>(panel, "_filter").SelectedIndex = 2;
                Tick();
                Assert.Empty(trials.ItemsSource.Cast<object>());

                app.MultiConfiguration.SetThickness(second, focus, originalGap + .5);
                Tick();
                Assert.Null(editor.CurrentResult);
                Assert.True(Field<TextBlock>(panel, "_empty").IsEffectivelyVisible);
                Assert.Empty(trials.ItemsSource.Cast<object>());
                Assert.False(Field<Button>(panel, "_histogram").IsEnabled);
                Assert.Empty(Field<OpticalSystemDesign.App.Controls.AnalysisPlotControl>(histogram, "_plot").Series);
                if (theme == "Light")
                {
                    var sensitivityOnly = options with
                    {
                        Mode = ToleranceAnalysisMode.InverseIncrement, InverseValue = .5,
                        MonteCarloRuns = 0, CompensationIterations = 0, AdditionalCompensators = []
                    };
                    Assert.True(await editor.RunAsync(sensitivityOnly), editor.ResultMessage);
                    Tick();
                    Assert.True(Field<TabItem>(panel, "_sensitivityTab").IsVisible);
                    Assert.False(Field<TabItem>(panel, "_trialsTab").IsVisible);
                    Assert.True(Field<TabItem>(panel, "_inverseTab").IsVisible);
                    Assert.Equal(2, Field<DataGrid>(panel, "_inverseGrid").ItemsSource.Cast<object>().Count());
                    Assert.False(Field<Button>(panel, "_yieldChart").IsEnabled);
                }
            }
            finally
            {
                window.Close();
                manager.Dispose();
                layoutDirectory.Delete(recursive: true);
            }

            void Tick()
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            static object? Property(object row, string name) => row.GetType().GetProperty(name)!.GetValue(row);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task JointScopeSettingsRemainVisibleAndRoundTripVersionSeven()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            app.MultiConfiguration.Add();
            var surface = app.Prescription.GetSurfaces().Count - 2;
            app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.Thickness, surface)], app.Events.Revision);
            var settings = new ToleranceConfigurationSettingsDto(ToleranceConfigurationScope.Selected, [1, 2],
                [new(1, .2, [new(1, .25)]), new(2, .3, [new(2, .4)])],
                [new(1, 1, surface, -.1, .1), new(1, 1, surface, -.2, .2, true, 2)]);
            var options = TolerancingRunWindow.DefaultOptions() with
            {
                Criterion = ToleranceCriterion.Mtf, MtfSettings = new(30, SeparateFields: true),
                ConfigurationSettings = settings,
                AdditionalCompensators = [new(1, ToleranceCompensatorKind.DecenterX, -.05, .05, Configuration: 2)]
            };
            var window = new TolerancingRunWindow(options, app.Prescription.GetFields(), app.Prescription.GetSurfaces(),
                app.Prescription.GetWavelengths(), app.MultiConfiguration.GetRows(), app.MultiConfiguration.GetOperandRows())
                { Width = 640, Height = 800 };
            try
            {
                window.Show();
                window.SetRenderScaling(1.5);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var scope = Field<ComboBox>(window, "_configurationScope");
                Assert.True(scope.IsEffectivelyVisible);
                Assert.Equal((int)ToleranceConfigurationScope.Selected, scope.SelectedIndex);
                Assert.InRange(scope.TranslatePoint(default, window)!.Value.X + scope.Bounds.Width, 0, window.Bounds.Width - 20);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_JOINT_TOLERANCE_CAPTURE_DIR");
                CaptureFrame(window, "joint-setup-light-640.png", directory);
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                var setup = Assert.IsType<ScrollViewer>(Assert.IsType<TabItem>(tabs.Items[0]).Content);
                setup.Offset = new Vector(0, setup.Extent.Height);
                CaptureFrame(window, "joint-mce-light-640.png", directory);
                var draft = (TolerancingRunOptions)window.GetType().GetMethod("BuildOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
                Assert.Equal(settings.Configurations, draft.ConfigurationSettings!.Configurations);
                Assert.Equal(.4, Assert.Single(draft.ConfigurationSettings.Limits![1].FieldLimits!).Minimum);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), box => box.Text == "2=0.4");
                Assert.Equal(settings.MceRows, draft.ConfigurationSettings.MceRows);
                Assert.Equal(options.AdditionalCompensators, draft.AdditionalCompensators);
                using var panel = new TolerancingPanel(app.Documents, app.Prescription, app.Tolerancing, app.Events);
                panel.GetType().GetField("_configurationSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(panel, draft.ConfigurationSettings);
                var study = panel.BuildStudyFile() with
                {
                    Operands = [new(1, true, ToleranceOperandKind.Radius, 1, -.01, .01)],
                    Criterion = draft.Criterion, MtfSettings = draft.MtfSettings, AdditionalCompensators = draft.AdditionalCompensators
                };
                Assert.Equal(7, study.SchemaVersion);
                var serializer = new JsonSerializerOptions
                {
                    WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                };
                var json = JsonSerializer.Serialize(study, serializer);
                var restored = panel.ParseStudyFile(json);
                Assert.Equal(json, JsonSerializer.Serialize(restored, serializer));
                Assert.Throws<InvalidDataException>(() => panel.ParseStudyFile(JsonSerializer.Serialize(study with { SchemaVersion = 6 }, serializer)));
                if (!string.IsNullOrWhiteSpace(directory))
                    File.WriteAllText(Path.Combine(directory, "cooke-joint-settings.startol.json"), json);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StudyVersionThreeRoundTripsMtfCompensationAndMigratesLegacyRms()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new TolerancingPanel(app.Documents, app.Prescription, app.Tolerancing, app.Events);
            var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
            var study = panel.BuildStudyFile() with
            {
                Criterion = ToleranceCriterion.Mtf,
                MtfSettings = new(40, 2, 1, ToleranceMtfMethod.Geometric, ToleranceMtfDirection.Minimum, true, [new(2, .3)]),
                AdditionalCompensators = [new(2, ToleranceCompensatorKind.DecenterX, -.01, .02)],
                CompensationAlgorithm = ToleranceCompensationAlgorithm.CoordinatePatternSearch
            };
            var restored = panel.ParseStudyFile(JsonSerializer.Serialize(study, options));
            Assert.Equal(3, restored.SchemaVersion); Assert.Equal(40, restored.MtfSettings!.Frequency);
            Assert.Equal(study.MtfSettings.FieldLimits, restored.MtfSettings.FieldLimits);
            Assert.Equal(study.AdditionalCompensators, restored.AdditionalCompensators); Assert.Equal(study.CompensationAlgorithm, restored.CompensationAlgorithm);
            foreach (var version in new[] { 1, 2 })
            {
                var legacy = $"{{\"SchemaVersion\":{version},\"Operands\":[{{\"Index\":1,\"Enabled\":true,\"Kind\":\"Thickness\",\"SurfaceNumber\":2,\"Minimum\":-0.01,\"Maximum\":0.01}}],\"Criterion\":\"RmsSpotRadius\",\"Trials\":1,\"Seed\":42,\"CompensationIterations\":0,\"YieldLimit\":0}}";
                var old = panel.ParseStudyFile(legacy); Assert.Equal(ToleranceCriterion.RmsSpotRadius, old.Criterion);
                Assert.Null(old.MtfSettings); Assert.Null(old.AdditionalCompensators); Assert.Equal(ToleranceCompensationAlgorithm.DampedLeastSquares, old.CompensationAlgorithm);
            }
            Assert.Throws<InvalidDataException>(() => panel.ParseStudyFile(JsonSerializer.Serialize(study with { MtfSettings = study.MtfSettings with { Frequency = -1 } }, options)));
            Assert.Throws<InvalidDataException>(() => panel.ParseStudyFile(JsonSerializer.Serialize(study with { AdditionalCompensators = [new(2, ToleranceCompensatorKind.Thickness, -.01, .01), new(2, ToleranceCompensatorKind.Thickness, -.02, .02)] }, options)));
            Assert.False(panel.HasUnsavedChanges); Assert.Equal(ToleranceCriterion.RmsSpotRadius, panel.BuildStudyFile().Criterion);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WizardGeneratesPowerAndIrregularityWithSelectedTestWavelength()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            var owner = new Window { Width = 640, Height = 720 };
            var wizard = new ToleranceWizardWindow(app.Prescription) { Width = 640 };
            owner.Show();
            try
            {
                var settings = wizard.ShowDialog<ToleranceWizardSettingsDto>(owner);
                wizard.SetRenderScaling(1.5);
                var mode = Field<ComboBox>(wizard, "_radiusMode");
                var wavelength = Field<NumericUpDown>(wizard, "_testWavelength");
                Assert.False(wavelength.IsEnabled);
                mode.SelectedIndex = (int)RadiusToleranceMode.Fringes;
                Assert.True(wavelength.IsEnabled);
                wavelength.Value = .5461m;
                Field<NumericUpDown>(wizard, "_powerFringes").Value = .75m;
                Field<CheckBox>(wizard, "_irregularityEnabled").IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                wizard.UpdateLayout();
                Assert.True(wavelength.IsEffectivelyVisible);
                Assert.InRange(mode.Bounds.Width, 120, 640);
                Assert.InRange(wavelength.TranslatePoint(default, wizard)!.Value.X + wavelength.Bounds.Width, 0, wizard.Bounds.Width);
                CaptureFrame(wizard, "fringe-wizard-light-640.png");
                try
                {
                    UiLocalization.Apply(UiLocalization.English);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Contains(mode.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Power fringes");
                    Assert.Equal((int)RadiusToleranceMode.Fringes, mode.SelectedIndex);
                    UiLocalization.Apply(UiLocalization.Japanese);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Contains(mode.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "パワー縞");
                }
                finally { UiLocalization.Apply(UiLocalization.Chinese); }

                var confirm = wizard.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "确定"));
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(settings.IsCompletedSuccessfully);
                var rows = app.Tolerancing.GenerateWizard(settings.Result);
                Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.FringePower
                    && row.Maximum == .75 && row.TestWavelengthMicrometers == .5461);
                Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.FringeIrregularity
                    && row.Maximum == .2 && row.TestWavelengthMicrometers == .5461);
                Assert.DoesNotContain(rows, row => row.Kind == ToleranceOperandKind.Radius);
                Assert.True(app.Tolerancing.ValidateOperands(rows).IsValid);
            }
            finally
            {
                if (wizard.IsVisible) wizard.Close();
                owner.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WizardGeneratesAStandardZernikeToleranceWithItsOwnUnitsAndTerms()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            var owner = new Window { Width = 640, Height = 720 };
            var wizard = new ToleranceWizardWindow(app.Prescription) { Width = 640 };
            owner.Show();
            try
            {
                var settings = wizard.ShowDialog<ToleranceWizardSettingsDto>(owner);
                var irregularity = Field<CheckBox>(wizard, "_irregularityEnabled");
                irregularity.IsChecked = true;
                Field<CheckBox>(wizard, "_zernikeEnabled").IsChecked = true;
                Assert.False(irregularity.IsChecked);
                Field<ComboBox>(wizard, "_zernikeKind").SelectedIndex = 1;
                Assert.False(Field<NumericUpDown>(wizard, "_testWavelength").IsEnabled);
                Field<NumericUpDown>(wizard, "_teziMagnitude").Value = .002m;
                Field<NumericUpDown>(wizard, "_zernikeMaximumTerm").Value = 85;
                Field<NumericUpDown>(wizard, "_zernikeMinimumTerm").Value = 21;
                var confirm = wizard.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "确定"));
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(settings.IsCompletedSuccessfully);
                var rows = app.Tolerancing.GenerateWizard(settings.Result);
                Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.StandardZernikeIrregularity
                    && row.Maximum == .002 && row.Minimum == -.002 && row.TestWavelengthMicrometers is null
                    && row.MinimumZernikeTerm == 21 && row.MaximumZernikeTerm == 85);
                Assert.DoesNotContain(rows, row => row.Kind == ToleranceOperandKind.FringeIrregularity);
                Assert.True(app.Tolerancing.ValidateOperands(rows).IsValid);
            }
            finally { wizard.Close(); owner.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FringeEditorAppliesWavelengthAndRoundTripsVersionFour()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new TolerancingPanel(app.Documents, app.Prescription, app.Tolerancing, app.Events);
            var window = new Window { Width = 840, Height = 820, Content = panel };
            try
            {
                window.Show();
                var grid = Field<DataGrid>(panel, "_operandGrid");
                var row = Assert.IsType<ToleranceOperandEditorRow>(grid.SelectedItem);
                row.Code = "TIRR";
                row.SurfaceNumber = 1;
                var wavelength = Field<NumericUpDown>(panel, "_testWavelength");
                Assert.True(wavelength.IsEffectivelyVisible);
                Assert.Equal(.6328m, wavelength.Value);
                wavelength.Value = .5461m;
                var apply = panel.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "应用到当前行"));
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(.5461, row.ToDto().TestWavelengthMicrometers);
                Assert.Contains("0.5461", panel.BuildToleranceReportText());

                var study = panel.BuildStudyFile() with
                {
                    AdditionalCompensators = [new(1, ToleranceCompensatorKind.ElementDecenterX, -.05, .05, 2)]
                };
                Assert.Equal(4, study.SchemaVersion);
                var json = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
                var restored = panel.ParseStudyFile(JsonSerializer.Serialize(study, json));
                Assert.Equal(study.Operands, restored.Operands);
                Assert.Equal(study.AdditionalCompensators, restored.AdditionalCompensators);
                Assert.Throws<InvalidDataException>(() =>
                    panel.ParseStudyFile(JsonSerializer.Serialize(study with { SchemaVersion = 3 }, json)));

                CaptureFrame(window, "fringe-editor-light-840.png");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SurfaceFormEditorAppliesTermsAndIsoUnitsAndRoundTripsVersionFive()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new TolerancingPanel(app.Documents, app.Prescription, app.Tolerancing, app.Events);
            var window = new Window { Width = 940, Height = 820, Content = panel };
            try
            {
                window.Show();
                var grid = Field<DataGrid>(panel, "_operandGrid");
                var row = Assert.IsType<ToleranceOperandEditorRow>(grid.SelectedItem);
                var minimumTerm = Field<NumericUpDown>(panel, "_minimumTerm");
                var maximumTerm = Field<NumericUpDown>(panel, "_maximumTerm");
                var wavelength = Field<NumericUpDown>(panel, "_testWavelength");
                var apply = panel.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "应用到当前行"));

                row.Code = "TEZI";
                row.SurfaceNumber = 1;
                Assert.True(minimumTerm.IsEffectivelyVisible);
                Assert.False(wavelength.IsEffectivelyVisible);
                minimumTerm.Value = 4;
                maximumTerm.Value = 231;
                Field<NumericUpDown>(panel, "_maximum").Value = .001m;
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(4, row.MinimumZernikeTerm);
                Assert.Equal(231, row.MaximumZernikeTerm);
                Assert.Equal(-.001, row.Minimum);

                row.Code = "ISOA";
                Assert.False(minimumTerm.IsEffectivelyVisible);
                Assert.False(wavelength.IsEffectivelyVisible);
                Field<ComboBox>(panel, "_isoUnitPicker").SelectedIndex = (int)IsoSurfaceFormUnit.Fringes;
                Assert.True(wavelength.IsEffectivelyVisible);
                Assert.False(Field<ComboBox>(panel, "_distributionPicker").IsEnabled);
                wavelength.Value = .5461m;
                Field<NumericUpDown>(panel, "_maximum").Value = 2;
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(IsoSurfaceFormUnit.Fringes, row.ToDto().IsoUnit);
                Assert.Equal(.5461, row.ToDto().TestWavelengthMicrometers);
                Assert.Equal(-2, row.Minimum);
                Assert.Contains("P–V (fringes)", panel.BuildToleranceReportText());

                var study = panel.BuildStudyFile();
                Assert.Equal(5, study.SchemaVersion);
                var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
                Assert.Equal(study.Operands, panel.ParseStudyFile(JsonSerializer.Serialize(study, options)).Operands);
                Assert.Throws<InvalidDataException>(() =>
                    panel.ParseStudyFile(JsonSerializer.Serialize(study with { SchemaVersion = 4 }, options)));
                row.Code = "TRAD";
                var pivot = new ToleranceCompensatorDto(1, ToleranceCompensatorKind.ElementTiltY, -.05, .05, 2, new(1, -2, 5));
                typeof(TolerancingPanel).GetField("_additionalCompensators", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(panel, new[] { pivot });
                var pivotStudy = panel.BuildStudyFile();
                Assert.Equal(5, pivotStudy.SchemaVersion);
                Assert.Equal(pivot, Assert.Single(panel.ParseStudyFile(JsonSerializer.Serialize(pivotStudy, options)).AdditionalCompensators!));
                Assert.Throws<InvalidDataException>(() =>
                    panel.ParseStudyFile(JsonSerializer.Serialize(pivotStudy with { SchemaVersion = 4 }, options)));
                var staged = pivot with { Stage = 2 };
                typeof(TolerancingPanel).GetField("_additionalCompensators", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(panel, new[] { staged });
                var stagedStudy = panel.BuildStudyFile();
                Assert.Equal(6, stagedStudy.SchemaVersion);
                Assert.Equal(staged, Assert.Single(panel.ParseStudyFile(JsonSerializer.Serialize(stagedStudy, options)).AdditionalCompensators!));
                Assert.Throws<InvalidDataException>(() =>
                    panel.ParseStudyFile(JsonSerializer.Serialize(stagedStudy with { SchemaVersion = 5 }, options)));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task IsoIrregularityEditorPersistsUnitsAndItsIndependentStatistics()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new TolerancingPanel(app.Documents, app.Prescription, app.Tolerancing, app.Events);
            var window = new Window { Width = 940, Height = 820, Content = panel };
            try
            {
                window.Show();
                var row = Assert.IsType<ToleranceOperandEditorRow>(Field<DataGrid>(panel, "_operandGrid").SelectedItem);
                row.Code = "ISOC";
                row.SurfaceNumber = 1;
                var statistics = Field<ComboBox>(panel, "_isoStatisticsPicker");
                Assert.True(statistics.IsEffectivelyVisible);
                Assert.False(Field<ComboBox>(panel, "_distributionPicker").IsEffectivelyVisible);
                Assert.False(Field<NumericUpDown>(panel, "_minimumTerm").IsEffectivelyVisible);
                statistics.SelectedIndex = (int)IsoSurfaceFormStatistics.TruncatedGaussian;
                Field<ComboBox>(panel, "_isoUnitPicker").SelectedIndex = (int)IsoSurfaceFormUnit.Fringes;
                Field<NumericUpDown>(panel, "_testWavelength").Value = .5461m;
                Field<NumericUpDown>(panel, "_maximum").Value = 2;
                var apply = panel.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "应用到当前行"));
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(IsoSurfaceFormStatistics.TruncatedGaussian, row.IsoStatistics);
                Assert.Equal(-2, row.Minimum);
                Assert.Equal(IsoSurfaceFormUnit.Fringes, row.IsoUnit);
                Assert.Equal(.5461, row.TestWavelengthMicrometers);
                Assert.Contains("截断正态", panel.BuildToleranceReportText());
                Assert.Contains("P–V (fringes)", panel.BuildToleranceReportText());
                row.Code = "ISOD";
                Assert.Contains("RMS (fringes)", panel.BuildToleranceReportText());

                row.Code = "ISOC";
                var rows = Field<System.Collections.ObjectModel.ObservableCollection<ToleranceOperandEditorRow>>(panel, "_operands");
                rows.Add(new(new ToleranceOperandDto(2, true, ToleranceOperandKind.IsoIrregularityPv, 1, -1000, 1000)));
                Assert.Contains("ISOB + ISOC 联合标定表面：1", panel.BuildToleranceReportText());

                var study = panel.BuildStudyFile();
                Assert.Equal(5, study.SchemaVersion);
                var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
                var json = JsonSerializer.Serialize(study, options);
                Assert.Equal(study.Operands, panel.ParseStudyFile(json).Operands);
                Assert.Throws<InvalidDataException>(() =>
                    panel.ParseStudyFile(JsonSerializer.Serialize(study with { SchemaVersion = 4 }, options)));
                var withoutStatistics = JsonNode.Parse(json)!;
                foreach (var operand in withoutStatistics["Operands"]!.AsArray())
                    operand!.AsObject().Remove("IsoStatistics");
                Assert.All(panel.ParseStudyFile(withoutStatistics.ToJsonString()).Operands,
                    operand => Assert.Equal(IsoSurfaceFormStatistics.AtMaximum, operand.IsoStatistics));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReportPublishesJointYieldIndividualFieldsAndBoundedCompensation()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfTolerancingPanelTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            var result = app.Tolerancing.RunAsync(new(2, 0, 0, 2, 42, 1,
                [new(1, true, ToleranceOperandKind.Thickness, 2, -.01, .01)], ToleranceCriterion.Mtf, .1,
                MaxDegreeOfParallelism: 1, MtfSettings: new(10, Method: ToleranceMtfMethod.Geometric, FieldLimits: [new(2, 1)]),
                AdditionalCompensators: [new(app.Prescription.GetSurfaces().Count - 2, ToleranceCompensatorKind.Thickness, -.05, .05)])).GetAwaiter().GetResult();
            using var panel = new TolerancingPanel(app.Documents, app.Prescription, app.Tolerancing, app.Events);
            panel.GetType().GetField("_lastResult", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, result);
            var report = panel.BuildToleranceReportText();
            Assert.Contains("cycles/mm", report); Assert.Contains("视场 2", report);
            Assert.Contains("0.0%", report); Assert.Contains("不合格", report); Assert.Contains("绝对范围", report);
            Assert.Contains("逐视场良率不能相乘", report); Assert.Contains("补偿前视场", report); Assert.Contains("补偿后视场", report);
            Field<ComboBox>(panel, "_criterion").SelectedIndex = (int)ToleranceCriterion.Mtf;
            Assert.Null(panel.GetType().GetField("_lastResult", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel));
            Assert.Contains("尚未运行", panel.BuildToleranceReportText());
            panel.GetType().GetField("_lastResult", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, result);
            var operands = Field<System.Collections.ObjectModel.ObservableCollection<ToleranceOperandEditorRow>>(panel, "_operands");
            operands[0].Comment = "制造扰动已修改";
            Assert.Null(panel.GetType().GetField("_lastResult", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel));
        }, CancellationToken.None);
    }

    private static void CaptureFrame(Window window, string name, string? directory = null)
    {
        directory ??= Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MTF_TOLERANCE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
