using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public sealed record AberrationEstimate(
    double Spherical,
    double Coma,
    double Astigmatism,
    double Chromatic);

public sealed class Aberrations
{
    private readonly Optic _optic;

    public Aberrations(Optic optic)
    {
        _optic = optic;
    }

    public AberrationEstimate Estimate()
    {
        var aperture = _optic.SurfaceGroup.ApertureRadius();
        var maxField = MaximumLaunchAngleDegrees();
        var poweredSurfaces = _optic.SurfaceGroup.Items.Where(surface => !surface.IsPlane).ToArray();
        var curvatureSquared = poweredSurfaces.Sum(surface => 1.0 / Math.Pow(Math.Abs(surface.Radius), 2));
        var curvatureCubed = poweredSurfaces.Sum(surface => 1.0 / Math.Pow(Math.Abs(surface.Radius), 3));
        var wavelengthSpan = _optic.Wavelengths.Count == 0
            ? 0
            : _optic.Wavelengths.Max(item => item.Nanometers) - _optic.Wavelengths.Min(item => item.Nanometers);

        return new AberrationEstimate(
            Spherical: aperture * aperture * aperture * curvatureCubed,
            Coma: aperture * maxField * curvatureSquared / 10.0,
            Astigmatism: maxField * maxField * curvatureSquared / 100.0,
            Chromatic: wavelengthSpan * curvatureSquared / 500.0);
    }

    private double MaximumLaunchAngleDegrees()
    {
        var wavelength = (_optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? _optic.Wavelengths.FirstOrDefault())?.Micrometers ?? 0.5876;
        return _optic.Fields.Select(field =>
        {
            var normalized = FieldCoordinates.Normalize(_optic.Fields, field.X, field.Y);
            var ray = _optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                normalized.X,
                normalized.Y,
                0,
                0,
                wavelength).Rays.Single();
            var transverse = Math.Sqrt(
                (ray.Direction.X * ray.Direction.X) + (ray.Direction.Y * ray.Direction.Y));
            return Math.Atan2(transverse, Math.Abs(ray.Direction.Z)) * 180.0 / Math.PI;
        }).DefaultIfEmpty(0).Max();
    }
}
