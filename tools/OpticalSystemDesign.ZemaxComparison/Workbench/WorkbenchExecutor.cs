using System.Globalization;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.ZemaxComparison.Workbench;

public sealed record WorkbenchJob(string SnapshotPath, string OutputDirectory, CanonicalAnalysisRequest Request);
public static class WorkbenchExecutor
{
    public static void Execute(WorkbenchJob job)
    {
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(job.SnapshotPath));
        var issues = OpticCapabilityPreflight.Inspect(optic);
        JsonFiles.Write(Path.Combine(job.OutputDirectory, "preflight.json"), issues);
        var runtime = new WorkbenchRuntime(optic);
        var settings = ResolveWorkbenchSettings(job.Request);
        var data = runtime.BuildAnalysisData(job.Request.CanonicalAnalysisKey, settings);
        JsonFiles.Write(Path.Combine(job.OutputDirectory, "data.json"), data);
        File.WriteAllText(Path.Combine(job.OutputDirectory, "data.txt"), data.ReportText ?? data.ExportText());
        JsonFiles.Write(Path.Combine(job.OutputDirectory, "provenance.json"), new
        {
            job.Request.CanonicalAnalysisKey,
            RequestFingerprint = job.Request.Fingerprint,
            SettingsTranslation = ReferenceEquals(settings, job.Request.WorkbenchSettings) ? null : settings,
            ExecutorId = "Application.WorkbenchRuntime.BuildAnalysisData/v1",
            SnapshotSha256 = JsonFiles.Hash(File.ReadAllBytes(job.SnapshotPath)),
            CodeFingerprint = string.Join(":", new[] { typeof(WorkbenchRuntime).Assembly, typeof(Optic).Assembly }
                .Select(a => JsonFiles.Hash(File.ReadAllBytes(a.Location))))
        });
    }

    internal static Dictionary<string, string> ResolveWorkbenchSettings(CanonicalAnalysisRequest request)
    {
        // Captured focal field curvature has no native user-settable pupil
        // delta. Replay its fixed chief-and-positive probe while preserving
        // the archived request and its independently captured settings.
        var recorded = request.WorkbenchSettings;
        if (request.ImageSpaceAfocal || request.SettingsOrigin != "CapturedSettings"
            || request.CanonicalAnalysisKey is not ("Field Curvature" or "Field Curvature and Distortion")
            || !request.ZemaxSettings.ContainsKey("ScanType")
            || !string.Equals(recorded.GetValueOrDefault("ChiefRayParabasal"), "true", StringComparison.OrdinalIgnoreCase)
            || !double.TryParse(recorded.GetValueOrDefault("ParabasalDelta"), NumberStyles.Float, CultureInfo.InvariantCulture, out var delta)
            || delta != 1e-5)
            return recorded;

        return new(recorded, recorded.Comparer)
        {
            ["ParabasalDelta"] = FieldCurvatureMetrics.NativeFocalPupilDelta.ToString("R", CultureInfo.InvariantCulture)
        };
    }
    public static async Task<ZemaxZmxImportResult> Import(string path) => await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(path);
}
