using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

public sealed class PolarizationAnalysisIntegrationTests
{
    internal static Optic Plate()
    {
        var optic = new Optic("Polarization plate");
        optic.Fields.Add(new FieldPoint()); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 2;
        var glass = new ConstantIndexMaterial("glass", 1.5);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, Thickness = 2, MaterialAfter = glass, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { Thickness = 5, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { SemiDiameter = 30, SemiDiameterFixed = true }]);
        return optic;
    }
    internal static Dictionary<string, string> SmallSettings() => new()
    { ["GridSize"] = "5", ["FanPoints"] = "9", ["FieldNumber"] = "1", ["WavelengthNumber"] = "1", ["UseRayAiming"] = "false" };

    [Theory]
    [InlineData(PolarizationAnalysisKind.RayTrace)]
    [InlineData(PolarizationAnalysisKind.PupilMap)]
    [InlineData(PolarizationAnalysisKind.Transmission)]
    [InlineData(PolarizationAnalysisKind.PhaseAberration)]
    [InlineData(PolarizationAnalysisKind.TransmissionFan)]
    public void EveryMenuAnalysisRunsItsOwnPhysicalResultWithoutChangingThePrescription(PolarizationAnalysisKind kind)
    {
        var optic = Plate(); var runtime = new WorkbenchRuntime(optic);
        var json = new System.Text.Json.JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var snapshot = System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(), json);
        var key = PolarizationAnalysis.Key(kind); var data = runtime.BuildAnalysisData(key, SmallSettings());
        Assert.Equal(key, data.Name); Assert.Equal(AnalysisOutcome.Success, data.Outcome); Assert.NotEmpty(data.Table!.Rows);
        Assert.Equal(snapshot, System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(), json));
        if (kind is PolarizationAnalysisKind.PhaseAberration or PolarizationAnalysisKind.TransmissionFan)
        { Assert.Equal(2, data.PlotPanes!.Count); Assert.All(data.PlotPanes, p => Assert.Equal(9, p.Series[0].Points.Count)); }
        if (kind == PolarizationAnalysisKind.PupilMap) Assert.Equal(14, data.PlotSeries.Count);
        Assert.True(WorkbenchAnalysisCatalog.TryGetDescriptor(key, out var descriptor)); Assert.NotNull(descriptor.RibbonCommand);
        Assert.Contains(runtime.GetAnalysisParameters(key), p => p.Key == "UseSystemPolarization");
    }
    [Fact]
    public void PupilIntegrationKeepsClippedRaysAndSurfacePowerMatchesIndependentFresnel()
    {
        var optic = Plate(); var settings = new PolarizationAnalysisSettings(GridSize: 5, UseRayAiming: false);
        var data = new PolarizationAnalysis(optic, PolarizationAnalysisKind.Transmission, settings).GenerateData();
        var rows = data.Table!.Rows;
        Assert.Equal(96, D(rows[0][5]), 10); Assert.Equal(92.16, D(rows[^1][3]), 10);
        Assert.Equal(96, D(rows[1][4]), 10); Assert.Equal(92.16, D(rows[^1][5]), 10);
        optic.SurfaceGroup.Items[2].PhysicalAperture = new CircularAperture(.5);
        var clipped = new PolarizationAnalysis(optic, PolarizationAnalysisKind.Transmission, settings).GenerateData();
        Assert.Equal(92.16 * 5 / 13, D(clipped.Table!.Rows[^1][3]), 9);
        Assert.Equal("5", clipped.Table.Rows[^1][6]);
        optic.SurfaceGroup.Items[2].PhysicalAperture = null;
        optic.Fields[0].VignetteFactorX = .2; optic.Fields[0].VignetteFactorY = .3;
        var compressed = new PolarizationAnalysis(optic, PolarizationAnalysisKind.Transmission, settings).GenerateData();
        Assert.Equal(92.16 * .8 * .7, D(compressed.Table!.Rows[^1][3]), 9);
        optic.Fields[0].VignetteFactorX = 0; optic.Fields[0].VignetteFactorY = 0;
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("bulk glass", 1.5, 1e-6);
        var absorbing = new PolarizationAnalysis(optic, PolarizationAnalysisKind.Transmission, settings).GenerateData();
        Assert.Equal(92.16 * Math.Exp(-4 * Math.PI * 1e-6 * 2e6 / 550), D(absorbing.Table!.Rows[^1][3]), 8);
    }
    [Fact]
    public void CoherentCoatingsUseTheFormalComplexTraceAndLocalOverridesKeepSystemInput()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film", 2.1), 87)]);
        optic.Polarization = new(false, 1, 0);
        var settings = SmallSettings(); settings["UseSystemPolarization"] = "false";
        settings["Jx"] = "1"; settings["Jy"] = "1"; settings["YPhase"] = "90";
        var runtime = new WorkbenchRuntime(optic); var map = runtime.BuildAnalysisData("Polarization Pupil Map", settings);
        var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, .55, false).Rays.Single();
        var expected = optic.SequentialRayTracer.TracePolarized(source, new(1, 1, 0, 90), includePropagationPhase: false);
        var chief = map.Table!.Rows.Single(r => r[0] == "0" && r[1] == "0");
        Assert.Equal(expected.PolarizedIntensity, D(chief[5]), 10);
        Assert.Equal(-90, D(chief[6]), 9); Assert.Equal(new SystemPolarization(false, 1, 0), optic.Polarization);
        var phases = runtime.BuildAnalysisData("Polarization Phase Aberration", settings);
        Assert.All(phases.PlotPanes!.SelectMany(p => p.Series).SelectMany(s => s.Points), p => Assert.InRange(Math.Abs(p.Y), 0, 1e-10));
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("bulk glass", 1.5, 1e-6);
        var coatedAbsorbing = runtime.BuildAnalysisData("Polarization Pupil Map", settings);
        var absorbingChief = coatedAbsorbing.Table!.Rows.Single(r => r[0] == "0" && r[1] == "0");
        Assert.Equal(D(chief[5]) * Math.Exp(-4 * Math.PI * 1e-6 * 2e6 / 550), D(absorbingChief[5]), 9);
    }
    [Fact]
    public void ZeroComponentsStayUndefinedAndCancellationAndAllFieldWaveSelectionsWork()
    {
        var optic = Plate(); optic.Fields.Add(new FieldPoint { Y = 5 }); optic.Wavelengths.Add(new Wavelength { Nanometers = 650, IsPrimary = false });
        var runtime = new WorkbenchRuntime(optic); var settings = SmallSettings(); settings["FieldNumber"] = "0"; settings["WavelengthNumber"] = "0";
        var all = runtime.BuildAnalysisData("Polarization Transmission", settings);
        Assert.Equal(12, all.Table!.Rows.Count); Assert.Equal(2, all.Values["FieldCount"]); Assert.Equal(2, all.Values["WavelengthCount"]);
        var phase = runtime.BuildAnalysisData("Polarization Phase Aberration", SmallSettings());
        Assert.All(phase.PlotPanes!.SelectMany(p => p.Series).Where(s => s.Name == "Ey").SelectMany(s => s.Points), p => Assert.True(double.IsNaN(p.Y)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => runtime.BuildAnalysisData("Polarization Pupil Map", SmallSettings(), cancel.Token));
    }
    private static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PolarizationAnalysisWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();
    [Theory]
    [InlineData("Light", "zh-CN")]
    [InlineData("Dark", "ja-JP")]
    [InlineData("Isekai", "en-US")]
    [InlineData("Lumen", "en-US")]
    [InlineData("Pixel", "zh-CN")]
    public async Task RealMenuCommandsOpenCalculateAndRenderTheFiveAnalyses(string theme, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PolarizationAnalysisWindowTests));
        await session.Dispatch(async () =>
        {
            var previous = UiLocalization.CurrentLanguage;
            var previousDirectory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            using var files = new CameraSensorFileTests.TemporaryFiles();
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", files.Path("settings"));
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create("cooke");
            var menu = WorkbenchAnalysisCatalog.RibbonMenus.Single(m => m.Group == "偏振");
            var invoked = new List<string>(); var actions = new ActionManager();
            foreach (var command in WorkbenchAnalysisCatalog.RibbonCommands)
                actions.Register(command.Id, command.Label, command.Group, () => invoked.Add(command.Id));
            var button = MainWindow.BuildAnalysisMenuButton(menu, actions);
            button.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            button.Margin = new Thickness(8);
            var window = new Window { Width = 380, Height = 360, Content = button };
            try
            {
                window.Show(); Tick(window);
                var flyout = Assert.IsType<MenuFlyout>(button.Flyout); flyout.ShowAt(button); Tick(window); Capture(window, theme + "-menu"); flyout.Hide();
                window.Width = 1200; window.Height = 900;
                foreach (var kind in Enum.GetValues<PolarizationAnalysisKind>())
                {
                    var descriptor = WorkbenchAnalysisCatalog.Descriptors.Single(d => d.CanonicalKey == PolarizationAnalysis.Key(kind));
                    flyout.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "ribbon-" + descriptor.RibbonCommand!.Id)
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.Equal(descriptor.RibbonCommand!.Id, invoked[^1]);
                    using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                        new AppSettings(), descriptor.DisplayName, initialSettings: PolarizationAnalysisIntegrationTests.SmallSettings());
                    var run = panel.RunForGuiCaptureAsync(); window.Content = panel; Tick(window);
                    var succeeded = await run.WaitAsync(TimeSpan.FromSeconds(30)); Tick(window);
                    if (!succeeded) Capture(window, theme + "-failure-" + kind);
                    Assert.True(succeeded, string.Join(" | ", panel.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
                    var view = (Dto.AnalysisViewDto)typeof(AnalysisPanel).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
                    Assert.Equal(Dto.AnalysisOutcome.Success, view.Outcome); Assert.NotEmpty(view.Table!.Rows);
                    Capture(window, theme + "-" + kind);
                    var toggle = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Content is LocalIconLabel label
                        && label.Children.OfType<LocalIcon>().Any(i => i.IconName == "settings-general"));
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                    var system = panel.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "analysis-parameter-UseSystemPolarization");
                    var jx = panel.GetVisualDescendants().OfType<NumericUpDown>().Single(c => AutomationProperties.GetAutomationId(c) == "analysis-parameter-Jx");
                    Assert.False(jx.IsEnabled); system.IsChecked = false; Assert.True(jx.IsEnabled);
                    if (kind == PolarizationAnalysisKind.PupilMap)
                    {
                        panel.GetVisualDescendants().OfType<NumericUpDown>().Single(c => AutomationProperties.GetAutomationId(c) == "analysis-parameter-Jy").Value = 1;
                        panel.GetVisualDescendants().OfType<NumericUpDown>().Single(c => AutomationProperties.GetAutomationId(c) == "analysis-parameter-YPhase").Value = 90;
                        Assert.True(await panel.RunForGuiCaptureAsync()); Tick(window);
                        Capture(window, theme + "-settings");
                        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                        Capture(window, theme + "-circular-pupil");
                        continue;
                    }
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
            finally
            {
                window.Close(); Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previousDirectory);
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); UiLocalization.Apply(previous);
            }
        }, CancellationToken.None);
    }
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Capture(Window window, string name)
    {
        var directory = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), ".tmp", "polarization-analyses"); Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame(); image!.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
