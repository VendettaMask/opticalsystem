using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Core.Services;

public static class CoatingLayerMetrics
{
    public static CoherentMultilayerCoating RequireCoating(OpticalSurface surface) =>
        surface.CoatingModel as CoherentMultilayerCoating
        ?? throw new NotSupportedException($"表面 {surface.Number} 没有可调整的物理相干膜层。");

    public static CoherentFilm Layer(OpticalSurface surface, int oneBasedLayer)
    {
        var layers = RequireCoating(surface).Layers;
        if (oneBasedLayer <= 0 || oneBasedLayer > layers.Count)
            throw new ArgumentOutOfRangeException(nameof(oneBasedLayer), "膜层编号不存在。");
        return layers[oneBasedLayer - 1];
    }

    public static void Write(Optic optic, OpticalSurface surface, int layer, CoatingLayerParameter kind, double value)
    {
        var replacement = RequireCoating(surface).WithLayerParameters(layer, Layer(surface, layer).Adjustment.WithValue(kind, value));
        ValidateAtSystemWavelengths(optic, replacement);
        optic.InvalidateRayTraceCache();
        surface.CoatingModel = replacement;
    }

    public static void ValidateAtSystemWavelengths(Optic optic, CoherentMultilayerCoating coating)
    {
        // Validation uses the same wavelength-dependent n/k path as the physical solver.
        var solver = new CoherentThinFilmSolver(new Materials.AirMaterial(), new Materials.AirMaterial(), coating.Layers);
        foreach (var wavelength in optic.Wavelengths)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            _ = solver.Evaluate(wavelength.Nanometers, 0, ThinFilmPolarization.S);
        }
    }

    public static IOptimizationVariable CreateVariable(Optic optic, OpticalSurface surface, int layer, CoatingLayerParameter kind)
    {
        var film = Layer(surface, layer);
        var current = film.Adjustment.Value(kind);
        if (kind == CoatingLayerParameter.Multiplier && film.ThicknessNanometers == 0)
            throw new ArgumentException("零基础厚度不能优化倍率。");
        if (optic.Wavelengths.Count == 0) throw new ArgumentException("膜层优化需要系统波长。");
        var lower = kind switch
        {
            CoatingLayerParameter.Multiplier => 0,
            CoatingLayerParameter.IndexOffset => -optic.Wavelengths.Min(w => film.Material.RefractiveIndex(w.Nanometers)) + 1e-6,
            _ => -optic.Wavelengths.Min(w => film.Material.ExtinctionCoefficient(w.Nanometers))
        };
        var upper = kind == CoatingLayerParameter.Multiplier ? 10 : Math.Max(current + 1, 10 + lower);
        // Preserve every legal starting value, including very small positive adjusted n.
        lower = Math.Min(lower, current);
        return new DelegateVariable($"表面 {surface.Number} 膜层 {layer} {kind}",
            () => Layer(surface, layer).Adjustment.Value(kind),
            value => Write(optic, surface, layer, kind, value), lower, upper, .01,
            new UnitRangeScaler(lower, upper));
    }
}
