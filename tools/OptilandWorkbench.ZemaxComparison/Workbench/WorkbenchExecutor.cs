using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.ZemaxComparison.Workbench;

public sealed record WorkbenchJob(string SnapshotPath, string OutputDirectory, CanonicalAnalysisRequest Request);
public static class WorkbenchExecutor
{
    public static void Execute(WorkbenchJob job)
    {
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(job.SnapshotPath));
        var issues = OpticCapabilityPreflight.Inspect(optic);
        JsonFiles.Write(Path.Combine(job.OutputDirectory, "preflight.json"), issues);
        var runtime = new WorkbenchRuntime(optic);
        var data = runtime.BuildAnalysisData(job.Request.CanonicalAnalysisKey, job.Request.WorkbenchSettings);
        JsonFiles.Write(Path.Combine(job.OutputDirectory, "data.json"), data);
        File.WriteAllText(Path.Combine(job.OutputDirectory, "data.txt"), data.ReportText ?? data.ExportText());
        JsonFiles.Write(Path.Combine(job.OutputDirectory, "provenance.json"), new
        {
            job.Request.CanonicalAnalysisKey,
            RequestFingerprint = job.Request.Fingerprint,
            ExecutorId = "Application.WorkbenchRuntime.BuildAnalysisData/v1",
            SnapshotSha256 = JsonFiles.Hash(File.ReadAllBytes(job.SnapshotPath)),
            CodeFingerprint = string.Join(":", new[] { typeof(WorkbenchRuntime).Assembly, typeof(Optic).Assembly }
                .Select(a => JsonFiles.Hash(File.ReadAllBytes(a.Location))))
        });
    }
    public static async Task<ZemaxZmxImportResult> Import(string path) => await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(path);
}
