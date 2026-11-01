using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private static IGeometry CreateGridGradientGeometry(ZemaxSurface source)
    {
        if (!double.IsInfinity(source.Radius) && source.Radius != 0 || source.Conic != 0 || source.TypeArguments.Length == 0
            || source.Parameters.Any(p => p.Key != 0 && p.Value != 0) || source.ExtraData.Any(p => p.Value != 0)) return CreateOpaqueZemaxGeometry(source);
        source.GridGradientProfile = GridGradientCatalog.Resolve(source.TypeArguments, source.AssetDirectory, source.LengthScaleToMillimeters);
        return source.GridGradientProfile is null ? CreateOpaqueZemaxGeometry(source) : new PlaneGeometry();
    }
    private static GradientIndexMaterial CreateGridGradientMaterial(ZemaxSurface source)
    {
        var step = source.Parameter(0);
        if (!double.IsFinite(step) || step <= 0) throw new InvalidDataException("Grid GRIN requires a positive maximum integration step.");
        var profile = source.GridGradientProfile ?? throw new InvalidDataException("Missing grid-gradient data.");
        var extent = new Backend.Vector3D((profile.Nx - 1) * profile.Spacing.X, (profile.Ny - 1) * profile.Spacing.Y, (profile.Nz - 1) * profile.Spacing.Z).Length;
        var attempts = (int)Math.Clamp(Math.Ceiling(4 * extent / step) + 128, 100_000, 1_000_000);
        var options = new GradientIndexIntegrationOptions(MaximumStep: step, MaximumAttempts: attempts);
        return new GradientIndexMaterial("Grid GRIN: " + Path.GetFileName(source.TypeArguments), source.GridGradientProfile
            ?? throw new InvalidDataException("Missing grid-gradient data."), step * options.MaximumAttempts, options);
    }
}
