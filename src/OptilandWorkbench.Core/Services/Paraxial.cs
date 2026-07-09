namespace OptilandWorkbench.Core.Services;

public sealed class Paraxial
{
    private readonly Optic _optic;

    public Paraxial(Optic optic)
    {
        _optic = optic;
    }

    public double EstimateEffectiveFocalLength()
    {
        var primary = _optic.Wavelengths.FirstOrDefault(item => item.IsPrimary) ?? _optic.Wavelengths.FirstOrDefault();
        if (primary is null)
        {
            return 0;
        }

        var power = _optic.SurfaceGroup.Items
            .Where(surface => !surface.IsPlane)
            .Sum(surface => (MaterialCatalog.RefractiveIndex(surface.Material, primary) - 1.0) / surface.Radius);

        return Math.Abs(power) < 1e-9 ? 0 : 1.0 / power;
    }

    public double EstimateFNumber()
    {
        var focalLength = Math.Abs(EstimateEffectiveFocalLength());
        var apertureDiameter = _optic.SurfaceGroup.ApertureRadius() * 2.0;
        return apertureDiameter <= 0 || focalLength <= 0 ? 0 : focalLength / apertureDiameter;
    }
}
