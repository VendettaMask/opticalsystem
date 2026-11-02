using System.IO.Compression;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraSfrTests
{
    [Fact]
    public async Task CurrentLensLinearArraySfrConvergenceAndFrozenReplaySurviveWorkspaceReplacement()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(SlantedEdgeSfrTests.ReferenceLens);
        var before = app.Documents.GetSnapshot(); var fields = app.Prescription.GetFields().ToArray();
        var result = await app.CameraImaging.SimulateSlantedEdgeAsync(new());
        Assert.Equal(before, app.Documents.GetSnapshot()); Assert.Equal(fields, app.Prescription.GetFields());
        Assert.Equal(before.Revision, result.SourceRevision); Assert.True(result.SamplingConverged);
        Assert.Equal(96 * 96, result.Baseline.LinearPixels.Count);
        Assert.Contains(result.Baseline.LinearPixels, v => Math.Abs(v * 255 - Math.Round(v * 255)) > .1);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sfr-experiment.zip");
        try
        {
            await app.CameraImaging.ExportSlantedEdgeAsync(result, path);
            using (var zip = ZipFile.OpenRead(path))
            {
                Assert.Equal(6, zip.Entries.Count);
                using var csv = new StreamReader(zip.GetEntry("linear-pixels.csv")!.Open());
                Assert.Equal(96 * 96 + 1, (await csv.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            }
            app.Documents.NewBlank(); app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            var blank = app.Documents.GetSnapshot();
            var opened = await app.CameraImaging.OpenSlantedEdgeAsync(path);
            var replay = await app.CameraImaging.RecomputeSlantedEdgeAsync(opened);
            Assert.Equal(result.Baseline.LinearPixels, replay.Baseline.LinearPixels);
            Assert.Equal(result.Baseline.Curve, replay.Baseline.Curve);
            Assert.Equal(result.LensSha256, replay.LensSha256); Assert.Equal(blank, app.Documents.GetSnapshot());
            Assert.Equal(before, replay.SourceDocument);
            Assert.All(replay.Baseline.Curve, p => Assert.NotNull(p.FullPupilPixelIntegratedMtf));
            var missingReference = result with { Baseline = result.Baseline with
                { Curve = result.Baseline.Curve.Select(p => p with { FullPupilMtf = null }).ToArray() } };
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSlantedEdgeAsync(missingReference, path));
            var inconsistentSampling = result with { Sampling = result.Sampling.Select((c, i) => i == 0
                ? c with { MaximumFullPupilMtfDifference = c.MaximumFullPupilMtfDifference + .0001 } : c).ToArray() };
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSlantedEdgeAsync(inconsistentSampling, path));
            var invalid = result with { Baseline = result.Baseline with { LinearPixels = new[] { double.NaN } } };
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSlantedEdgeAsync(invalid, path));
            Assert.Equal(result.Baseline.Curve, (await app.CameraImaging.OpenSlantedEdgeAsync(path)).Baseline.Curve);
            var evidence = Environment.GetEnvironmentVariable("OPTICAL_SFR_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, "reference-lens.sfr-experiment.zip"), true);
                await File.WriteAllTextAsync(Path.Combine(evidence, "service-summary.json"), System.Text.Json.JsonSerializer.Serialize(new
                { result.Algorithm, result.LensSha256, result.Request, result.SamplingConverged, result.Baseline.Mtf50CyclesPerPixel,
                    result.Baseline.MaximumSfrReferenceDifference, result.Baseline.MaximumSfrFullPupilDifference,
                    result.Baseline.MaximumWindowFullPupilDifference, result.Baseline.RelativePsfReadoutIntegral,
                    Checks = result.Sampling.Select(c => new { c.Axis, c.WithinCriteria, c.MaximumImageDifference, c.MaximumSfrDifference,
                        c.MaximumMtfDifference, c.MaximumFullPupilMtfDifference }), ExactReplay = true }));
            }
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("sha256.txt")!; entry.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("sha256.txt").Open()); await writer.WriteAsync("bad");
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.OpenSlantedEdgeAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UnsupportedModeSamplingAndCancellationHaveExplicitFailures()
    {
        using var app = WorkbenchApplication.Create();
        app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.CameraImaging.SimulateSlantedEdgeAsync(new()));
        app.Modes.SwitchTo(OpticalWorkbenchMode.Sequential); await app.Documents.OpenAsync(SlantedEdgeSfrTests.ReferenceLens);
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateSlantedEdgeAsync(new(ImageSamplesPerPixel: 3)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.SimulateSlantedEdgeAsync(new(), cancellation.Token));
        var field2 = await app.CameraImaging.SimulateSlantedEdgeAsync(new(FieldNumber: 2, CheckSampling: false));
        Assert.Equal(2, field2.Request.FieldNumber); Assert.Null(field2.SamplingConverged); Assert.Empty(field2.Sampling);
    }

    [Theory]
    [InlineData("monochrome-sfr", CameraSfrResult.PreviousAlgorithm)]
    [InlineData("double-gauss-sfr", CameraSfrResult.WindowAlgorithm)]
    public async Task ActualPreviousArchivesKeepTheirOriginalCurvesAndRemainViewOnly(string folder, string algorithm)
    {
        using var app = WorkbenchApplication.Create();
        var original = await app.CameraImaging.OpenSlantedEdgeAsync(Path.Combine(SlantedEdgeSfrTests.RepositoryRoot,
            "validation/rechecks/2026-11-02", folder, "reference-lens.sfr-experiment.zip"));
        Assert.Equal(algorithm, original.Algorithm);
        Assert.All(original.Baseline.Curve, p => Assert.Null(p.FullPupilMtf));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sfr-experiment.zip");
        try
        {
            await app.CameraImaging.ExportSlantedEdgeAsync(original, path);
            var reopened = await app.CameraImaging.OpenSlantedEdgeAsync(path);
            Assert.Equal(algorithm, reopened.Algorithm); Assert.Equal(original.Baseline.Curve, reopened.Baseline.Curve);
            await Assert.ThrowsAsync<NotSupportedException>(() => app.CameraImaging.RecomputeSlantedEdgeAsync(reopened));
        }
        finally { File.Delete(path); }
    }
}
