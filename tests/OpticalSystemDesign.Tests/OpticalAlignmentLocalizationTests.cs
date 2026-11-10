using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class OpticalAlignmentLocalizationTests
{
    [Theory]
    [InlineData("Light", 1)]
    [InlineData("Dark", 1.5)]
    [InlineData("Isekai", 2)]
    public async Task ActualAlignmentWindowTranslatesParametersRowsAndImagesWithoutRecalculation(string theme, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { Theme = theme });
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.English);
            using var app = WorkbenchApplication.Create("cooke");
            var window = new OpticalAssemblyWindow(app) { Width = 1560, Height = 950 };
            try
            {
                window.Show(); window.SetRenderScaling(scaling);
                await window.CalculateAsync(); Tick(window);
                Assert.NotNull(window.Result); Assert.NotNull(window.Preview);
                foreach (var section in window.GetLogicalDescendants().OfType<Expander>()) section.IsExpanded = true;
                Tick(window);
                AssertEnglish(window);
                var grid = Field<DataGrid>(window, "_grid");
                var modes = Field<ComboBox>(window, "_probeMode");
                modes.IsDropDownOpen = true; Tick(window); AssertEnglish(window); modes.IsDropDownOpen = false;
                var result = window.Result; var image = window.Preview; var selected = grid.SelectedItem;
                var revision = app.Events.Revision;
                var prescription = app.Documents.GetRecoveryState();
                if (theme != "Isekai")
                {
                    UiLocalization.Apply(UiLocalization.Chinese); Tick(window);
                    Assert.Contains("顶点像", Rows(grid).Select(row => Property(row, "Kind")));
                    Assert.Contains("近轴倍率", Field<TextBlock>(window, "_imageInfo").Text);
                    UiLocalization.Apply(UiLocalization.Japanese); Tick(window);
                    Assert.Contains("頂点像", Rows(grid).Select(row => Property(row, "Kind")));
                    UiLocalization.Apply(UiLocalization.English); Tick(window); AssertEnglish(window);
                }
                else
                {
                    UiLocalization.Apply(UiLocalization.Chinese); Tick(window); AssertEnglish(window);
                    Assert.Equal(UiLocalization.English, UiLocalization.CurrentLanguage);
                }
                Assert.Same(result, window.Result); Assert.Same(image, window.Preview);
                Assert.Same(selected, grid.SelectedItem);
                Assert.Contains("Vertex image", Rows(grid).Select(row => Property(row, "Kind")));
                Assert.Contains("Center-of-curvature image", Rows(grid).Select(row => Property(row, "Kind")));
                Capture(window, theme);
                modes.SelectedIndex = 1; Tick(window); AssertEnglish(window);
                Assert.Null(window.Preview); Assert.Same(result, window.Result);
                await window.SimulateAsync(); Tick(window); AssertEnglish(window);
                Assert.NotNull(window.Preview);
                Assert.Contains("not focused", Field<TextBlock>(window, "_imageInfo").Text);
                Field<NumericUpDown>(window, "_distance").Value = .1m;
                await window.SimulateAsync(); Tick(window); AssertEnglish(window);
                Assert.Contains("enter the lens group", Field<TextBlock>(window, "_imageInfo").Text);
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal(prescription, app.Documents.GetRecoveryState());
                // A user-supplied name can be an exact UI keyword and must still remain data.
                Field<TextBlock>(window, "_source").Text = "保存"; Tick(window);
                Assert.Equal("保存", Field<TextBlock>(window, "_source").Text);
                app.Documents.NewBlank(); await window.CalculateAsync(); Tick(window); AssertEnglish(window);
            }
            finally
            {
                window.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
                UiLocalization.Apply(UiLocalization.Chinese); DisplayTypography.Configure(new AppSettings());
            }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    private static IEnumerable<object> Rows(DataGrid grid) => ((IEnumerable)grid.ItemsSource!).Cast<object>();
    private static string Property(object row, string name) => (string)row.GetType().GetProperty(name)!.GetValue(row)!;
    private static T Field<T>(object window, string name) => (T)window.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    private static bool HasChinese(string text) => text.Any(c => c is >= '\u4e00' and <= '\u9fff');
    private static void AssertEnglish(Window window)
    {
        var controls = window.GetVisualDescendants().OfType<Control>()
            .Concat(window.GetLogicalDescendants().OfType<Control>()).Distinct().ToArray();
        var strings = controls.SelectMany(control => new[]
        {
            control is TextBlock text ? text.Text : null,
            ToolTip.GetTip(control) as string, AutomationProperties.GetName(control), AutomationProperties.GetHelpText(control)
        }).OfType<string>().Concat([window.Title ?? ""])
            .Concat(Rows(Field<DataGrid>(window, "_grid")).SelectMany(row => new[] { Property(row, "Kind"), Property(row, "Message") }));
        var data = Rows(Field<DataGrid>(window, "_grid")).Select(row => Property(row, "Label"))
            .Append(Field<TextBlock>(window, "_source").Text ?? "").ToHashSet();
        var remaining = strings.Where(text => !data.Contains(text) && HasChinese(text)).Distinct().ToArray();
        Assert.True(remaining.Length == 0, "Untranslated alignment UI:\n" + string.Join("\n", remaining));
    }
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Capture(Window window, string theme)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_ALIGNMENT_LANGUAGE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory) || theme != "Light") return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        image.Save(Path.Combine(directory, "alignment-english.png"), PngBitmapEncoderOptions.Default);
    }
}
