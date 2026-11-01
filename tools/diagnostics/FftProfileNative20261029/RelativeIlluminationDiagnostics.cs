using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;

internal static class RelativeIlluminationDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        var archivePath = Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip");
        if (Hash(archivePath) != "235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1") throw new InvalidDataException("Frozen source changed");
        using var zip = ZipFile.OpenRead(archivePath);
        byte[] Read(string name)
        {
            using var input = zip.GetEntry(name)!.Open(); using var stream = new MemoryStream(); input.CopyTo(stream); return stream.ToArray();
        }
        var ledgerPath = Path.Combine(repository, "validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json");
        var ledger = JsonFiles.Read<JsonElement>(ledgerPath);
        var rows = new List<object>();
        foreach (var item in ledger.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("request").GetProperty("canonicalAnalysisKey").GetString() == "Relative Illumination"))
        {
            var id = item.GetProperty("id").GetString()!;
            var request = item.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var model = Regex.Match(id, @"^\d{4}-(?<model>.+?)-primary-").Groups["model"].Value;
            var snapshot = Read("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json");
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(snapshot, JsonFiles.Options)!);
            var directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "original-native-error.json"), Read("artifacts/validation/afocal-recompare-20261026/expanded/" + id + "/native/error.json"));
            object result;
            try
            {
                var data = new WorkbenchRuntime(optic).BuildAnalysisData("Relative Illumination", request.WorkbenchSettings);
                JsonFiles.Write(Path.Combine(directory, "workbench.json"), data);
                result = new { outcome = data.Outcome.ToString(), data.OutcomeReason, curves = data.PlotSeries.Count, numericRiPresent = data.Values.ContainsKey("RelativeIllumination") };
            }
            catch (Exception exception)
            {
                result = new { outcome = "Exception", type = exception.GetType().FullName, exception.Message };
                File.WriteAllText(Path.Combine(directory, "exception.txt"), exception.ToString());
            }
            rows.Add(new { id, request.Fingerprint, sourceSha256 = item.GetProperty("sourceSha256").GetString(), snapshotSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshot)), originalConclusion = item.GetProperty("conclusion").GetString(), originalEffectiveTolerances = item.GetProperty("effectiveTolerances"), request, result, formalNumericalPass = false });
            Console.WriteLine(id + ": " + JsonSerializer.Serialize(result));
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { coreSha256 = Hash(typeof(Optic).Assembly.Location), archiveSha256 = Hash(archivePath), ledgerSha256 = Hash(ledgerPath), originalInputsAndGatesChanged = false, nativeNumericalPassesAdded = 0 });
        return 0;
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
