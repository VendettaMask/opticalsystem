using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class RemainingLensControls
{
    internal static void SpotApodization(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var all = JsonDocument.Parse(File.ReadAllText(WideSchmidtRepair.Baseline + "/analysis-results.json"));
        var rows = new List<object>();
        foreach (var lens in new[] { "Interconnects/2 Ball lens interconnect.ZMX", "Interconnects/Ball coupling.ZMX" })
        {
            var item = all.RootElement.EnumerateArray().First(i => i.GetProperty("lens").GetString() == lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Spot Diagram");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            foreach (var factor in new[] { 0d, .5, 1, 2 })
            {
                var optic = WorkbenchExecutor.Import(Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens)).GetAwaiter().GetResult().Configurations[0];
                optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, factor);
                var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
                rows.Add(new { lens, factor, data });
            }
        }
        JsonFiles.Write(Path.Combine(output, "controls.json"), rows);
    }
}
