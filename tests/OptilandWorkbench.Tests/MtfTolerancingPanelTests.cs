using System.Reflection;
using System.Text.Json;
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
public sealed class MtfTolerancingPanelTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_MTF_TOLERANCE_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
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
                AdditionalCompensators = [new(2, ToleranceCompensatorKind.Thickness, -.05, .05)]
            };
            var window = new TolerancingRunWindow(options, app.Prescription.GetFields(), app.Prescription.GetSurfaces(), app.Prescription.GetWavelengths()) { Width = width, Height = 760 };
            try
            {
                window.Show(); Tick(); var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
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
                Assert.Single(draft.AdditionalCompensators!);
                Assert.Contains("accent", window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "运行")).Classes);
                Assert.DoesNotContain("accent", window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "取消")).Classes);

                void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                void Capture(string name)
                {
                    Tick(); var directory = Environment.GetEnvironmentVariable("OPTILAND_MTF_TOLERANCE_CAPTURE_DIR");
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

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
