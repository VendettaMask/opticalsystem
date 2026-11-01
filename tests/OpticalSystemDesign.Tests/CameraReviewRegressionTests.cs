using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CameraReviewRegressionTests
{
    [Fact]
    public async Task InvalidTextClearsBRetainsAAndCannotRunUntilCorrected()
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create(); var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); window.UpdateLayout(); SmallSize(window);
                await window.RunAsync(); window.PinComparison(); var a = window.Comparison;
                Assert.NotNull(a);
                var exposure = Find<NumericUpDown>(window, "camera-exposure");
                var run = Find<Button>(window, "camera-run");
                foreach (var text in new[] { "not-a-number", "10001", "" })
                {
                    exposure.Text = text; window.UpdateLayout();
                    Assert.Null(window.Result); Assert.Same(a, window.Comparison); Assert.False(run.IsEnabled);
                    Assert.False(Find<Button>(window, "camera-save-experiment").IsEnabled);
                    Assert.NotNull(AutomationProperties.GetHelpText(run));
                    await window.RunAsync(); Assert.Null(window.Result); Assert.Same(a, window.Comparison);
                    exposure.Text = "10"; window.UpdateLayout(); Assert.True(run.IsEnabled);
                    await window.RunAsync(); Assert.Equal(10, window.Result!.Request.Sensor!.ExposureMilliseconds);
                }
                exposure.GetVisualDescendants().OfType<TextBox>().First().Focus(); exposure.Text = "invalid";
                Find<CheckBox>(window, "camera-noise").Focus(); window.UpdateLayout();
                Assert.Equal("invalid", exposure.Text); Assert.False(run.IsEnabled);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("camera-bits", 12.5)]
    [InlineData("camera-black", 64.5)]
    [InlineData("camera-seed", 42.5)]
    public async Task FractionalIntegersAreRejectedInsteadOfTruncated(string name, double number)
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create(); var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); window.UpdateLayout(); SmallSize(window);
                Find<NumericUpDown>(window, name).Value = (decimal)number;
                Assert.False(Find<Button>(window, "camera-run").IsEnabled);
                await window.RunAsync(); Assert.Null(window.Result);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task DecimalCultureAndImportedPrecisionRemainValid()
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create(); var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); window.UpdateLayout(); SmallSize(window);
                var exposure = Find<NumericUpDown>(window, "camera-exposure");
                exposure.NumberFormat = CultureInfo.GetCultureInfo("de-DE").NumberFormat;
                exposure.Text = "12,5";
                Assert.True(Find<Button>(window, "camera-run").IsEnabled);
                await window.RunAsync(); Assert.Equal(12.5, window.Result!.Request.Sensor!.ExposureMilliseconds);
                var qe = Find<NumericUpDown>(window, "camera-qe-r"); qe.Value = .123456789012345678901m;
                qe.Text = qe.Value.Value.ToString(qe.FormatString, qe.NumberFormat);
                Assert.True(Find<Button>(window, "camera-run").IsEnabled);
                await window.RunAsync(); Assert.Equal((double)qe.Value.Value, window.Result!.Request.Sensor!.RedQe);
                var source = Find<ComboBox>(window, "camera-source");
                source.SelectedIndex = (int)CameraSource.Scene3D;
                Find<ComboBox>(window, "camera-aberration").SelectedIndex = (int)CameraAberration.Diffraction;
                Find<NumericUpDown>(window, "camera-scene3d-guard").Text = "invalid";
                Assert.False(Find<Button>(window, "camera-run").IsEnabled);
                await window.RunAsync(); Assert.Null(window.Result);
                source.SelectedIndex = 0; window.UpdateLayout();
                Assert.True(Find<Button>(window, "camera-run").IsEnabled);
                await window.RunAsync(); Assert.NotNull(window.Result);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ExperimentAndCalibrationControlsAndErrorsSwitchAllThreeLanguages()
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(() =>
        {
            using var app = WorkbenchApplication.Create(); var window = new CameraImagingWindow(app);
            var original = UiLocalization.CurrentLanguage;
            try
            {
                window.Show(); window.UpdateLayout();
                foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese, UiLocalization.Chinese })
                {
                    UiLocalization.Apply(language); window.UpdateLayout();
                    foreach (var (name, chinese, english, japanese) in new[]
                    {
                        ("camera-save-experiment", "保存完整实验", "Save Complete Experiment", "完全な実験を保存"),
                        ("camera-open-experiment", "打开实验", "Open Experiment", "実験を開く"),
                        ("camera-recompute-experiment", "复算 A/B", "Recompute A/B", "A/Bを再計算"),
                        ("camera-calibrate-sensor", "暗场／平场标定", "Dark / Flat Calibration", "暗・フラット校正")
                    }) Assert.Equal(language == UiLocalization.English ? english : language == UiLocalization.Japanese ? japanese : chinese,
                        Find<Button>(window, name).Content);
                    Assert.Equal(UiLocalization.Text("请先完成仿真，再保存完整实验。"), AutomationProperties.GetHelpText(Find<Button>(window, "camera-save-experiment")));
                    Assert.Equal(UiLocalization.Text("实验保存失败：") + "source · 42.dat", UiLocalization.Text("实验保存失败：source · 42.dat"));
                    if (language != UiLocalization.Chinese)
                        Assert.DoesNotContain("请输入", UiLocalization.Text("仿真未完成：请输入有效数值。"));
                }
                return Task.FromResult(true);
            }
            finally { UiLocalization.Apply(original); window.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void SmallSize(CameraImagingWindow window)
    { var size = Find<ComboBox>(window, "camera-size"); size.SelectedIndex = size.ItemCount - 1; }
}
