using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.App.Services;

internal static class GuiAnalysisCaptureRunner
{
    private const int CaptureWidth = 1600;
    private const int CaptureHeight = 1000;
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static async Task RunAndShutdownAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        GuiAnalysisCaptureRequest request)
    {
        var exitCode = 1;
        try
        {
            exitCode = await RunAsync(desktop, request);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
        finally
        {
            desktop.Shutdown(exitCode);
        }
    }

    private static async Task<int> RunAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        GuiAnalysisCaptureRequest request)
    {
        if (!File.Exists(request.SourcePath))
        {
            throw new FileNotFoundException("GUI capture source was not found.", request.SourcePath);
        }

        if (!File.Exists(request.SettingsManifestPath))
        {
            throw new FileNotFoundException(
                "GUI capture settings manifest was not found.",
                request.SettingsManifestPath);
        }

        Directory.CreateDirectory(request.OutputDirectory);
        Environment.SetEnvironmentVariable(
            "OPTILAND_SETTINGS_DIRECTORY",
            Path.Combine(request.OutputDirectory, ".settings"));
        var manifest = JsonSerializer.Deserialize<CaptureSettingsManifest>(
            await BoundedApplicationFile.ReadAllTextAsync(
                request.SettingsManifestPath,
                BoundedApplicationFile.MaximumSettingsBytes,
                "GUI capture settings"),
            ReadJsonOptions)
            ?? throw new InvalidOperationException("GUI capture settings manifest is invalid.");
        var settingsByName = manifest.Analyses.ToDictionary(
            analysis => analysis.Name,
            analysis => (IReadOnlyDictionary<string, string>)analysis.Settings,
            StringComparer.Ordinal);
        var appSettings = new AppSettings
        {
            Theme = AppSettings.DefaultTheme,
            WindowWidth = CaptureWidth,
            WindowHeight = CaptureHeight
        };

        if (global::Avalonia.Application.Current is App currentApp)
        {
            currentApp.ApplyTheme("Light");
        }

        using var application = WorkbenchApplication.Create();
        await application.Documents.OpenAsync(request.SourcePath);

        var host = new Border();
        host.BindThemeResource(
            Border.BackgroundProperty,
            ThemeResourceBindings.Workspace);
        var window = new Window
        {
            Title = "Optiland Workbench GUI baseline capture",
            Width = CaptureWidth,
            Height = CaptureHeight,
            MinWidth = CaptureWidth,
            MinHeight = CaptureHeight,
            CanResize = false,
            ShowInTaskbar = false,
            RequestedThemeVariant = ThemeVariant.Light,
            Content = host
        };
        desktop.MainWindow = window;
        window.Show();

        var started = DateTimeOffset.UtcNow;
        var runs = new List<GuiCaptureRun>();
        foreach (var (analysisName, zeroBasedIndex) in application.Analyses.AnalysisNames
                     .Select((name, index) => (name, index)))
        {
            var index = zeroBasedIndex + 1;
            if (index < request.StartIndex || index > request.EndIndex)
            {
                continue;
            }

            var canonicalName = application.Analyses.CanonicalKey(analysisName);
            var settings = settingsByName.TryGetValue(canonicalName, out var saved)
                ? application.Analyses.MergeSettings(analysisName, saved)
                : application.Analyses.MergeSettings(analysisName, null);
            var fileName = $"{index:D3}-{Slug(canonicalName)}.png";
            var outputPath = Path.Combine(request.OutputDirectory, fileName);
            var stopwatch = Stopwatch.StartNew();
            var panel = new AnalysisPanel(
                application.Analyses,
                application.Visualization,
                application.Documents,
                application.Events,
                appSettings,
                analysisName,
                initialSettings: settings);
            try
            {
                var succeeded = await panel.RunForGuiCaptureAsync();
                host.Child = panel;
                window.Title = $"{index:D3} - {analysisName}";
                await Dispatcher.UIThread.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
                await Task.Delay(180);
                await Dispatcher.UIThread.InvokeAsync(
                    () => Capture(host, outputPath),
                    DispatcherPriority.Render);
                stopwatch.Stop();
                runs.Add(new GuiCaptureRun(
                    index,
                    analysisName,
                    canonicalName,
                    succeeded ? "captured" : "analysis-error",
                    stopwatch.ElapsedMilliseconds,
                    fileName,
                    null));
                Console.WriteLine(
                    $"{index:D3}/{application.Analyses.AnalysisNames.Count:D3} " +
                    $"{analysisName}: GUI captured in {stopwatch.Elapsed.TotalSeconds:F2}s");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                runs.Add(new GuiCaptureRun(
                    index,
                    analysisName,
                    canonicalName,
                    "failed",
                    stopwatch.ElapsedMilliseconds,
                    null,
                    exception.ToString()));
                Console.Error.WriteLine(
                    $"{index:D3}/{application.Analyses.AnalysisNames.Count:D3} " +
                    $"{analysisName}: GUI capture FAILED: {exception.Message}");
            }
            finally
            {
                host.Child = null;
                panel.Dispose();
            }
        }

        window.Close();
        var captureManifestPath = Path.Combine(
            request.OutputDirectory,
            "capture-manifest.json");
        var manifestRuns = await MergeExistingRunsAsync(
            captureManifestPath,
            request,
            runs);
        var result = new GuiCaptureManifest(
            "actual-avalonia-analysis-panel",
            request.SourcePath,
            request.SettingsManifestPath,
            CaptureWidth,
            CaptureHeight,
            "Light",
            started,
            DateTimeOffset.UtcNow,
            manifestRuns);
        await BoundedApplicationFile.WriteAllTextAtomicAsync(
            captureManifestPath,
            JsonSerializer.Serialize(result, WriteJsonOptions),
            BoundedApplicationFile.MaximumSettingsBytes,
            "GUI capture manifest");
        return runs.All(run => run.Status == "captured") ? 0 : 1;
    }

    private static async Task<IReadOnlyList<GuiCaptureRun>> MergeExistingRunsAsync(
        string manifestPath,
        GuiAnalysisCaptureRequest request,
        IReadOnlyList<GuiCaptureRun> currentRuns)
    {
        if (!File.Exists(manifestPath)
            || (request.StartIndex == 1 && request.EndIndex == int.MaxValue))
        {
            return currentRuns;
        }

        try
        {
            var existing = JsonSerializer.Deserialize<GuiCaptureManifest>(
                await BoundedApplicationFile.ReadAllTextAsync(
                    manifestPath,
                    BoundedApplicationFile.MaximumSettingsBytes,
                    "GUI capture manifest"),
                ReadJsonOptions);
            if (existing is null)
            {
                return currentRuns;
            }

            return existing.Runs
                .Where(run => run.Index < request.StartIndex || run.Index > request.EndIndex)
                .Concat(currentRuns)
                .OrderBy(run => run.Index)
                .ToArray();
        }
        catch (JsonException)
        {
            return currentRuns;
        }
    }

    private static void Capture(Control control, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(control.Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(control.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(control);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static string Slug(string value)
    {
        var characters = value.ToLowerInvariant().Select(character =>
            char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray();
        return string.Join(
            '-',
            new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record CaptureSettingsManifest(IReadOnlyList<CaptureSettingsAnalysis> Analyses);

    private sealed record CaptureSettingsAnalysis(
        string Name,
        Dictionary<string, string> Settings);

    private sealed record GuiCaptureRun(
        int Index,
        string Analysis,
        string CanonicalAnalysis,
        string Status,
        long ElapsedMilliseconds,
        string? Image,
        string? Error);

    private sealed record GuiCaptureManifest(
        string CaptureKind,
        string SourceFile,
        string SettingsManifest,
        int Width,
        int Height,
        string Theme,
        DateTimeOffset StartedUtc,
        DateTimeOffset CompletedUtc,
        IReadOnlyList<GuiCaptureRun> Runs);
}
