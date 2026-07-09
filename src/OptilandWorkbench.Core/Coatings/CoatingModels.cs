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

public sealed record ThinFilmLayer(string MaterialName, double ThicknessNanometers);

public sealed class ThinFilmStackCoating : ICoatingModel
{
    public ThinFilmStackCoating(IEnumerable<ThinFilmLayer> layers)
    {
        Layers = layers.ToList();
    }

    public string Kind => "thin_film_stack";

    public IReadOnlyList<ThinFilmLayer> Layers { get; }

    public RealRay Apply(RealRay ray, SurfaceInteractionContext context)
    {
        var transmission = EstimateTransmission(context.WavelengthNanometers);
        return ray with { Intensity = ray.Intensity * transmission };
    }

    public ICoatingModel Clone() => new ThinFilmStackCoating(Layers);

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

public sealed class NeedleSynthesisDesigner
{
    public ThinFilmStackCoating DesignQuarterWaveStack(
        IReadOnlyList<string> candidateMaterials,
        double targetWavelengthNanometers,
        int layers)
    {
        if (candidateMaterials.Count == 0)
        {
            return new ThinFilmStackCoating(Array.Empty<ThinFilmLayer>());
        }

        var stack = Enumerable.Range(0, Math.Max(1, layers))
            .Select(index => new ThinFilmLayer(
                candidateMaterials[index % candidateMaterials.Count],
                targetWavelengthNanometers / 4.0))
            .ToArray();

        return new ThinFilmStackCoating(stack);
    }
}
