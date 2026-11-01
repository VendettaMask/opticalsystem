using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string SeedRankingProtocol = "dense-feasibility-allocation/1";

    // Diversity builds the exploratory pool. Allocation of the scarce complete
    // solves must first use physical target quality, rather than the response
    // distance of a poor seed from an already good incumbent.
    private List<GeneralSeed> RankGeneralFinalists(Experiment d, IEnumerable<GeneralSeed> source,
        IReadOnlyDictionary<string, IMaterial> materials, GeneralCounter work, List<SearchSeedValidation> validations, IProgress<DesignProgress>? progress, CancellationToken token)
    {
        var ordered = source.DistinctBy(s => LayerIdentity(s.Layers))
            .OrderBy(s => s.Assessment.MaximumViolation).ThenBy(s => s.Assessment.Merit)
            .ThenByDescending(s => s.Assessment.PeakTransmittance).ToArray();
        static string Family(GeneralSeed s) => s.Origin is { } p
            ? string.Join(',',p.MirrorLayers)+"/"+string.Join(',',p.CavityOrders)+"/"+p.SubstrateStartsHigh
            : "incumbent-structure";
        // Cap/matching roots of the same physical skeleton must not consume all
        // complete solves. First allocate one best representative per skeleton;
        // fill remaining slots only when there are fewer structural families.
        var representatives=ordered.DistinctBy(Family).ToArray();
        var shortlist=representatives.Concat(ordered).DistinctBy(s=>LayerIdentity(s.Layers)).Take(ShortCandidateLimit).ToArray();
        var t = d.Target;
        var verified = new List<GeneralSeed>();
        foreach (var seed in shortlist)
        {
            token.ThrowIfCancellationRequested();
            var layers=seed.Layers;
            var value = Evaluate(d with { Layers = layers, Result = null, Candidates = null, Tolerance = null }, progress: progress, token: token); work.Dense++;
            var assessment = AssessGeneral(value, value.Result!.Spectrum);
            RunRecord? refinementRun=null;
            if(!value.Result.Passed)
            {
                var allowance=Math.Min(d.Settings.MaximumIterations,Math.Max(1,(int)Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations))));
                var refined=RefineGeneralThickness(d with{Layers=layers},materials,ObjectiveGrid(d),allowance,progress,token,validatedStart:value);
                refinementRun=refined.Run;work.Add(refinementRun.GeneralWork);
                var checkedValue=Evaluate(d with{Layers=refined.Layers,Result=null,Candidates=null,Tolerance=null},refinementRun,progress,token);work.Dense++;
                var next=AssessGeneral(checkedValue,checkedValue.Result!.Spectrum);
                if((!value.Result.SamplingConverged||checkedValue.Result.SamplingConverged)&&WithinGeneralTargetEnvelope(next,assessment)
                    &&(checkedValue.Result.Passed&&!value.Result.Passed||checkedValue.Result.Passed==value.Result.Passed&&PreferGeneral(next,assessment)))
                {value=checkedValue;assessment=next;layers=value.Layers;}
            }
            validations.Add(new(layers, value.Result.Metrics, t.StopBands!.Select(b => MinimumOd(value.Result.Spectrum, b)).ToArray(),
                value.Result.SamplingConverged, value.Result.Passed, assessment.Violations) { Prototype=seed.Origin, InitialLayers=seed.Layers, RefinementRun=refinementRun });
            verified.Add(seed with { Layers=layers, Assessment = assessment });
        }
        return verified.OrderByDescending(s => validations.First(v => ReferenceEquals(v.Layers, s.Layers)).Passed)
            .ThenBy(s => s.Assessment.MaximumViolation).ThenBy(s => s.Assessment.Merit).ThenByDescending(s => s.Assessment.PeakTransmittance).ToList();
    }
}
