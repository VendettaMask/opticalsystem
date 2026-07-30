using System.Text;
using System.Text.Json;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Phase;
using static OptilandWorkbench.Core.Serialization.PythonOptilandJsonConversion;

namespace OptilandWorkbench.Core.Serialization;

internal static partial class PythonOptilandJsonWriter
{
    private static readonly HashSet<string> PythonCatalogMaterialNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "F2",
            "N-F2",
            "N-SK15",
            "K10",
            "SK16"
        };

    private static object WriteAperture(Optic optic)
    {
        var type = optic.Aperture.Kind switch
        {
            ApertureKind.FNumber => "imageFNO",
            ApertureKind.NumericalAperture => "objectNA",
            ApertureKind.FloatByStopSize => "float_by_stop_size",
            _ => "EPD"
        };
        return new Dictionary<string, object?>
        {
            ["type"] = type,
            ["value"] = optic.Aperture.Kind == ApertureKind.FloatByStopSize
                ? optic.SurfaceGroup.ApertureRadius()
                : optic.Aperture.Value,
            ["object_space_telecentric"] = optic.Aperture.ObjectSpaceTelecentric
        };
    }

    private static object WriteFields(Optic optic)
    {
        return new Dictionary<string, object?>
        {
            ["fields"] = optic.Fields.Select(field => new Dictionary<string, object?>
            {
                ["x"] = field.XAngleDegrees,
                ["y"] = field.YAngleDegrees,
                ["vx"] = field.VignetteFactorX,
                ["vy"] = field.VignetteFactorY
            }).ToArray(),
            ["telecentric"] = optic.FieldGroupTelecentric,
            ["field_definition"] = new Dictionary<string, object?>
            {
                ["field_type"] = optic.FieldDefinition switch
                {
                    FieldDefinitionKind.ObjectHeight => "ObjectHeightField",
                    FieldDefinitionKind.ParaxialImageHeight => "ParaxialImageHeightField",
                    FieldDefinitionKind.RealImageHeight => "RealImageHeightField",
                    _ => "AngleField"
                }
            },
            ["object_space_telecentric"] = optic.ObjectSpaceTelecentric
        };
    }

    private static object WriteWavelengths(Optic optic)
    {
        return new Dictionary<string, object?>
        {
            ["wavelengths"] = optic.Wavelengths.Select(wavelength => new Dictionary<string, object?>
            {
                ["value"] = wavelength.Micrometers,
                ["is_primary"] = wavelength.IsPrimary,
                ["unit"] = "um",
                ["weight"] = wavelength.Weight
            }).ToArray(),
            ["polarization"] = "ignore"
        };
    }

    private static object WriteSurface(
        Optic optic,
        OpticalSurface surface,
        int index,
        double objectDistance)
    {
        if (surface.ScatteringModel is not null)
        {
            throw new NotSupportedException("BSDF/scattering surfaces cannot be exported to Python Optiland JSON yet.");
        }
        if ((surface.Geometry is IGratingGeometry) != (surface.InteractionModel is DiffractiveInteractionModel))
        {
            throw new NotSupportedException(
                "Python Optiland grating geometry and DiffractiveInteractionModel must be used together.");
        }
        if (index == 0 && surface.Geometry is IGratingGeometry)
        {
            throw new NotSupportedException("Python Optiland object surfaces cannot preserve diffractive interaction data.");
        }

        var geometry = WriteGeometry(surface, index == 0, objectDistance);
        var material = WriteMaterial(surface.MaterialAfter);
        if (index == 0)
        {
            return new Dictionary<string, object?>
            {
                ["type"] = "ObjectSurface",
                ["geometry"] = geometry,
                ["material_post"] = material,
                ["comment"] = surface.Label
            };
        }

        var physicalAperture = surface.PhysicalAperture;
        if (surface.IsStop
            && optic.Aperture.Kind == ApertureKind.FloatByStopSize
            && physicalAperture is null)
        {
            physicalAperture = new CircularAperture(surface.SemiDiameter);
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "Surface",
            ["thickness"] = surface.Thickness,
            ["geometry"] = geometry,
            ["material_post"] = material,
            ["is_stop"] = surface.IsStop,
            ["aperture"] = WritePhysicalAperture(physicalAperture),
            ["interaction_model"] = WriteInteractionModel(surface),
            ["comment"] = surface.Label
        };
    }

    private static object WriteInteractionModel(OpticalSurface surface)
    {
        var coating = WriteCoating(surface.CoatingModel);
        return surface.InteractionModel switch
        {
            RefractiveReflectiveInteractionModel interaction => new Dictionary<string, object?>
            {
                ["type"] = "RefractiveReflectiveModel",
                ["is_reflective"] = interaction.IsReflective || surface.IsReflective,
                ["coating"] = coating,
                ["bsdf"] = null
            },
            ThinLensInteractionModel thinLens => new Dictionary<string, object?>
            {
                ["type"] = "ThinLensInteractionModel",
                ["is_reflective"] = thinLens.IsReflective || surface.IsReflective,
                ["coating"] = coating,
                ["bsdf"] = null,
                ["focal_length"] = double.IsPositiveInfinity(thinLens.FocalLength)
                    ? PositiveInfinitySentinel
                    : double.IsNegativeInfinity(thinLens.FocalLength)
                        ? NegativeInfinitySentinel
                        : thinLens.FocalLength
            },
            PhaseInteractionModel phase when surface.Geometry is PlaneGeometry => new Dictionary<string, object?>
            {
                ["type"] = "PhaseInteractionModel",
                ["is_reflective"] = phase.IsReflective || surface.IsReflective,
                ["coating"] = coating,
                ["bsdf"] = null,
                ["phase_profile"] = WritePhaseProfile(phase.Profile)
            },
            PhaseInteractionModel => throw new NotSupportedException(
                "PhaseInteractionModel can only be exported on Plane geometry."),
            DiffractiveInteractionModel diffractive when surface.Geometry is IGratingGeometry =>
                new Dictionary<string, object?>
                {
                    ["type"] = "DiffractiveInteractionModel",
                    ["is_reflective"] = diffractive.IsReflective || surface.IsReflective,
                    ["coating"] = coating,
                    ["bsdf"] = null
                },
            DiffractiveInteractionModel => throw new NotSupportedException(
                "DiffractiveInteractionModel can only be exported with grating geometry."),
            _ => throw new NotSupportedException(
                $"Interaction '{surface.InteractionModel.Kind}' cannot be exported to Python Optiland JSON yet.")
        };
    }

    private static object WritePhaseProfile(IPhaseProfile profile)
    {
        return profile switch
        {
            ConstantPhaseProfile constant => new Dictionary<string, object?>
            {
                ["phase_type"] = "constant",
                ["phase"] = constant.PhaseValue
            },
            LinearGratingPhaseProfile linear => new Dictionary<string, object?>
            {
                ["phase_type"] = "linear_grating",
                ["period"] = linear.Period,
                ["angle"] = linear.Angle,
                ["order"] = linear.Order,
                ["efficiency"] = linear.Efficiency
            },
            RadialPhaseProfile radial => new Dictionary<string, object?>
            {
                ["phase_type"] = "radial",
                ["coefficients"] = radial.Coefficients
            },
            GridPhaseProfile grid => new Dictionary<string, object?>
            {
                ["phase_type"] = "grid",
                ["x_coords"] = grid.XCoordinates,
                ["y_coords"] = grid.YCoordinates,
                ["phase_grid"] = WriteDoubleMatrix(grid.PhaseGrid)
            },
            _ => throw new NotSupportedException(
                $"Phase profile '{profile.Kind}' cannot be exported to Python Optiland JSON yet.")
        };
    }

    private static object WriteGeometry(OpticalSurface surface, bool isObject, double objectDistance)
    {
        var cs = WriteCoordinateSystem(surface.CoordinateSystem, isObject, objectDistance);
        return surface.Geometry switch
        {
            PlaneGeometry => new Dictionary<string, object?>
            {
                ["type"] = "Plane",
                ["cs"] = cs,
                ["radius"] = PositiveInfinitySentinel
            },
            PlaneGratingGeometry grating => WriteGratingGeometry(
                "PlaneGrating",
                cs,
                double.PositiveInfinity,
                0,
                grating),
            StandardGratingGeometry grating => WriteGratingGeometry(
                "StandardGratingGeometry",
                cs,
                grating.Base.Radius,
                grating.Base.Conic,
                grating),
            StandardGeometry standard => WriteConicGeometry("StandardGeometry", cs, standard.Radius, standard.Conic),
            EvenAsphereGeometry even => WriteConicGeometry(
                "EvenAsphere",
                cs,
                even.Base.Radius,
                even.Base.Conic,
                WithLeadingZero(even.Coefficients)),
            OddAsphereGeometry odd => WriteConicGeometry(
                "OddAsphere",
                cs,
                odd.Base.Radius,
                odd.Base.Conic,
                WithLeadingZero(odd.Coefficients)),
            SeparableBiconicGeometry biconic => new Dictionary<string, object?>
            {
                ["type"] = "BiconicGeometry",
                ["cs"] = cs,
                ["radius_x"] = biconic.RadiusX,
                ["radius_y"] = biconic.RadiusY,
                ["conic_x"] = biconic.ConicX,
                ["conic_y"] = biconic.ConicY
            },
            BiconicGeometry => throw new NotSupportedException(
                "Zemax-style shared-root BiconicGeometry cannot be exported as Python Optiland's separable BiconicGeometry."),
            ToroidalGeometry toroidal => new Dictionary<string, object?>
            {
                ["type"] = "ToroidalGeometry",
                ["cs"] = cs,
                ["radius"] = toroidal.SagittalRadius,
                ["conic"] = 0.0,
                ["tol"] = 1e-10,
                ["max_iter"] = 100,
                ["geometry_type"] = "Toroidal",
                ["radius_x"] = toroidal.TangentialRadius,
                ["radius_y"] = toroidal.SagittalRadius,
                ["conic_yz"] = 0.0,
                ["coeffs_poly_y"] = Array.Empty<double>()
            },
            PolynomialGeometry polynomial => new Dictionary<string, object?>
            {
                ["type"] = "PolynomialGeometry",
                ["cs"] = cs,
                ["radius"] = PositiveInfinitySentinel,
                ["conic"] = 0.0,
                ["tol"] = 1e-10,
                ["max_iter"] = 100,
                ["coefficients"] = WritePolynomialCoefficients(polynomial.Coefficients)
            },
            ChebyshevGeometry chebyshev => new Dictionary<string, object?>
            {
                ["type"] = "ChebyshevPolynomialGeometry",
                ["cs"] = cs,
                ["radius"] = PositiveInfinitySentinel,
                ["conic"] = 0.0,
                ["tol"] = 1e-10,
                ["max_iter"] = 100,
                ["coefficients"] = WritePolynomialCoefficients(chebyshev.Coefficients),
                ["norm_x"] = chebyshev.NormalizationX,
                ["norm_y"] = chebyshev.NormalizationY
            },
            ZernikeGeometry zernike => new Dictionary<string, object?>
            {
                ["type"] = "ZernikePolynomialGeometry",
                ["cs"] = cs,
                ["radius"] = PositiveInfinitySentinel,
                ["conic"] = 0.0,
                ["tol"] = 1e-10,
                ["max_iter"] = 100,
                ["coefficients"] = WriteFringeZernikeCoefficients(zernike.Coefficients),
                ["zernike_type"] = "fringe",
                ["norm_radius"] = zernike.PupilRadius
            },
            _ => throw new NotSupportedException($"Geometry '{surface.Geometry.Kind}' cannot be exported to Python Optiland JSON yet.")
        };
    }

    private static Dictionary<string, object?> WriteGratingGeometry(
        string type,
        Dictionary<string, object?> coordinateSystem,
        double radius,
        double conic,
        IGratingGeometry grating)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = type,
            ["cs"] = coordinateSystem,
            ["radius"] = double.IsPositiveInfinity(radius) ? PositiveInfinitySentinel : radius,
            ["conic"] = conic,
            ["order"] = grating.GratingOrder,
            ["period"] = double.IsPositiveInfinity(grating.GratingPeriodMicrometers)
                ? PositiveInfinitySentinel
                : grating.GratingPeriodMicrometers,
            ["angle"] = grating.GrooveOrientationAngleRadians
        };
    }

    private static Dictionary<string, object?> WriteCoordinateSystem(
        CoordinateSystem coordinate,
        bool isObject,
        double objectDistance)
    {
        return new Dictionary<string, object?>
        {
            ["x"] = isObject ? 0 : coordinate.Origin.X,
            ["y"] = isObject ? 0 : coordinate.Origin.Y,
            ["z"] = isObject
                ? Math.Abs(objectDistance) <= 1e-12 ? NegativeInfinitySentinel : -objectDistance
                : coordinate.Origin.Z - objectDistance,
            ["rx"] = DegreesToRadians(coordinate.RotationXDegrees),
            ["ry"] = DegreesToRadians(coordinate.RotationYDegrees),
            ["rz"] = DegreesToRadians(coordinate.RotationZDegrees),
            ["reference_cs"] = null
        };
    }

    private static Dictionary<string, object?> WriteConicGeometry(
        string type,
        Dictionary<string, object?> coordinateSystem,
        double radius,
        double conic,
        IReadOnlyList<double>? coefficients = null)
    {
        var geometry = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["cs"] = coordinateSystem,
            ["radius"] = radius,
            ["conic"] = conic
        };
        if (coefficients is not null)
        {
            geometry["coefficients"] = coefficients;
        }

        return geometry;
    }

    private static object WriteMaterial(IMaterial material)
    {
        var propagation = new Dictionary<string, object?> { ["class"] = "HomogeneousPropagation" };
        return material switch
        {
            AirMaterial => new Dictionary<string, object?>
            {
                ["type"] = "IdealMaterial",
                ["propagation_model"] = propagation,
                ["index"] = 1.0,
                ["absorp"] = 0.0
            },
            ConstantIndexMaterial constant => new Dictionary<string, object?>
            {
                ["type"] = "IdealMaterial",
                ["propagation_model"] = propagation,
                ["index"] = constant.Index,
                ["absorp"] = constant.Extinction
            },
            AbbeMaterial abbe => new Dictionary<string, object?>
            {
                ["type"] = "AbbeMaterial",
                ["propagation_model"] = propagation,
                ["index"] = abbe.Nd,
                ["abbe"] = abbe.Vd
            },
            CatalogGlassMaterial catalog => new Dictionary<string, object?>
            {
                ["type"] = "Material",
                ["propagation_model"] = propagation,
                ["name"] = catalog.CatalogName,
                ["reference"] = catalog.Manufacturer.ToLowerInvariant(),
                ["robust_search"] = false,
                ["min_wavelength"] = null,
                ["max_wavelength"] = null
            },
            _ when PythonCatalogMaterialNames.Contains(material.Name) => new Dictionary<string, object?>
            {
                ["type"] = "Material",
                ["propagation_model"] = propagation,
                ["name"] = material.Name,
                ["reference"] = material.Name.Equals("F2", StringComparison.OrdinalIgnoreCase)
                    || material.Name.Equals("N-F2", StringComparison.OrdinalIgnoreCase)
                        ? "schott"
                        : null,
                ["robust_search"] = true,
                ["min_wavelength"] = null,
                ["max_wavelength"] = null
            },
            _ => throw new NotSupportedException($"Material '{material.Name}' cannot be exported to Python Optiland JSON yet.")
        };
    }

    private static object? WriteApodization(IApodizationModel? apodization)
    {
        return apodization switch
        {
            null => null,
            UniformApodization => new Dictionary<string, object?>
            {
                ["type"] = "UniformApodization"
            },
            GaussianApodization gaussian => new Dictionary<string, object?>
            {
                ["type"] = "GaussianApodization",
                ["sigma"] = gaussian.Sigma
            },
            CosineSquaredApodization cosine => new Dictionary<string, object?>
            {
                ["type"] = "CosineSquaredApodization",
                ["R"] = cosine.Radius
            },
            HannApodization hann => new Dictionary<string, object?>
            {
                ["type"] = "HannApodization",
                ["D"] = hann.Diameter
            },
            PolynomialApodization polynomial => new Dictionary<string, object?>
            {
                ["type"] = "PolynomialApodization",
                ["R"] = polynomial.Radius,
                ["p"] = polynomial.Power
            },
            SuperGaussianApodization superGaussian => new Dictionary<string, object?>
            {
                ["type"] = "SuperGaussianApodization",
                ["w"] = superGaussian.Width,
                ["n"] = superGaussian.Exponent
            },
            TukeyApodization tukey => new Dictionary<string, object?>
            {
                ["type"] = "TukeyApodization",
                ["R"] = tukey.Radius,
                ["alpha"] = tukey.Alpha
            },
            _ => throw new NotSupportedException(
                $"Apodization '{apodization.Kind}' cannot be exported to Python Optiland JSON yet.")
        };
    }

    private static object? WriteCoating(ICoatingModel coating)
    {
        return coating switch
        {
            NoneCoatingModel => null,
            SimpleCoatingModel simple => new Dictionary<string, object?>
            {
                ["type"] = "SimpleCoating",
                ["transmittance"] = simple.Transmittance,
                ["reflectance"] = simple.Reflectance
            },
            _ => throw new NotSupportedException($"Coating '{coating.Kind}' cannot be exported to Python Optiland JSON yet.")
        };
    }

    private static object? WritePhysicalAperture(IPhysicalAperture? aperture)
    {
        return aperture switch
        {
            null => null,
            CircularAperture circular => new Dictionary<string, object?>
            {
                ["type"] = "RadialAperture",
                ["r_max"] = circular.Radius,
                ["r_min"] = 0.0
            },
            AnnularAperture annular => new Dictionary<string, object?>
            {
                ["type"] = "RadialAperture",
                ["r_max"] = annular.OuterRadius,
                ["r_min"] = annular.InnerRadius
            },
            OffsetRadialAperture offset => new Dictionary<string, object?>
            {
                ["type"] = "OffsetRadialAperture",
                ["r_max"] = offset.OuterRadius,
                ["r_min"] = offset.InnerRadius,
                ["offset_x"] = offset.OffsetX,
                ["offset_y"] = offset.OffsetY
            },
            RectangularAperture rectangular => new Dictionary<string, object?>
            {
                ["type"] = "RectangularAperture",
                ["x_min"] = rectangular.XMinimum,
                ["x_max"] = rectangular.XMaximum,
                ["y_min"] = rectangular.YMinimum,
                ["y_max"] = rectangular.YMaximum
            },
            EllipticalAperture elliptical => new Dictionary<string, object?>
            {
                ["type"] = "EllipticalAperture",
                ["a"] = elliptical.SemiAxisX,
                ["b"] = elliptical.SemiAxisY,
                ["offset_x"] = elliptical.OffsetX,
                ["offset_y"] = elliptical.OffsetY
            },
            FileAperture file => WriteFileAperture(file),
            PolygonAperture polygon => WritePolygonAperture("PolygonAperture", polygon),
            UnionAperture union => WriteBooleanAperture("UnionAperture", union),
            IntersectionAperture intersection => WriteBooleanAperture("IntersectionAperture", intersection),
            DifferenceAperture difference => WriteBooleanAperture("DifferenceAperture", difference),
            _ => throw new NotSupportedException($"Physical aperture '{aperture.Kind}' cannot be exported to Python Optiland JSON yet.")
        };
    }

    private static Dictionary<string, object?> WritePolygonAperture(
        string type,
        PolygonAperture aperture)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = type,
            ["x"] = aperture.Vertices.Select(vertex => vertex.X).ToArray(),
            ["y"] = aperture.Vertices.Select(vertex => vertex.Y).ToArray()
        };
    }

    private static Dictionary<string, object?> WriteFileAperture(FileAperture aperture)
    {
        var output = WritePolygonAperture("FileAperture", aperture);
        output["filepath"] = aperture.FilePath;
        output["delimiter"] = aperture.Delimiter;
        output["skip_header"] = aperture.SkipHeader;
        return output;
    }

    private static Dictionary<string, object?> WriteBooleanAperture(
        string type,
        BooleanAperture aperture)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = type,
            ["a"] = WritePhysicalAperture(aperture.Left),
            ["b"] = WritePhysicalAperture(aperture.Right)
        };
    }
}
