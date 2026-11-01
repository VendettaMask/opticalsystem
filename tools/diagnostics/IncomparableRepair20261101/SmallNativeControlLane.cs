using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class SmallNativeControlLane
{
    internal static async Task Run(string repository, string study)
    {
        var deadline = DateTime.UtcNow.AddSeconds(120);
        FileStream lease;
        while (true)
        {
            try { lease = new(Path.Combine(repository, "artifacts/validation/.background-small-native-controls.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); break; }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(250); }
        }
        using (lease)
        {
            var execution = new List<object>();
            var jobs = JsonFiles.Read<JsonElement[]>(Path.Combine(study, "jobs.json"));
            var models = JsonFiles.Read<JsonElement[]>(Path.Combine(study, "models.json"));
            if (jobs.Length is < 1 or > 10) throw new InvalidDataException("Only bounded tiny chief-ray controls may use this lane");
            foreach (var job in jobs)
            {
                var id = job.GetProperty("id").GetString()!;
                var requestPath = Path.Combine(study, job.GetProperty("request").GetString()!);
                var request = JsonFiles.Read<JsonElement>(requestPath);
                var model = models.Single(m => m.GetProperty("id").GetString() == id);
                var input = request.GetProperty("input").GetString()!;
                var host = Path.GetFullPath(Path.Combine(repository, job.GetProperty("frozenHost").GetString()!));
                var output = Path.Combine(study, id);
                string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
                var bodyLength = model.GetProperty("bodyLength").GetDouble();
                var hasOperands = request.TryGetProperty("modelOperands", out var operands) && operands.ValueKind == JsonValueKind.Array;
                var operandCount = hasOperands ? operands.GetArrayLength() : 0;
                var tinyRays = request.GetProperty("adapter").GetString() == "surface-model-controls" && operandCount is > 0 and <= 64
                    && operands.EnumerateArray().All(p => p.GetProperty("data1").GetDouble() == 0 && p.GetProperty("data2").GetDouble() == 0
                        && Math.Abs(p.GetProperty("data3").GetDouble()) <= .001 && Math.Abs(p.GetProperty("data4").GetDouble()) <= .001);
                var profileAxis = request.TryGetProperty("zemaxSettings", out var profileSettings) && profileSettings.TryGetProperty("ShowIndexVs", out var axisValue)
                    ? axisValue.GetString() : null;
                var indexCurve = profileAxis is "X" or "Y" or "Z" && request.GetProperty("adapter").GetString() == "contract"
                    && request.TryGetProperty("analysisType", out var type) && type.GetString() == "GrinProfile"
                    && request.TryGetProperty("zemaxSettings", out var settings)
                    && new[] { "Minimum" + profileAxis, "Maximum" + profileAxis }.Concat(new[] { "X", "Y", "Z" }
                        .Where(axis => axis != profileAxis).Select(axis => axis + "_Value"))
                        .All(key => settings.TryGetProperty(key, out var value) && double.IsFinite(value.GetDouble()) && Math.Abs(value.GetDouble()) <= 4);
                var extendedTinyAxis = bodyLength <= 1 && model.TryGetProperty("maximumStep", out var step)
                    && step.GetDouble() >= .00001 && operandCount * bodyLength / step.GetDouble() <= 2_000_000;
                if ((!tinyRays && !indexCurve) || request.GetProperty("captureScreenshots").GetBoolean()
                    || request.GetProperty("formalAcceptanceRequest").GetBoolean()
                    || model.GetProperty("nodes").GetInt32() > (indexCurve ? 21 : 8)
                    || bodyLength > .02 && !(indexCurve && bodyLength <= 4) && !extendedTinyAxis
                    || Directory.Exists(output) || !host.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || Hash(host) != job.GetProperty("frozenHostSha256").GetString()
                    || Hash(input) != job.GetProperty("inputSha256").GetString())
                    throw new InvalidDataException("Small read-only control, frozen host, source or fresh output precondition failed");
                // Leave all existing applications alone. At most one other
                // engine may be open before this independent headless launch.
                deadline = DateTime.UtcNow.AddSeconds(120);
                while (CountEngines() > 1 && DateTime.UtcNow < deadline) await Task.Delay(250);
                var existingEngines = CountEngines();
                if (existingEngines > 1) throw new InvalidOperationException("No independent native instance slot; no application was closed");
                var startedUtc = DateTime.UtcNow;
                Console.WriteLine("Independent small read-only control: " + id);
                var result = await ProcessIsolation.Run(host, [requestPath, output], study, 120, CancellationToken.None,
                    new Dictionary<string, string> { ["PATH"] = request.GetProperty("zosApiPath").GetString() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") });
                var sourceUnchanged = Hash(input) == job.GetProperty("inputSha256").GetString();
                execution.Add(new { id, result, sourceUnchanged, budgetSeconds = 120, host, hostSha256 = Hash(host),
                    startedUtc, elapsedSeconds = (DateTime.UtcNow - startedUtc).TotalSeconds, existingEngines,
                    mode = "IndependentReadOnlySmallAxialControls", captureScreenshots = false });
                JsonFiles.Write(Path.Combine(study, "execution.json"), execution);
                if (result.ExitCode != 0 || !sourceUnchanged) throw new InvalidOperationException("Small control failed; raw failure retained");
            }
        }
        static int CountEngines()
        {
            var processes = Process.GetProcessesByName("OpticStudio");
            var count = processes.Length;
            foreach (var p in processes) p.Dispose();
            return count;
        }
    }
}
