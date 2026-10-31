using System.Text.Json;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Tests;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
if (args.Contains("--inventory"))
{
    var descriptors = ZemaxOperandRegistry.Descriptors;
    var compatibility = descriptors.Where(d => d.SupportLevel.ToString() != "Executable" && !ZemaxOperandRegistry.IsDocumentedUnused(d.Code)).ToArray();
    var inventory = new { Registered = descriptors.Count, Levels = descriptors.GroupBy(d => d.SupportLevel.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        DocumentedUnused = descriptors.Count(d => ZemaxOperandRegistry.IsDocumentedUnused(d.Code)),
        NonExecutableExcludingUnused = compatibility.Length, Codes = compatibility.Select(d => d.Code).ToArray() };
    await File.WriteAllTextAsync(Path.Combine(output, "operand-inventory.json"), JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new { inventory.Registered, inventory.Levels, inventory.DocumentedUnused, inventory.NonExecutableExcludingUnused }));
    return;
}
var results = new List<object>();
foreach (var defocus in new[] { 0d, 1d })
{
    var optic = Optic.CreateBlank("Review: folded concave mirror");
    optic.SurfaceGroup.Replace([
        new OpticalSurface { Thickness = double.PositiveInfinity },
        new OpticalSurface { Geometry = new StandardGeometry(-100), Thickness = -50 + defocus,
            IsReflective = true, SemiDiameter = 6, IsStop = true },
        new OpticalSurface { Geometry = new PlaneGeometry() }
    ]);
    optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 2;
    optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 0 });
    optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
    var path = Path.Combine(output, $"mirror-{defocus}.optic.json");
    await OpticJsonStore.SaveAsync(optic, path);
    var beforeMetric = FocusMetricEvaluator.Evaluate(optic);
    using var application = WorkbenchApplication.Create();
    await application.Documents.OpenAsync(path);
    var before = application.Prescription.GetSurfaces()[^2].Thickness;
    try
    {
        var result = await application.Optimization.QuickFocusAsync();
        var after = application.Prescription.GetSurfaces()[^2].Thickness;
        var afterPath = Path.Combine(output, $"after-{defocus}.optic.json");
        await application.Documents.SaveAsync(afterPath);
        double? actualRms = null; string? actualMetricError = null;
        try { actualRms = FocusMetricEvaluator.Evaluate(await OpticJsonStore.LoadAsync(afterPath), focusStep: 0).BestRmsSpotRadius; }
        catch (Exception e) { actualMetricError = e.Message; }
        results.Add(new { Case = "folded-mirror-quick-focus", Defocus = defocus, Before = before,
            PredictedBestShift = beforeMetric.BestFocusShift, PredictedBestRms = beforeMetric.BestRmsSpotRadius, Result = result, After = after,
            ActualRms = actualRms, ActualMetricError = actualMetricError });
    }
    catch (Exception error)
    { results.Add(new { Case = "folded-mirror-quick-focus", Defocus = defocus, Before = before, Error = error.ToString() }); }
}
using (var session = SafeHeadlessUnitTestSession.StartNew(typeof(AuditUi)))
    await session.Dispatch(async () =>
    {
        using var application = WorkbenchApplication.Create();
        var window = new CameraImagingWindow(application);
        try
        {
            window.Show(); window.UpdateLayout();
            var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
            controls.OfType<ComboBox>().Single(c => c.Name == "camera-optics").SelectedIndex = 1;
            var exposure = controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-exposure");
            exposure.Value = 10; exposure.Text = "not-a-number";
            var textBefore = exposure.Text; var valueBefore = exposure.Value;
            await (Task)typeof(CameraImagingWindow).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null)!;
            var result = (CameraImagingResult?)typeof(CameraImagingWindow).GetProperty("Result", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
            results.Add(new { Case = "camera-invalid-numeric-text", TextBeforeRun = textBefore, NumericValueBeforeRun = valueBefore,
                ResultProduced = result is not null, UsedExposureMilliseconds = result?.Request.Sensor?.ExposureMilliseconds,
                TextAfterRun = exposure.Text });
            var localization = typeof(CameraImagingWindow).Assembly.GetType("OpticalSystemDesign.App.Services.UiLocalization")!;
            var languageRows = new List<object>();
            foreach (var language in new[] { "en-US", "ja-JP" })
            {
                localization.GetMethod("Apply", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [language]);
                window.UpdateLayout();
                languageRows.Add(new { Language = language, WindowTitle = window.Title,
                    RunButton = controls.OfType<Button>().Single(c => c.Name == "camera-run").Content?.ToString(),
                    SaveExperiment = controls.OfType<Button>().Single(c => c.Name == "camera-save-experiment").Content?.ToString() });
            }
            results.Add(new { Case = "camera-language-coverage", Results = languageRows });
        }
        finally { window.Close(); }
        return true;
    }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(45));
var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(output, "reproductions.json"), json); Console.WriteLine(json);

public sealed class AuditUi
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();
}
