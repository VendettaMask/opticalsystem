using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Tests;
using ProductWindow = OpticalSystemDesign.App.MainWindow;

if (args.Length != 4) throw new ArgumentException("Usage: <repository> <output-directory> <Light|Dark> <zh-CN|en-US>");
var repository = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var theme = args[2];
var language = args[3];
if (theme is not ("Light" or "Dark") || language is not ("zh-CN" or "en-US"))
    throw new ArgumentException("Unsupported capture theme or language.");
Directory.CreateDirectory(output);
var isolatedProfile = Path.Combine(output, "isolated-profiles", theme + "-" + language);
Directory.CreateDirectory(isolatedProfile);
Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", isolatedProfile);
const int viewportWidth = 1760;
const int viewportHeight = 990;
new AppSettings { Theme = theme, Language = language, WindowWidth = viewportWidth, WindowHeight = viewportHeight }.Save();
var example = Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Zoom systems/Zoom lens.zmx");
var suffix = language == "en-US" ? "-en" : string.Empty;
var filename = "workbench-" + theme.ToLowerInvariant() + suffix + ".png";
using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ReadmeCaptureBootstrap));
await session.Dispatch(async () =>
{
    // This is the actual product window, ribbon, title bar, commands and panels.
    // Only its persistence destination is redirected before the first UI yield.
    var window = new ProductWindow();
    var panels = Field<PanelManager>(window, "_panels");
    var store = Field<WorkspaceSessionStore>(panels, "_sessionStore");
    typeof(WorkspaceSessionStore).GetField("_rootDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(store, Path.Combine(isolatedProfile, "workspace"));
    if (!Path.GetFullPath(store.DefaultLayoutPath).StartsWith(isolatedProfile + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        throw new InvalidOperationException("Workspace persistence was not isolated.");
    var application = Field<IWorkbenchApplication>(window, "_application");
    window.Width = viewportWidth;
    window.Height = viewportHeight;
    try
    {
        await application.Documents.OpenAsync(example);
        window.Show();
        window.SetRenderScaling(2);
        Tick(window);
        var viewer = panels.Factory.OpenDocuments().Single(d => d.Id == WorkspaceDockFactory.Viewer2DDocumentId);
        panels.Factory.SetActiveDockable(viewer);
        Tick(window);
        panels.TileAllWindows();
        Tick(window);
        // Resize the real MDI documents, as a user can with the product's handles.
        // Give the prescription enough width to show the material column.
        var dock = panels.Factory.PrimaryDocumentDock!;
        dock.GetVisibleBounds(out _, out _, out var availableWidth, out var availableHeight);
        var lens = panels.Factory.OpenDocuments().Single(d => d.Id == WorkspaceDockFactory.LensDocumentId);
        ((IMdiDocument)lens).MdiBounds = new DockRect(0, 0, availableWidth * 0.60, availableHeight);
        ((IMdiDocument)viewer).MdiBounds = new DockRect(availableWidth * 0.60, 0, availableWidth * 0.40, availableHeight);
        Tick(window);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        OpticSceneControl? scene = null;
        while (DateTime.UtcNow < deadline)
        {
            Tick(window);
            scene = window.GetVisualDescendants().OfType<OpticSceneControl>()
                .FirstOrDefault(control => control.IsEffectivelyVisible && control.Scene?.SourceRevision == application.Events.Revision);
            if (scene is not null && panels.WorkspaceControl.Bounds.Width > 0) break;
            await Task.Delay(30);
        }
        if (scene?.Scene?.SourceRevision != application.Events.Revision)
            throw new InvalidOperationException("The real viewer did not finish the current model revision.");
        if (application.Prescription.GetSurfaces().Count < 10 || panels.Factory.OpenDocuments().Count != 2)
            throw new InvalidOperationException("Unexpected example or workspace content.");
        // Fluent theme content transitions need to settle before bitmap capture.
        for (var frame = 0; frame < 70; frame++) { await Task.Delay(30); Tick(window); }
        using var bitmap = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The real headless compositor did not produce a frame.");
        if (bitmap.PixelSize != new PixelSize(viewportWidth * 2, viewportHeight * 2))
            throw new InvalidOperationException("Unexpected compositor frame dimensions.");
        var path = Path.Combine(output, filename);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        var metadata = new
        {
            file = filename, theme, language, width = viewportWidth * 2, height = viewportHeight * 2,
            viewportWidth, viewportHeight, renderScale = 2, lensPaneFraction = 0.60,
            windowType = window.GetType().FullName,
            applicationVersion = typeof(ProductWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            input = Path.GetRelativePath(repository, example).Replace('\\', '/'),
            inputSha256 = Hash(example), imageSha256 = Hash(path), bytes = new FileInfo(path).Length,
            surfaceCount = application.Prescription.GetSurfaces().Count,
            configurationCount = application.MultiConfiguration.GetNavigationState().Count,
            sceneRevision = scene.Scene.SourceRevision, documentRevision = application.Events.Revision,
            realMainWindow = true, captureApi = "HeadlessWindowExtensions.CaptureRenderedFrame", headless = true, nativeDesktopWindow = false,
            persistenceIsolated = true, userProjectsOpened = false, physicalInput = false, clipboardChanged = false
        };
        await File.WriteAllTextAsync(Path.Combine(output, Path.GetFileNameWithoutExtension(filename) + ".json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { file = filename, metadata.surfaceCount, metadata.configurationCount, metadata.bytes }));
    }
    finally
    {
        window.Close();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (window.IsVisible && DateTime.UtcNow < deadline) { Tick(window); await Task.Delay(20); }
        if (window.IsVisible) throw new InvalidOperationException("The isolated window did not finish normal shutdown.");
    }
}, CancellationToken.None);

static T Field<T>(object owner, string name) => (T)owner.GetType()
    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}
static void Tick(Window window)
{
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
}

public sealed class ReadmeCaptureBootstrap
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
