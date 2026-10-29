using System.IO.Compression;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class CameraExperimentTests
{
    [Fact]
    public async Task ExternalInputAndBothLensSnapshotsSurviveDeletionAndWorkspaceReplacement()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var imagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var archive = imagePath + ".camera-experiment.zip";
        try
        {
            void Stage(string label)
            {
                var directory = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EXPERIMENT_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, "lens-replay.log"), DateTimeOffset.UtcNow.ToString("O") + " " + label + Environment.NewLine);
                }
            }
            using (var bitmap = new SKBitmap(20, 18))
            {
                bitmap.Erase(new SKColor(112, 98, 80));
                using var bytes = bitmap.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(imagePath, bytes.ToArray());
            }
            Stage("start A");
            var a = await app.CameraImaging.SimulateAsync(new(Source: CameraSource.ExternalImage, SourceFile: imagePath,
                Width: 4, Height: 4, Aberration: CameraAberration.Geometric));
            Stage("complete A; start B");
            var b = await app.CameraImaging.SimulateAsync(a.Request with { Sensor = a.Request.Sensor! with { ExposureMilliseconds = 20 } });
            Stage("complete B");
            Assert.NotNull(a.Inputs?.LensSnapshotJson);
            Assert.Contains("\"Fields\"", a.Inputs.LensSnapshotJson);
            File.Delete(imagePath); // Saving must use the original bytes captured when computing, not reread the file.
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b, a), archive);
            app.Documents.NewBlank(); app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            var before = app.Documents.GetSnapshot();
            var opened = await app.CameraImaging.OpenExperimentAsync(archive);
            Assert.Equal(a.Inputs.ImageOrigin, opened.A!.Inputs!.ImageOrigin);
            foreach (var saved in new[] { opened.A, opened.B })
            {
                Stage("start replay " + saved.Request.Sensor!.ExposureMilliseconds);
                var replay = await app.CameraImaging.RecomputeAsync(saved);
                Stage("complete replay " + saved.Request.Sensor!.ExposureMilliseconds);
                Assert.Equal(saved.RawDn, replay.RawDn); Assert.Equal(saved.PhotoElectrons, replay.PhotoElectrons);
                Assert.Equal(saved.Developed.Rgb24, replay.Developed.Rgb24);
                Assert.Equal(saved.Inputs!.LensSnapshotJson, replay.Inputs!.LensSnapshotJson);
            }
            var builtin = await app.CameraImaging.RecomputeAsync(opened.B, opened.B.Request with { Source = CameraSource.ColorChart, Optics = CameraOptics.Ideal });
            var restoredInput = await app.CameraImaging.RecomputeAsync(builtin, opened.B.Request);
            Assert.Equal(opened.B.RawDn, restoredInput.RawDn);
            Assert.Equal(before, app.Documents.GetSnapshot());
            var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EXPERIMENT_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidence))
            { Directory.CreateDirectory(evidence); File.Copy(archive, Path.Combine(evidence, "frozen-lens-input-AB.camera-experiment.zip"), true); }
        }
        finally { File.Delete(imagePath); File.Delete(archive); }
    }

    [Fact]
    public async Task ContinuousMaterialInputsAndEditedIspRecomputeIndependentlyOfSourceFiles()
    {
        using var app = WorkbenchApplication.Create();
        var request = await CameraMaterialServiceTests.Request(app.CameraImaging);
        var run = await app.CameraImaging.SimulateAsync(request);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run), path);
            var opened = await app.CameraImaging.OpenExperimentAsync(path);
            var replay = await app.CameraImaging.RecomputeAsync(opened.B);
            Assert.Equal(run.RawDn, replay.RawDn); Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons);
            Assert.Equal(run.Request.MaterialScene!.MaterialIds, replay.Request.MaterialScene!.MaterialIds);
            var edited = await app.CameraImaging.RecomputeAsync(opened.B, opened.B.Request with { Isp = new(Gamma: 1) });
            Assert.Equal(run.RawDn, edited.RawDn); Assert.NotEqual(run.Developed.Rgb24, edited.Developed.Rgb24);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CorruptUnknownAndCancelledExperimentsPreserveExistingFiles()
    {
        using var app = WorkbenchApplication.Create();
        var run = await app.CameraImaging.SimulateAsync(new(Width: 8, Height: 8, Optics: CameraOptics.Ideal));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        var experiment = new CameraExperiment(CameraExperiment.FormatIdentifier, 1, run);
        try
        {
            File.WriteAllText(path, "keep");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.SaveExperimentAsync(experiment, path, cancel.Token));
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SaveExperimentAsync(experiment with { SchemaVersion = 2 }, path));
            Assert.Equal("keep", File.ReadAllText(path));
            await app.CameraImaging.SaveExperimentAsync(experiment, path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            { zip.GetEntry("sha256.txt")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("sha256.txt").Open()); writer.Write(new string('0', 64)); }
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.OpenExperimentAsync(path));
            await app.CameraImaging.SaveExperimentAsync(experiment, path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) zip.CreateEntry("../unsafe");
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.OpenExperimentAsync(path));
            var future = run with { Inputs = run.Inputs! with { EngineVersion = "future-model" } };
            await app.CameraImaging.SaveExperimentAsync(experiment with { B = future }, path);
            Assert.NotNull(await app.CameraImaging.OpenExperimentAsync(path));
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.RecomputeAsync(future));
        }
        finally { File.Delete(path); }
    }
}
