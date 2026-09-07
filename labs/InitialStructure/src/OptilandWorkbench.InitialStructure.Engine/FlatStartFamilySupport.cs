using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal static class FlatStartFamilySupport
{
    public static void Validate(InitialStructureSpecification specification, FlatStartFamily family)
    {
        if (family.GlassNames is null || family.CenterThicknesses is null || family.AirGaps is null
            || family.ElementCount < specification.MinimumElementCount || family.ElementCount > specification.MaximumElementCount
            || family.CenterThicknesses.Count != family.ElementCount || family.AirGaps.Count != family.ElementCount - 1
            || family.StopSurfaceIndex < 1 || family.StopSurfaceIndex > 2 * family.ElementCount || family.SeedIndex < 0
            || family.GlassNames.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 256)
            || family.CenterThicknesses.Any(value => !double.IsFinite(value) || value < specification.MinimumCenterThicknessMillimeters)
            || family.AirGaps.Any(value => !double.IsFinite(value) || value < specification.MinimumAirGapMillimeters))
            throw new ArgumentException("Invalid flat-start family.", nameof(family));
        var length = family.CenterThicknesses.Sum() + family.AirGaps.Sum()
            + (specification.FlatStart?.FixedBackFocusMillimeters ?? specification.MinimumBackFocusMillimeters);
        if (!double.IsFinite(length) || length > specification.MaximumTrackLengthMillimeters + 1e-9)
            throw new ArgumentException("The flat family exceeds the requested track length.", nameof(family));
    }

    public static void ValidateGlass(Optic optic, string name, InitialStructureSpecification specification)
    {
        var material = optic.Materials.Resolve(name);
        if (material is not CatalogGlassMaterial catalog
            || !double.IsFinite(catalog.MinimumWavelengthNanometers) || !double.IsFinite(catalog.MaximumWavelengthNanometers)
            || catalog.MinimumWavelengthNanometers <= 0 || catalog.MaximumWavelengthNanometers < catalog.MinimumWavelengthNanometers)
            throw new InvalidOperationException($"Glass '{name}' has no declared catalog wavelength range for flat-start validation.");
        foreach (var wavelength in specification.Wavelengths)
        {
            if (wavelength.Nanometers < catalog.MinimumWavelengthNanometers || wavelength.Nanometers > catalog.MaximumWavelengthNanometers)
                throw new InvalidOperationException($"Glass '{name}' does not cover {wavelength.Nanometers} nm; catalog range is {catalog.MinimumWavelengthNanometers}–{catalog.MaximumWavelengthNanometers} nm.");
            var index = material.RefractiveIndex(wavelength.Nanometers);
            if (!double.IsFinite(index) || index <= 1)
                throw new InvalidOperationException($"Glass '{name}' has no usable index at {wavelength.Nanometers} nm.");
        }
    }
}
