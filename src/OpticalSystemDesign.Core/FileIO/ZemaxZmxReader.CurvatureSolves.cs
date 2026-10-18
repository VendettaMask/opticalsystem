using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private sealed record ZemaxCurvatureSolve(int Code, double Value);

    private static void ApplyCurvatureSolves(Optic optic, IReadOnlyList<ZemaxSurface> configuredSurfaces,
        IReadOnlyList<string> glassCatalogs)
    {
        // These imported paraxial solves are defined for a centered standard
        // prescription. Other solve/geometry contracts retain their stored data.
        if (configuredSurfaces.Any(surface => surface.Type != "STANDARD")) return;
        var wavelength = (optic.Wavelengths.FirstOrDefault(wave => wave.IsPrimary)
            ?? optic.Wavelengths.First()).Micrometers;
        var stop = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        for (var index = 1; index < configuredSurfaces.Count - 1; index++)
        {
            var source = configuredSurfaces[index];
            if (source.CurvatureSolve is not { } solve) continue;
            if (stop < 0 || index <= stop || optic.SurfaceGroup.Items[index].IsReflective
                || solve.Code == 11 && optic.Aperture.Kind != ApertureKind.EntrancePupilDiameter) continue;
            var target = solve.Code == 2 ? solve.Value
                : 1 / (solve.Value * optic.Paraxial.EstimateEntrancePupilDiameter());
            if (!double.IsFinite(target))
                throw new InvalidDataException($"Zemax curvature solve on surface {source.Number} has an invalid target.");
            var current = Metric();
            // Preserve already solved serialized prescriptions. Re-evaluate
            // when aperture/configuration changes invalidate their stored value.
            if (Math.Abs(current - target) <= 1e-12 * Math.Max(Math.Abs(target), 1e-12)) continue;
            var surface = optic.SurfaceGroup.Items[index];
            var storedRadius = surface.Radius;
            double zero;
            double unit;
            try
            {
                surface.Radius = 0;
                zero = Metric();
                surface.Radius = 1;
                unit = Metric();
            }
            finally { surface.Radius = storedRadius; }
            var response = unit - zero;
            // A slope solve usually adjusts an already close stored solution.
            // Solve its increment so subtraction of the large zero-curvature
            // response does not lose the small aperture-induced correction.
            var curvature = solve.Code == 2
                ? (storedRadius == 0 ? 0 : 1 / storedRadius) + (target - current) / response
                : (target - zero) / response;
            if (!double.IsFinite(curvature) || response == 0)
                throw new InvalidDataException($"Zemax curvature solve on surface {source.Number} is singular.");
            source.Radius = curvature == 0 ? 0 : 1 / curvature;
            var converted = ConvertSurfaces(optic, configuredSurfaces, glassCatalogs);
            InstallConvertedSurfaces(optic, converted);

            double Metric() => solve.Code == 2
                ? optic.Paraxial.MarginalRay(wavelength).Slopes[index][0]
                : optic.Paraxial.EstimateOpticalPower();
        }
    }
}
