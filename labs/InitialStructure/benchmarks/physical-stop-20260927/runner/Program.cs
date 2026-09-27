using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.InitialStructure.Engine;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var inputs = JsonDocument.Parse(File.ReadAllText(args[0]));
var fields = Enumerable.Range(0, 11).Select(i => (X: 0.0, Y: i / 10.0)).ToArray();
var integration = ApertureSampler.GenerateGaussianQuadrature(6, 24).ToArray();
var boundary = new[] { new PupilSample(0, 0, 1) }.Concat(ApertureSampler.Generate(24, PupilSampling.Ring)).ToArray();
var rows = new List<object>();
foreach (var input in inputs.RootElement.EnumerateArray())
    foreach (var candidate in input.GetProperty("Candidates").EnumerateArray())
    {
        var snapshot = candidate.GetProperty("Optic").Deserialize<OpticSnapshot>(json)!;
        var optic = Optic.FromSnapshot(snapshot);
        var originalFingerprint = ContentFingerprint.Compute(optic.ToSnapshot());
        var originalPupils = ApertureSampler.GenerateGaussianQuadrature(10, 32).ToArray();
        var originalSpots = Enumerable.Range(0, 21).Select(i => SpotMetricEvaluator.EvaluatePupilSamples(
            optic, 0, i / 20.0, originalPupils, includeSurfaceTransmission: false)).ToArray();
        var originalRms = originalSpots.All(s => s.VignettedRayCount == 0 && s.Metrics is not null)
            ? originalSpots.Max(s => s.Metrics!.RmsSpotRadius) : (double?)null;
        if (originalFingerprint != ContentFingerprint.Compute(optic.ToSnapshot()))
            throw new InvalidOperationException("Original prescription changed during analysis.");
        var stop = optic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        var oldRadius = stop.SemiDiameter;
        var calibration = PhysicalStopCalibration.Apply(optic);
        optic.RayAimingEnabled = true;
        var floated = Optic.FromSnapshot(optic.ToSnapshot());
        floated.Aperture.Kind = ApertureKind.FloatByStopSize;
        var floatError = Math.Abs(floated.Paraxial.EstimateEntrancePupilDiameter() - calibration.EntrancePupilDiameterMillimeters);
        var groups = Enumerable.Range(0, (optic.SurfaceGroup.Items.Count - 2) / 2)
            .Select(i => (IReadOnlyList<int>)new[] { 2 * i + 1, 2 * i + 2 }).ToArray();
        var sizing = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, groups, fields,
            integration.Concat(boundary).ToArray(), 1.15, preserveStopAperture: true);
        var withoutApertures = Optic.FromSnapshot(optic.ToSnapshot());
        var unconstrainedSizing = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(withoutApertures, groups, fields,
            integration.Concat(boundary).ToArray(), 1.15);
        var generatedZeroIntensity = fields.Sum(f => optic.Wavelengths.Where(w => w.Weight > 0).Sum(w =>
            optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(f.X, f.Y, w.Micrometers,
                integration.Concat(boundary), aimAtStop: true, applyVignettingFactors: false)
                .Rays.Count(ray => ray.Intensity <= 0)));
        // Remove only the calibrated stop on a private diagnostic snapshot. Report both
        // missing-ray counts; this diagnosis never supplies candidate acceptance or a score.
        var diagnostic = Optic.FromSnapshot(optic.ToSnapshot());
        var diagnosticStop = diagnostic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        diagnosticStop.SemiDiameterDefinesPhysicalAperture = false;
        diagnosticStop.PhysicalAperture = null;
        var physicalSpots = fields.Select(f => SpotMetricEvaluator.EvaluatePupilSamples(optic, f.X, f.Y, integration,
            includeSurfaceTransmission: false)).ToArray();
        var physicalChecks = fields.Select(f => SpotMetricEvaluator.EvaluatePupilSamples(optic, f.X, f.Y, boundary,
            includeSurfaceTransmission: false)).ToArray();
        var diagnosticChecks = fields.Select(f => SpotMetricEvaluator.EvaluatePupilSamples(diagnostic, f.X, f.Y, boundary,
            includeSurfaceTransmission: false)).ToArray();
        var row = new
        {
            Id = input.GetProperty("Id").GetString(),
            CandidateId = candidate.GetProperty("CandidateId").GetString(),
            OriginalSnapshotFingerprint = ContentFingerprint.Compute(snapshot),
            FrozenRmsMillimeters = candidate.GetProperty("RmsMillimeters").GetDouble(),
            OriginalRecalculatedRmsMillimeters = originalRms,
            OriginalStopRadiusMillimeters = oldRadius,
            CalibratedStopRadiusMillimeters = stop.SemiDiameter,
            FloatedPupilErrorMillimeters = floatError,
            Sizing = sizing,
            WithoutAperturesCompleted = unconstrainedSizing.CompletedRays,
            GeneratedZeroIntensityRays = generatedZeroIntensity,
            IntegrationAttempted = physicalSpots.Sum(s => s.RayCount),
            IntegrationMissing = physicalSpots.Sum(s => s.VignettedRayCount),
            BoundaryAttempted = physicalChecks.Sum(s => s.RayCount),
            BoundaryMissing = physicalChecks.Sum(s => s.VignettedRayCount),
            WithoutStopBoundaryMissing = diagnosticChecks.Sum(s => s.VignettedRayCount),
            WorstRmsMillimeters = sizing.Applied && physicalChecks.All(s => s.VignettedRayCount == 0)
                && physicalSpots.All(s => s.VignettedRayCount == 0 && s.Metrics is not null)
                ? physicalSpots.Max(s => s.Metrics!.RmsSpotRadius) : (double?)null
        };
        rows.Add(row);
        Console.WriteLine($"{row.CandidateId}: envelope={sizing.CompletedRays}/{sizing.AttemptedRays}, interior missing={row.IntegrationMissing}, boundary missing={row.BoundaryMissing}, without stop={row.WithoutStopBoundaryMissing}");
    }
File.WriteAllText(args[1], JsonSerializer.Serialize(new
{
    Scope = "Diagnostic conversion of 30 frozen v16 snapshots, not a new search or candidate acceptance. All metrics from shared Core; no Zemax comparison.",
    InputSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(args[0]))),
    CoreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))),
    Rows = rows
}, json));
