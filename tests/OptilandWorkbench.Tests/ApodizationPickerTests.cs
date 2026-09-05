using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ApodizationPickerTests
{
    private static readonly string[] Keys = ["均匀（Zemax）", "高斯（Zemax）", "余弦立方（Zemax）"];
    private static readonly string[] Labels = ["均匀", "高斯", "余弦立方"];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PickerShowsOnlyThreePlainNamesAndEditsZemaxFactor(int selection)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            app.Prescription.UpdateSystemSettings(app.Prescription.GetSystemSettings() with
            {
                ApodizationKind = Keys[selection],
                FirstApodizationParameter = 2.75
            });
            using var panel = new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events);
            var window = new Window { Width = 440, Height = 650, Content = panel };
            try
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var picker = Field<ComboBox>(panel, "_apodizationPicker");
                Assert.Equal(Keys, picker.Items.Cast<string>());
                Assert.Equal(selection, picker.SelectedIndex);
                Assert.Equal(Labels, picker.Items.Cast<string>().Select(key =>
                    Assert.IsType<TextBlock>(picker.ItemTemplate!.Build(key)).Text));
                Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == Labels[selection]);

                var factor = Field<NumericUpDown>(panel, "_firstApodizationParameter");
                Assert.True(factor.IsEffectivelyVisible);
                Assert.Equal("因子", Field<TextBlock>(panel, "_firstApodizationLabel").Text);
                Assert.False(Field<NumericUpDown>(panel, "_secondApodizationParameter").IsVisible);
                Assert.Equal(2.75m, factor.Value);
                factor.Value = 0.375m;
                Apply(panel);
                var saved = app.Prescription.GetSystemSettings();
                Assert.Equal(Keys[selection], saved.ApodizationKind);
                Assert.Equal(0.375, saved.FirstApodizationParameter);
                panel.RefreshDisplaySettings();
                Assert.Equal(selection, picker.SelectedIndex);
                Assert.Equal(0.375m, factor.Value);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("无", 1, 1)]
    [InlineData("均匀", 1, 1)]
    [InlineData("高斯", 0.7, 1)]
    [InlineData("余弦平方", 0.8, 1)]
    [InlineData("Hann", 1.8, 1)]
    [InlineData("多项式", 0.8, 3)]
    [InlineData("超高斯", 0.7, 4)]
    [InlineData("Tukey", 0.9, 0.25)]
    public async Task LegacyModelsAreNotOfferedAndUnrelatedEditsPreserveTheirSettings(
        string kind, double first, double second)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            app.Prescription.UpdateSystemSettings(app.Prescription.GetSystemSettings() with
            {
                ApodizationKind = kind,
                FirstApodizationParameter = first,
                SecondApodizationParameter = second
            });
            var before = app.Prescription.GetSystemSettings();
            using var panel = new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events);
            var picker = Field<ComboBox>(panel, "_apodizationPicker");
            Assert.Equal(Keys, picker.Items.Cast<string>());
            Assert.Null(picker.SelectedItem);
            Assert.Equal(kind is "无" or "均匀" ? "均匀" : "旧版设置（保留）", picker.PlaceholderText);
            Assert.False(Field<NumericUpDown>(panel, "_firstApodizationParameter").IsVisible);

            Field<NumericUpDown>(panel, "_apertureValue").Value = (decimal)before.ApertureValue + 1;
            Apply(panel);
            var after = app.Prescription.GetSystemSettings();
            Assert.Equal(before.ApertureValue + 1, after.ApertureValue);
            Assert.Equal(before.ApodizationKind, after.ApodizationKind);
            Assert.Equal(before.FirstApodizationParameter, after.FirstApodizationParameter);
            Assert.Equal(before.SecondApodizationParameter, after.SecondApodizationParameter);
            panel.RefreshDisplaySettings();
            Assert.Null(picker.SelectedItem);

            // An explicit choice replaces the legacy model. Gaussian now means a Zemax factor, not sigma.
            picker.SelectedIndex = 1;
            Apply(panel);
            var converted = app.Prescription.GetSystemSettings();
            Assert.Equal("高斯（Zemax）", converted.ApodizationKind);
            Assert.Equal(1, converted.FirstApodizationParameter);
            Assert.True(Field<NumericUpDown>(panel, "_firstApodizationParameter").IsVisible);
            Assert.Null(ToolTip.GetTip(picker));
        }, CancellationToken.None);
    }

    private static T Field<T>(SystemPropertiesPanel panel, string name) where T : class =>
        Assert.IsType<T>(typeof(SystemPropertiesPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(panel));

    private static void Apply(SystemPropertiesPanel panel) =>
        typeof(SystemPropertiesPanel).GetMethod("ApplySystemControls", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(panel, null);
}
