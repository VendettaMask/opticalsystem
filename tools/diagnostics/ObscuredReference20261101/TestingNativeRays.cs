using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class TestingNativeRays
{
    internal static async Task Run(string repository, string output, string coreHash)
    {
        using var lease = new FileStream(Path.Combine(repository, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var native = await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None);
        var executions = new List<object>();
        foreach (var lens in TestingPropagationProbe.Lenses)
        {
            var source = Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
            var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
            var imported = await WorkbenchExecutor.Import(source);
            for (var c = 1; c <= imported.Configurations.Count; c++)
            {
                var id = Path.GetFileNameWithoutExtension(lens).Replace(' ', '-') + "-c" + c;
                var folder = Path.Combine(output, id);
                var job = new RayAuditJob { Source = source, SourceSha256 = hash, Output = folder, Configuration = c,
                    Wavelength = 1, RayDensity = 6, Fields = [[0, 0]], TimeoutSeconds = 120, RemoveVignettingFactors = false };
                var pupils = job.CreateSamples();
                var inputs = pupils.Select((p, i) => new { fieldIndex = 0, pupilIndex = i, hx = 0d, hy = 0d, px = p.X, py = p.Y, weight = p.Weight }).ToArray();
                var engines = Process.GetProcessesByName("OpticStudio"); var existing = engines.Length; foreach (var engine in engines) engine.Dispose();
                if (existing > 1) throw new InvalidOperationException("No independent native instance slot; no existing process was changed.");
                var result = await native.CaptureRayAudit(source, Path.Combine(folder, "native"), job, inputs, CancellationToken.None);
                var unchanged = hash == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
                executions.Add(new { id, lens, c, result, unchanged, existingEngines = existing, coreSha256 = coreHash, budgetSeconds = 120, captureScreenshots = false });
                JsonFiles.Write(Path.Combine(output, "executions.json"), executions);
                if (result.ExitCode != 0 || !unchanged) throw new InvalidOperationException("Native fixed-ray capture failed.");
                var optic = imported.Configurations[c - 1];
                JsonFiles.Write(Path.Combine(folder, "source.optic.json"), optic.ToSnapshot());
                var histories = pupils.Select(p => optic.SequentialRayTracer.Trace(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, p.X, p.Y,
                    optic.Wavelengths[0].Micrometers, optic.RayAimingEnabled)).RayHistories.Single()).ToArray();
                JsonFiles.Write(Path.Combine(folder, "core-rays.json"), histories);
                Console.WriteLine($"Native {id}: {result.ExitCode}; {pupils.Count} fixed rays");
            }
        }
    }
}
