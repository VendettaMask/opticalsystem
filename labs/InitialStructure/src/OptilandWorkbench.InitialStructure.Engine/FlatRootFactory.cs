using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

public sealed class FlatRootFactory
{
    public OpticSnapshot Create(
        InitialStructureSpecification specification,
        int elementCount,
        int stopVariant = 1)
    {
        SpecificationValidator.Validate(specification);
        if (elementCount < specification.MinimumElementCount
            || elementCount > specification.MaximumElementCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elementCount),
                $"Element count must be between {specification.MinimumElementCount} and {specification.MaximumElementCount}.");
        }

        if (stopVariant is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(stopVariant));
        }

        var optic = new Optic($"{specification.Name} - {elementCount} element flat root");
        optic.Aperture.Kind = ApertureKind.FNumber;
        optic.Aperture.Value = specification.FNumber;
        optic.Materials.SetPreferredGlassCatalogs(specification.GlassCatalogs);
        optic.Materials.Resolve(specification.InitialGlass);

        AddFields(optic, specification.MaximumFieldAngleDegrees);
        AddWavelengths(optic, specification.Wavelengths);
        optic.SurfaceGroup.ImportLegacySurfaces(BuildSurfaces(specification, elementCount, stopVariant));

        var snapshot = optic.ToSnapshot();
        OpticSnapshotValidator.Validate(snapshot);
        return snapshot;
    }

    private static void AddFields(Optic optic, double maximumFieldAngleDegrees)
    {
        optic.Fields.Add(new FieldPoint { Label = "On axis", Weight = 1 });
        if (maximumFieldAngleDegrees > 0)
        {
            optic.Fields.Add(new FieldPoint
            {
                Label = "Mid field",
                YAngleDegrees = maximumFieldAngleDegrees / 2,
                Weight = 1
            });
            optic.Fields.Add(new FieldPoint
            {
                Label = "Full field",
                YAngleDegrees = maximumFieldAngleDegrees,
                Weight = 1
            });
        }
    }

    private static void AddWavelengths(
        Optic optic,
        IReadOnlyList<WavelengthSpecification> wavelengths)
    {
        foreach (var wavelength in wavelengths)
        {
            optic.Wavelengths.Add(new Wavelength
            {
                Label = wavelength.Label,
                Nanometers = wavelength.Nanometers,
                Weight = wavelength.Weight,
                IsPrimary = wavelength.IsPrimary
            });
        }
    }

    private static IReadOnlyList<OpticalSurface> BuildSurfaces(
        InitialStructureSpecification specification,
        int elementCount,
        int stopVariant)
    {
        var apertureRadius = specification.EffectiveFocalLengthMillimeters
            / (2 * specification.FNumber);
        var semiDiameter = Math.Max(1, apertureRadius * specification.SemiDiameterMarginFactor);
        var surfaces = new List<OpticalSurface>
        {
            new()
            {
                Label = "Object",
                Radius = 0,
                Thickness = double.PositiveInfinity,
                Material = "Air",
                SemiDiameter = semiDiameter
            }
        };
        var stopElement = stopVariant switch
        {
            0 => 0,
            2 => elementCount - 1,
            _ => elementCount / 2
        };

        for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
        {
            surfaces.Add(new OpticalSurface
            {
                Label = $"Element {elementIndex + 1} front",
                Radius = 0,
                Thickness = specification.MinimumCenterThicknessMillimeters,
                Material = specification.InitialGlass,
                SemiDiameter = semiDiameter,
                RadiusVariable = true,
                ThicknessVariable = true,
                IsStop = elementIndex == stopElement
            });
            surfaces.Add(new OpticalSurface
            {
                Label = $"Element {elementIndex + 1} back",
                Radius = 0,
                Thickness = elementIndex == elementCount - 1
                    ? specification.MinimumBackFocusMillimeters
                    : specification.MinimumAirGapMillimeters,
                Material = "Air",
                SemiDiameter = semiDiameter,
                RadiusVariable = true,
                ThicknessVariable = true
            });
        }

        surfaces.Add(new OpticalSurface
        {
            Label = "Image",
            Radius = 0,
            Thickness = 0,
            Material = "Air",
            SemiDiameter = semiDiameter
        });
        return surfaces;
    }
}
