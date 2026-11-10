using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PresentationLocalizationTests
{
    [Fact]
    public void EveryRegisteredAnalysisParameterAndChoiceHasEnglishPresentationWithoutChangingItsIdentity()
    {
        using var app = WorkbenchApplication.Create("cooke");
        using var language = UiLocalization.UseLanguage(UiLocalization.English);
        var missing = app.Analyses.AnalysisNames.SelectMany(name => app.Analyses.GetParameters(name)
            .SelectMany(parameter => new[] { parameter.DisplayName }.Concat(parameter.Choices?.Select(value =>
                parameter.ChoiceLabels?.GetValueOrDefault(value) ?? value) ?? [])))
            .Distinct().Where(source => Cjk(UiLocalization.Text(source))).ToArray();
        Assert.True(missing.Length == 0, string.Join("\n", missing));
    }

    [Fact]
    public void InterpolatedTemplatesRetainOpaqueArgumentsFormatsAndEscapedBraces()
    {
        using var language = UiLocalization.UseLanguage(UiLocalization.English);
        Assert.Equal("Scale: 1.250", UiLocalization.Template($"比例：{1.25:0.000}"));
        Assert.Equal("{1.250}", UiLocalization.Template($"{{{1.25:0.000}}}"));
        Assert.Equal("Edge: 材料", UiLocalization.Template($"边缘：{ "材料" }"));
        Assert.Equal("1.25 / 1.250", UiLocalization.Template(FormattableStringFactory.Create("{0:0.00} / {0:0.000}", 1.25)));
        Assert.DoesNotContain("主波长", UiLocalization.Text("0.55 µm\n主波长"));
    }

    [Fact]
    public async Task LoadedGridAndChoiceBindingsRoundTripWithoutChangingDataOrSelection()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.Chinese);
            var item = new Row("材料", "光源");
            var grid = new DataGrid { ItemsSource = new[] { item }, AutoGenerateColumns = false, Height = 180 };
            grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(Row.Name)) });
            grid.Columns.Add(new DataGridTextColumn { Header = "角色", Binding = UiLocalization.TranslateBinding(new Binding(nameof(Row.Role))) });
            var choices = new[] { "正面", "背面" };
            var combo = new ComboBox { ItemsSource = choices, SelectedIndex = 1 };
            UiLocalization.BindChoices(combo);
            var window = new Window { Width = 700, Height = 400, Content = new StackPanel { Children = { grid, combo } } };
            try
            {
                window.Show(); Tick(window);
                foreach (var language in new[] { UiLocalization.English, UiLocalization.Japanese, UiLocalization.Chinese, UiLocalization.English })
                {
                    UiLocalization.Apply(language); Tick(window);
                    Assert.Equal(UiLocalization.Text("角色"), grid.Columns[1].Header);
                    Assert.Contains(UiLocalization.Text("背面"), Text(window));
                    Assert.Contains(UiLocalization.Text("光源"), Text(window));
                    Assert.Contains("材料", Text(window));
                    Assert.Same(choices, combo.ItemsSource); Assert.Equal(1, combo.SelectedIndex);
                    Assert.Equal("光源", item.Role); Assert.Equal("材料", item.Name);
                }
                grid.Columns.Add(new DataGridTextColumn { Header = "说明", Binding = new Binding(nameof(Row.Name)) });
                Tick(window); Assert.Equal(UiLocalization.Text("说明"), grid.Columns[^1].Header);
            }
            finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OutputLanguageScopeDoesNotRetargetExistingUiOrFollowLaterUiChanges()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.English);
            var label = new TextBlock { Text = "保存" };
            var window = new Window { Content = label };
            UiLocalization.Attach(window);
            using (UiLocalization.UseLanguage(UiLocalization.Chinese))
            {
                var outputLabel = new TextBlock { Text = "保存" };
                var output = new Window { Content = outputLabel };
                UiLocalization.Attach(output); UiLocalization.Attach(window);
                Assert.Equal("Save", label.Text); Assert.Equal("保存", outputLabel.Text);
                UiLocalization.Apply(UiLocalization.Japanese);
                Assert.Equal("保存", outputLabel.Text); Assert.Equal("保存", label.Text);
                UiLocalization.Apply(UiLocalization.English);
                Assert.Equal("Save", label.Text); Assert.Equal("保存", outputLabel.Text);
                output.Close();
            }
            window.Close(); UiLocalization.Apply(UiLocalization.Chinese);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Isekai")]
    public async Task WorkspaceAndDrawingDefaultsFollowLanguageWhileEditedFieldsRemainData(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.English);
            using var app = WorkbenchApplication.Create("cooke");
            var before = app.Documents.GetRecoveryState();
            var factory = new WorkspaceDockFactory(app, new() { Theme = theme });
            var root = factory.CreateLayout(); factory.InitLayout(root);
            using var drawing = new OpticalDrawingPanel(app.Prescription, app.Materials, app.Events, app.Visualization);
            using var system = new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events);
            var window = new Window { Width = 1400, Height = 1000, Content = new Grid { ColumnDefinitions = new("*,*"), Children = { drawing, system } } };
            Grid.SetColumn(system, 1);
            try
            {
                window.Show(); Tick(window);
                Assert.All(WorkspaceDockFactory.EnumerateDockables(root), dock => Assert.False(Cjk(dock.Title), dock.Title));
                Assert.DoesNotContain(Text(system), Cjk);
                Assert.DoesNotContain(Text(drawing), Cjk);
                Field<ComboBox>(drawing, "_elementPicker").SelectedIndex = 1; Tick(window);
                Assert.DoesNotContain(Text(drawing), Cjk);
                if (theme == "Light" && Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_LOCALIZATION_EVIDENCE") is { } evidence)
                {
                    Directory.CreateDirectory(evidence);
                    var sheet = (OpticalDrawingSheet)drawing.GetType().GetMethod("CreateSheet", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawing, null)!;
                    File.WriteAllBytes(Path.Combine(evidence, "drawing-english.png"), OpticalDrawingRenderer.RenderPreview(sheet));
                    OpticalDrawingRenderer.ExportPdf(Path.Combine(evidence, "drawing-english.pdf"), sheet);
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(evidence, "drawing-workspace-english.png"), PngBitmapEncoderOptions.Default);
                }
                var part = Field<TextBox>(drawing, "_partName");
                var edge = Field<TextBox>(drawing, "_edgeTreatment");
                part.Text = "用户零件：材料";
                UiLocalization.Apply(UiLocalization.Chinese); Tick(window);
                Assert.Equal("用户零件：材料", part.Text);
                if (theme != "Isekai") Assert.Equal("倒边 0.2 × 45°；不允许崩边进入净口径", edge.Text);
                UiLocalization.Apply(UiLocalization.English); Tick(window);
                Assert.False(Cjk(edge.Text), edge.Text);
                Assert.All(WorkspaceDockFactory.EnumerateDockables(root), dock => Assert.False(Cjk(dock.Title), dock.Title));
                Field<ComboBox>(drawing, "_standardPicker").SelectedIndex = 1; Tick(window);
                Assert.True(!Text(drawing).Any(Cjk), string.Join("\n", Text(drawing).Where(Cjk)));
                Assert.DoesNotContain(drawing.GetVisualDescendants().OfType<TextBox>().Where(input => input.IsEffectivelyVisible && input != part), input => Cjk(input.Text));
                var legacySheet = (OpticalDrawingSheet)drawing.GetType().GetMethod("CreateSheet", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawing, null)!;
                Assert.True(OpticalDrawingRenderer.RenderPreview(legacySheet, 800).Length > 1000);
                Assert.Equal(before, app.Documents.GetRecoveryState());
            }
            finally { window.Close(); factory.DisposeContent(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RealLaboratoriesDatabaseAndEditorsBindVisiblePresentationOnly()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.English);
            using var app = WorkbenchApplication.Create("cooke");
            var before = app.Documents.GetRecoveryState();
            await Inspect(new CameraImagingWindow(app));
            await Inspect(new global::OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow());
            await Inspect(new LensLibraryPanel(app.Lenses));
            await Inspect(new ManufacturabilityPanel(app.Prescription, app.Events));
            await Inspect(new OptimizationPanel(app.Prescription, app.Optimization, app.Events));
            await Inspect(new MarkdownReportWindow(app, new() { Language = UiLocalization.English }));
            Assert.Equal(before, app.Documents.GetRecoveryState());
            Assert.All(app.Optimization.GetMeritOperandTypes().SelectMany(type => type.Parameters ?? []),
                parameter => Assert.False(Cjk(UiLocalization.Text(parameter.DisplayName)), parameter.DisplayName));
            using var nsc = WorkbenchApplication.Create(); nsc.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            if (nsc.NonSequential.GetDocument().Objects.Count == 0)
            { nsc.NonSequential.AddObject(NonSequentialObjectKind.SourcePoint); nsc.NonSequential.AddObject(NonSequentialObjectKind.DetectorRectangle); }
            var names = nsc.NonSequential.GetDocument().Objects.Select(item => item.Name).ToArray();
            await Inspect(new NonSequentialObjectEditorPanel(nsc.NonSequential, nsc.Events), names);
            await Inspect(new NonSequentialModePanel(nsc.NonSequential, nsc.Events), names);
            Assert.Equal(names, nsc.NonSequential.GetDocument().Objects.Select(item => item.Name));
            foreach (var standard in Enum.GetValues<OpticalDrawingStandard>())
            {
                var template = OpticalDrawingTemplateCatalog.For(standard);
                Assert.All(template.Specification.Columns.Select(column => column.Title)
                    .Concat(template.Specification.Gb1991?.AllRows.Select(row => row.Item) ?? []),
                    value => Assert.False(Cjk(UiLocalization.Text(value)), value));
            }
            UiLocalization.Apply(UiLocalization.Chinese);

            async Task Inspect(Control content, string[]? dataNames = null)
            {
                var window = content as Window ?? new Window { Width = 1500, Height = 1000, Content = content };
                try
                {
                    window.Show(); Tick(window); await Task.Delay(100); Tick(window);
                    foreach (var expander in window.GetVisualDescendants().OfType<Expander>().ToArray()) expander.IsExpanded = true;
                    await Task.Delay(100); Tick(window);
                    if (content is global::OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow)
                        Assert.Contains(window.GetVisualDescendants().OfType<Control>(), control => control.Name == "CoatingLayerCard");
                    foreach (var language in new[] { UiLocalization.English, UiLocalization.Chinese, UiLocalization.English })
                    {
                        UiLocalization.Apply(language); Tick(window);
                        if (language == UiLocalization.English)
                        {
                            var unexpected = Text(window).Where(value => Cjk(value) && !(dataNames?.Contains(value) ?? false));
                            Assert.True(!unexpected.Any(), content.GetType().Name + "\n" + string.Join("\n", unexpected));
                        }
                    }
                }
                finally { window.Close(); (content as IDisposable)?.Dispose(); }
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Spot Diagram")]
    [InlineData("Field Curvature and Distortion")]
    [InlineData("Wavefront Map")]
    [InlineData("Interferogram")]
    [InlineData("PSF")]
    [InlineData("MTF")]
    [InlineData("RMS vs Field")]
    [InlineData("Encircled Energy")]
    [InlineData("Relative Illumination")]
    public async Task RealAnalysisCaptionsParametersMetricsAndTextRoundTripWithoutRecalculation(string key)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.English);
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                new() { Theme = "Light", Language = UiLocalization.English }, key);
            var window = new Window { Width = 1500, Height = 1050, Content = panel };
            try
            {
                Assert.True(await panel.RunForGuiCaptureAsync());
                var view = Field<AnalysisViewDto>(panel, "_view");
                var revision = app.Events.Revision;
                window.Show(); Tick(window);
                Assert.True(!Cjk(UiLocalization.AnalysisReport(view.ReportText)), UiLocalization.AnalysisReport(view.ReportText));
                Assert.True(!Text(panel).Any(Cjk), string.Join("\n", Text(panel).Where(Cjk)));
                Assert.All(view.Rows, row => Assert.False(Cjk(UiLocalization.AnalysisMetric(row.Metric)), row.Metric));
                foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English })
                {
                    UiLocalization.Apply(language); Tick(window);
                    Assert.Same(view, Field<AnalysisViewDto>(panel, "_view"));
                    Assert.Equal(revision, app.Events.Revision);
                }
                Assert.True(!Text(panel).Any(Cjk), string.Join("\n", Text(panel).Where(Cjk)));
                if (key == "Wavefront Map" && Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_LOCALIZATION_EVIDENCE") is { } evidence)
                {
                    Directory.CreateDirectory(evidence);
                    using var plot = panel.CapturePlotImage(1);
                    AnalysisPanel.SavePlotImage(plot, Path.Combine(evidence, "wavefront-english.png"));
                }
                var report = panel.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.IsReadOnly && box.AcceptsReturn);
                if (report is not null) Assert.False(Cjk(report.Text), report.Text);
            }
            finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CachedDrawingDefaultsNeverBorrowAnotherSelectedElementsText()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.Apply(UiLocalization.Chinese);
            var input = new TextBox();
            UiLocalization.SetDefaultText(input, "N-BK7 光学元件");
            var oldDisplay = input.Text!;
            UiLocalization.SetDefaultText(input, "N-F2 光学元件");
            UiLocalization.Apply(UiLocalization.English);
            Assert.Equal(oldDisplay, UiLocalization.DisplayedDefault(input, oldDisplay));
            Assert.Equal("N-F2 Optical Element", UiLocalization.DisplayedDefault(input, "N-F2 光学元件"));
            input.Text = "用户零件";
            Assert.Equal("N-F2 光学元件", UiLocalization.DisplayedDefault(input, "N-F2 光学元件"));
            UiLocalization.Apply(UiLocalization.Chinese);
        }, CancellationToken.None);
    }

    internal static bool Cjk(string? source) => source is not null && Regex.IsMatch(source, "[\\u3400-\\u9fff]");
    internal static string[] Text(Control root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(block => block.IsEffectivelyVisible).Select(block => block.Text ?? "").ToArray();
    internal static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    internal static void Tick(Window window) { window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); }
    private sealed record Row(string Name, string Role);
}
