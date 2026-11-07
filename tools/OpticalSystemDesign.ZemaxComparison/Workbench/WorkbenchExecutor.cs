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
        var recorded = request.WorkbenchSettings;
        if (request.SettingsOrigin != "CapturedSettings"
            || request.CanonicalAnalysisKey is not ("Field Curvature" or "Field Curvature and Distortion")
            || !request.ZemaxSettings.ContainsKey("ScanType"))
            return recorded;

        // The native dialog has no pupil-probe setting. Translate the legacy
        // adapter's probe to the independently verified native convention.
        var legacyProbe = string.Equals(recorded.GetValueOrDefault("ChiefRayParabasal"), "true", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(recorded.GetValueOrDefault("ParabasalDelta"), NumberStyles.Float, CultureInfo.InvariantCulture, out var delta)
            && delta == 1e-5;
        // Ordinary native distortion in object-height coordinates uses the
        // near-axis magnification. ReferenceField does not re-anchor it.
        var axialDistortion = request.CanonicalAnalysisKey == "Field Curvature and Distortion"
            && !request.ImageSpaceAfocal && request.FieldDefinition == "ObjectHeight"
            && request.ZemaxSettings.GetValueOrDefault("Distortion")?.ToString() == "F_TanTheta";
        if (!legacyProbe && !axialDistortion) return recorded;

        var translated = new Dictionary<string, string>(recorded, recorded.Comparer);
        if (legacyProbe)
        {
            translated["ParabasalDelta"] = (request.ImageSpaceAfocal ? FieldCurvatureMetrics.DefaultAfocalPupilDelta
                : FieldCurvatureMetrics.NativeFocalPupilDelta).ToString("R", CultureInfo.InvariantCulture);
            translated["ChiefRayParabasal"] = request.ImageSpaceAfocal ? "false" : recorded["ChiefRayParabasal"];
        }
        if (axialDistortion) translated["ReferenceFieldNumber"] = "0";
        return translated;
    }
    public static async Task<ZemaxZmxImportResult> Import(string path) => await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(path);
}
