using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class TestingPhaseControls
{
    internal static async Task Run(string repository, string output, string coreHash)
    {
        var folder = Path.Combine(repository, "validation/rechecks/2026-11-01/testing-three/native-phase");
        var native = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model-controls.json")).GetProperty("rows").EnumerateArray().ToArray();
        var optic = (await WorkbenchExecutor.Import(Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Optical testing/Twyman Green Interferometer.zmx"))).Configurations[0];
        var pupils = native.Where(r => r.GetProperty("input").GetProperty("code").GetString() == "OPDX")
            .Select(r => (r.GetProperty("input").GetProperty("data3").GetDouble(), r.GetProperty("input").GetProperty("data4").GetDouble())).ToArray();
        var phase = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), optic.Wavelengths[0], pupils, aimAtStop: false);
        var rows = pupils.Select((p, i) =>
        {
            double Value(string code) => native.Single(r => r.GetProperty("input").GetProperty("code").GetString() == code
                && r.GetProperty("input").GetProperty("data3").GetDouble() == p.Item1 && r.GetProperty("input").GetProperty("data4").GetDouble() == p.Item2).GetProperty("value").GetDouble();
            var history = optic.SequentialRayTracer.Trace(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, p.Item1, p.Item2, optic.Wavelengths[0].Micrometers)).RayHistories.Single();
            var nativePath = Value("OPTH"); var nativeOpd = Value("OPDX");
            return new { px = p.Item1, py = p.Item2, nativePath, corePath = history.Last().CumulativeOpticalPathLength,
                corePathFromFirst = history.Last().CumulativeOpticalPathLength - history[1].CumulativeOpticalPathLength,
                nativeOpd, coreOpd = phase.Samples[i].OpdWaves };
        }).ToArray();
        JsonFiles.Write(Path.Combine(output, "results.json"), new { coreHash, nativeExitPupil = native.First().GetProperty("value").GetDouble(),
            coreExitPupil = optic.Paraxial.EstimateExitPupilLocation(), sphere = phase.ReferenceSphere, rows });
        foreach (var row in rows) Console.WriteLine(JsonSerializer.Serialize(row));
    }
}
