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
        var maxField = _optic.Fields.Count == 0 ? 0 : _optic.Fields.Max(field => Math.Abs(field.YAngleDegrees));
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
}
