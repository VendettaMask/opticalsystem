using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

internal static class FrozenReplay
{
    internal sealed record Input(string Run, InitialStructureSpecification Specification,
        FlatStartFamily Family, CandidateSnapshot Candidate, DesignEvaluation FinalValidation);

    public static async Task Run(string inputPath, string outputDirectory, JsonSerializerOptions json)
    {
        var inputJson = new JsonSerializerOptions(json) { PropertyNameCaseInsensitive = true };
        inputJson.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(inputPath), inputJson)!;
        Directory.CreateDirectory(outputDirectory);
        var results = new List<object>();
        foreach (var input in inputs)
        {
            var candidate = input.Candidate;
            var optic = Optic.FromSnapshot(candidate.Optic);
            var problem = new FlatStartDesignProblem(input.Specification, input.Family.ElementCount, candidate.Optic, input.Family);
            var fresh = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default, true);
            if (ContentFingerprint.Compute(fresh with { Residuals = [] }) != ContentFingerprint.Compute(input.FinalValidation)
                || ContentFingerprint.Compute(fresh.EvaluatedOptic!) != candidate.OpticFingerprint
                || fresh.Residuals.Sum(value => value * value) != fresh.Merit)
                throw new InvalidDataException("Frozen candidate recalculation differs: " + candidate.CandidateId);
            var path = Path.Combine(outputDirectory, input.Run + "-" + candidate.CandidateId + ".staropt");
            await new CandidateExportService().ExportStarOptAsync(candidate, path);
            var loaded = await StarOptProjectStore.LoadAsync(path);
            if (ContentFingerprint.Compute(loaded.Configurations[0].ToSnapshot()) != candidate.OpticFingerprint)
                throw new InvalidDataException("Frozen export differs: " + candidate.CandidateId);
            var fields = Enumerable.Range(0, 41).Select(index =>
            {
                var result = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, 0, index / 40.0,
                    ApertureSampler.GenerateGaussianQuadrature(16, 48),
                    new[] { new PupilSample(0, 0, 1) }.Concat(ApertureSampler.Generate(72, PupilSampling.Ring)).ToArray(),
                    includeSurfaceTransmission: false);
                return new
                {
                    NormalizedField = index / 40.0,
                    RmsMillimeters = result.Integration.Metrics?.RmsSpotRadius,
                    result.MaximumRadius,
                    LostRays = result.Integration.VignettedRayCount + result.Checks.VignettedRayCount,
                    AttemptedRays = result.Integration.RayCount + result.Checks.RayCount
                };
            }).ToArray();
            results.Add(new
            {
                input.Run,
                candidate.CandidateId,
                candidate.OpticFingerprint,
                SameSettingsIdentical = true,
                ExportIdentical = true,
                RecordedWorstRmsMillimeters = fresh.Fields.Max(field => field.RmsRadiusMillimeters),
                DenseWorstRmsMillimeters = fields.Max(field => field.RmsMillimeters),
                DenseMaximumRadiusMillimeters = fields.Max(field => field.MaximumRadius),
                DenseLostRays = fields.Sum(field => field.LostRays),
                Fields = fields
            });
            Console.WriteLine($"Replayed {input.Run}/{candidate.CandidateId}");
        }
        File.WriteAllText(Path.Combine(outputDirectory, "replay.json"), JsonSerializer.Serialize(results, json));
    }
}
