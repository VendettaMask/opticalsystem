using System.Globalization;
using System.Reflection;
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
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MaterialConstantsTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Fact]
    public void CalculateUsesActualDispersionInsteadOfRoundedReferenceAndDoesNotEditTheProject()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Documents.GetRecoveryState();
        var revision = app.Events.Revision;
        var reference = app.Materials.FindGlass("APEL:APL5014CL")!;
        var referenceJson = JsonSerializer.Serialize(reference);
        var result = app.Materials.CalculateGlassConstants("APEL:APL5014CL")!;
        Assert.Equal(GlassConstantsCalculationStatus.Available, result.Status);
        Assert.Equal(1.544501600284279, result.RefractiveIndexD!.Value, 14);
        Assert.Equal(55.98699089030721, result.AbbeNumber!.Value, 10);
        Assert.Equal(1.5445, reference.CatalogReferenceIndexD);
        Assert.Equal(56, reference.CatalogReferenceAbbeNumber);
        Assert.NotEqual(reference.RefractiveIndexD, result.RefractiveIndexD);
        Assert.NotEqual(reference.AbbeNumber, result.AbbeNumber);
        Assert.Equal((result.RefractiveIndexD.Value - 1) / (result.RefractiveIndexF!.Value - result.RefractiveIndexC!.Value), result.AbbeNumber);
        Assert.Equal(referenceJson, JsonSerializer.Serialize(app.Materials.FindGlass("APEL:APL5014CL")));
        Assert.Equal(before, app.Documents.GetRecoveryState());
        Assert.Equal(revision, app.Events.Revision);
        Assert.Null(app.Materials.CalculateGlassConstants("MISSING:NO-SUCH-GLASS"));
    }

    [Fact]
    public void CoefficientChangeAffectsCalculatedConstantsWithoutUsingCatalogReferenceValues()
    {
        var first = new CatalogGlassMaterial("fixture", "fixture", "zemax formula 1", 400, 700,
            [2.25, .01, .02, 0, 0, 0], zemaxData: new() { ReferenceIndexD = 9, ReferenceAbbeNumber = 999 });
        var second = new CatalogGlassMaterial("fixture", "fixture", "zemax formula 1", 400, 700,
            [2.5, .01, .02, 0, 0, 0], zemaxData: first.ZemaxData);
        var a = CatalogGlassConstants.Calculate(first);
        var b = CatalogGlassConstants.Calculate(second);
        Assert.Equal(GlassConstantsStatus.Available, a.Status);
        Assert.Equal(GlassConstantsStatus.Available, b.Status);
        Assert.NotEqual(a.IndexD, b.IndexD);
        Assert.NotEqual(a.AbbeNumber, b.AbbeNumber);
        Assert.NotEqual(9, a.IndexD);
        Assert.NotEqual(999, a.AbbeNumber);
    }

    [Fact]
    public void TabulatedConstantsUseActualSamplesAndOutOfTableRequestsAreRejected()
    {
        var wavelengths = new[] { CatalogGlassConstants.WavelengthF, CatalogGlassConstants.WavelengthD, CatalogGlassConstants.WavelengthC };
        var glass = new CatalogGlassMaterial("table", "fixture", "tabulated n", 400, 700,
            refractiveIndexWavelengthsNanometers: wavelengths, refractiveIndices: [1.52, 1.51, 1.50]);
        var result = CatalogGlassConstants.Calculate(glass);
        Assert.Equal(1.51, result.IndexD);
        Assert.Equal(25.5, result.AbbeNumber!.Value, 11);
        var narrowTable = new CatalogGlassMaterial("table", "fixture", "tabulated n", 400, 700,
            refractiveIndexWavelengthsNanometers: [550, 600], refractiveIndices: [1.52, 1.51]);
        Assert.Equal(GlassConstantsStatus.WavelengthOutOfRange, CatalogGlassConstants.Calculate(narrowTable).Status);
    }

    [Fact]
    public void InvalidUnsupportedAndZeroDispersionHaveExplicitStatuses()
    {
        var infrared = new CatalogGlassMaterial("ir", "fixture", "zemax formula 1", 1000, 2000, [2.25, 0, 0, 0, 0, 0]);
        var invalid = new CatalogGlassMaterial("bad", "fixture", "zemax formula 1", 400, 700, [-1, 0, 0, 0, 0, 0]);
        var missing = new CatalogGlassMaterial("bad", "fixture", "zemax formula 1", 400, 700, [2.25]);
        var unsupported = new CatalogGlassMaterial("bad", "fixture", "unmapped model", 400, 700);
        var constant = new CatalogGlassMaterial("constant", "fixture", "zemax formula 1", 400, 700, [2.25, 0, 0, 0, 0, 0]);
        Assert.Equal(GlassConstantsStatus.WavelengthOutOfRange, CatalogGlassConstants.Calculate(infrared).Status);
        Assert.Equal(GlassConstantsStatus.InvalidIndex, CatalogGlassConstants.Calculate(invalid).Status);
        Assert.Equal(GlassConstantsStatus.InvalidIndex, CatalogGlassConstants.Calculate(missing).Status);
        Assert.Equal(GlassConstantsStatus.UnsupportedModel, CatalogGlassConstants.Calculate(unsupported).Status);
        var result = CatalogGlassConstants.Calculate(constant);
        Assert.Equal(GlassConstantsStatus.UndefinedAbbeNumber, result.Status);
        Assert.Equal(1.5, result.IndexD);
        Assert.Null(result.AbbeNumber);
    }

    [Theory]
    [InlineData(1, "A0,A1,A2,A3,A4,A5")]
    [InlineData(2, "K1,L1,K2,L2,K3,L3")]
    [InlineData(3, "A,B,C,D,E,F")]
    [InlineData(4, "A,B1,λ1,B2,λ2")]
    [InlineData(5, "n0,A,B")]
    [InlineData(6, "K1,L1,K2,L2,K3,L3,K4,L4")]
    [InlineData(7, "A,B,C,D")]
    [InlineData(8, "A,B,C,D")]
    [InlineData(9, "A,B,C,D,E")]
    [InlineData(10, "A0,A1,A2,A3,A4,A5,A6,A7")]
    [InlineData(11, "K1,L1,K2,L2,K3,L3,K4,L4,K5,L5")]
    [InlineData(12, "A0,A1,A2,A3,A4,A5,A6,A7")]
    [InlineData(13, "A0,A1,A2,A3,A4,A5,A6,A7,A8")]
    public void ImportedModelsUseTheirOwnSymbolsAndPreserveCoefficientOrder(int model, string symbols)
    {
        using var app = WorkbenchApplication.Create();
        var labels = symbols.Split(',');
        var values = Enumerable.Range(1, labels.Length).Select(i => i + .125).ToArray();
        var fixture = app.Materials.FindGlass("SCHOTT:N-BK7")! with
        { Formula = $"zemax formula {model}", ZemaxFormulaNumber = model, DispersionCoefficients = values.Concat(Enumerable.Repeat(0d, 10 - labels.Length)).ToArray() };
        var displayed = MaterialLibraryPanel.DisplayCoefficients(fixture);
        Assert.Equal(labels, displayed.Take(labels.Length).Select(item => item.Label));
        Assert.Equal(fixture.DispersionCoefficients, displayed.Select(item => item.Value));
        Assert.All(displayed.Skip(labels.Length), item => Assert.EndsWith("*", item.Label));
        Assert.All(displayed, item => Assert.Contains(item.Label + "：", MaterialLibraryPanel.BuildGlassReport(fixture)));
    }

    [Fact]
    public void GeneralModelsRetainConstantOffsetsPowersAndNonzeroExtraSourceCoefficients()
    {
        using var app = WorkbenchApplication.Create();
        var basis = app.Materials.FindGlass("SCHOTT:N-BK7")!;
        var sellmeier = basis with { Formula = "formula 2", ZemaxFormulaNumber = null, DispersionCoefficients = [.1, 1.2, .01, .3, .02] };
        Assert.Equal(new[] { "C0", "B1", "C1", "B2", "C2" }, MaterialLibraryPanel.DisplayCoefficients(sellmeier).Select(c => c.Label));
        var polynomial = basis with { Formula = "formula 5", ZemaxFormulaNumber = null, DispersionCoefficients = [1.5, .02, -2, .003, 4] };
        var terms = MaterialLibraryPanel.DisplayCoefficients(polynomial);
        Assert.Equal(new[] { "C0", "C1 (λ^-2)", "C2 (λ^4)" }, terms.Select(c => c.Label));
        Assert.Equal(new[] { 1.5, .02, .003 }, terms.Select(c => c.Value));
        var extra = basis with { Formula = "zemax formula 5", DispersionCoefficients = [1.5, .01, .002, 0, 123] };
        Assert.Equal(("CD4*", 123d), MaterialLibraryPanel.DisplayCoefficients(extra)[^1]);
    }

    [Theory]
    [InlineData("Light", "zh-CN", 1400, 960, 1)]
    [InlineData("Dark", "en-US", 1100, 960, 1.5)]
    [InlineData("Isekai", "en-US", 900, 1100, 2)]
    public async Task ButtonRecalculatesAndSelectionRestoresReferenceWithModelSpecificLabels(string theme, string language, int width, int height, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MaterialConstantsTests));
        await session.Dispatch(() =>
        {
            DisplayTypography.Configure(new() { Theme = theme });
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create("cooke");
            var before = app.Documents.GetRecoveryState(); var revision = app.Events.Revision;
            var panel = new MaterialLibraryPanel(app.Materials);
            var window = new Window { Width = width, Height = height, Content = panel };
            try
            {
                window.Show(); window.SetRenderScaling(scale); Tick(window); UiLocalization.Attach(window);
                var family = Private<ComboBox>(panel, "_catalog"); var glasses = Private<ListBox>(panel, "_glassList");
                family.SelectedItem = "APEL"; glasses.SelectedItem = "APL5014CL"; Tick(window);
                var calculate = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CalculateGlassConstants");
                var nd = Private<TextBlock>(panel, "_nd"); var vd = Private<TextBlock>(panel, "_vd");
                Assert.Equal("1.544500", nd.Text); Assert.Equal("56.000", vd.Text);
                Assert.Equal("A0：", Private<TextBlock[]>(panel, "_coefficientLabels")[0].Text);
                calculate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal("1.544502", nd.Text); Assert.Equal("55.987", vd.Text);
                var source = Private<TextBlock>(panel, "_constantsSource");
                Assert.Contains("1.544500", source.Text); Assert.Contains("56.000", source.Text);
                var original = app.Materials.FindGlass("APEL:APL5014CL")!;
                var report = MaterialLibraryPanel.BuildGlassReport(original, app.Materials.CalculateGlassConstants("APEL:APL5014CL"));
                Assert.Contains("Nd：1.544502", report); Assert.Contains("Vd：55.987", report);
                Assert.Contains("目录参考 Nd：1.544500；Vd：56.000", report);
                Capture(window, $"material-constants-{theme}.png");
                family.SelectedItem = "SCHOTT"; glasses.SelectedItem = "N-BK7"; Tick(window);
                Assert.Equal("K1：", Private<TextBlock[]>(panel, "_coefficientLabels")[0].Text);
                Assert.Equal("L1：", Private<TextBlock[]>(panel, "_coefficientLabels")[1].Text);
                family.SelectedItem = "APEL"; glasses.SelectedItem = "APL5014CL"; Tick(window);
                Assert.Equal("1.544500", nd.Text); Assert.Equal("56.000", vd.Text);
                var infrared = app.Materials.GetGlasses().First(g => g.MinimumWavelengthMicrometers > .7);
                family.SelectedItem = infrared.Manufacturer; glasses.SelectedItem = infrared.Name; Tick(window);
                calculate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal("—", nd.Text); Assert.Equal("—", vd.Text);
                Assert.Equal(GlassConstantsCalculationStatus.WavelengthOutOfRange,
                    Private<GlassConstantsDto>(panel, "_calculatedConstants").Status);
                Assert.NotEqual(UiLocalization.Text("Nd/Vd 已按 Fraunhofer F-d-C 谱线计算"), Private<TextBlock>(panel, "_selectionSummary").Text);
                if (theme == "Isekai") Assert.DoesNotMatch(@"[\u4e00-\u9fff]", source.Text!);
                Private<TextBox>(panel, "_search").Text = "no-such-material"; Tick(window);
                Assert.False(calculate.IsEnabled); Assert.Equal("—", nd.Text); Assert.Equal("—", vd.Text);
                Assert.Equal(before, app.Documents.GetRecoveryState()); Assert.Equal(revision, app.Events.Revision);
            }
            finally { window.Close(); UiLocalization.Apply("zh-CN"); DisplayTypography.Configure(new()); }
        }, CancellationToken.None);
    }

    private static T Private<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Capture(Window window, string file)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_MATERIAL_CONSTANTS_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, file), PngBitmapEncoderOptions.Default);
    }
}
