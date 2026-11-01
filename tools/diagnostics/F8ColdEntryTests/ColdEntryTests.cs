using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

public sealed class ColdEntryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void ConflictingStopDispatchesColdModeAndNeverReturnsAnOldSeed(int version)
    {
        var plan = Fixture(conflict: true, version); var before = ContentFingerprint.Compute(plan);
        var result = ParaxialSeedSearch.Run(plan);
        Assert.Equal("initial-structure-cold-design-v" + version, result.Algorithm);
        Assert.Equal("OriginalExternalStopApertureConflict", result.ColdStart!.StopReason);
        Assert.Empty(result.Seeds); Assert.Equal(0, result.Work.FunctionEvaluations); Assert.Equal(0, result.Work.TracedRays);
        Assert.Equal(before, ContentFingerprint.Compute(plan));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void FunctionBudgetIncludesFreshConstructionAndRetainsAttemptedWork(int version)
    {
        var plan = Fixture(conflict: false, version);
        plan = plan with { Options = plan.Options with { SeedPreparation = plan.Options.SeedPreparation! with { MaximumFunctionEvaluations = 10 } } };
        var result = ParaxialSeedSearch.Run(plan);
        Assert.Empty(result.Seeds); Assert.Equal(ParaxialSeedTermination.BudgetExhausted, result.Termination);
        Assert.Equal(10, result.Work.FunctionEvaluations);
        Assert.Equal(result.Work.FunctionEvaluations, result.ColdStart!.Construction.Sum(c => c.Work.FunctionEvaluations));
    }

    [Fact]
    public void PreparationStrategiesCannotBeCombined()
    {
        var plan = Fixture(false); var options = plan.Options.SeedPreparation!;
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Competition = new() }));
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Evolution = new() }));
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { ImageScreening = new() }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public async Task AutomaticFailureSavesColdEvidenceAndDoesNotCreateAnAutonomousRun(int version)
    {
        var directory = Path.Combine(Path.GetTempPath(), "f8-entry-" + Guid.NewGuid().ToString("N"));
        var session = new AutonomousDesktopSession(directory);
        await session.RunAsync(Fixture(true, version), false, _ => throw new InvalidOperationException("Failed cold start cannot publish a main run."));
        Assert.Null(session.Checkpoint); Assert.Null(session.RecordPath); Assert.NotNull(session.PreparationResult);
        Assert.Empty(session.PreparationResult!.Seeds); Assert.True(File.Exists(session.PreparationReportPath));
        Assert.Empty(Directory.GetFiles(directory, "*.autonomous.json", SearchOption.AllDirectories));
        // Evidence stays in the unique test-owned temp folder for audit; never removes other files.
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public async Task AutomaticCancellationIsSavedWithoutStartingTheMainSearch(int version)
    {
        var directory = Path.Combine(Path.GetTempPath(), "f8-entry-cancel-" + Guid.NewGuid().ToString("N"));
        var session = new AutonomousDesktopSession(directory);
        var pending = session.RunAsync(Fixture(false, version), false, _ => throw new InvalidOperationException("Cancelled cold start cannot start a main run."));
        session.Cancel(); await pending;
        Assert.Null(session.Checkpoint); Assert.Equal(ParaxialSeedTermination.Cancelled, session.PreparationResult!.Termination);
        Assert.True(File.Exists(session.PreparationReportPath)); Assert.Empty(session.PreparationResult.Seeds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public async Task QualifiedSyntheticControlHandsOffFreshSeedAndResumeDoesNotRepeatPreparation(int version)
    {
        // Loose synthetic flow control only. The real F/8 study keeps its original 50/150 micrometre gates.
        var plan = Fixture(false, version); var preparation = plan.Options.SeedPreparation!;
        plan = plan with { Targets = plan.Targets with { MaximumRmsRadiusMillimeters = 1, MaximumSpotRadiusMillimeters = 2,
            MaximumDistortionFraction = 1 }, Options = plan.Options with { MaximumTrials = 1,
                SeedPreparation = preparation with { ColdStart = preparation.ColdStart! with { BackFocusSlackFractions = [.5], AttemptsPerFraction = 1 } } } };
        var directory = Path.Combine(Path.GetTempPath(), "f8-entry-positive-" + Guid.NewGuid().ToString("N"));
        var session = new AutonomousDesktopSession(directory);
        await session.RunAsync(plan, false, _ => { });
        Assert.NotNull(session.PreparationResult); Assert.Single(session.PreparationResult!.Seeds);
        Assert.NotNull(session.Checkpoint); Assert.NotEmpty(session.Checkpoint!.AcceptedArchive);
        Assert.Equal(ContentFingerprint.Compute(session.PreparationResult.Seeds), ContentFingerprint.Compute(session.Checkpoint.Plan.Seeds));
        Assert.NotEqual(ContentFingerprint.Compute(plan.Seeds), ContentFingerprint.Compute(session.Checkpoint.Plan.Seeds));
        var report = await File.ReadAllBytesAsync(session.PreparationReportPath!);
        var resumed = new AutonomousDesktopSession(directory); await resumed.LoadAsync(session.RecordPath!);
        await resumed.RunAsync(resumed.Checkpoint!.Plan, true, _ => { });
        Assert.Null(resumed.PreparationResult);
        Assert.Single(Directory.GetFiles(directory, "*.seed-search.json", SearchOption.AllDirectories));
        Assert.Equal(report, await File.ReadAllBytesAsync(session.PreparationReportPath!));
    }

    [Fact]
    public void OldFrozenColdResultChecksumSurvivesNullableEntryFields()
    {
        var path = Environment.GetEnvironmentVariable("F8_COLD_CHECKSUM_FIXTURE");
        Assert.False(string.IsNullOrWhiteSpace(path));
        using var saved = JsonDocument.Parse(File.ReadAllBytes(path!));
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        var result = saved.RootElement.GetProperty("result").Deserialize<ColdStartDesignResult>(json)!;
        Assert.Equal(saved.RootElement.GetProperty("sha256").GetString(), ContentFingerprint.Compute(result));
    }

    private static AutonomousSearchPlan Fixture(bool conflict, int version = 1)
    {
        var source = AutonomousDesignExamples.ThreeElement(); var envelope = source.Seeds[0];
        TopologyFace Face(TopologyFace f) => f with { ClearSemiDiameterMillimeters = 5, MechanicalSemiDiameterMillimeters = 5,
            Profile = new() };
        envelope = envelope with { Stop = envelope.Stop with { ClearSemiDiameterMillimeters = conflict ? 100 : 2 },
            Elements = envelope.Elements.Select(e => e with { Front = Face(e.Front), Back = Face(e.Back) }).ToImmutableArray() };
        var preparation = new ParaxialSeedSearchOptions { ColdStart = new() { Version = version }, MaximumFunctionEvaluations = 40,
            MaximumTracedRays = 500000, TimeLimit = TimeSpan.FromSeconds(20),
            Generation = new() { Method = StructureGenerationMethod.Delano, StopAfterElement = 0, MinimumThicknessMillimeters = .5,
                MaximumThicknessMillimeters = 2, MinimumBackFocusMillimeters = 5, AnnealingProposals = 12,
                Elements = envelope.Elements.Select(e => new StructureElementDescriptor { Materials = [e.Material] }).ToImmutableArray() } };
        return source with { Seeds = [envelope], Options = source.Options with { SeedPreparation = preparation } };
    }
}
