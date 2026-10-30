using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class InitialSearchStrategyTests
{
    [Theory]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(8, 16)]
    [InlineData(16, 24)]
    public void SignCoverageIsBoundedDistinctAndReproducible(int count, int maximum)
    {
        var patterns = StructureCompetitionSearch.PowerSignPatterns(count, maximum);
        Assert.Equal(maximum, patterns.Length);
        Assert.Equal(maximum, patterns.Select(p => string.Join(",", p)).Distinct().Count());
        Assert.All(patterns, p => { Assert.Equal(count, p.Length); Assert.All(p, x => Assert.True(x is -1 or 1)); });
        Assert.Equal(ContentFingerprint.Compute(patterns), ContentFingerprint.Compute(StructureCompetitionSearch.PowerSignPatterns(count, maximum)));
        Assert.All(patterns, p => Assert.Equal(2 * count - 1, FixedStopDelano.ProposePowerSignShape(p).Length));
    }

    [Fact]
    public void CoreElementPowerAgreesWithIndependentThickLensReference()
    {
        var plan = AutonomousDesignExamples.ThreeElement();
        var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints); var optic = source.CreateOptic();
        foreach (var (lens, map) in source.Topology.Elements.Zip(source.Elements))
        {
            var wave = source.Topology.Wavelengths.Single(w => w.IsPrimary).Nanometers;
            var n = optic.Materials.Resolve(lens.Material).RefractiveIndex(wave);
            var c1 = lens.Front.Profile.CurvaturePerMillimeter; var c2 = lens.Back.Profile.CurvaturePerMillimeter;
            var reference = (n - 1) * (c1 - c2) + (n - 1) * (n - 1) * lens.ThicknessMillimeters * c1 * c2 / n;
            Assert.Equal(reference, optic.Paraxial.ElementOpticalPower(map.FrontIndex, map.BackIndex, wave), 12);
        }
    }

    [Fact]
    public void CoreQuickUnitsMatchCoefficientConversionAndAspheresReturnNoScore()
    {
        var plan = AutonomousDesignExamples.ThreeElement();
        var topology = plan.Seeds[0]; var first = topology.Elements[0];
        topology = topology with
        {
            Elements = topology.Elements.SetItem(0, first with
            { Front = first.Front with { Profile = first.Front.Profile with { Kind = TopologyProfileKind.Standard, Conic = 0, MaximumEvenPower = 0, Terms = [] } } })
        };
        var source = TopologyCompiler.Compile(topology, plan.Constraints);
        var optic = source.CreateOptic(); const double wavelength = .5876;
        var q = SeidelQuickEvaluator.Evaluate(optic, wavelength);
        Assert.True(q.Supported); Assert.Equal("waves", q.Units);
        var coefficients = SeidelMetrics.Calculate(optic, wavelength).Total;
        Assert.Equal(coefficients.Spherical / (8 * wavelength / 1000), q.CoefficientsInWaves![0], 10);
        Assert.Equal(coefficients.ToArray().Length, q.CoefficientsInWaves.Length);
        Assert.Equal(q.CoefficientsInWaves.Max(Math.Abs), q.Score);
        optic.SurfaceGroup.Items[source.Elements[0].FrontIndex].Geometry = new StandardGeometry(.02, -.5);
        var unsupported = SeidelQuickEvaluator.Evaluate(optic, wavelength);
        Assert.False(unsupported.Supported); Assert.Null(unsupported.Score); Assert.Null(unsupported.CoefficientsInWaves);
        Assert.Throws<ArgumentOutOfRangeException>(() => SeidelQuickEvaluator.Evaluate(optic, double.NaN));
    }

    [Fact]
    public void NominalFailureDiagnosticsPreserveSnapshotAndIdentifyClippingMissAndTir()
    {
        var optic = Plate(); var tracer = optic.SequentialRayTracer;
        var before = JsonSerializer.Serialize(optic.ToSnapshot(), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        var ray = new RealRay(new(2, 0, -1), new(0, 0, 1), 587.6);
        var clip = tracer.DiagnoseFirstFailure(ray);
        Assert.Equal(SurfaceTraceFailure.ApertureClipping, clip.Failure); Assert.Equal(1, clip.SurfaceIndex);
        Assert.Equal(2, clip.LocalInterceptMillimeters!.Value.X); Assert.Equal(.5, clip.ClearSemiDiameterMillimeters);
        var after = JsonSerializer.Serialize(optic.ToSnapshot(), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        Assert.Equal(before, after);
        optic.SurfaceGroup.Items[1].PhysicalAperture = null;
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(.5);
        Assert.Equal(SurfaceTraceFailure.IntersectionMiss, tracer.DiagnoseFirstFailure(new(new(5, 0, -1), new(0, 0, 1), 587.6)).Failure);
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(1, 1, [0, .00001]);
        var domain = tracer.DiagnoseFirstFailure(new(new(5, 0, -1), new(0, 0, 1), 587.6));
        Assert.Equal(SurfaceTraceFailure.DomainError, domain.Failure);
        Assert.Equal("DomainError", domain.Detail); Assert.Null(domain.LocalInterceptMillimeters);
        optic = Plate(); optic.SurfaceGroup.Items[1].PhysicalAperture = null;
        optic.SurfaceGroup.Items[0].MaterialAfter = new ConstantIndexMaterial("Dense", 1.5);
        optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        var tir = optic.SequentialRayTracer.DiagnoseFirstFailure(new(new(0, 0, -1), new(Math.Sqrt(3) / 2, 0, .5), 587.6));
        Assert.Equal(SurfaceTraceFailure.TotalInternalReflection, tir.Failure); Assert.Equal(1, tir.SurfaceIndex);
    }

    [Fact]
    public void DiagnosticCancellationBudgetAndInvalidLaunchAreDistinct()
    {
        var tracer = Plate().SequentialRayTracer; var ray = new RealRay(new(0, 0, -1), new(0, 0, 1), 587.6);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(SurfaceTraceFailure.Cancelled, tracer.DiagnoseFirstFailure(ray, cancelled.Token).Failure);
        Assert.Equal(SurfaceTraceFailure.InvalidLaunchOrAiming, tracer.DiagnoseFirstFailure(ray with { Intensity = 0 }).Failure);
        using var budget = SequentialTraceMeasurement.BeginCpuBudget(1);
        tracer.DiagnoseFirstFailure(ray);
        Assert.Equal(SurfaceTraceFailure.RayBudgetExhausted, tracer.DiagnoseFirstFailure(ray).Failure);
        Assert.Equal(1, budget.RayCount);
    }

    [Fact]
    public void PrecheckOnlyProvesExplicitAxialContradictionsAndReserveTracksActualProtocol()
    {
        var (plan, options) = StructureCompetitionTests.Specification();
        Assert.All(StructureSearchDiagnostics.Precheck(plan, options.Generation!), p => Assert.False(p.ProvenInfeasible));
        var impossible = plan with { Seeds = [plan.Seeds[0] with { Image = plan.Seeds[0].Image with { ZMillimeters = 1 } }] };
        Assert.Contains(StructureSearchDiagnostics.Precheck(impossible, options.Generation!), p => p.ProvenInfeasible && p.Actual > p.Limit);
        var compiled = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var one = StructureSearchDiagnostics.EstimateFinalRays(compiled, 1);
        Assert.Equal(2 * one, StructureSearchDiagnostics.EstimateFinalRays(compiled, 2));
        var mono = compiled.Topology with { Wavelengths = [compiled.Topology.Wavelengths.Single(w => w.IsPrimary)] };
        Assert.Equal(one, 3 * StructureSearchDiagnostics.EstimateFinalRays(TopologyCompiler.Compile(mono, plan.Constraints), 1));
    }

    [Fact]
    public void NullStrategyKeepsLegacySerializationAndVersionsAreValidated()
    {
        var (_, options) = StructureCompetitionTests.Specification();
        Assert.DoesNotContain("SearchStrategy", JsonSerializer.Serialize(options));
        foreach (var strategy in new[] {
            new StructureSearchStrategy { Version = 2 },
            new StructureSearchStrategy { PowerSigns = new() { Version = 2 } },
            new StructureSearchStrategy { QuickEvaluation = new() { CoordinateStep = double.NaN } },
            new StructureSearchStrategy { FinalRayRecoveryFactor = 0 },
            new StructureSearchStrategy { ChallengeCandidatesPerRound = 3 } })
            Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Competition = options.Competition! with { SearchStrategy = strategy } }));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExperimentalSearchAccountsForWorkAndOnlyOriginalAssessmentQualifies(bool signs, bool quick)
    {
        var (plan, options) = StructureCompetitionTests.Specification(); var before = ContentFingerprint.Compute(plan);
        options = options with
        {
            Competition = options.Competition! with
            {
                SearchStrategy = new()
                { PowerSigns = signs ? new() : null, QuickEvaluation = quick ? new() : null }
            }
        };
        var result = ParaxialSeedSearch.Run(plan, options); var competition = result.Competition!;
        Assert.Equal("initial-structure-competition-m1-v1", result.Algorithm);
        Assert.Equal(before, ContentFingerprint.Compute(plan)); Assert.NotEmpty(competition.SearchEvidence!.Costs);
        Assert.InRange(result.Work.FunctionEvaluations, 1, options.MaximumFunctionEvaluations);
        Assert.Equal(result.Work.FunctionEvaluations, competition.Candidates.Sum(c => c.Work.FunctionEvaluations));
        Assert.Equal(result.Work.TracedRays, competition.Candidates.Sum(c => c.Work.TracedRays));
        Assert.All(competition.Candidates, c =>
        {
            Assert.InRange(c.Work.FunctionEvaluations, 0, options.MaximumEvaluationsPerStart);
            Assert.True(c.Work.Elapsed > TimeSpan.Zero);
            if (c.SearchEvidence!.RequestedPowerSigns.Length > 0 && c.Topology is not null)
                Assert.True(c.SearchEvidence.SignsVerified);
            if (c.OriginalAssessment is { } a)
            { Assert.Equal(plan.Targets, a.Targets); Assert.Equal(a.Completed && a.MeetsTargets, competition.QualifiedIds.Contains(c.Id)); }
        });
        Assert.All(competition.Rounds.GroupBy(r => r.StageIndex), group => Assert.All(group, row => Assert.Equal(group.First().Allocation, row.Allocation)));
        Assert.All(competition.Rounds, row => Assert.Equal(options.Competition.MaximumStageTimePerCandidate, row.Allocation.MaximumStageTime));
        Assert.All(competition.SearchEvidence.QuickEvaluations, q => { Assert.Equal("waves", q.Units); Assert.True(q.EndpointScore <= q.InitialScore); });
        Assert.All(competition.SearchEvidence.Challenges, c => Assert.Contains(competition.Rounds, r => r.CandidateId == c.CandidateId && r.StageIndex == c.StageIndex && r.Selected));
        Assert.All(competition.Candidates.Where(c => c.Topology is not null), c => Assert.Equal(plan.Seeds[0].Stop, c.Topology!.Stop));
        var path = Path.Combine(Path.GetTempPath(), "initial-search-m1-" + Guid.NewGuid().ToString("N") + ".seed-search.json");
        try
        {
            await ParaxialSeedSearchFiles.SaveAsync(result, path);
            Assert.InRange(new FileInfo(path).Length, 1, InitialStructureLimits.MaximumManifestBytes);
            using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            Assert.Equal(ContentFingerprint.Compute(result), saved.RootElement.GetProperty("sha256").GetString());
            Assert.Equal(result.Algorithm, saved.RootElement.GetProperty("result").GetProperty("algorithm").GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ExperimentalTinyBudgetAndCancellationNeverQualifyCourseEndpoints()
    {
        var (plan, options) = StructureCompetitionTests.Specification();
        options = options with { MaximumTracedRays = 100, Competition = options.Competition! with { SearchStrategy = new() } };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.Equal("InsufficientOriginalValidationReserve", result.Competition!.StopReason); Assert.Empty(result.Competition.QualifiedIds);
        Assert.Equal(0, result.Work.TracedRays);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var cancelled = ParaxialSeedSearch.Run(plan, options with { MaximumTracedRays = 500000 }, stop.Token);
        Assert.Equal(ParaxialSeedTermination.Cancelled, cancelled.Termination); Assert.Empty(cancelled.Competition!.QualifiedIds);
    }

    [Fact]
    public void CancellationAfterQuickStageSettlesCumulativeWork()
    {
        var (plan, options) = StructureCompetitionTests.Specification();
        options = options with { Competition = options.Competition! with { SearchStrategy = new() { QuickEvaluation = new() } } };
        using var stop = new CancellationTokenSource(); var completedPoolNotifications = 0;
        var result = ParaxialSeedSearch.Run(plan, options, stop.Token, (starts, _) =>
        { if (starts == 4 && ++completedPoolNotifications == 2) stop.Cancel(); });
        Assert.Equal(ParaxialSeedTermination.Cancelled, result.Termination);
        Assert.NotEmpty(result.Competition!.Rounds); Assert.Empty(result.Competition.QualifiedIds);
        Assert.Equal(result.Work.TracedRays, result.Competition.Candidates.Sum(c => c.Work.TracedRays));
        Assert.Equal(result.Work.FunctionEvaluations, result.Competition.Candidates.Sum(c => c.Work.FunctionEvaluations));
        Assert.InRange(result.Work.TracedRays, 1, options.MaximumTracedRays);
    }

    [Fact]
    public void ProvenSpaceContradictionStopsBeforeAnyCandidateWork()
    {
        var (plan, options) = StructureCompetitionTests.Specification();
        options = options with
        {
            Generation = options.Generation! with { MinimumThicknessMillimeters = 40, MaximumThicknessMillimeters = 40 },
            Competition = options.Competition! with { SearchStrategy = new() }
        };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.StartsWith("ProvenInfeasible:", result.Competition!.StopReason);
        Assert.Empty(result.Competition.Candidates); Assert.Empty(result.Competition.QualifiedIds);
        Assert.Equal(0, result.Work.FunctionEvaluations); Assert.Equal(0, result.Work.TracedRays);
    }

    private static Optic Plate()
    {
        var optic = new Optic("diagnostic plate");
        optic.Wavelengths.Add(new() { Nanometers = 587.6, Weight = 1, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 5, Material = "N-BK7", SemiDiameter = .5, SemiDiameterFixed = true, PhysicalAperture = new CircularAperture(.5) },
            new OpticalSurface { Thickness = 10, Material = "Air" }, new OpticalSurface() ]);
        // Legacy import intentionally initializes apertures separately from the prescription.
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.5);
        return optic;
    }
}
