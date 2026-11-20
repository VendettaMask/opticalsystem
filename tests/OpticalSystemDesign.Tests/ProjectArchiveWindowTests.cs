using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using InitialWindow = OpticalSystemDesign.App.Laboratories.InitialStructure.MainWindow;
using CoatingWindow = OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ProjectArchiveWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light", "zh-CN", 96)]
    [InlineData("Dark", "ja-JP", 144)]
    [InlineData("Isekai", "en-US", 192)]
    public async Task CreateAndRestoreWindowsOperateOffscreen(string theme, string language, double dpi)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProjectArchiveWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language);
            using var files = new CameraSensorFileTests.TemporaryFiles(); using var source = WorkbenchApplication.Create();
            var note = files.Path("设计说明.txt"); await File.WriteAllTextAsync(note, "Archive functional input");
            source.ProjectArchives.LinkFile(note, required: true);
            var create = new ProjectArchiveWindow(source.ProjectArchives) { Width = 680, Height = 500 };
            using var destination = WorkbenchApplication.Create(); ProjectArchiveWindow? restore = null;
            try
            {
                create.Show(); await create.InitialLoad; create.UpdateLayout();
                var archive = files.Path("UI 工程.starpack"); Find<TextBox>(create, "archive-target").Text = archive;
                Assert.True(Find<Button>(create, "archive-create").IsEnabled);
                Capture(create, "create-" + theme, dpi);
                await create.ExecuteAsync(); Assert.True(File.Exists(archive));
                restore = new ProjectArchiveWindow(destination.ProjectArchives, archive: archive) { Width = 680, Height = 500 };
                restore.Show(); await restore.InitialLoad;
                Find<TextBox>(restore, "archive-target").Text = files.Path("UI restored");
                await restore.ExecuteAsync(); restore.UpdateLayout();
                Assert.True(File.Exists(destination.Documents.CurrentPath));
                Assert.Contains("main.staropt", destination.Documents.CurrentPath);
                foreach (var window in new[] { create, restore })
                {
                    window.UpdateLayout();
                    Assert.All(window.GetLogicalDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible), button =>
                    {
                        var point = button.TranslatePoint(default, window)!.Value;
                        Assert.InRange(point.Y, 0, window.Bounds.Height - button.Bounds.Height);
                        Assert.InRange(point.X, 0, window.Bounds.Width - button.Bounds.Width);
                    });
                }
                Capture(restore, "restored-" + theme, dpi);
                return true;
            }
            finally
            {
                create.Close(); restore?.Close();
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); UiLocalization.Apply(previousLanguage);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FourLaboratoriesInputsAndCameraResultsArePortable()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProjectArchiveWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
            var original = files.Path("original"); Directory.CreateDirectory(original);
            var initial = new InitialWindow(new() { RunDirectory = files.Path("runs") });
            var coating = new CoatingWindow(); var camera = new CameraImagingWindow(app); var assembly = new OpticalAssemblyWindow(app);
            var windows = new Window[] { initial, coating, camera, assembly };
            var targetInitial = new InitialWindow(new() { RunDirectory = files.Path("new-runs") });
            var targetCoating = new CoatingWindow(); var targetCamera = new CameraImagingWindow(app); var targetAssembly = new OpticalAssemblyWindow(app);
            try
            {
                foreach (var window in windows) { window.Show(); window.UpdateLayout(); }
                Find<ComboBox>(camera, "camera-optics").SelectedIndex = (int)CameraOptics.Ideal;
                await camera.RunAsync(); Assert.NotNull(camera.Result);
                var raw = camera.Result.RawDn.ToArray();
                var contributions = new List<ProjectArchiveFileDto>();
                foreach (var participant in windows.Cast<IProjectArchiveParticipant>())
                    contributions.AddRange(await participant.CaptureArchiveAsync(original, CancellationToken.None));
                Assert.Contains(contributions, file => file.Kind == "initial-input");
                Assert.Contains(contributions, file => file.Kind == "coating-input");
                Assert.Contains(contributions, file => file.Kind == "assembly-input");
                Assert.Contains(contributions, file => file.Kind == "camera-input");
                Assert.Contains(contributions, file => file.Kind == "camera-result");
                using var capture = await app.ProjectArchives.CaptureAsync(contributions);
                var archive = files.Path("laboratories.starpack");
                await app.ProjectArchives.CreateAsync(archive, capture, capture.Files.Select(file => file.RelativePath).ToHashSet());
                using var restoredApp = WorkbenchApplication.Create();
                var restored = await restoredApp.ProjectArchives.RestoreAsync(archive, files.Path("portable"));
                ProjectArchiveFileDto Resource(string kind) => restored.Files.Single(file => file.Kind == kind);
                Directory.Delete(original, recursive: true);
                foreach (var window in new Window[] { targetInitial, targetCoating, targetCamera, targetAssembly }) { window.Show(); window.UpdateLayout(); }
                await targetInitial.OpenArchiveAsync(Resource("initial-input").SourcePath, false);
                Assert.True(await targetCoating.OpenArchiveAsync(Resource("coating-input").SourcePath));
                targetAssembly.OpenArchiveInput(Resource("assembly-input").SourcePath);
                targetCamera.OpenArchiveInput(Resource("camera-input").SourcePath, restored.Directory);
                Assert.True(await targetCamera.OpenExperimentFromPathAsync(Resource("camera-result").SourcePath));
                Assert.Equal(raw, targetCamera.Result!.RawDn);
                return true;
            }
            finally
            {
                initial.Close(); targetInitial.Close(); camera.Close(); targetCamera.Close(); assembly.Close(); targetAssembly.Close();
                coating.CloseAfterHostApproval(); targetCoating.CloseAfterHostApproval();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CameraImageReferencesAreReboundInsideTheRestoredProject()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProjectArchiveWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
            var source = new CameraImagingWindow(app); var target = new CameraImagingWindow(app);
            var input = files.Path("input.json"); var image = files.Path("original-image.png");
            try
            {
                source.Show(); target.Show();
                using (var bitmap = new RenderTargetBitmap(new PixelSize(4, 4), new Vector(96, 96)))
                    bitmap.Save(image, PngBitmapEncoderOptions.Default);
                var sensor = new CameraSensorParameters();
                await ProjectArchiveArtifacts.WriteAsync(Path.GetDirectoryName(input)!, "input.json", "camera-input",
                    new CameraImagingRequest(CameraSource.ExternalImage, "original-image.png", Sensor: sensor));
                source.OpenArchiveInput(input, Path.GetDirectoryName(input)!);
                var directory = files.Path("capture"); Directory.CreateDirectory(directory);
                var resources = await source.CaptureArchiveAsync(directory, CancellationToken.None);
                using var capture = await app.ProjectArchives.CaptureAsync(resources);
                var archive = files.Path("image.starpack");
                await app.ProjectArchives.CreateAsync(archive, capture, capture.Files.Select(file => file.RelativePath).ToHashSet());
                using var destination = WorkbenchApplication.Create();
                var restored = await destination.ProjectArchives.RestoreAsync(archive, files.Path("image-project"));
                File.Delete(image); target.OpenArchiveInput(restored.Files.Single(file => file.Kind == "camera-input").SourcePath, restored.Directory);
                var second = await target.CaptureArchiveAsync(files.Path("recapture"), CancellationToken.None);
                Assert.All(second.Where(file => file.Kind == "camera-image"), file => Assert.StartsWith(restored.Directory, file.SourcePath));
                return true;
            }
            finally { source.Close(); target.Close(); }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetLogicalDescendants()
        .Concat(root.GetVisualDescendants()).Distinct().OfType<T>().Single(control => control.Name == name);

    [Fact]
    public async Task WorkspaceOptimizationTolerancesAndAutonomousPlanReopenAsSettings()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProjectArchiveWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create("cooke");
            using var panels = new PanelManager(app, new AppSettings(), new WorkspaceSessionStore(files.Path("sessions")));
            panels.Show(WorkspacePanelId.Optimization); panels.Show(WorkspacePanelId.Tolerancing);
            var autonomous = new OpticalSystemDesign.App.Laboratories.InitialStructure.AutonomousTopologyWindow(new() { RunDirectory = files.Path("autonomous-runs") });
            var reopened = new OpticalSystemDesign.App.Laboratories.InitialStructure.AutonomousTopologyWindow(new() { RunDirectory = files.Path("reopened-runs") });
            try
            {
                autonomous.Show(); reopened.Show();
                var directory = files.Path("settings-input"); Directory.CreateDirectory(directory);
                var contributions = new List<ProjectArchiveFileDto>(await panels.CaptureArchiveAsync(directory, CancellationToken.None));
                contributions.AddRange(await autonomous.CaptureArchiveAsync(directory, CancellationToken.None));
                using var capture = await app.ProjectArchives.CaptureAsync(contributions);
                var archive = files.Path("settings.starpack");
                await app.ProjectArchives.CreateAsync(archive, capture, capture.Files.Select(file => file.RelativePath).ToHashSet());
                using var destination = WorkbenchApplication.Create();
                var restored = await destination.ProjectArchives.RestoreAsync(archive, files.Path("settings-restored"));
                using var target = new PanelManager(destination, new AppSettings(), new WorkspaceSessionStore(files.Path("target-sessions")));
                var workspace = restored.Files.Single(file => file.Kind == "workspace");
                Assert.True(target.ApplyArchiveLayout(workspace.SourcePath));
                target.Show(WorkspacePanelId.Optimization); target.Show(WorkspacePanelId.Tolerancing);
                target.Factory.OpenArchiveOptimization(restored.Files.Single(file => file.Kind == "optimization-input").SourcePath);
                target.Factory.TolerancingEditor().OpenArchiveInput(restored.Files.Single(file => file.Kind == "tolerances").SourcePath);
                await reopened.OpenArchiveAsync(restored.Files.Single(file => file.Kind == "autonomous-input").SourcePath, false);
                return true;
            }
            finally { autonomous.Close(); reopened.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CoatingAndAssemblyResultFilesRestoreThroughTheirNativeViewers()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProjectArchiveWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
            var directory = files.Path("lab-records"); Directory.CreateDirectory(directory);
            var coating = new CoatingWindow(); var assembly = new OpticalAssemblyWindow(app);
            try
            {
                coating.Show(); assembly.Show();
                var design = OpticalSystemDesign.CoatingDesign.Engine.Examples.Create(OpticalSystemDesign.CoatingDesign.Engine.DesignKind.Antireflection);
                var evaluated = new OpticalSystemDesign.CoatingDesign.Engine.DesignService().Evaluate(design);
                var coatingPath = Path.Combine(directory, "result.coating.json");
                await OpticalSystemDesign.CoatingDesign.Engine.ExperimentStore.SaveAsync(evaluated, coatingPath);
                Assert.True(await coating.OpenArchiveAsync(coatingPath));
                var result = new AssemblyArchiveResult(1, new("Archived", 17, new(), []),
                    new(17, 2, 4, new double[] { 0, 1, 1, 0 }, 4, 4, 4, 1, 1, 1, "recorded", AssemblyMeasurementMode.Focused, 0, true));
                var assemblyFile = await ProjectArchiveArtifacts.WriteAsync(directory, "assembly.json", "assembly-result", result, true);
                using var capture = await app.ProjectArchives.CaptureAsync([
                    new("labs/coating/result.coating.json", "coating-result", coatingPath, new FileInfo(coatingPath).Length, false, true), assemblyFile]);
                var archive = files.Path("results.starpack");
                await app.ProjectArchives.CreateAsync(archive, capture, capture.Files.Select(file => file.RelativePath).ToHashSet());
                using var target = WorkbenchApplication.Create();
                var restored = await target.ProjectArchives.RestoreAsync(archive, files.Path("lab-results-restored"));
                assembly.OpenArchiveResult(restored.Files.Single(file => file.Kind == "assembly-result").SourcePath);
                Assert.Equal(result.Image!.Pixels, assembly.Preview!.Pixels);
                Assert.Equal(17, assembly.Result!.SourceRevision);
                Assert.True(await coating.OpenArchiveAsync(restored.Files.Single(file => file.Kind == "coating-result").SourcePath));
                return true;
            }
            finally { coating.CloseAfterHostApproval(); assembly.Close(); }
        }, CancellationToken.None);
    }
    private static void Capture(Window window, string name, double dpi)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Bounds.Width * dpi / 96), (int)(window.Bounds.Height * dpi / 96)), new Vector(dpi, dpi));
        bitmap.Render(window);
        var directory = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), ".tmp", "starpack");
        Directory.CreateDirectory(directory); bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
