using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

public sealed record DesignFormPlan(int DistinctSignForms, int PlannedRoots, int PlannedSignForms,
    int ScreeningEvaluationsPerRoot, DesignSearchMode Mode)
{
    public int NeighborhoodEvaluationsReserved { get; init; }
}

/// <summary>Discrete prescription initialization only; no optical power or ray calculations.</summary>
public static class DesignFormSearch
{
    public static int CompletedSignForms(FlatStartSearchCheckpoint checkpoint) => checkpoint.Trials
        .Where(trial => trial.State == FamilyTrialState.Completed && trial.FinalValidation is not null
            && trial.BootstrapProof?.Steps.Any(step => step.Operation == "binary-form-initialization") == true)
        .Select(trial => trial.Family.BinaryStart!.Signs).Distinct().Count();

    public static void Validate(InitialStructureSpecification spec, DesignSearchSettings settings)
    {
        if (!Enum.IsDefined(settings.Mode) || settings.SignFilter is null
            || settings.SignFilter.Any(sign => sign is not ('+' or '-' or '*'))
            || settings.SignFilter.Length > 0 && (spec.MinimumElementCount != spec.MaximumElementCount
                || settings.SignFilter.Length != spec.MaximumElementCount)
            || settings.StartingRadiusMillimeters is { } radius && (!double.IsFinite(radius) || radius <= 0)
            || settings.StartingCenterThicknessMillimeters is { } center && (!double.IsFinite(center) || center < spec.MinimumCenterThicknessMillimeters)
            || settings.StartingAirGapMillimeters is { } air && (!double.IsFinite(air) || air < spec.MinimumAirGapMillimeters)
            || settings.StopSurfaceIndex is { } stop && (stop < 1 || stop > 2 * spec.MinimumElementCount))
            throw new ArgumentException("Invalid form search controls: use positive radii, permitted thicknesses, a physical stop, and one + / - / * per element for a fixed element count.");
        var length = spec.MaximumElementCount * (settings.StartingCenterThicknessMillimeters ?? spec.MinimumCenterThicknessMillimeters)
            + (spec.MaximumElementCount - 1) * (settings.StartingAirGapMillimeters ?? spec.MinimumAirGapMillimeters)
            + (spec.FlatStart?.FixedBackFocusMillimeters ?? spec.MinimumBackFocusMillimeters);
        if (length > spec.MaximumTrackLengthMillimeters)
            throw new ArgumentException("The requested starting thicknesses and gaps exceed the total length limit.");
    }

    public static DesignFormPlan Preview(InitialStructureSpecification spec, FlatStartSearchOptions options,
        string algorithmVersion = FlatStartAlgorithm.Version)
    {
        FlatStartSearchPlanning.Validate(spec, options);
        var settings = options.DesignSearch ?? new();
        var count = FormCount(spec, settings);
        var roots = Math.Min(spec.Budget.InitialSeedCount, Math.Max(1, spec.Budget.MaximumEvaluations / 2));
        var balanced = FlatStartAlgorithm.SchedulingPolicy(algorithmVersion, spec) == 2;
        if (balanced)
        {
            var rootBudget = spec.Budget.MaximumEvaluations - NeighborhoodBudget.RootReserve(spec, options);
            var targetQuota = settings.Mode == DesignSearchMode.Full ? options.MaximumEvaluationsPerTrial
                : FlatStartScheduler.Allocation(spec, options, "flat-root", spec.MaximumElementCount);
            roots = Math.Min(roots, Math.Max(1, rootBudget / targetQuota));
        }
        var quota = RootQuota(spec, options, roots, algorithmVersion);
        return new(count, roots, Math.Min(count, roots), quota, settings.Mode)
        {
            NeighborhoodEvaluationsReserved = balanced ? spec.Budget.MaximumEvaluations - roots * quota : 0
        };
    }

    internal static int RootQuota(InitialStructureSpecification spec, FlatStartSearchOptions options, int roots,
        string algorithmVersion = FlatStartAlgorithm.Version) => roots == 0 ? 0
        : Math.Min((spec.Budget.MaximumEvaluations - (FlatStartAlgorithm.SchedulingPolicy(algorithmVersion, spec) == 2
            ? NeighborhoodBudget.RootReserve(spec, options) : 0)) / roots, (options.DesignSearch ?? new()).Mode == DesignSearchMode.Full
            ? options.MaximumEvaluationsPerTrial
            : FlatStartScheduler.Allocation(spec, options, "flat-root", spec.MaximumElementCount));

    internal static int FormCount(InitialStructureSpecification spec, DesignSearchSettings settings) =>
        Enumerable.Range(spec.MinimumElementCount, spec.MaximumElementCount - spec.MinimumElementCount + 1)
            .Sum(count => 1 << (settings.SignFilter.Length == 0 ? count : settings.SignFilter.Count(sign => sign == '*')));

    internal static IReadOnlyList<FlatStartFamily> Roots(InitialStructureSpecification spec, FlatStartSearchOptions options,
        IReadOnlyList<string> glasses, string algorithmVersion = FlatStartAlgorithm.Version)
    {
        if (glasses.Count == 0) return [];
        var settings = options.DesignSearch ?? new();
        Validate(spec, settings);
        var forms = new List<(int Count, string Signs)>();
        var byCount = Enumerable.Range(spec.MinimumElementCount, spec.MaximumElementCount - spec.MinimumElementCount + 1)
            .Select(count => Signs(count, settings.SignFilter, spec.Budget.RandomSeed)).ToArray();
        // Interleave element counts so a limited budget does not silently omit the larger forms.
        for (var index = 0; index < byCount.Max(signs => signs.Length); index++)
            for (var offset = 0; offset < byCount.Length; offset++)
                if (index < byCount[offset].Length) forms.Add((spec.MinimumElementCount + offset, byCount[offset][index]));
        var plan = Preview(spec, options, algorithmVersion);
        var roots = new List<FlatStartFamily>();
        for (var index = 0; index < plan.PlannedRoots; index++)
        {
            var (count, signs) = forms[index % forms.Count];
            var cycle = index / forms.Count;
            var family = FlatStartSearchPlanning.Family(spec, glasses,
                cycle * byCount.Length + count - spec.MinimumElementCount);
            family = family with
            {
                CenterThicknesses = Enumerable.Repeat(settings.StartingCenterThicknessMillimeters ?? spec.MinimumCenterThicknessMillimeters, count).ToArray(),
                AirGaps = Enumerable.Repeat(settings.StartingAirGapMillimeters ?? spec.MinimumAirGapMillimeters, count - 1).ToArray(),
                StopSurfaceIndex = settings.ExploreStopPositions && cycle > 0 ? family.StopSurfaceIndex : settings.StopSurfaceIndex ?? 2 * (count / 2) + 1,
                SeedIndex = index,
                BinaryStart = new(signs, settings.StartingRadiusMillimeters ?? 10 * spec.EffectiveFocalLengthMillimeters)
            };
            roots.Add(family);
        }
        return roots;
    }

    private static string[] Signs(int count, string filter, long seed)
    {
        var result = Enumerable.Range(0, 1 << count).Reverse()
            .Select(mask => new string(Enumerable.Range(0, count).Select(bit => (mask & (1 << bit)) != 0 ? '+' : '-').ToArray()))
            .Where(signs => filter.Length == 0 || signs.Where((sign, index) => filter[index] != '*' && filter[index] != sign).Any() == false).ToArray();
        var random = new DeterministicRandom(unchecked(seed + count * 104729L));
        // The first trial is the most-positive permitted form; the rest are a seeded permutation without repeats.
        for (var index = result.Length - 1; index > 1; index--)
        {
            var other = 1 + random.NextInt32(index);
            (result[index], result[other]) = (result[other], result[index]);
        }
        return result;
    }
}
