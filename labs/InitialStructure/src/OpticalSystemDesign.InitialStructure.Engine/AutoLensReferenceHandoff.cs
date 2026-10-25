using System.Globalization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>AutoLens e58 JSON write/read conventions, independent of formal Core optical analysis.</summary>
internal static class AutoLensReferenceHandoff
{
    public static void Apply(Optic optic, double originOffset)
    {
        var surfaces = optic.SurfaceGroup.Items;
        var original = surfaces.Select(surface => surface.CoordinateSystem.Origin.Z).ToArray();
        var positions = new double[surfaces.Count];
        // read_lens_json starts d at zero and overwrites EVERY stored surface d with the
        // running sum of rounded d_next. Independent rounding of surface d is not equivalent.
        for (var index = 1; index + 1 < surfaces.Count; index++)
        {
            positions[index + 1] = positions[index] + Math.Round(original[index + 1] - original[index], 4);
            if (!surfaces[index].IsStop)
            {
                var profile = OpticalDesignParameters.Profile(surfaces[index].Geometry);
                var radius = profile.Curvature == 0 ? 0 : Math.Round(1 / profile.Curvature, 4);
                var coefficients = profile.Coefficients.Select(value => double.Parse(value.ToString("e6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)).ToArray();
                surfaces[index].Geometry = coefficients.Length == 0 ? new StandardGeometry(radius, Math.Round(profile.Conic, 4))
                    : new EvenAsphereGeometry(radius, Math.Round(profile.Conic, 4), coefficients);
            }
            RotationalDesignGeometry.SetRadius(optic, index, Math.Round(surfaces[index].SemiDiameter, 4));
        }
        for (var index = 1; index + 1 < surfaces.Count; index++) surfaces[index].Thickness = positions[index + 1] - positions[index];
        surfaces[0].Thickness = originOffset;
        optic.SurfaceGroup.Renumber();
        optic.InvalidateRayTraceCache();
    }
}
