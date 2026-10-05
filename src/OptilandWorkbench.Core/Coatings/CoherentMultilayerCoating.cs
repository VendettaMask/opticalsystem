using System.Numerics;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Coatings;

public sealed record CoatingJonesPower(Complex S, Complex P);

/// <summary>
/// Physical coherent, planar, isotropic films in incident-to-substrate order.
/// Exterior media and angle come from the actual surface interaction, not names.
/// Scalar tracing uses the single-interface unpolarized mean. Polarized tracing
/// bypasses that scalar factor and transports the full complex Jones response.
/// </summary>
public sealed class CoherentMultilayerCoating : ICoatingModel
{
    private readonly CoherentFilm[] _layers;

    public CoherentMultilayerCoating(IEnumerable<CoherentFilm> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        _layers = layers.Take(CoherentThinFilmSolver.MaximumLayers + 1).ToArray();
        if (_layers.Length > CoherentThinFilmSolver.MaximumLayers)
            throw new ArgumentException("膜层数量超过计算上限。", nameof(layers));
        if (_layers.Any(layer => layer.Material is null
            || layer.Material.PropagationModel is not HomogeneousPropagationModel
            || !double.IsFinite(layer.ThicknessNanometers) || layer.ThicknessNanometers < 0))
            throw new ArgumentException("相干膜层需要均匀材料及非负有限厚度（nm）。", nameof(layers));
        foreach (var layer in _layers)
        {
            layer.Adjustment.Validate();
            if (!double.IsFinite(layer.ThicknessNanometers * layer.Adjustment.Multiplier))
                throw new ArgumentException("调整后的膜层厚度溢出。", nameof(layers));
            if (layer.ThicknessNanometers == 0 && layer.Adjustment.MultiplierVariable)
                throw new ArgumentException("零基础厚度不能设为倍率变量。", nameof(layers));
        }
        _layers = _layers.Select(layer => layer with { Material = layer.Material.Clone() }).ToArray();
    }

    public string Kind => "coherent_multilayer";
    // Material instances are not exposed; edits must replace the immutable model.
    public IReadOnlyList<CoherentFilm> Layers => _layers.Select(layer =>
        layer with { Material = layer.Material.Clone() }).ToArray();

    public CoherentMultilayerCoating WithLayerParameters(int oneBasedLayer, CoatingLayerParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (oneBasedLayer < 1 || oneBasedLayer > _layers.Length)
            throw new ArgumentOutOfRangeException(nameof(oneBasedLayer), "膜层编号不存在。");
        var layers = _layers.ToArray();
        layers[oneBasedLayer - 1] = layers[oneBasedLayer - 1] with { Parameters = parameters };
        return new(layers);
    }

    public RealRay Apply(RealRay ray, SurfaceInteractionContext context)
    {
        var (solver, angle) = Prepare(context);
        var response = solver.Evaluate(context.WavelengthNanometers, angle, ThinFilmPolarization.Unpolarized);
        return ray with { Intensity = ray.Intensity * (context.IsReflective ? response.Reflectance : response.Transmittance) };
    }

    public CoatingJonesPower EvaluateJones(SurfaceInteractionContext context)
    {
        var (s, p) = EvaluateAmplitudes(context);
        if (context.IsReflective) return new(s.Reflection, p.Reflection);
        if (s.PowerTransmission is not { } ts || p.PowerTransmission is not { } tp)
            throw new NotSupportedException("吸收基底中的非均匀透射波尚不能接入实光线 Jones 功率链。");
        return new(ts, tp);
    }

    public (ThinFilmAmplitude S, ThinFilmAmplitude P) EvaluateAmplitudes(SurfaceInteractionContext context)
    {
        var (solver, angle) = Prepare(context);
        return (solver.EvaluateAmplitude(context.WavelengthNanometers, angle, ThinFilmPolarization.S),
            solver.EvaluateAmplitude(context.WavelengthNanometers, angle, ThinFilmPolarization.P));
    }

    public ICoatingModel Clone() => new CoherentMultilayerCoating(_layers);

    private (CoherentThinFilmSolver Solver, double Angle) Prepare(SurfaceInteractionContext context)
    {
        if (context.IncidentDirection is not { } incoming
            || context.ExtinctionCoefficientBefore is not { } k1
            || context.ExtinctionCoefficientAfter is not { } k2)
            throw new InvalidOperationException("物理镀膜需要实际入射方向和两侧介质的消光系数。");
        var normal = context.SurfaceNormal;
        var cosine = Math.Abs((incoming.X * normal.X + incoming.Y * normal.Y + incoming.Z * normal.Z)
            / (incoming.Length * normal.Length));
        if (!double.IsFinite(cosine)) throw new ArgumentException("镀膜入射方向或表面法线无效。");
        var angle = Math.Acos(Math.Clamp(cosine, 0, 1)) * 180 / Math.PI;
        return (new CoherentThinFilmSolver(
            new ConstantIndexMaterial("incident", context.RefractiveIndexBefore, k1),
            new ConstantIndexMaterial("substrate", context.RefractiveIndexAfter, k2), _layers), angle);
    }
}
