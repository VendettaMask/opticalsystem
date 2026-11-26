using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Dock.Model.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SequentialExampleWorkspaceTests
{
    [Fact]
    public async Task AllPackagedPrescriptionsOpenTheirOwnSavedWindows()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            using var panels = new PanelManager(app, new AppSettings(), new WorkspaceSessionStore(Path.GetTempPath()));
            await panels.InitializeAsync();
            var count = 0;
            foreach (var example in SequentialExampleCatalog.Examples)
            {
                var sample = Assert.IsType<SequentialExampleWorkspace>(SequentialExampleWorkspace.ForDocument(example.FilePath));
                var expected = Windows(sample.Layout).ToArray();
                Assert.Single(expected, window => window.Kind == WorkspaceDocumentTypes.LensEditor);
                await app.Documents.OpenAsync(example.FilePath);
                Dispatcher.UIThread.RunJobs();
                var actual = panels.Factory.Descriptors;
                Assert.True(expected.Length == actual.Count, example.RelativePath);
                Assert.Equal(expected.Length, panels.Factory.OpenDocuments().Count());
                foreach (var w in expected.Where(w => w.Id.HasValue))
                {
                    var descriptor = Assert.Single(actual, d => d.InstanceId == w.Id);
                    if (descriptor.TypeId == WorkspaceDocumentTypes.Analysis)
                        Assert.Contains(w.Kind, app.Analyses.AnalysisNames.Select(app.Analyses.CanonicalKey));
                    if (w.Settings?.GetValueOrDefault("FrontSurface") is { } surface)
                    {
                        var drawing = Assert.IsType<OpticalDrawingPanel>(panels.Factory.OpenDocuments().Single(d => d.Id == descriptor.Id).Context);
                        Assert.Contains(drawing.GetLogicalDescendants().OfType<ComboBox>(),
                            picker => picker.SelectedItem?.ToString()?.Contains($" S{surface}-S", StringComparison.Ordinal) == true);
                    }
                    if (w.Kind == "Image Simulation" && w.Settings?.GetValueOrDefault("FieldNumber") is { } field)
                    {
                        var image = Assert.IsType<AnalysisPanel>(panels.Factory.OpenDocuments().Single(d => d.Id == descriptor.Id).Context);
                        Assert.StartsWith(field + " - ", image.Settings["FieldNumber"]);
                    }
                }
                count += actual.Count;
            }
            Assert.Equal(95, SequentialExampleCatalog.Examples.Count);
            Assert.Equal(464, count);
            app.Documents.NewBlank();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, panels.Factory.Descriptors.Count);
            Assert.Null(SequentialExampleWorkspace.ForDocument(Path.Combine(Path.GetTempPath(), "Zoom lens.zmx")));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TiltedImagePlaneRestoresDistinctImageWindowsAndTheRecordedDockGroups()
    {
        var example = SequentialExampleCatalog.Examples.Single(x => x.Name.Contains("tilted image plane", StringComparison.Ordinal));
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(example.FilePath);
            var userSettings = new AppSettings();
            var userReportSettings = new Dictionary<string, string> { ["personal"] = "existing" };
            userSettings.AnalysisSettings["Prescription Report"] = userReportSettings;
            using var panels = new PanelManager(app, userSettings, new WorkspaceSessionStore(
                Path.Combine(Path.GetTempPath(), "example-analysis-layout-" + Guid.NewGuid().ToString("N"))));
            await panels.InitializeAsync();
            var documents = panels.Factory.OpenDocuments().ToArray();
            Assert.Equal(6, documents.Length);
            Assert.Equal(5, documents.Select(d => d.Owner).Distinct().Count());
            var images = panels.Factory.Descriptors.Where(d => d.AnalysisName == "Image Simulation")
                .OrderBy(d => d.Title).ToArray();
            Assert.Equal(2, images.Length);
            Assert.NotEqual(images[0].InstanceId, images[1].InstanceId);
            Assert.Equal("PSF 网格", images[0].Settings!["DisplayAs"]);
            Assert.Equal("仿真图", images[1].Settings!["DisplayAs"]);
            Assert.Equal("1280", images[1].Settings!["DetectorXPixels"]);
            Assert.Equal("10.0", images[1].Settings!["FieldHeight"]);
            Assert.All(documents.Where(d => images.Any(i => i.Id == d.Id)), d => Assert.IsType<AnalysisPanel>(d.Context));
            Assert.Contains(panels.Factory.Descriptors, d => d.AnalysisName == "Prescription Report");
            Assert.Contains(panels.Factory.Descriptors, d => d.AnalysisName == "RMS vs Field");
            Assert.Contains(panels.Factory.Descriptors, d => d.TypeId == WorkspaceDocumentTypes.Viewer3D);
            var report = documents.Select(d => d.Context).OfType<AnalysisPanel>().Single(p => p.AnalysisKey == "Prescription Report");
            Assert.True(await report.RunForGuiCaptureAsync());
            Assert.Same(userReportSettings, userSettings.AnalysisSettings["Prescription Report"]);

            panels.ShowAnalysis("Image Simulation");
            var third = Assert.Single(panels.Factory.Descriptors, d => d.Title == "图像模拟 3");
            var newPanel = panels.Factory.DocumentContent<AnalysisPanel>(third.Id)!;
            newPanel.IsLocked = true;
            await panels.SaveLayoutSlotAsync(1);
            await panels.LoadLayoutSlotAsync(1);
            foreach (var original in images)
            {
                var restored = panels.Factory.DocumentContent<AnalysisPanel>(original.Id)!;
                Assert.Equal(original.InstanceId, restored.InstanceId);
                Assert.Equal(original.Settings!["DisplayAs"], restored.Settings["DisplayAs"]);
                Assert.False(restored.IsLocked);
            }
            var restoredThird = panels.Factory.DocumentContent<AnalysisPanel>(third.Id)!;
            Assert.Equal(third.InstanceId, restoredThird.InstanceId);
            Assert.True(restoredThird.IsLocked);
            panels.Factory.CloseDockable(panels.Factory.OpenDocuments().Single(d => d.Id == third.Id));
            panels.ShowAnalysis("图像模拟");
            var reopened = Assert.Single(panels.Factory.Descriptors, d => d.Title == "图像模拟 3");
            Assert.NotEqual(third.InstanceId, reopened.InstanceId);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlueNotchSavedWindowsKeepTheSourceAndExtinguishOnlyTheBlueImageChannel(bool fullResolution)
    {
        var example = SequentialExampleCatalog.Examples.Single(x => x.Name.Contains("blue notch filter", StringComparison.OrdinalIgnoreCase));
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(example.FilePath);
        var windows = Windows(SequentialExampleWorkspace.ForDocument(example.FilePath)!.Layout)
            .Where(window => window.Kind == "Image Simulation").ToArray();
        Assert.Equal(2, windows.Length);
        foreach (var window in windows)
        {
            var settings = app.Analyses.MergeSettings(window.Kind, window.Descriptor([]).Settings);
            if (fullResolution) settings["PreviewReduction"] = "1";
            Assert.Equal("true", settings["UsePolarization"], ignoreCase: true);
            var run = await app.Analyses.RunAsync(new(window.Id!.Value, 1, window.Kind, settings));
            Assert.True(run.View.Outcome == OpticalSystemDesign.Application.Contracts.AnalysisOutcome.Success, run.View.OutcomeReason);
            var points = Assert.Single(Assert.Single(run.View.PlotPanes).Series).Points;
            if (fullResolution) Assert.Equal(306 * 306, points.Count);
            Assert.True(points.Max(p => p.Red) > .8 && points.Max(p => p.Green) > .8);
            if (settings["DisplayAs"] == "源位图") Assert.True(points.Max(p => p.Blue) > .8);
            else Assert.All(points, p => Assert.Equal(0d, p.Blue));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProjectCoatingUsesOneExposureForDifferentNonzeroRgbTransmissions(bool relativeIllumination)
    {
        var example = SequentialExampleCatalog.Examples.Single(x => x.Name.Contains("blue notch filter", StringComparison.OrdinalIgnoreCase));
        var source = Windows(SequentialExampleWorkspace.ForDocument(example.FilePath)!.Layout)
            .Single(w => w.Kind == "Image Simulation" && w.Settings?["DisplayAs"] == "仿真图");
        var folder = Directory.CreateTempSubdirectory("image-colour-transfer-");
        try
        {
            var lens = Path.Combine(folder.FullName, "three bands.zmx");
            File.WriteAllText(lens, File.ReadAllText(example.FilePath).Replace("COAT BLUENOTCH", "COAT ThreeBands", StringComparison.Ordinal));
            var coatings = folder.CreateSubdirectory("Coatings");
            File.WriteAllText(Path.Combine(coatings.FullName, "ThreeBands.coating.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    Format = "CoatingLab.PowerSpectrum", Version = 1, Name = "ThreeBands", AngleDegrees = 0, ZeroPhase = true,
                    Rows = new[] { (465, .25), (535, .5), (606, 1d) }
                        .Select(p => new { WavelengthNm = p.Item1, Rs = 0, Rp = 0, Ts = p.Item2, Tp = p.Item2 })
                }));
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(lens);
            var settings = app.Analyses.MergeSettings(source.Kind, source.Descriptor([]).Settings);
            settings["RelativeIllumination"] = relativeIllumination.ToString();
            var run = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, source.Kind, settings));
            Assert.True(run.View.Outcome == OpticalSystemDesign.Application.Contracts.AnalysisOutcome.Success, run.View.OutcomeReason);
            var points = Assert.Single(Assert.Single(run.View.PlotPanes).Series).Points;
            var red = points.Max(p => p.Red)!.Value;
            Assert.True(red > .8);
            // The bare rear interface and 2 mm of N-BK7 add a small chromatic loss.
            Assert.InRange(points.Max(p => p.Green)!.Value / red, .49, .51);
            Assert.InRange(points.Max(p => p.Blue)!.Value / red, .24, .26);
            if (!relativeIllumination)
            {
                // The central chief ray enters through the power table, travels 2 mm in
                // glass, and leaves through one bare glass-to-air interface at normal incidence.
                var glass = OpticalSystemDesign.Core.FileIO.OpticalFormatCatalog.Import(File.ReadAllText(lens), ".zmx")
                    .SurfaceGroup.Items[1].MaterialAfter;
                double Plate(double nm)
                {
                    var n = glass.RefractiveIndex(nm);
                    return 4 * n / Math.Pow(n + 1, 2) * Math.Exp(-4 * Math.PI * glass.ExtinctionCoefficient(nm) * 2e6 / nm);
                }
                Assert.Equal(.5 * Plate(535) / Plate(606), points.Max(p => p.Green)!.Value / red, 8);
                Assert.Equal(.25 * Plate(465) / Plate(606), points.Max(p => p.Blue)!.Value / red, 8);
            }
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public async Task TiltedImagePlaneProducesTwoIndependentRastersFromThePackagedInputs()
    {
        var example = SequentialExampleCatalog.Examples.Single(x => x.Name.Contains("tilted image plane", StringComparison.Ordinal));
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(example.FilePath);
        var sample = SequentialExampleWorkspace.ForDocument(example.FilePath)!;
        var ids = new HashSet<string>();
        foreach (var window in Windows(sample.Layout).Where(w => w.Kind == "Image Simulation"))
        {
            var descriptor = window.Descriptor(ids);
            var settings = app.Analyses.MergeSettings(window.Kind, descriptor.Settings);
            Assert.True(File.Exists(settings["SourceFile"]));
            var run = await app.Analyses.RunAsync(new(window.Id!.Value, 1, window.Kind, settings));
            Assert.True(run.View.Outcome == OpticalSystemDesign.Application.Contracts.AnalysisOutcome.Success, run.View.OutcomeReason);
            var pane = Assert.Single(run.View.PlotPanes);
            var points = Assert.Single(pane.Series).Points;
            if (settings["DisplayAs"] == "PSF 网格")
            {
                Assert.Equal("PSF Grid", pane.Title);
                Assert.Equal(7 * 32 * 7 * 32, points.Count);
            }
            else
            {
                Assert.Equal("Simulated Image (1/4)", pane.Title);
                Assert.Equal(320 * 240, points.Count);
                Assert.Equal("180°", settings["SourceRotation"]);
                // In the original building image the blue sky is above the facade.
                var skyAtTop = points.Count(p => p.Y >= 180 && p.Blue > p.Red + .08);
                var skyAtBottom = points.Count(p => p.Y < 60 && p.Blue > p.Red + .08);
                Assert.True(skyAtTop > 2 * skyAtBottom);
            }
            Assert.Contains(points, p => p.Red > .01);
        }
    }

    [Fact]
    public void PsfGridUsesTheSampledKernelsWithoutAnInputBitmap()
    {
        var config = new ImageSimulationConfig
        {
            PsfGridRows = 3, PsfGridColumns = 5, PsfSize = 8, NumRays = 8, ImageWidth = 64, ImageHeight = 48,
            WavelengthsMicrometers = [.55], AberrationMode = "None", DisplayAs = "PSF 网格"
        };
        var result = new ImageSimulationAnalysis(Optic.CreateBlank(), config).GenerateData();
        var pane = Assert.Single(result.PlotPanes!);
        Assert.Equal("PSF Grid", pane.Title);
        var points = Assert.Single(pane.Series).Points;
        Assert.Equal(3 * 5 * 8 * 8, points.Count);
        Assert.Equal(15, points.Count(p => p.Red == 1));
    }

    [Fact]
    public async Task SavedSurfaceCoatingWindowsCalculateFromThePackagedLabFiles()
    {
        using var app = WorkbenchApplication.Create();
        var count = 0;
        foreach (var example in SequentialExampleCatalog.Examples)
        {
            var windows = Windows(SequentialExampleWorkspace.ForDocument(example.FilePath)!.Layout)
                .Where(w => w.Settings?.ContainsKey("CoatingFile") == true).ToArray();
            if (windows.Length == 0) continue;
            await app.Documents.OpenAsync(example.FilePath);
            foreach (var w in windows)
            {
                var settings = app.Analyses.MergeSettings(w.Kind, w.Descriptor([]).Settings);
                var file = settings["CoatingFile"];
                Assert.True(File.Exists(file), file);
                if (Path.GetFileName(file) == "BLUENOTCH.coating.json")
                {
                    var table = CoatingDesign.Engine.CoatingResponseTable.Read(await File.ReadAllTextAsync(file))!;
                    Assert.Equal(0d, table.Power(465, 0, Core.Coatings.ThinFilmPolarization.S).Transmittance);
                    Assert.Equal(1d, table.Power(500, 0, Core.Coatings.ThinFilmPolarization.P).Transmittance);
                }
                else
                {
                    var design = await CoatingDesign.Engine.ExperimentStore.OpenAsync(file);
                    if (design.Name == "ETALON")
                    {
                        Assert.Equal(17, design.Layers.Length);
                        Assert.Equal(5_000_000, design.Layers[8].ThicknessNm);
                    }
                }
                var run = await app.Analyses.RunAsync(new(w.Id!.Value, 1, w.Kind, settings));
                Assert.True(run.View.Outcome == Application.Contracts.AnalysisOutcome.Success, $"{example.Name}: {run.View.OutcomeReason}");
                var series = run.View.Series;
                Assert.Equal(w.Kind == SurfaceCoatingAnalysis.Phase ? 2 : 3, series.Count);
                Assert.All(series, s => Assert.True(s.Points.Count >= 100));
                if (Path.GetFileName(file) == "GAP.coating.json")
                    Assert.Equal(1, series[0].Points.Last().Y, 9);
                count++;
            }
        }
        Assert.Equal(7, count);
    }

    private static IEnumerable<ExampleWorkspaceWindow> Windows(ExampleWorkspacePane pane) =>
        pane.Windows ?? pane.Children!.SelectMany(Windows).ToArray();
}
