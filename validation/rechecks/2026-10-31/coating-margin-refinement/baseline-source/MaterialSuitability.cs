using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record FilmMaterialSuitability(string MaterialId, string Name, string Source,
    double CenterIndex, double CenterExtinction, double MaximumSampledExtinction, double MaximumExtinctionWavelengthNm);
public sealed record MaterialSuitabilityReport(double ReferenceLosslessThreshold, bool ReferenceLosslessPrototypeApplicable,
    FilmMaterialSuitability[] Films, ThinFilmPower? CenterPower, string[] Messages)
{
    public ThinFilmSample? SampledWorstReflectance { get; init; }
}

public sealed partial class DesignService
{
    // This is the pinned reference wizard's applicability threshold, not a feasibility bound.
    public const double ReferenceLosslessThreshold = 1e-5;

    public MaterialSuitabilityReport AssessMaterials(Experiment input, CancellationToken token = default)
    {
        using var scope = OpticalSystemDesign.Core.Services.ComputationCancellation.Push(token);
        token.ThrowIfCancellationRequested();
        var d = input.Snapshot();
        return AssessMaterials(d, Materials(d), token);
    }

    private static MaterialSuitabilityReport AssessMaterials(Experiment d, IReadOnlyDictionary<string, IMaterial> materials,
        CancellationToken token, ThinFilmSample[]? verifiedSpectrum = null)
    {
        var grid = ThinFilmSpectrum.Grid(d.Target.MinimumNm, d.Target.MaximumNm, 64).Append(d.Target.CenterNm).Distinct().ToArray();
        var films = new List<FilmMaterialSuitability>();
        foreach (var id in d.Layers.Select(l => l.MaterialId).Concat([d.LowId, d.HighId]).Distinct())
        {
            token.ThrowIfCancellationRequested();
            var material = materials[id]; var snapshot = d.Materials.Single(m => m.Id == id);
            var values = grid.Select(w => (Wave: w, N: material.RefractiveIndex(w), K: material.ExtinctionCoefficient(w))).ToArray();
            if (values.Any(v => !double.IsFinite(v.N) || v.N <= 0 || !double.IsFinite(v.K) || v.K < 0))
                throw new ArgumentException($"材料 {snapshot.Name} 在诊断采样点的 n/k 无效，要求 n>0、k≥0。");
            var maximum = values.MaxBy(p => p.K);
            films.Add(new(id, snapshot.Name, snapshot.Source, material.RefractiveIndex(d.Target.CenterNm),
                material.ExtinctionCoefficient(d.Target.CenterNm), maximum.K, maximum.Wave));
        }
        var applicable = new[] { d.LowId, d.HighId }.All(id => materials[id].ExtinctionCoefficient(d.Target.CenterNm) <= ReferenceLosslessThreshold);
        var messages = new List<string>();
        if (d.Target.Kind == DesignKind.NarrowBand && !applicable)
            messages.Add("当前 H/L 材料不满足参考无吸收窄带原型的中心 k≤1e-5 前提；继续使用真实 n/k 竞争与匹配，不代表规格已证明无解。");
        foreach (var film in films.Where(f => f.MaximumSampledExtinction > ReferenceLosslessThreshold))
            messages.Add(FormattableString.Invariant($"材料 {film.Name}：中心 k={film.CenterExtinction:G7}；64 区间及中心锚点采样内最大 k={film.MaximumSampledExtinction:G7}（{film.MaximumExtinctionWavelengthNm:G7} nm），须关注实际吸收。"));
        var power = d.Layers.Length == 0 ? null : Solver(d, materials).Evaluate(d.Target.CenterNm, d.Target.AngleDegrees, d.Target.Polarization);
        if (power is not null)
            messages.Add(FormattableString.Invariant($"中心能量：R={power.Reflectance:P5}，T={power.Transmittance:P5}，有限膜层 A={power.Absorptance:P5}；T 为进入基板的功率。"));
        ThinFilmSample? worst = null;
        if (d.Target.Kind == DesignKind.HighReflector && d.Layers.Length > 0)
        {
            worst = (verifiedSpectrum ?? ThinFilmSpectrum.Sample(Solver(d, materials), grid, d.Target.AngleDegrees, d.Target.Polarization))
                .MinBy(s => s.Power.Reflectance);
            messages.Add(FormattableString.Invariant($"{(verifiedSpectrum is null ? "诊断采样" : "独立复验")}最差反射点 {worst!.WavelengthNanometers:G8} nm：R={worst.Power.Reflectance:P5}，T={worst.Power.Transmittance:P5}，有限膜层 A={worst.Power.Absorptance:P5}；不由吸收阈值推断物理无解。"));
        }
        return new(ReferenceLosslessThreshold, applicable, films.ToArray(), power, messages.ToArray()) { SampledWorstReflectance = worst };
    }
}
