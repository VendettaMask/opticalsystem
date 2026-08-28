using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Scattering;

public interface IScatteringModel
{
    string Kind { get; }

    RealRay Scatter(RealRay ray, Vector3D surfaceNormal);

    IScatteringModel Clone();
}

public class MainRayScatterLossApproximation : IScatteringModel
{
    public MainRayScatterLossApproximation(double scatterFraction)
    {
        ScatterFraction = Math.Clamp(scatterFraction, 0, 1);
    }

    public virtual string Kind => "main_ray_scatter_loss_approximation";

    public string ExperimentalWarning =>
        "Experimental：仅从主光线扣除散射能量，不生成 Lambertian 散射方向或分支。";

    public double ScatterFraction { get; }

    public RealRay Scatter(RealRay ray, Vector3D surfaceNormal)
    {
        return ray with { Intensity = ray.Intensity * (1.0 - ScatterFraction) };
    }

    public virtual IScatteringModel Clone() => new MainRayScatterLossApproximation(ScatterFraction);
}

[Obsolete("Compatibility alias only; no Lambertian direction sampling is performed. Use MainRayScatterLossApproximation.")]
public sealed class LambertianScatteringModel : MainRayScatterLossApproximation
{
    public LambertianScatteringModel(double scatterFraction) : base(scatterFraction)
    {
    }

    public override IScatteringModel Clone() => new LambertianScatteringModel(ScatterFraction);
}

public class MeanMeasuredScatterLoss : IScatteringModel
{
    public MeanMeasuredScatterLoss(IReadOnlyList<(double AngleDegrees, double Value)> samples)
    {
        Samples = samples.ToArray();
    }

    public virtual string Kind => "mean_measured_scatter_loss";

    public string ExperimentalWarning =>
        "Experimental：仅使用测量样本均值作为主光线损耗，不执行 BSDF Evaluate、Sample 或 Pdf。";

    public IReadOnlyList<(double AngleDegrees, double Value)> Samples { get; }

    public RealRay Scatter(RealRay ray, Vector3D surfaceNormal)
    {
        var loss = Samples.Count == 0 ? 0.0 : Math.Clamp(Samples.Average(sample => sample.Value), 0, 1);
        return ray with { Intensity = ray.Intensity * (1.0 - loss) };
    }

    public virtual IScatteringModel Clone() => new MeanMeasuredScatterLoss(Samples);
}

[Obsolete("Compatibility alias only; no BSDF evaluation or direction sampling is performed. Use MeanMeasuredScatterLoss.")]
public sealed class MeasuredBsdfScatteringModel : MeanMeasuredScatterLoss
{
    public MeasuredBsdfScatteringModel(IReadOnlyList<(double AngleDegrees, double Value)> samples) : base(samples)
    {
    }

    public override IScatteringModel Clone() => new MeasuredBsdfScatteringModel(Samples);
}
