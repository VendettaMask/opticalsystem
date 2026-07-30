using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Legacy;

public partial class OpticalWorkspaceModel
{
    private OpticalSurface GetSurfaceByNumber(int surfaceNumber)
    {
        return Surfaces.First(surface => surface.Number == surfaceNumber);
    }

    private void SyncActiveConfigurationFromCurrent()
    {
        if (_activeConfigurationIndex >= 0 && _activeConfigurationIndex < _multiConfiguration.Configurations.Count)
        {
            _multiConfiguration.Configurations[_activeConfigurationIndex].ApplySnapshot(CurrentOptic.ToSnapshot());
        }
    }

    private static void SetSurfaceRadius(OpticalSurface surface, double radius)
    {
        surface.Radius = Math.Abs(radius) < 1e-9
            ? Math.CopySign(1e-9, radius == 0 ? 1 : radius)
            : radius;
        SyncSurfaceGeometry(surface);
    }

    private static void SyncSurfaceGeometry(OpticalSurface surface)
    {
        surface.Geometry = surface.Geometry switch
        {
            PlaneGratingGeometry grating when Math.Abs(surface.Radius) < 1e-9 => grating,
            StandardGratingGeometry grating when Math.Abs(surface.Radius) < 1e-9 =>
                new PlaneGratingGeometry(
                    grating.GratingOrder,
                    grating.GratingPeriodMicrometers,
                    grating.GrooveOrientationAngleRadians),
            IGratingGeometry grating => new StandardGratingGeometry(
                surface.Radius,
                surface.Conic,
                grating.GratingOrder,
                grating.GratingPeriodMicrometers,
                grating.GrooveOrientationAngleRadians),
            EvenAsphereGeometry even => new EvenAsphereGeometry(
                surface.Radius,
                surface.Conic,
                even.Coefficients),
            OddAsphereGeometry odd => new OddAsphereGeometry(
                surface.Radius,
                surface.Conic,
                odd.Coefficients),
            ForbesQGeometry forbes => new ForbesQGeometry(
                surface.Radius,
                surface.Conic,
                forbes.NormalizationRadius,
                forbes.QCoefficients),
            BiconicGeometry biconic => new BiconicGeometry(
                surface.Radius,
                biconic.RadiusY,
                surface.Conic,
                biconic.ConicY),
            SeparableBiconicGeometry biconic => new SeparableBiconicGeometry(
                surface.Radius,
                biconic.RadiusY,
                surface.Conic,
                biconic.ConicY),
            ToroidalGeometry toroidal => new ToroidalGeometry(
                toroidal.TangentialRadius,
                surface.Radius),
            StandardGeometry when Math.Abs(surface.Radius) < 1e-9 => new PlaneGeometry(),
            StandardGeometry => new StandardGeometry(surface.Radius, surface.Conic),
            PlaneGeometry when Math.Abs(surface.Radius) >= 1e-9 =>
                new StandardGeometry(surface.Radius, surface.Conic),
            _ => surface.Geometry
        };
    }

    private static void SyncLegacyCoating(OpticalSurface surface)
    {
        surface.CoatingModel = surface.Coating.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? new NoneCoatingModel()
            : new ThinFilmStackCoating(new[] { new ThinFilmLayer(surface.Coating, 120) });
    }

    private bool IsValidBackend(string? backendName) =>
        !string.IsNullOrWhiteSpace(backendName) && CurrentOptic.Backend.Names.Contains(backendName);

    private void ApplyBackendValue(string? backendName)
    {
        if (IsValidBackend(backendName))
        {
            CurrentOptic.Backend.SetBackend(backendName!);
        }
    }

    private void ApplySystemApertureValue(string apertureKindName, double value)
    {
        if (TryNormalizeApertureKind(apertureKindName, out var kind))
        {
            ApplySystemApertureValue(kind, value);
        }
    }

    private void ApplySystemApertureValue(ApertureKind kind, double value)
    {
        CurrentOptic.Aperture.Kind = kind;
        CurrentOptic.Aperture.Value = kind == ApertureKind.FloatByStopSize
            ? CurrentOptic.SurfaceGroup.ApertureRadius()
            : Math.Max(0.001, value);
    }

    private void ApplyFieldDefinitionValue(string fieldDefinitionName, bool objectSpaceTelecentric)
    {
        var fieldDefinition = fieldDefinitionName switch
        {
            "物高" => FieldDefinitionKind.ObjectHeight,
            "近轴像高" => FieldDefinitionKind.ParaxialImageHeight,
            "实际像高" => FieldDefinitionKind.RealImageHeight,
            _ => FieldDefinitionKind.Angle
        };
        var telecentric = objectSpaceTelecentric && fieldDefinition != FieldDefinitionKind.Angle;

        CurrentOptic.FieldDefinition = fieldDefinition;
        CurrentOptic.ObjectSpaceTelecentric = telecentric;
        if (telecentric)
        {
            CurrentOptic.Aperture.Kind = ApertureKind.NumericalAperture;
            CurrentOptic.Aperture.Value = Math.Clamp(CurrentOptic.Aperture.Value, 0.001, 1);
        }
    }

    private void ApplyApodizationValue(string apodizationKind, double firstParameter, double secondParameter)
    {
        CurrentOptic.Apodization = CanonicalApodizationKind(apodizationKind) switch
        {
            "None" => null,
            "Uniform" => new UniformApodization(),
            "Gaussian" => new GaussianApodization(Math.Max(0.001, firstParameter)),
            "CosineSquared" => new CosineSquaredApodization(Math.Max(0.001, firstParameter)),
            "Hann" => new HannApodization(Math.Max(0.001, firstParameter)),
            "Polynomial" => new PolynomialApodization(
                Math.Max(0.001, firstParameter),
                Math.Max(0, secondParameter)),
            "SuperGaussian" => new SuperGaussianApodization(
                Math.Max(0.001, firstParameter),
                Math.Max(2, secondParameter)),
            "Tukey" => new TukeyApodization(
                Math.Max(0.001, firstParameter),
                Math.Clamp(secondParameter, 0, 1)),
            _ => null
        };
    }

    private static void ApplyGeometry(OpticalSurface surface, string geometryKind)
    {
        geometryKind = CanonicalGeometryKind(geometryKind);
        var radius = Math.Abs(surface.Radius) < 1e-9 ? 40 : surface.Radius;
        switch (geometryKind)
        {
            case "Plane":
                surface.Radius = 0;
                surface.Geometry = new PlaneGeometry();
                break;
            case "Plane Grating":
                surface.Radius = 0;
                surface.Geometry = new PlaneGratingGeometry(1, 1, 0);
                break;
            case "Standard Grating":
                surface.Radius = radius;
                surface.Geometry = new StandardGratingGeometry(radius, surface.Conic, 1, 1, 0);
                break;
            case "Even Asphere":
                surface.Radius = radius;
                surface.Geometry = new EvenAsphereGeometry(radius, surface.Conic, new[] { 0.0, 0.0 });
                break;
            case "Odd Asphere":
                surface.Radius = radius;
                surface.Geometry = new OddAsphereGeometry(radius, surface.Conic, new[] { 0.0, 0.0 });
                break;
            case "Biconic":
                surface.Radius = radius;
                surface.Geometry = new BiconicGeometry(radius, radius, surface.Conic, surface.Conic);
                break;
            case "Toroidal":
                surface.Radius = radius;
                surface.Geometry = new ToroidalGeometry(radius, radius);
                break;
            case "Polynomial":
                surface.Radius = radius;
                surface.Geometry = new PolynomialGeometry(new Dictionary<(int X, int Y), double>
                {
                    [(2, 0)] = Math.Abs(radius) < 1e-9 ? 0 : 1.0 / (2.0 * radius)
                });
                break;
            case "Chebyshev":
                surface.Radius = radius;
                surface.Geometry = new ChebyshevGeometry(new Dictionary<(int XOrder, int YOrder), double>
                {
                    [(2, 0)] = Math.Abs(radius) < 1e-9 ? 0 : 0.01 / Math.Abs(radius),
                    [(0, 2)] = Math.Abs(radius) < 1e-9 ? 0 : 0.01 / Math.Abs(radius)
                }, Math.Max(1, surface.SemiDiameter), Math.Max(1, surface.SemiDiameter));
                break;
            case "Zernike":
                surface.Radius = radius;
                surface.Geometry = new ZernikeGeometry(new Dictionary<(int RadialOrder, int AzimuthalFrequency), double>
                {
                    [(2, 0)] = Math.Abs(radius) < 1e-9 ? 0 : 0.01 / Math.Abs(radius)
                }, Math.Max(1, surface.SemiDiameter));
                break;
            case "Forbes Q":
                surface.Radius = radius;
                surface.Geometry = new ForbesQGeometry(radius, surface.Conic, Math.Max(1, surface.SemiDiameter), new[] { 0.0, 0.0 });
                break;
            default:
                surface.Radius = radius;
                surface.Geometry = new StandardGeometry(radius, surface.Conic);
                break;
        }
    }

    private void ApplyMaterial(OpticalSurface surface, string materialName)
    {
        var selectedMaterial = string.IsNullOrWhiteSpace(materialName) ? "Air" : materialName;
        var isMirror = selectedMaterial.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
        surface.Material = isMirror ? "MIRROR" : selectedMaterial;
        surface.MaterialAfter = isMirror
            ? surface.MaterialBefore.Clone()
            : CurrentOptic.Materials.Resolve(selectedMaterial);
        SyncInteractionReflectivity(surface, isMirror);
    }

    private static void SyncInteractionForEditedGeometry(
        OpticalSurface surface,
        double thinLensFocalLength)
    {
        var isMirror = surface.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
        if (surface.Geometry is IGratingGeometry)
        {
            surface.InteractionModel = new DiffractiveInteractionModel(isMirror);
            surface.IsReflective = isMirror;
            return;
        }

        if (surface.InteractionModel is DiffractiveInteractionModel)
        {
            surface.InteractionModel = new RefractiveReflectiveInteractionModel(isMirror);
            surface.IsReflective = isMirror;
            return;
        }

        if (surface.InteractionModel is ThinLensInteractionModel)
        {
            surface.InteractionModel = new ThinLensInteractionModel(thinLensFocalLength, isMirror);
            surface.IsReflective = isMirror;
            return;
        }

        SyncInteractionReflectivity(surface, isMirror);
    }

    private static void SyncInteractionReflectivity(OpticalSurface surface, bool isMirror)
    {
        surface.IsReflective = isMirror;
        surface.InteractionModel = surface.InteractionModel switch
        {
            ThinLensInteractionModel thinLens => new ThinLensInteractionModel(thinLens.FocalLength, isMirror),
            DiffractiveInteractionModel diffractive when diffractive.GrooveFrequencyLinesPerMillimeter is double frequency =>
                new DiffractiveInteractionModel(frequency, diffractive.Order ?? 1),
            DiffractiveInteractionModel => new DiffractiveInteractionModel(isMirror),
            PhaseInteractionModel phase => new PhaseInteractionModel(phase.Profile.Clone(), isMirror),
            _ => new RefractiveReflectiveInteractionModel(isMirror)
        };
    }

    private static void ApplyCoating(OpticalSurface surface, string coatingKind)
    {
        switch (CanonicalCoatingKind(coatingKind))
        {
            case "MgF2":
                surface.Coating = "MgF2";
                surface.CoatingModel = new ThinFilmStackCoating(new[] { new ThinFilmLayer("MgF2", 120) });
                break;
            case "Quarter-wave Stack":
                surface.Coating = "Quarter-wave Stack";
                surface.CoatingModel = new NeedleSynthesisDesigner().DesignQuarterWaveStack(new[] { "MgF2", "TiO2" }, 587.6, 4);
                break;
            default:
                surface.Coating = "None";
                surface.CoatingModel = new NoneCoatingModel();
                break;
        }
    }

    private static void ApplyInteraction(
        OpticalSurface surface,
        string interactionKind,
        double thinLensFocalLength)
    {
        interactionKind = CanonicalInteractionKind(interactionKind);
        if (interactionKind is "Diffractive" or "Reflective Diffractive")
        {
            surface.Geometry = surface.Geometry switch
            {
                IGratingGeometry grating => grating,
                PlaneGeometry => new PlaneGratingGeometry(1, 1, 0),
                StandardGeometry standard => new StandardGratingGeometry(
                    standard.Radius,
                    standard.Conic,
                    1,
                    1,
                    0),
                _ when Math.Abs(surface.Radius) < 1e-9 => new PlaneGratingGeometry(1, 1, 0),
                _ => new StandardGratingGeometry(surface.Radius, surface.Conic, 1, 1, 0)
            };
        }
        else
        {
            surface.Geometry = surface.Geometry switch
            {
                PlaneGratingGeometry => new PlaneGeometry(),
                StandardGratingGeometry grating => new StandardGeometry(grating.Base.Radius, grating.Base.Conic),
                _ => surface.Geometry
            };
        }

        surface.IsReflective = interactionKind is "Reflective" or "Reflective Thin Lens" or "Reflective Diffractive";
        surface.InteractionModel = interactionKind switch
        {
            "Reflective" => new RefractiveReflectiveInteractionModel(true),
            "Thin Lens" => new ThinLensInteractionModel(thinLensFocalLength),
            "Reflective Thin Lens" => new ThinLensInteractionModel(thinLensFocalLength, true),
            "Diffractive" => new DiffractiveInteractionModel(),
            "Reflective Diffractive" => new DiffractiveInteractionModel(true),
            "Phase" => new PhaseInteractionModel(new ConstantPhaseProfile()),
            _ => new RefractiveReflectiveInteractionModel(false)
        };
    }

    private static void ApplyGratingParameters(
        OpticalSurface surface,
        int order,
        double periodMicrometers,
        double angleDegrees)
    {
        if (surface.Geometry is not IGratingGeometry grating)
        {
            return;
        }

        periodMicrometers = Math.Max(1e-6, periodMicrometers);
        var angleRadians = angleDegrees * Math.PI / 180.0;
        surface.Geometry = grating switch
        {
            PlaneGratingGeometry => new PlaneGratingGeometry(order, periodMicrometers, angleRadians),
            StandardGratingGeometry standard => new StandardGratingGeometry(
                standard.Base.Radius,
                standard.Base.Conic,
                order,
                periodMicrometers,
                angleRadians),
            _ => surface.Geometry
        };
    }

    private static void ApplyPhysicalAperture(OpticalSurface surface, string physicalApertureKind)
    {
        surface.PhysicalAperture = CanonicalPhysicalApertureKind(physicalApertureKind) switch
        {
            "Annular" => new AnnularAperture(surface.SemiDiameter, surface.SemiDiameter * 0.5),
            "Offset Radial" => new OffsetRadialAperture(
                surface.SemiDiameter * 0.8,
                offsetX: surface.SemiDiameter * 0.2),
            "Rectangular" => new RectangularAperture(surface.SemiDiameter, surface.SemiDiameter),
            "Elliptical" => new EllipticalAperture(surface.SemiDiameter, surface.SemiDiameter * 0.75),
            "Polygon" => new PolygonAperture(new[]
            {
                (-surface.SemiDiameter, -surface.SemiDiameter),
                (surface.SemiDiameter, -surface.SemiDiameter),
                (surface.SemiDiameter, surface.SemiDiameter),
                (-surface.SemiDiameter, surface.SemiDiameter)
            }),
            "Boolean" => new DifferenceAperture(
                new CircularAperture(surface.SemiDiameter),
                new CircularAperture(surface.SemiDiameter * 0.5)),
            "None" => null,
            _ => new CircularAperture(surface.SemiDiameter)
        };
    }

    private void SetPrimaryWavelengthGuard(Wavelength? preferred = null)
    {
        if (Wavelengths.Count == 0)
        {
            return;
        }

        var primary = preferred is { IsPrimary: true } && Wavelengths.Contains(preferred)
            ? preferred
            : Wavelengths.FirstOrDefault(item => item.IsPrimary) ?? Wavelengths[0];
        foreach (var wavelength in Wavelengths)
        {
            wavelength.IsPrimary = ReferenceEquals(wavelength, primary);
        }
    }
}
