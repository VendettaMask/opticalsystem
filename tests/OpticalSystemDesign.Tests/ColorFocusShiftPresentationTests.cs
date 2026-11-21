using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ColorFocusShiftPresentationTests
{
    [Theory]
    [InlineData(false, UiLocalization.Chinese)]
    [InlineData(true, UiLocalization.Chinese)]
    [InlineData(false, UiLocalization.English)]
    [InlineData(true, UiLocalization.English)]
    [InlineData(false, UiLocalization.Japanese)]
    [InlineData(true, UiLocalization.Japanese)]
    public async Task RuntimePreservesPhysicalUnitsAndFooterUsesUnroundedTypedResults(bool afocal, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.Apply(language);
            var optic = afocal ? Riflescope() : Optic.CreateCookeTriplet();
            var runtime = new WorkbenchRuntime(optic);
            var parameter = runtime.GetAnalysisParameters("Color Focus Shift").Single(p => p.Key == "MaximumShift");
            Assert.Contains(afocal ? "(D)" : "(µm)", parameter.DisplayName);
            Assert.Equal(afocal ? 0.01 : 0.1, parameter.Increment);
            var view = WorkbenchMapper.ToAnalysisViewDto(runtime.BuildAnalysisView("Color Focus Shift",
                new Dictionary<string, string> { ["MaximumShift"] = "0.25", ["PupilZone"] = "0" }))
                with { PresentationKind = AnalysisPresentationKind.ColorFocusShift };
            var summary = Assert.IsType<ColorFocusShiftSummaryDto>(view.ColorFocusShiftSummary);
            Assert.Equal(afocal ? AnalysisAxisUnit.Diopter : AnalysisAxisUnit.Micrometer, summary.Unit);
            Assert.Equal(summary.Unit, Assert.Single(view.Series).XUnit);
            Assert.Equal(-0.25, view.PlotOptions.XMinimum);
            Assert.Equal(0.25, view.PlotOptions.XMaximum);
            Assert.Equal(afocal, summary.DiffractionLimitChange is null);
            if (afocal)
            {
                Assert.InRange(summary.MaximumChange, 0.15847046, 0.15847048);
                Assert.Contains("最大焦移变化（D）", view.ReportText);
            }
            var text = Caption(view);
            Assert.Contains(AnalysisAxisFormatting.FormatValue(summary.MaximumChange, summary.Unit, CultureInfo.InvariantCulture), text);
            Assert.Equal(!afocal, text.Contains(UiLocalization.Text("衍射极限变化"), StringComparison.Ordinal));
            if (afocal) Assert.DoesNotContain("µm", text);
            Assert.Contains(UiLocalization.Text("光瞳区域") + (language == UiLocalization.English ? ": " : "：") + "0.0000", text);
            if (language == UiLocalization.English)
                Assert.Equal(afocal ? "Focal shift (D)" : "Focal shift (µm)", UiLocalization.Text(
                    AnalysisAxisFormatting.FormatLabel(view.Series[0].XAxisLabel, view.Series[0].XQuantity, view.Series[0].XUnit)));
            // Localized or rounded table rows must never determine the physical caption.
            var replacedRows = view with { Rows = [new("最大焦移变化", "999999"), new("光瞳区域", "invalid")] };
            Assert.Equal(text, Caption(replacedRows));
        }, CancellationToken.None);
    }

    private static string Caption(AnalysisViewDto view)
    {
        var footer = (Border)typeof(AnalysisPanel).GetMethod("BuildAnalysisTitleBlock", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [view, new OpticalDocumentSnapshot("test", "test.ZMX", 0, "", false, false, 0, 0, 0, 0, 17, 3, 3),
                new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero)])!;
        return footer.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Single(t => t?.StartsWith(UiLocalization.Text("最大焦移变化"), StringComparison.Ordinal) == true)!;
    }

    private static Optic Riflescope()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples", "Afocal", "Afocal Riflescope.ZMX");
            if (File.Exists(path)) return OpticalFormatCatalog.Import(File.ReadAllText(path), ".zmx");
        }
        throw new FileNotFoundException("Bundled Afocal Riflescope.ZMX not found.");
    }
}
