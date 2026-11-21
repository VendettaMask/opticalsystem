using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Core.Services;

public static class ModelGlassParameters
{
    public static string Property(string key) => $"model:{key}";
    public static bool IsProperty(string? property) => property is not null && property.StartsWith("model:", StringComparison.Ordinal)
        && ModelGlassMaterial.ParameterNames.Contains(property[6..]);
    public static ModelGlassMaterial Require(OpticalSurface surface) => surface.MaterialAfter as ModelGlassMaterial
        ?? throw new NotSupportedException("The surface does not contain a model glass.");
    public static double Read(OpticalSurface surface, string key)
    {
        var model = Require(surface);
        return key switch { "nd" => model.Nd, "vd" => model.Vd, "dpgf" => model.DPgF, _ => throw new ArgumentException("Unknown model-glass parameter.", nameof(key)) };
    }
    public static void WriteStored(OpticalSurface surface, string key, double value)
    {
        _ = Read(surface, key); var model = Require(surface);
        surface.MaterialAfter = new ModelGlassMaterial(model.Name, key == "nd" ? value : model.Nd, key == "vd" ? value : model.Vd,
            key == "dpgf" ? value : model.DPgF, model.VaryNd, model.VaryVd, model.VaryDPgF);
    }
    public static void Write(Optic optic, OpticalSurface surface, string key, double value)
    {
        WriteStored(surface, key, value);
        SynchronizeFollowingMedia(optic, surface.Number);
        optic.InvalidateRayTraceCache();
    }
    public static void SynchronizeFollowingMedia(Optic optic, int surfaceNumber)
    {
        for (var index = surfaceNumber + 1; index < optic.SurfaceGroup.Items.Count; index++)
        {
            var surface = optic.SurfaceGroup.Items[index];
            surface.MaterialBefore = optic.SurfaceGroup.Items[index - 1].MaterialAfter.Clone();
            if (!surface.IsReflective && surface.InteractionModel is not Interactions.CoordinateReferenceInteractionModel) break;
            surface.MaterialAfter = surface.MaterialBefore.Clone();
        }
    }
    public static void SetVariable(OpticalSurface surface, string key, bool enabled)
    {
        _ = Read(surface, key); var model = Require(surface);
        surface.MaterialAfter = new ModelGlassMaterial(model.Name, model.Nd, model.Vd, model.DPgF,
            key == "nd" ? enabled : model.VaryNd, key == "vd" ? enabled : model.VaryVd, key == "dpgf" ? enabled : model.VaryDPgF);
    }
    public static IOptimizationVariable CreateVariable(Optic optic, OpticalSurface surface, string key, Action changed)
    {
        var model = Require(surface);
        if (!model.Variables.Contains(key)) throw new ArgumentException("The model-glass parameter is not marked variable.");
        if (key != "nd" && model.Vd == 0) throw new NotSupportedException("Set a positive Vd before optimizing model-glass dispersion; Vd=0 explicitly disables dispersion.");
        var initial = Read(surface, key);
        var extent = key switch { "nd" => .2, "vd" => Math.Max(10, initial * .5), _ => Math.Max(.03, Math.Abs(initial) * 2) };
        var lower = key == "nd" ? Math.Max(1.000001, initial - extent) : key == "vd" ? Math.Max(.01, initial - extent) : initial - extent;
        var upper = initial + extent;
        return new DelegateVariable($"Surface {surface.Number} model glass {key}", () => Read(surface, key),
            value => { Write(optic, surface, key, value); changed(); }, lower, upper, (upper - lower) * .01, new UnitRangeScaler(lower, upper));
    }
}
