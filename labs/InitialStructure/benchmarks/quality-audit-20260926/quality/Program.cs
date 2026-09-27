using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(args[0]), json)!;
var rows = new List<object>();
foreach (var input in inputs)
{
    var problem = new FlatStartDesignProblem(input.Specification, input.Family.ElementCount, input.Initial, input.Family);
    var entry = problem.Evaluate(Optic.FromSnapshot(input.Initial), FlatStartDesignProblem.FullStage, true, default);
    if (!entry.MeetsTargets) continue;
    var pupilPlans = new[] { (10, 32), (16, 48), (24, 64) };
    var scans = pupilPlans.Select(plan =>
    {
        var optic = Optic.FromSnapshot(input.Initial);
        var pupils = ApertureSampler.GenerateGaussianQuadrature(plan.Item1, plan.Item2);
        var fields = Enumerable.Range(0, 41).Select(i =>
        {
            var spot = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, i / 40.0, pupils, includeSurfaceTransmission: false);
            return new { NormalizedField = i / 40.0, spot.Metrics, spot.VignettedRayCount };
        }).ToArray();
        return new { RadialSamples = plan.Item1, AzimuthalSamples = plan.Item2, WorstRms = fields.Max(f => f.Metrics!.RmsSpotRadius), Fields = fields };
    }).ToArray();
    var distortion = new DistortionAnalysis(Optic.FromSnapshot(input.Initial), numPoints: 41, distortionType: "f-tan", displayMode: "percent").GenerateData();
    var pupil = new PupilAberrationAnalysis(Optic.FromSnapshot(input.Initial), numPoints: 65).GenerateData();
    var mtfs = new[] { 65, 97 }.Select(density =>
    {
        var result = new GeometricMtfAnalysis(Optic.FromSnapshot(input.Initial), numRays: density,
            distribution: "uniform", numPoints: 11, maximumFrequency: 50, scale: false).GenerateData();
        return new { PupilAxisSamples = density, Method = "Unscaled geometric MTF; not FFT/Huygens diffraction MTF", Data = result };
    }).ToArray();
    rows.Add(new
    {
        input.Id,
        input.Family.ElementCount,
        Entry = entry.Objective,
        FrozenWorstRms = entry.Fields.Max(f => f.RmsRadiusMillimeters),
        GaussianScans = scans,
        Distortion = distortion,
        PupilAberration = pupil,
        GeometricMtf = mtfs
    });
    File.WriteAllText(args[1], JsonSerializer.Serialize(rows, json));
    Console.WriteLine($"{input.Id}: frozen RMS={entry.Fields.Max(f => f.RmsRadiusMillimeters):G6}; Gaussian 24x64 RMS={scans[^1].WorstRms:G6}");
}

internal sealed record Input(string Id, InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial);
