using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Serialization;
using Dto = OpticalSystemDesign.Application.Contracts;
using Vector3D = OpticalSystemDesign.Core.Backend.Vector3D;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialColorFringesTests
{
    internal static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "OpticalSystemDesign.Core"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root");
        }
    }
    internal static string Source => Path.Combine(Root, "src", "OpticalSystemDesign.App", "Assets", "NonSequentialExamples", "Birefringence", "Color Fringes.ZMX");
    internal static ZemaxNscImportResult Import() => ZemaxNonSequentialReader.Read(Source);
    private static readonly Vector3D Z = new(0, 0, 1);

    [Fact]
    public void OriginalColorFringesMapsFullObjectsPickupsCrystalDispersionAndExplicitSpectrum()
    {
        var result = Import(); Assert.True(result.CanApply, string.Join("\n", result.Diagnostics));
        Assert.Equal(File.ReadAllBytes(Source), result.OriginalBytes()); Assert.Equal(815, result.Records.Count);
        Assert.Equal(new[] { 146, 138, 138, 145, 127 }, result.Objects.Select(o => o.Records.Count));
        Assert.Empty(result.Assets); Assert.Single(result.Diagnostics); Assert.False(result.Diagnostics[0].Blocking);
        var doc = result.Document!; Assert.Equal(5, doc.Objects.Count); Assert.Equal(2, doc.MeshAssets.Count);
        Assert.Equal(45, doc.Objects[3].LocalCoordinateSystem.RotationZDegrees);
        Assert.Equal(.562, doc.Objects[2].LocalCoordinateSystem.Origin.Z, 12);
        Assert.All(doc.Objects.Skip(1).Take(2), o =>
        {
            Assert.Equal("BIREFRINGENT:QUARTZ", ((MeshObjectParameters)o.Parameters).Material);
            Assert.Equal("BIREFRINGENT:QUARTZ-E", o.Parameters.Waveplate!.ExtraordinaryMaterial);
            Assert.True(doc.FindMeshAsset(((MeshObjectParameters)o.Parameters).MeshAssetId).IsClosed);
        });
        var emitter = (SourceEllipseParameters)doc.Objects[0].Parameters;
        Assert.Equal(20, emitter.Physics!.Spectrum!.Count); Assert.Equal(440, emitter.Physics.Spectrum[0].Nanometers);
        Assert.Equal(640, emitter.Physics.Spectrum[^1].Nanometers); Assert.Equal(NonSequentialColorimetry.SpectrumFitProtocol, emitter.Physics.SpectrumProtocol);
        var detector = (DetectorRectangleParameters)doc.Objects[3].Parameters;
        Assert.True(detector.RecordColor); Assert.Equal(1, detector.PolarizationComponent); Assert.False(detector.Absorb);
        Assert.IsType<NullObjectParameters>(doc.Objects[4].Parameters);
        Assert.True(doc.TraceSettings.UsePolarization); Assert.True(doc.TraceSettings.SplitFresnelRays);
        Assert.Equal(500, doc.TraceSettings.MaximumSegmentsPerRay); Assert.Equal(.001, doc.TraceSettings.MinimumRelativeIntensity);
        var registry = new MaterialRegistry();
        Assert.InRange(registry.Resolve("BIREFRINGENT:QUARTZ-E").RefractiveIndex(550) - registry.Resolve("BIREFRINGENT:QUARTZ").RefractiveIndex(550), .009, .01);
    }

    [Fact]
    public void AllOriginalExamplesRemainLosslessAndUnknownActivePhysicsStaysBlocked()
    {
        var directory = Path.GetDirectoryName(Path.GetDirectoryName(Source))!;
        var count = 0;
        var audit = new List<object>();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(p => Path.GetExtension(p).Equals(".zmx", StringComparison.OrdinalIgnoreCase)))
        {
            var result = ZemaxNonSequentialReader.Read(path);
            Assert.Equal(File.ReadAllBytes(path), result.OriginalBytes()); Assert.NotEmpty(result.Objects); count++;
            Assert.Equal(result.CanApply, result.Document is not null && !result.Diagnostics.Any(d => d.Blocking));
            audit.Add(new { Path = Path.GetRelativePath(directory, path), result.SourceSha256, result.CanApply,
                Objects = result.Objects.Count, Records = result.Records.Count,
                Blocking = result.Diagnostics.Where(d => d.Blocking).GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count()) });
        }
        Assert.Equal(106, count);
        var capture = Environment.GetEnvironmentVariable("NSC_COLOR_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(capture)) File.WriteAllText(Path.Combine(capture, "import-audit.json"), JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
        var text = File.ReadAllText(Source);
        foreach (var changed in new[] { text.Replace("NSBR 0 3 1", "NSBR 0 0 1"), text.Replace("NSCS 7 1 0 1 1", "NSCS 7 1 1 1 1"), text + "\nENVD 40 1 0", text.Replace("NSCS 24 0 0", "NSCS 24 0 1") })
        {
            var result = ZemaxNonSequentialReader.Parse(changed); Assert.False(result.CanApply); Assert.Null(result.Document);
            Assert.Contains(result.Diagnostics, d => d.Blocking);
        }
    }

    [Fact]
    public void PickupUsesReferencedValueAndRejectsCyclesInsteadOfTrustingTheSavedCache()
    {
        var text = File.ReadAllText(Source).Replace("NSOD 7 -1.955100000000E-01 2 2", "NSOD 7 99 2 2")
            .Replace("NSOP 0.000000000000E+00 0.000000000000E+00 5.620000000000E-01", "NSOP 0.000000000000E+00 0.000000000000E+00 99");
        var result = ZemaxNonSequentialReader.Parse(text); Assert.True(result.CanApply);
        Assert.Equal(.562, result.Document!.Objects[2].LocalCoordinateSystem.Origin.Z, 12);
        var mesh = result.Document.FindMeshAsset(((MeshObjectParameters)result.Document.Objects[2].Parameters).MeshAssetId);
        Assert.InRange(mesh.BoundsMinimum.Z, -.04, -.039);
        var cycle = ZemaxNonSequentialReader.Parse(text.Replace("NSOD 7 99 2 2", "NSOD 7 99 2 3").Replace("0.000000000000E+00 15 \"\"", "0.000000000000E+00 13 \"\""));
        Assert.False(cycle.CanApply); Assert.Contains(cycle.Diagnostics, d => d.Message.Contains("循环"));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(.25, .5)] [InlineData(.5, 1)] [InlineData(1, 0)]
    public void WaveplateJonesTransportMatchesCrossedAnalyzerClosedForm(double waves, double expected)
    {
        var field = NonSequentialFieldState.Create(Z, new(new(false, 0, 1)), "beam");
        var output = field.Retard(Z, new(1, 1, 0), waves * .00055, 550);
        Assert.Equal(1, output.Power, 12);
        Assert.Equal(expected, output.First.Field.X.Magnitude * output.First.Field.X.Magnitude, 12);
        var reversed = output.Retard(Z, new(1, 1, 0), -waves * .00055, 550);
        Assert.InRange(reversed.First.Field.X.Magnitude, 0, 1e-13);
        Assert.Equal(1, field.Retard(Z, Z, 100, 550).Power, 12);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(45, .5)] [InlineData(90, 1)]
    public void DetectorAnalyzerUsesItsOwnRotatedAxesAndDoesNotDestroyThroughFlux(double angle, double expected)
    {
        var source = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with { Parameters = new SourceRayParameters { Physics = new(new(false, 0, 1)) } };
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, 10), RotationZDegrees: angle), Parameters = new DetectorRectangleParameters(4, 4, 1, 1, Absorb: false) { RecordColor = true, PolarizationComponent = 1 } };
        var doc = new NonSequentialDocument("Analyzer", [new("green", 550, 1, true)], [source, detector], traceSettings: new(UsePolarization: true));
        var result = new NonSequentialDocumentTracer().Trace(doc, new());
        Assert.Equal(expected, result.Detectors.Single().TotalPowerWatts, 12);
        Assert.Equal(1, result.EnergyBalance.EscapedPowerWatts, 12); Assert.Equal(1, result.EnergyBalance.AccountedPowerWatts, 12);
    }

    [Fact]
    public void TouchingWaveplatesHaveNoFictitiousAirInterfaceAndKeepPhaseThroughTheSharedFace()
    {
        var source = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with { Parameters = new SourceRayParameters { Physics = new(new(false, 0, 1)) } };
        var plate = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.Box) with
        { LocalCoordinateSystem = new(new(0, 0, 1)), Parameters = new BoxParameters(4, 4, 1, "test-crystal") { Waveplate = new("test-crystal", new(1, 0, 0)) } };
        var second = plate with { Id = Guid.NewGuid(), LocalCoordinateSystem = new(new(0, 0, 2)) };
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, 4)), Parameters = new DetectorRectangleParameters(4, 4, 1, 1) };
        var doc = new NonSequentialDocument("Touching", [new("green", 550, 1, true)], [source, plate, second, detector], traceSettings: new(UsePolarization: true, MinimumRelativeIntensity: 1e-13));
        var registry = new MaterialRegistry(); registry.Register(new ConstantIndexMaterial("test-crystal", 1.5));
        var result = new NonSequentialDocumentTracer().Trace(doc, registry);
        Assert.Equal((1 - .04) / (1 + .04), result.Detectors.Single().TotalPowerWatts, 11);
        Assert.Equal(1, result.EnergyBalance.AccountedPowerWatts, 11);
        Assert.Throws<NotSupportedException>(() => new NonSequentialDocumentTracer().Trace(doc, registry, new(SplittingMode: NonSequentialSplittingMode.SimpleStochastic)));
    }

    [Fact]
    public void CieOriginalDataAndSpectrumFitRetainTristimulusChromaticityAndRadiometricPower()
    {
        Assert.Equal("17cca777db64b17170f06f67ce9d3ab7", Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(Path.Combine(Root, "third_party", "colorimetry", "CIE_xyz_1931_2deg.csv")))));
        Assert.Equal((.5120501, 1d, .005749999), NonSequentialColorimetry.Response(555));
        var spectrum = NonSequentialColorimetry.FitSpectrum(.9505, 1, 1.089, 440, 640, 20);
        Assert.Equal(1, spectrum.Sum(b => b.RelativePower), 12); Assert.All(spectrum, b => Assert.True(b.RelativePower >= 0));
        var x = spectrum.Sum(b => b.RelativePower * NonSequentialColorimetry.Response(b.Nanometers).X);
        var y = spectrum.Sum(b => b.RelativePower * NonSequentialColorimetry.Response(b.Nanometers).Y);
        var z = spectrum.Sum(b => b.RelativePower * NonSequentialColorimetry.Response(b.Nanometers).Z);
        Assert.Equal(.9505, x / y, 8); Assert.Equal(1.089, z / y, 8);
        Assert.Throws<InvalidDataException>(() => NonSequentialColorimetry.FitSpectrum(1, 0, 0, 500, 600, 20));
    }

    [Fact]
    public void ActualFringesHaveSpatialSpectralVariationAndDisappearWhenRetardanceIsRemoved()
    {
        var doc = Import().Document!; var registry = new MaterialRegistry();
        var request = new NonSequentialDocumentTraceRequest(RayCountOverride: 5000, OutputMode: NonSequentialTraceOutputMode.RayDatabase);
        using var stream = new MemoryStream(); NonSequentialDocumentTraceResult traced;
        using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(doc, materials: registry), true))
        { traced = new NonSequentialDocumentTracer().Trace(doc, registry, request, writer); writer.Complete(); }
        stream.Position = 0; using var reader = new NonSequentialRayDatabaseReader(stream, true);
        var rebuilt = NonSequentialDetectorReconstruction.Reconstruct(doc, reader.ReadBranches()).Single();
        Assert.Equal(traced.Detectors.Single().TotalPowerWatts, rebuilt.TotalPowerWatts, 12);
        Assert.Equal(NonSequentialColorimetry.DetectorRgb(traced.Detectors.Single(), doc), NonSequentialColorimetry.DetectorRgb(rebuilt, doc));
        Assert.InRange(traced.EnergyBalance.AccountedPowerWatts, .999999999, 1.000000001);
        var originalPower = rebuilt.TotalPowerWatts; var originalHash = NonSequentialSceneHasher.Compute(doc, registry);
        foreach (var plate in doc.Objects.Where(o => o.Parameters.Waveplate is not null).ToArray()) doc.Replace(plate.Id, plate with { Parameters = plate.Parameters with { Waveplate = null } });
        Assert.NotEqual(originalHash, NonSequentialSceneHasher.Compute(doc, registry)); Assert.True(reader.IsStale(doc, registry));
        var flat = new NonSequentialDocumentTracer().Trace(doc, registry, request with { OutputMode = NonSequentialTraceOutputMode.SummaryOnly });
        Assert.InRange(flat.Detectors.Single().TotalPowerWatts, 0, 1e-5); Assert.InRange(originalPower, .4, .5);
    }

    [Fact]
    public async Task RealApplicationAppliesEditsTracesReconstructsAndReopensTheCrystalProject()
    {
        using var app = WorkbenchApplication.Create();
        app.NonSequential.ApplyZemaxNscImport(Import(), app.NonSequential.GetDesignSceneIdentity());
        var original = app.NonSequential.GetDocument(); Assert.Equal(5, original.Objects.Count);
        foreach (var o in original.Objects) app.NonSequential.UpdateObject(Update(o) with { Name = o.Name + " edited" });
        var doc = app.NonSequential.GetDocument();
        Assert.NotNull(doc.Objects[1].Parameters.Waveplate);
        Assert.Equal(20, ((Dto.SourceParameters)doc.Objects[0].Parameters).Physics!.Spectrum!.Count);
        var detector = doc.Objects[3]; Assert.True(((Dto.DetectorRectangleParameters)detector.Parameters).RecordColor);
        await app.NonSequentialAnalysis.TraceAsync(new(RayCountOverride: 400));
        var view = app.NonSequentialAnalysis.GetDetectorView(new(detector.Id));
        Assert.NotNull(view.Rgb); Assert.Equal(30000, view.Rgb.Count);
        Assert.Null(app.NonSequentialAnalysis.GetDetectorView(new(detector.Id, DataType: Dto.NonSequentialDetectorDataType.HitCount)).Rgb);
        var file = Path.Combine(Path.GetTempPath(), "color-fringes-" + Guid.NewGuid().ToString("N") + ".staropt");
        try
        {
            await app.Documents.SaveAsync(file);
            var loaded = await StarOptProjectStore.LoadAsync(file); Assert.Equal(2, loaded.NonSequentialDocument!.MeshAssets.Count);
            Assert.NotNull(loaded.NonSequentialDocument.Objects[1].Parameters.Waveplate);
            using var reopened = WorkbenchApplication.Create(); await reopened.Documents.OpenAsync(file);
            Assert.Equal(JsonSerializer.Serialize(doc), JsonSerializer.Serialize(reopened.NonSequential.GetDocument()));
        }
        finally { File.Delete(file); }
        app.NonSequential.UpdateObject(Update(detector) with { Z = detector.Z + 1 });
        Assert.Throws<InvalidOperationException>(() => app.NonSequentialAnalysis.GetDetectorView(new(detector.Id)));
    }
    internal static Dto.NonSequentialObjectUpdateDto Update(Dto.NonSequentialObjectRowDto o) => new(o.Id, o.Enabled, o.Visible, o.Kind,
        o.Name, o.ReferenceObjectId, o.ContainingObjectId, o.X, o.Y, o.Z, o.TiltXDegrees, o.TiltYDegrees, o.TiltZDegrees, o.Parameters, o.SurfaceBsdf);
}

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialColorFringesViewerTests
{
    [Theory]
    [InlineData("Light", "zh-CN", 1)] [InlineData("Dark", "ja-JP", 1.5)] [InlineData("Isekai", "en-US", 2)]
    public async Task ActualViewerShowsPhysicalRgbAndKeepsItsScalarControlsAndPngConsistent(string theme, string language, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new() { Theme = theme, Language = language }); UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create(); app.NonSequential.ApplyZemaxNscImport(NonSequentialColorFringesTests.Import(), app.NonSequential.GetDesignSceneIdentity());
            using (var editor = new NonSequentialObjectEditorPanel(app.NonSequential, app.Events))
            {
                // The real property editor must retain the imported spectrum when it edits Jones settings.
                typeof(NonSequentialObjectEditorPanel).GetMethod("ApplyProperties", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
                Assert.Equal(20, ((Dto.SourceParameters)app.NonSequential.GetDocument().Objects[0].Parameters).Physics!.Spectrum!.Count);
            }
            await app.NonSequentialAnalysis.TraceAsync(new(RayCountOverride: 200));
            using var panel = new NonSequentialDetectorViewerPanel(app.NonSequential, app.NonSequentialAnalysis);
            var window = new Window { Width = 1400, Height = 1000, Content = panel };
            try
            {
                window.Show(); window.SetRenderScaling(scaling); UiLocalization.Attach(window); DisplayTypography.Apply(window);
                await panel.RefreshViewAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var toggle = panel.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "nsc-detector-true-color");
                Assert.True(toggle.IsVisible); Assert.True(toggle.IsChecked);
                Assert.Equal(language == "en-US" ? "True color" : language == "ja-JP" ? "トゥルーカラー" : "真彩色", toggle.Content);
                var plot = panel.GetVisualDescendants().OfType<AnalysisPlotControl>().First();
                Assert.Equal(Dto.AnalysisSeriesKind.Raster, plot.Series.Single().Kind); Assert.All(plot.Series.Single().Points, p => Assert.NotNull(p.Red));
                toggle.IsChecked = false; Assert.Equal(Dto.AnalysisSeriesKind.Heatmap, plot.Series.Single().Kind);
                toggle.IsChecked = true; Assert.Equal(Dto.AnalysisSeriesKind.Raster, plot.Series.Single().Kind);
                AssertRenderedPixelsMatchSource(plot);
                var directory = Environment.GetEnvironmentVariable("NSC_COLOR_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, theme + ".png"), PngBitmapEncoderOptions.Default);
                    var dataFile = Environment.GetEnvironmentVariable("NSC_COLOR_TRACE_DATA");
                    if (!string.IsNullOrWhiteSpace(dataFile))
                    {
                        using var data = JsonDocument.Parse(File.ReadAllText(dataFile));
                        var bands = JsonSerializer.Deserialize<Dictionary<int, double[]>>(data.RootElement.GetProperty("Power"))!;
                        var doc = NonSequentialColorFringesTests.Import().Document!;
                        var detectorFrame = new NonSequentialDetectorFrame(doc.Objects[3].Id, "Color Fringes", 100, 100,
                            bands.ToDictionary(b => b.Key, b => (IReadOnlyList<double>)b.Value), bands.Values.Sum(b => b.Sum()));
                        var rgb = NonSequentialColorimetry.DetectorRgb(detectorFrame, doc);
                        var points = Enumerable.Range(0, 10000).Select(i => new Dto.AnalysisPointDto(-10 + (i % 100 + .5) * .2,
                            -10 + (i / 100 + .5) * .2, Value: bands.Values.Sum(b => b[i]), Red: rgb[3 * i], Green: rgb[3 * i + 1], Blue: rgb[3 * i + 2])).ToArray();
                        // Capture the million-ray computed data through the actual product plot and PNG path.
                        var imagePlot = new AnalysisPlotControl
                        {
                            PlotOptions = new("Color Fringes · 1,000,000 rays", EqualAspect: true, XMinimum: -10, XMaximum: 10, YMinimum: -10, YMaximum: 10),
                            Series = [new("X (mm)", "Y (mm)", points, Dto.AnalysisSeriesKind.Raster)]
                        };
                        window.Content = imagePlot; window.Width = 800; window.Height = 800; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        AssertRenderedPixelsMatchSource(imagePlot);
                        using var png = ExportImageFrame.Capture(imagePlot, 1, imagePlot);
                        png.Save(Path.Combine(directory, theme + "-million-ray.png"), PngBitmapEncoderOptions.Default);
                    }
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertRenderedPixelsMatchSource(AnalysisPlotControl plot)
    {
        var options = plot.PlotOptions;
        try
        {
            plot.PlotOptions = options with { GridOpacity = 0, ShowHorizontalZeroLine = false, ShowVerticalZeroLine = false };
            using var image = new RenderTargetBitmap(new PixelSize((int)plot.Bounds.Width, (int)plot.Bounds.Height), new Vector(96, 96));
            image.Render(plot);
            var rectangle = (Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plot)!;
            var viewport = typeof(AnalysisPlotControl).GetField("_lastRenderedViewport", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plot)!;
            double V(string name) => (double)viewport.GetType().GetProperty(name)!.GetValue(viewport)!;
            var bytes = new byte[image.PixelSize.Width * image.PixelSize.Height * 4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { image.CopyPixels(new PixelRect(image.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, image.PixelSize.Width * 4); }
            finally { handle.Free(); }
            foreach (var row in new[] { 7, 17, 27, 37, 47, 57, 67, 77, 87 })
            foreach (var column in new[] { 13, 33, 53, 73, 93 })
            {
                var point = plot.Series.Single().Points[row * 100 + column];
                var x = (int)(rectangle.Left + (point.X - V("XMinimum")) / (V("XMaximum") - V("XMinimum")) * rectangle.Width);
                var y = (int)(rectangle.Bottom - (point.Y - V("YMinimum")) / (V("YMaximum") - V("YMinimum")) * rectangle.Height);
                var index = (y * image.PixelSize.Width + x) * 4;
                Assert.InRange(Math.Abs(bytes[index] - Math.Round(point.Blue!.Value * 255)), 0, 2);
                Assert.InRange(Math.Abs(bytes[index + 1] - Math.Round(point.Green!.Value * 255)), 0, 2);
                Assert.InRange(Math.Abs(bytes[index + 2] - Math.Round(point.Red!.Value * 255)), 0, 2);
            }
        }
        finally { plot.PlotOptions = options; }
    }
}
