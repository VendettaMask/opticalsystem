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
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MTF_TOLERANCE_CAPTURE_DIR"));
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
                AdditionalCompensators = [new(1, ToleranceCompensatorKind.ElementTiltY, -.05, .05, 2, new(1, -2, 5))]
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

    private static void CaptureFrame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MTF_TOLERANCE_CAPTURE_DIR");
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
