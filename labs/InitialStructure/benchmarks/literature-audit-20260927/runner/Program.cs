using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
if (args[0] == "--capture")
{
    var cases = new List<AuditCase>();
    foreach (var path in args.Skip(2))
    {
        var checkpoint = await new FlatStartSearchCheckpointStore().LoadAsync(path);
        var retained = FlatStartCandidateArchive.Select(checkpoint.Specification, checkpoint.Options, checkpoint.Trials);
        var roots = checkpoint.Trials.Where(trial => trial.ParentTrialId is null).ToArray();
        var children = checkpoint.Trials.Where(trial => trial.ParentTrialId is not null).ToArray();
        cases.Add(new(Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), checkpoint.Algorithm.Version,
            checkpoint.Specification.FNumber, checkpoint.Specification.MaximumFieldAngleDegrees,
            checkpoint.ChargedEvaluations,
            Enumerable.Range(checkpoint.Specification.MinimumElementCount,
                checkpoint.Specification.MaximumElementCount - checkpoint.Specification.MinimumElementCount + 1)
                .Select(count => new BranchAllocation(count,
                    roots.Count(trial => trial.Family.ElementCount == count),
                    roots.Count(trial => trial.Family.ElementCount == count && trial.FinalValidation is { IsFeasible: true }),
                    children.Count(trial => trial.Family.ElementCount == count),
                    children.Where(trial => trial.Family.ElementCount == count).Sum(trial => trial.ChargedEvaluations))).ToArray(),
            retained.Select(candidate => new AuditCandidate(candidate.CandidateId, candidate.Status.ToString(),
                candidate.Evaluation.RmsSpotRadiusMillimeters, candidate.Optic)).ToArray()));
    }
    File.WriteAllText(args[1], JsonSerializer.Serialize(cases, json));
    return;
}
if (args[0] != "--audit") throw new ArgumentException("Use --capture output checkpoints... or --audit inputs output.");
var inputs = JsonSerializer.Deserialize<AuditCase[]>(File.ReadAllText(args[1]), json)!;
var rows = new List<object>();
var fields = Enumerable.Range(0, 11).Select(index => index / 10.0).ToArray();
var pupils = new[] { new PupilSample(0, 0, 1) }.Concat(ApertureSampler.Generate(24, PupilSampling.Ring)).ToArray();
foreach (var input in inputs)
    foreach (var candidate in input.Candidates)
    {
        var optic = Optic.FromSnapshot(candidate.Optic);
        var before = ContentFingerprint.Compute(optic.ToSnapshot());
        var distortion = new DistortionAnalysis(optic, numPoints: 41, distortionType: "f-tan",
            wavelengthNumber: 0, scanDirection: "+y", displayMode: "percent", referenceFieldNumber: 1,
            ignoreVignettingFactors: true).GenerateData();
        var maxIncidence = 0.0;
        var missing = 0;
        var attempts = 0;
        object? worst = null;
        for (var wave = 1; wave <= optic.Wavelengths.Count; wave++)
            foreach (var field in fields)
                foreach (var pupil in pupils)
                {
                    attempts++;
                    var data = new SingleRayTraceAnalysis(optic, fieldNumber: 0, hx: 0, hy: field,
                        wavelengthNumber: wave, px: pupil.X, py: pupil.Y, useRayAiming: optic.RayAimingEnabled).GenerateData();
                    if (Convert.ToInt32(data.Values["VignettedSurface"]) != 0
                        || Convert.ToInt32(data.Values["LastSurface"]) != optic.SurfaceGroup.Items.Count - 1) missing++;
                    var rays = (double[][])data.Values["RealRayData"];
                    foreach (var ray in rays.Where(ray => ray[0] > 0 && ray[0] < optic.SurfaceGroup.Items.Count - 1))
                        if (double.IsFinite(ray[10]) && ray[10] > maxIncidence)
                        {
                            maxIncidence = ray[10];
                            worst = new { FieldFraction = field, pupil.X, pupil.Y, WavelengthNumber = wave, SurfaceNumber = ray[0] };
                        }
                }
        var stop = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var footprint = new[] { 1, optic.Fields.Count }.Select(field => new FootprintDiagramAnalysis(optic,
            rayDensity: 20, surfaceNumber: stop, wavelengthNumber: 0, fieldNumber: field, deleteVignetted: true)
            .GenerateData()).Select(data => data.Values).ToArray();
        if (before != ContentFingerprint.Compute(optic.ToSnapshot())) throw new InvalidOperationException("Analysis changed the input snapshot.");
        rows.Add(new
        {
            input.Id,
            candidate.CandidateId,
            candidate.Status,
            candidate.RmsMillimeters,
            optic.RayAimingEnabled,
            MaximumAbsoluteDistortionPercent = distortion.Values["MaximumAbsoluteDistortionPercent"],
            DistortionSeries = distortion.PlotSeries,
            MaximumSampledIncidenceDegrees = maxIncidence,
            WorstIncidenceSample = worst,
            AttemptedIncidenceRays = attempts,
            IncompleteIncidenceRays = missing,
            StopSurface = stop,
            StopFootprints = footprint
        });
        File.WriteAllText(args[2], JsonSerializer.Serialize(new
        {
            InputSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(args[1]))),
            CoreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))),
            Scope = "Workbench Core recalculation; original snapshots/settings; sampled incidence/footprints, not throughput or manufacturing certification; no Zemax comparison",
            Rows = rows
        }, json));
        Console.WriteLine($"{input.Id} {candidate.CandidateId}: distortion={distortion.Values["MaximumAbsoluteDistortionPercent"]:F3}%, incidence={maxIncidence:F3} deg, missing={missing}/{attempts}");
    }

internal sealed record AuditCase(string Id, string CheckpointSha256, string AlgorithmVersion,
    double FNumber, double HalfFieldDegrees, int ChargedEvaluations, BranchAllocation[] Branches, AuditCandidate[] Candidates);
internal sealed record BranchAllocation(int ElementCount, int Roots, int PhysicallyFeasibleRoots, int Neighborhoods, int ChargedNeighborhoodEvaluations);
internal sealed record AuditCandidate(string CandidateId, string Status, double? RmsMillimeters, OpticSnapshot Optic);
