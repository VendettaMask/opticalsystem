using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal sealed record SearchMaterialSet(IReadOnlyList<string> Names, string Fingerprint,
    IReadOnlyList<SearchDiagnostic> Diagnostics);

internal static class FlatStartSearchPlanning
{
    public const int MaximumRoots = 128;
    public const int MaximumTrials = 256;

    public static void Validate(InitialStructureSpecification specification, FlatStartSearchOptions options)
    {
        SpecificationValidator.Validate(specification);
        if (specification.FlatStart is null) throw new ArgumentException("Flat-start settings are required.");
        if (specification.Budget.InitialSeedCount > MaximumRoots)
            throw new ArgumentException($"Family search supports at most {MaximumRoots} initial families.");
        if (options.AllowedGlassNames is not { Count: > 0 and <= 64 }
            || options.AllowedGlassNames.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 256)
            || options.MaximumEvaluationsPerTrial is < 2 or > InitialStructureLimits.MaximumEvaluations
            || options.MaximumDisplayedCandidates is < 1 or > 64
            || !double.IsFinite(options.MinimumStructuralDistance) || options.MinimumStructuralDistance < 0)
            throw new ArgumentException("Invalid family search options.", nameof(options));
    }

    public static SearchMaterialSet Materials(InitialStructureSpecification specification, FlatStartSearchOptions options)
    {
        var optic = new Optic("Family material preflight");
        optic.Materials.SetPreferredGlassCatalogs(specification.GlassCatalogs);
        var names = new List<string>();
        var snapshots = new List<ComponentSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<SearchDiagnostic>();
        foreach (var name in options.AllowedGlassNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                FlatStartFamilySupport.ValidateGlass(optic, name, specification);
                var snapshot = ComponentSnapshotFactory.FromMaterial(optic.Materials.Resolve(name));
                if (!seen.Add(ContentFingerprint.Compute(snapshot)))
                {
                    diagnostics.Add(new("search.glass-alias", $"'{name}' resolves to an already selected catalog item."));
                    continue;
                }
                names.Add(name);
                snapshots.Add(snapshot);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or InvalidDataException)
            {
                diagnostics.Add(new("search.glass-excluded", $"'{name}': {exception.Message}"));
            }
        }
        if (!names.Contains(specification.InitialGlass, StringComparer.OrdinalIgnoreCase))
            diagnostics.Add(new("search.initial-glass-unavailable", "The initial glass is not in the usable allowed set; roots use only the explicitly allowed remaining catalog materials."));
        return new(names, ContentFingerprint.Compute(snapshots), diagnostics);
    }

    public static FlatStartFamily Family(InitialStructureSpecification specification, IReadOnlyList<string> glasses, int ordinal)
    {
        var counts = specification.MaximumElementCount - specification.MinimumElementCount + 1;
        var count = specification.MinimumElementCount + ordinal % counts;
        var round = ordinal / counts;
        var random = new DeterministicRandom(unchecked(specification.Budget.RandomSeed + 104729L * (ordinal + 1)));
        var baseGlass = glasses.FirstOrDefault(name => name.Equals(specification.InitialGlass, StringComparison.OrdinalIgnoreCase)) ?? glasses[0];
        var selected = Enumerable.Repeat(baseGlass, count).ToArray();
        if (round > 0 && glasses.Count > 1)
        {
            var alternatives = glasses.Where(name => !name.Equals(baseGlass, StringComparison.OrdinalIgnoreCase)).ToArray();
            var other = alternatives[(round - 1) / 2 % alternatives.Length];
            for (var element = 0; element < count; element++)
                if ((element + round) % 2 == 0) selected[element] = other;
        }
        var center = Enumerable.Repeat(specification.MinimumCenterThicknessMillimeters, count).ToArray();
        var gaps = Enumerable.Repeat(specification.MinimumAirGapMillimeters, count - 1).ToArray();
        var available = specification.MaximumTrackLengthMillimeters - center.Sum() - gaps.Sum()
            - (specification.FlatStart!.FixedBackFocusMillimeters ?? specification.MinimumBackFocusMillimeters);
        if (round > 0)
        {
            var extra = Enumerable.Range(0, center.Length + gaps.Length).Select(_ => random.NextUnitDouble()).ToArray();
            var scale = Math.Min(1, Math.Max(0, available) / Math.Max(1e-12, extra.Sum()));
            for (var index = 0; index < center.Length; index++) center[index] += extra[index] * scale;
            for (var index = 0; index < gaps.Length; index++) gaps[index] += extra[center.Length + index] * scale;
        }
        return new()
        {
            GlassNames = selected,
            CenterThicknesses = center,
            AirGaps = gaps,
            StopSurfaceIndex = (round % 3) switch { 0 => 1, 1 => 2 * (count / 2) + 1, _ => 2 * count },
            SeedIndex = ordinal
        };
    }

    public static IReadOnlyList<FlatStartFamily> Roots(InitialStructureSpecification specification, IReadOnlyList<string> glasses)
    {
        if (glasses.Count == 0) return [];
        var count = Math.Min(specification.Budget.InitialSeedCount, Math.Max(1, specification.Budget.MaximumEvaluations / 2));
        return Enumerable.Range(0, count).Select(index => Family(specification, glasses, index)).ToArray();
    }
}
