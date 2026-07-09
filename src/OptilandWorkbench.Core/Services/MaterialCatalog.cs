using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public static class MaterialCatalog
{
    public static double RefractiveIndex(string material, Wavelength wavelength)
    {
        var normalized = material.Trim().ToUpperInvariant();
        var dLineOffset = (wavelength.Nanometers - 587.6) / 1000.0;

        return normalized switch
        {
            "AIR" => 1.0,
            "VACUUM" => 1.0,
            "N-BK7" or "BK7" => 1.5168 - 0.011 * dLineOffset,
            "N-F2" or "F2" => 1.6200 - 0.021 * dLineOffset,
            "FUSED SILICA" or "SILICA" => 1.4585 - 0.006 * dLineOffset,
            _ => 1.50 - 0.010 * dLineOffset
        };
    }
}
