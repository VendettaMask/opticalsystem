using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record StructureFeedbackTrial(FilmLayer[] ParentLayers, NarrowBandPrototype ParentPrototype,
    FilmLayer[] Layers, NarrowBandPrototype Prototype);
public sealed record StructureFeedbackRecord(string Protocol, int CandidateLimit, int CandidateEvaluations,
    int ShortIterationLimit, StructureFeedbackTrial[] Transfers, IncidentMatchingTrial[] Matching, ScreeningRun[] Screening)
{
    public int Round { get; init; }
    public FilmLayer[]? BaselineLayers { get; init; }
    public ThinFilmBandMetrics? BaselineMetrics { get; init; }
    public double[]? BaselineBlockingOd { get; init; }
    public FilmLayer[]? FullStartLayers { get; init; }
    public FilmLayer[]? FullResultLayers { get; init; }
    public NarrowBandPrototype? FullPrototype { get; init; }
    public RunRecord? RefinementRun { get; init; }
    public bool? Accepted { get; init; }
}

public sealed partial class DesignService
{
    public const string StructureFeedbackProtocol = "matched-structure-feedback/1";

    private static bool JointStructureFeedbackImprovement(Experiment candidate, Experiment baseline)
    {
        var a = candidate.Result!; var b = baseline.Result!; var t = baseline.Target;
        if (b.SamplingConverged && !a.SamplingConverged || b.Passed && !a.Passed
            || a.Metrics.FwhmNanometers is not { } width || b.Metrics.FwhmNanometers is not { } oldWidth) return false;
        var od = t.StopBands!.Select(band => MinimumOd(a.Spectrum, band)).ToArray();
        var oldOd = t.StopBands!.Select(band => MinimumOd(b.Spectrum, band)).ToArray();
        return a.Metrics.PeakTransmittance >= b.Metrics.PeakTransmittance
            && Math.Abs(width - t.FwhmNm) <= Math.Abs(oldWidth - t.FwhmNm)
            && od.Zip(oldOd).All(p => p.First >= p.Second)
            && WithinGeneralTargetEnvelope(AssessGeneral(candidate, a.Spectrum), AssessGeneral(baseline, b.Spectrum))
            && (a.Metrics.PeakTransmittance > b.Metrics.PeakTransmittance || Math.Abs(width - t.FwhmNm) < Math.Abs(oldWidth - t.FwhmNm)
                || od.Zip(oldOd).Any(p => p.First > p.Second));
    }

    // Origin denotes physical mirror/cavity roles. Fitted thickness ratios follow those
    // roles; arbitrary layer numbers and material names have no meaning here.
    private static FilmLayer[]? TransferGeneralPrototype(Experiment d, IReadOnlyDictionary<string, IMaterial> materials,
        FilmLayer[] fitted, NarrowBandPrototype origin, NarrowBandPrototype next, bool keepPrefix)
    {
        if (origin.CavityOrders is null || next.CavityOrders is null || fitted.Any(l => !l.Variable) || origin.CavityOrders.Length != (d.Target.Structure?.Cavities ?? 1)
            || next.CavityOrders.Length != origin.CavityOrders.Length) return null;
        var oldBasis = PrototypeLayers(d, materials, origin); var basis = PrototypeLayers(d, materials, next);
        if (oldBasis is null || basis is null) return null;
        var prefix = fitted.Length - oldBasis.Length;
        if (prefix < 0 || oldBasis.Where((l, i) => l.MaterialId != fitted[prefix + i].MaterialId).Any()) return null;
        if (origin.SubstrateStartsHigh == next.SubstrateStartsHigh && origin.MirrorLayers.SequenceEqual(next.MirrorLayers)
            && origin.CavityOrders.SequenceEqual(next.CavityOrders))
        {
            var retained = keepPrefix ? fitted.ToArray() : fitted.Skip(prefix).ToArray();
            return WithinBounds(d, retained) ? retained : null;
        }
        static (bool Cavity, int Segment, double Depth)[] Roles(NarrowBandPrototype p)
        {
            var roles = new List<(bool, int, double)>();
            for (var segment = 0; segment < p.MirrorLayers.Length; segment++)
            {
                for (var j = 0; j < p.MirrorLayers[segment]; j++) roles.Add((false, segment, (j + .5) / p.MirrorLayers[segment]));
                if (segment < p.CavityOrders.Length) roles.Add((true, segment, 0));
            }
            roles.Reverse(); return roles.ToArray();
        }
        var oldRoles = Roles(origin); var roles = Roles(next);
        var layers = keepPrefix ? fitted.Take(prefix).ToList() : new List<FilmLayer>();
        for (var i = 0; i < basis.Length; i++)
        {
            var role = roles[i]; var available = Enumerable.Range(0, oldRoles.Length)
                .Where(j => oldRoles[j].Cavity == role.Cavity && oldRoles[j].Segment == role.Segment).ToArray();
            if (available.Length == 0) return null;
            var sameMaterial = available.Where(j => oldBasis[j].MaterialId == basis[i].MaterialId).ToArray();
            var nearest = (sameMaterial.Length > 0 ? sameMaterial : available).MinBy(j => Math.Abs(oldRoles[j].Depth - role.Depth));
            layers.Add(basis[i] with { ThicknessNm = basis[i].ThicknessNm * fitted[prefix + nearest].ThicknessNm / oldBasis[nearest].ThicknessNm });
        }
        var result = layers.ToArray(); return WithinBounds(d, result) ? result : null;
    }

    private (GeneralSeed[] Seeds, StructureFeedbackRecord Record) GeneralStructureFeedback(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, IEnumerable<GeneralSeed> parents, GeneralCounter work,
        IProgress<DesignProgress>? progress, CancellationToken token, GeneralSeed? explorer = null)
    {
        const int limit = 256;
        var count = Math.Min(4, (d.Settings.Search ?? new()).Starts);
        var allowance = Math.Min(d.Settings.MaximumIterations, (int)Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations)));
        var grid = GeneralGrid(d, ObjectiveGrid(d).Concat(ThinFilmSpectrum.Grid(d.Target.MinimumNm, d.Target.MaximumNm, 512)));
        var candidates = new List<GeneralSeed>(); var transfers = new List<StructureFeedbackTrial>();
        var matching = new List<IncidentMatchingTrial>(); var screening = new List<ScreeningRun>(); var seen = new HashSet<string>();
        double[] Response(ThinFilmSample[] samples) => Enumerable.Range(0, 17).Select(i => samples.MinBy(s => Math.Abs(s.WavelengthNanometers
            - Math.Clamp(d.Target.CenterNm + (i - 8) * d.Target.FwhmNm / 4, d.Target.MinimumNm, d.Target.MaximumNm)))!.Power.Transmittance).ToArray();
        GeneralSeed? Add(string name, FilmLayer[] layers, NarrowBandPrototype origin)
        {
            if (candidates.Count >= limit || !seen.Add(LayerIdentity(layers))) return null;
            token.ThrowIfCancellationRequested();
            var samples = GeneralConstraintSystem(d with { Layers = layers }, Solver(d, materials, layers), grid, false, work).Samples;
            var response = Response(samples);
            var seed = new GeneralSeed(name, layers, origin, AssessGeneral(d, samples), response); candidates.Add(seed); return seed;
        }
        var active = GeneralPool(parents.Where(p => p.Origin is not null), count).ToList();
        if (explorer?.Origin is not null)
        {
            active.RemoveAll(s => LayerIdentity(s.Layers) == LayerIdentity(explorer.Layers));
            active.Insert(0, explorer);
        }
        foreach (var parent in active.Take(count))
            foreach (var neighbor in GeneralNeighbors(parent.Origin!))
            {
                var layers = TransferGeneralPrototype(d, materials, parent.Layers, parent.Origin!, neighbor, true);
                if (layers is null || Add(parent.Name + " · 拟合角色反馈", layers, neighbor) is null) continue;
                transfers.Add(new(parent.Layers, parent.Origin!, layers, neighbor));
            }
        // Reusing an old cap after changing the interior is not a new impedance match.
        // Strip only the declared prefix and solve physical throughput roots again.
        foreach (var seed in GeneralPool(candidates, count * 2).ToArray())
        {
            var body = TransferGeneralPrototype(d, materials, seed.Layers, seed.Origin!, seed.Origin!, false);
            if (body is null) continue;
            foreach (var trial in MatchGeneralIncidentMedium(d with { Layers = body }, materials, token))
            { matching.Add(trial); work.Add(trial.Run.GeneralWork); Add(seed.Name + " · 重新" + trial.Pattern, trial.Layers, seed.Origin!); }
        }
        var result = new List<GeneralSeed>();
        foreach (var seed in GeneralPool(candidates, count))
        {
            progress?.Report(new("匹配后结构反馈短程精化", screening.Count));
            var refined = RefineGeneralThickness(d with { Layers = seed.Layers }, materials, ObjectiveGrid(d), allowance, progress, token);
            work.Add(refined.Run.GeneralWork);
            screening.Add(new(screening.Count + 1, seed.Layers, refined.Layers, refined.Merit, refined.Run) { OriginPrototype = seed.Origin });
            var samples = GeneralConstraintSystem(d with { Layers = refined.Layers }, Solver(d, materials, refined.Layers), grid, false, work).Samples;
            result.Add(seed with { Layers = refined.Layers, Assessment = AssessGeneral(d, samples), Response = Response(samples) });
        }
        return (result.ToArray(), new(StructureFeedbackProtocol, limit, candidates.Count, allowance, transfers.ToArray(), matching.ToArray(), screening.ToArray()));
    }
}
