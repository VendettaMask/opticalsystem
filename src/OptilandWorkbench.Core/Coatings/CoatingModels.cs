using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Coatings;

public interface ICoatingModel
{
    string Kind { get; }

    RealRay Apply(RealRay ray, SurfaceInteractionContext context);

    ICoatingModel Clone();
}

public sealed class NoneCoatingModel : ICoatingModel
{
    public string Kind => "none";

    public RealRay Apply(RealRay ray, SurfaceInteractionContext context) => ray;

    public ICoatingModel Clone() => new NoneCoatingModel();
}

public sealed class SimpleCoatingModel : ICoatingModel
{
    public SimpleCoatingModel(double transmittance, double reflectance = 0)
    {
        Transmittance = transmittance;
        Reflectance = reflectance;
    }

    public string Kind => "simple";

    public double Transmittance { get; }

    public double Reflectance { get; }

    public RealRay Apply(RealRay ray, SurfaceInteractionContext context)
    {
        var factor = context.IsReflective ? Reflectance : Transmittance;
        return ray with { Intensity = ray.Intensity * factor };
    }

    public ICoatingModel Clone() => new SimpleCoatingModel(Transmittance, Reflectance);
}

public sealed record ThinFilmLayer(string MaterialName, double ThicknessNanometers);

public class ApproximateTransmissionRippleCoating : ICoatingModel
{
    public ApproximateTransmissionRippleCoating(IEnumerable<ThinFilmLayer> layers)
    {
        Layers = layers.ToList();
    }

    public virtual string Kind => "approximate_transmission_ripple";

    public string ExperimentalWarning =>
        "Experimental：仅按总物理厚度生成经验透过率起伏；不计算膜层折射率、入射角、偏振、相位或吸收。";

    public IReadOnlyList<ThinFilmLayer> Layers { get; }

    public RealRay Apply(RealRay ray, SurfaceInteractionContext context)
    {
        var transmission = EstimateTransmission(context.WavelengthNanometers);
        return ray with { Intensity = ray.Intensity * transmission };
    }

    public virtual ICoatingModel Clone() => new ApproximateTransmissionRippleCoating(Layers);

    public double EstimateTransmission(double wavelengthNanometers)
    {
        if (Layers.Count == 0)
        {
            return 1.0;
        }

        var phaseDepth = Layers.Sum(layer => layer.ThicknessNanometers / Math.Max(1, wavelengthNanometers));
        var ripple = 0.04 * Math.Abs(Math.Sin(2 * Math.PI * phaseDepth));
        return Math.Clamp(0.96 - ripple, 0.0, 1.0);
    }
}

[Obsolete("Compatibility alias only; this is not a physical thin-film solver. Use ApproximateTransmissionRippleCoating.")]
public sealed class ThinFilmStackCoating : ApproximateTransmissionRippleCoating
{
    public ThinFilmStackCoating(IEnumerable<ThinFilmLayer> layers) : base(layers)
    {
    }

    public override ICoatingModel Clone() => new ThinFilmStackCoating(Layers);
}

public sealed class ApproximateTransmissionRippleDesigner
{
    public ApproximateTransmissionRippleCoating DesignAlternatingLayers(
        IReadOnlyList<string> candidateMaterials,
        double targetWavelengthNanometers,
        int layers)
    {
        if (candidateMaterials.Count == 0)
        {
            return new ApproximateTransmissionRippleCoating(Array.Empty<ThinFilmLayer>());
        }

        var stack = Enumerable.Range(0, Math.Max(1, layers))
            .Select(index => new ThinFilmLayer(
                candidateMaterials[index % candidateMaterials.Count],
                targetWavelengthNanometers / 4.0))
            .ToArray();

        return new ApproximateTransmissionRippleCoating(stack);
    }
}

[Obsolete("Compatibility alias only; this does not perform needle synthesis or a physical quarter-wave design. Use ApproximateTransmissionRippleDesigner.")]
public sealed class NeedleSynthesisDesigner
{
    public ThinFilmStackCoating DesignQuarterWaveStack(
        IReadOnlyList<string> candidateMaterials,
        double targetWavelengthNanometers,
        int layers)
    {
        var approximation = new ApproximateTransmissionRippleDesigner().DesignAlternatingLayers(
            candidateMaterials,
            targetWavelengthNanometers,
            layers);
        return new ThinFilmStackCoating(approximation.Layers);
    }
}
