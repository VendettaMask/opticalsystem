using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record PolarizedDisplaySpectrum(ThinFilmSample[] Average, ThinFilmSample[] S, ThinFilmSample[] P);
public sealed record AnalysisSpectrumOptions(double MinimumNm, double MaximumNm, double StepNm, double[] AnglesDegrees)
{
    public static AnalysisSpectrumOptions Default => new(400, 800, .5, [0]);
    public void Validate()
    {
        if (!double.IsFinite(MinimumNm) || !double.IsFinite(MaximumNm) || !double.IsFinite(StepNm)
            || MinimumNm <= 0 || MaximumNm <= MinimumNm || StepNm <= 0 || AnglesDegrees is null
            || AnglesDegrees.Length is < 1 or > 16 || AnglesDegrees.Any(a => !double.IsFinite(a) || a < 0 || a >= 90)
            || AnglesDegrees.Distinct().Count() != AnglesDegrees.Length
            || Math.Ceiling((MaximumNm - MinimumNm) / StepNm + 1) * AnglesDegrees.Length > ThinFilmSpectrum.MaximumSamples)
            throw new ArgumentException("评估范围、步长或入射角无效；最多 16 个不同角度，总采样最多 200001 点。");
    }
}
public sealed record AngleDisplaySpectrum(double AngleDegrees, PolarizedDisplaySpectrum Spectrum);

public sealed partial class DesignService
{
    public AngleDisplaySpectrum[] EvaluateAnalysisDisplay(Experiment input, AnalysisSpectrumOptions options, CancellationToken token = default)
    {
        options.Validate(); using var scope = ComputationCancellation.Push(token); token.ThrowIfCancellationRequested();
        var d = input.Snapshot();
        var materials = Materials(d, new(options.MinimumNm, options.MaximumNm));
        var evaluate = PowerEvaluator(d, materials, display: true);
        var intervals = (int)Math.Floor((options.MaximumNm - options.MinimumNm) / options.StepNm);
        var grid = Enumerable.Range(0, intervals + 1).Select(i => options.MinimumNm + i * options.StepNm)
            .Where(w => w < options.MaximumNm).Append(options.MaximumNm).ToArray();
        ThinFilmSample[] Sample(double angle, ThinFilmPolarization pol) => ThinFilmSpectrum.Sample(w => evaluate(w, angle, pol), grid);
        return options.AnglesDegrees.Select(angle => new AngleDisplaySpectrum(angle, new(
            Sample(angle, ThinFilmPolarization.Unpolarized), Sample(angle, ThinFilmPolarization.S), Sample(angle, ThinFilmPolarization.P)))).ToArray();
    }
    /// <summary>Read-only display sampling on the exact certified grid. Does not change targets or acceptance.</summary>
    public PolarizedDisplaySpectrum EvaluateDisplayPolarizations(Experiment input, CancellationToken token = default)
    {
        using var scope = ComputationCancellation.Push(token);
        token.ThrowIfCancellationRequested();
        var d = input.Snapshot();
        if (d.Result is not { } result || !d.MatchesFingerprint(result.Fingerprint))
            throw new ArgumentException("请先计算当前输入的光谱，再查看平均 / s / p 曲线。");
        var grid = result.Spectrum.Select(p => p.WavelengthNanometers).ToArray();
        if (grid.Length < 3 || grid.Length > ThinFilmSpectrum.MaximumSamples)
            throw new ArgumentException("显示采样数量无效。");
        for (var i = 0; i < grid.Length; i++)
            if (!double.IsFinite(grid[i]) || grid[i] < d.Target.MinimumNm || grid[i] > d.Target.MaximumNm || i > 0 && grid[i] <= grid[i - 1])
                throw new ArgumentException("显示采样必须在已计算波段内严格递增。");
        var evaluate = PowerEvaluator(d, Materials(d));
        ThinFilmSample[] Sample(ThinFilmPolarization polarization) =>
            ThinFilmSpectrum.Sample(w => evaluate(w, d.Target.AngleDegrees, polarization), grid);
        // Unpolarized power and its stable log(T) are computed only by the shared Core.
        return new(Sample(ThinFilmPolarization.Unpolarized), Sample(ThinFilmPolarization.S), Sample(ThinFilmPolarization.P));
    }
}
