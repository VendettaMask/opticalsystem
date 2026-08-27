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

internal static partial class PythonOptilandJsonReader
{
    private static void ValidateUnsupportedRootContracts(JsonElement root)
    {
        if (root.TryGetProperty("pickups", out var pickups) && IsNonEmptyCollection(pickups))
        {
            throw new NotSupportedException("Python Optiland pickups import is not supported yet.");
        }

        if (root.TryGetProperty("solves", out var solves) && HasNonEmptySolves(solves))
        {
            throw new NotSupportedException("Python Optiland solves import is not supported yet.");
        }
    }

    private static bool IsNonEmptyCollection(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => value.EnumerateObject().Any(),
            _ => true
        };
    }

    private static bool HasNonEmptySolves(JsonElement solves)
    {
        if (solves.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        if (solves.ValueKind == JsonValueKind.Object
            && solves.TryGetProperty("solves", out var solveArray)
            && solveArray.ValueKind == JsonValueKind.Array)
        {
            return solveArray.GetArrayLength() > 0;
        }

        return IsNonEmptyCollection(solves);
    }

    private static IApodizationModel? ReadApodization(JsonElement root)
    {
        if (!root.TryGetProperty("apodization", out var apodization)
            || apodization.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return GetString(apodization, "type", string.Empty) switch
        {
            "UniformApodization" => new UniformApodization(),
            "GaussianApodization" => new GaussianApodization(GetDouble(apodization, "sigma", 1)),
            "CosineSquaredApodization" => new CosineSquaredApodization(GetDouble(apodization, "R", 1)),
            "HannApodization" => new HannApodization(GetDouble(apodization, "D", 2)),
            "PolynomialApodization" => new PolynomialApodization(
                GetDouble(apodization, "R", 1),
                GetDouble(apodization, "p", 1)),
            "SuperGaussianApodization" => new SuperGaussianApodization(
                GetDouble(apodization, "w", 1),
                GetDouble(apodization, "n", 2)),
            "TukeyApodization" => new TukeyApodization(
                GetDouble(apodization, "R", 1),
                GetDouble(apodization, "alpha", 0.5)),
            var type => throw new NotSupportedException(
                $"Python Optiland apodization '{type}' is not supported yet.")
        };
    }

    private static void ReadAperture(JsonElement root, Optic optic)
    {
        if (!root.TryGetProperty("aperture", out var aperture) || aperture.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var type = GetString(aperture, "type", "EPD");
        optic.Aperture.Kind = type.ToLowerInvariant() switch
        {
            "epd" => ApertureKind.EntrancePupilDiameter,
            "imagefno" => ApertureKind.FNumber,
            "objectna" => ApertureKind.NumericalAperture,
            "float_by_stop_size" => ApertureKind.FloatByStopSize,
            _ => throw new NotSupportedException($"Python Optiland system aperture '{type}' is not supported yet.")
        };
        optic.Aperture.Value = GetDouble(aperture, "value", optic.Aperture.Value);
        optic.Aperture.ObjectSpaceTelecentric = GetBoolean(aperture, "object_space_telecentric");
    }

    private static void ReadFields(JsonElement root, Optic optic)
    {
        if (!root.TryGetProperty("fields", out var fields))
        {
            return;
        }

        if (fields.TryGetProperty("field_definition", out var definition)
            && definition.ValueKind == JsonValueKind.Object)
        {
            var fieldType = GetString(definition, "field_type", "AngleField");
            optic.FieldDefinition = fieldType.ToLowerInvariant() switch
            {
                "anglefield" => FieldDefinitionKind.Angle,
                "objectheightfield" => FieldDefinitionKind.ObjectHeight,
                "paraxialimageheightfield" => FieldDefinitionKind.ParaxialImageHeight,
                "realimageheightfield" => FieldDefinitionKind.RealImageHeight,
                _ => throw new NotSupportedException($"Python Optiland field type '{fieldType}' is not supported yet.")
            };
        }

        optic.FieldGroupTelecentric = GetBoolean(fields, "telecentric");
        optic.ObjectSpaceTelecentric = GetBoolean(fields, "object_space_telecentric");

        if (fields.TryGetProperty("fields", out var fieldArray))
        {
            var index = 0;
            foreach (var field in fieldArray.EnumerateArray())
            {
                optic.Fields.Add(new FieldPoint
                {
                    Label = index == 0 ? "On axis" : $"Field {index}",
                    XAngleDegrees = GetDouble(field, "x", 0),
                    YAngleDegrees = GetDouble(field, "y", 0),
                    Weight = 1,
                    VignetteFactorX = GetDouble(field, "vx", 0),
                    VignetteFactorY = GetDouble(field, "vy", 0)
                });
                index++;
            }
        }

        if (optic.Fields.Count == 0)
        {
            optic.Fields.Add(new FieldPoint { Label = "On axis", Weight = 1 });
        }
    }

    private static void ReadWavelengths(JsonElement root, Optic optic)
    {
        if (root.TryGetProperty("wavelengths", out var wavelengths)
            && wavelengths.TryGetProperty("wavelengths", out var wavelengthArray))
        {
            if (wavelengths.TryGetProperty("polarization", out var polarization)
                && polarization.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                && (polarization.ValueKind != JsonValueKind.String
                    || !string.Equals(polarization.GetString(), "ignore", StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException("Python Optiland polarization import is not supported yet.");
            }

            var index = 0;
            foreach (var wavelength in wavelengthArray.EnumerateArray())
            {
                var value = GetDouble(wavelength, "value", 0.5875618);
                var unit = GetString(wavelength, "unit", "um");
                optic.Wavelengths.Add(new Wavelength
                {
                    Label = $"W{index + 1}",
                    Nanometers = unit.Equals("nm", StringComparison.OrdinalIgnoreCase) ? value : value * 1000.0,
                    Weight = GetDouble(wavelength, "weight", 1),
                    IsPrimary = GetBoolean(wavelength, "is_primary")
                });
                index++;
            }
        }

        if (optic.Wavelengths.Count == 0)
        {
            optic.Wavelengths.Add(new Wavelength
            {
                Label = "d",
                Nanometers = 587.5618,
                Weight = 1,
                IsPrimary = true
            });
        }
        else if (!optic.Wavelengths.Any(wavelength => wavelength.IsPrimary))
        {
            optic.Wavelengths[0].IsPrimary = true;
        }
    }

    private static ParsedSurface ReadSurface(Optic optic, JsonElement source, int number)
    {
        var type = GetString(source, "type", number == 0 ? "ObjectSurface" : "Surface");
        var geometry = source.GetProperty("geometry");
        var parsedGeometry = ReadGeometry(geometry);
        var objectAtInfinity = type == "ObjectSurface"
            && geometry.TryGetProperty("cs", out var objectCoordinate)
            && double.IsInfinity(GetDouble(objectCoordinate, "z", 0));

        var materialName = ReadMaterial(optic, source.GetProperty("material_post"));
        var radius = GeometryRadius(parsedGeometry);
        var conic = GeometryConic(parsedGeometry);
        var aperture = source.TryGetProperty("aperture", out var apertureElement)
            ? ReadPhysicalAperture(apertureElement)
            : null;
        var semiDiameter = PhysicalApertureBoundsCalculator.TryGetBounds(aperture, out var apertureBounds)
            ? new[]
            {
                Math.Abs(apertureBounds.XMinimum),
                Math.Abs(apertureBounds.XMaximum),
                Math.Abs(apertureBounds.YMinimum),
                Math.Abs(apertureBounds.YMaximum)
            }.Max()
            : 1.0;
        var hasInteraction = source.TryGetProperty("interaction_model", out var interactionElement)
            && interactionElement.ValueKind == JsonValueKind.Object;
        var parsedInteraction = hasInteraction
            ? ReadInteractionModel(interactionElement)
            : new ParsedInteraction(new RefractiveReflectiveInteractionModel(), false, new NoneCoatingModel());
        if (parsedGeometry is not INonComputableGeometry
            && parsedInteraction.Interaction is PhaseInteractionModel
            && parsedGeometry is not PlaneGeometry)
        {
            throw new NotSupportedException("Python Optiland phase interactions require Plane geometry.");
        }
        if (parsedGeometry is not INonComputableGeometry
            && parsedInteraction.Interaction is DiffractiveInteractionModel
            && parsedGeometry is not IGratingGeometry)
        {
            throw new NotSupportedException("Python Optiland diffractive interactions require grating geometry.");
        }
        if (parsedGeometry is not INonComputableGeometry
            && parsedGeometry is IGratingGeometry
            && parsedInteraction.Interaction is not DiffractiveInteractionModel)
        {
            throw new NotSupportedException("Python Optiland grating geometry requires DiffractiveInteractionModel.");
        }
        var label = GetString(source, "comment", string.Empty);
        if (string.IsNullOrWhiteSpace(label))
        {
            label = type == "ObjectSurface" ? "Object" : $"Surface {number}";
        }

        var surface = new OpticalSurface
        {
            Number = number,
            Label = label,
            Radius = radius,
            Thickness = type == "ObjectSurface"
                ? objectAtInfinity ? double.PositiveInfinity : 0
                : GetDouble(source, "thickness", 0),
            Material = materialName,
            SemiDiameter = semiDiameter,
            Conic = conic,
            IsStop = GetBoolean(source, "is_stop"),
            IsReflective = parsedInteraction.IsReflective
        };
        surface.Geometry = parsedGeometry;
        surface.InteractionModel = parsedInteraction.Interaction;
        surface.CoatingModel = parsedInteraction.Coating;
        return new ParsedSurface(
            surface,
            parsedGeometry,
            parsedInteraction.Interaction,
            parsedInteraction.Coating,
            aperture,
            ReadCoordinateSystem(geometry));
    }

    private static IGeometry ReadGeometry(JsonElement geometry)
    {
        var geometryType = GetString(geometry, "type", "Plane");
        var radius = GetDouble(geometry, "radius", 0);
        var conic = GetDouble(geometry, "conic", 0);
        try
        {
            return geometryType switch
            {
                "Plane" => new PlaneGeometry(),
                "PlaneGrating" => ReadPlaneGratingGeometry(geometry),
                "StandardGratingGeometry" => ReadStandardGratingGeometry(geometry),
                "StandardGeometry" => new StandardGeometry(radius, conic),
                "EvenAsphere" => new EvenAsphereGeometry(
                    radius,
                    conic,
                    ReadHighOrderAsphereCoefficients(geometry, geometryType)),
                "OddAsphere" => new OddAsphereGeometry(
                    radius,
                    conic,
                    ReadHighOrderAsphereCoefficients(geometry, geometryType)),
                "BiconicGeometry" => new SeparableBiconicGeometry(
                    GetDouble(geometry, "radius_x", radius),
                    GetDouble(geometry, "radius_y", radius),
                    GetDouble(geometry, "conic_x", conic),
                    GetDouble(geometry, "conic_y", conic)),
                "ToroidalGeometry" => ReadToroidalGeometry(geometry),
                "PolynomialGeometry" => ReadPolynomialGeometry(geometry),
                "ChebyshevPolynomialGeometry" => ReadChebyshevGeometry(geometry),
                "ZernikePolynomialGeometry" => ReadZernikeGeometry(geometry),
                _ => OpaquePythonGeometry(
                    geometryType,
                    geometry,
                    $"当前版本不支持 Python Optiland 几何“{geometryType}”；原始 JSON 仅作为不可计算数据保存。")
            };
        }
        catch (NotSupportedException exception)
        {
            return OpaquePythonGeometry(geometryType, geometry, exception.Message);
        }
    }

    private static OpaqueGeometryPayload OpaquePythonGeometry(
        string geometryType,
        JsonElement geometry,
        string reason) => new(new ComponentSnapshot(
            geometryType,
            new Dictionary<string, double>(),
            new Dictionary<string, string>
            {
                ["optiland.sourceFormat"] = "Python Optiland JSON",
                ["optiland.rawJson"] = geometry.GetRawText(),
                ["optiland.blockingReason"] = reason
            }));

    private static PlaneGratingGeometry ReadPlaneGratingGeometry(JsonElement geometry)
    {
        var settings = ReadGratingSettings(geometry, "PlaneGrating");
        return new PlaneGratingGeometry(
            settings.Order,
            settings.Period,
            settings.Angle);
    }

    private static StandardGratingGeometry ReadStandardGratingGeometry(JsonElement geometry)
    {
        var settings = ReadGratingSettings(geometry, "StandardGratingGeometry");
        return new StandardGratingGeometry(
            GetDouble(geometry, "radius", double.PositiveInfinity),
            GetDouble(geometry, "conic", 0),
            settings.Order,
            settings.Period,
            settings.Angle);
    }

    private static (int Order, double Period, double Angle) ReadGratingSettings(
        JsonElement geometry,
        string geometryType)
    {
        if (!geometry.TryGetProperty("order", out _)
            || !geometry.TryGetProperty("period", out _)
            || !geometry.TryGetProperty("angle", out _))
        {
            throw new NotSupportedException(
                $"Python Optiland {geometryType} dictionary omits required order/period/angle grating data.");
        }

        var order = GetDouble(geometry, "order", double.NaN);
        var period = GetDouble(geometry, "period", double.NaN);
        var angle = GetDouble(geometry, "angle", double.NaN);
        if (!double.IsFinite(order)
            || Math.Abs(order - Math.Round(order)) > 1e-12
            || order < int.MinValue
            || order > int.MaxValue)
        {
            throw new InvalidDataException($"Python Optiland {geometryType} order must be a finite integer.");
        }
        if (double.IsNaN(period) || period <= 0)
        {
            throw new InvalidDataException($"Python Optiland {geometryType} period must be positive.");
        }
        if (!double.IsFinite(angle))
        {
            throw new InvalidDataException($"Python Optiland {geometryType} angle must be finite.");
        }

        return ((int)order, period, angle);
    }

    private static ToroidalGeometry ReadToroidalGeometry(JsonElement geometry)
    {
        var conicYz = GetDouble(geometry, "conic_yz", 0);
        if (Math.Abs(conicYz) > 1e-14)
        {
            throw new NotSupportedException(
                "Python Optiland ToroidalGeometry with nonzero conic_yz is not supported yet.");
        }

        var polynomialY = ReadDoubleArray(geometry, "coeffs_poly_y");
        if (polynomialY.Any(coefficient => Math.Abs(coefficient) > 1e-14))
        {
            throw new NotSupportedException(
                "Python Optiland ToroidalGeometry with coeffs_poly_y terms is not supported yet.");
        }

        return new ToroidalGeometry(
            GetDouble(geometry, "radius_x", GetDouble(geometry, "radius", 0)),
            GetDouble(geometry, "radius_y", GetDouble(geometry, "radius", 0)));
    }

    private static PolynomialGeometry ReadPolynomialGeometry(JsonElement geometry)
    {
        var radius = GetDouble(geometry, "radius", double.NaN);
        if (double.IsFinite(radius))
        {
            throw new NotSupportedException(
                "Python Optiland PolynomialGeometry with a finite base radius is not supported yet.");
        }

        return new PolynomialGeometry(ReadPolynomialCoefficients(geometry));
    }

    private static ChebyshevGeometry ReadChebyshevGeometry(JsonElement geometry)
    {
        var radius = GetDouble(geometry, "radius", double.NaN);
        if (double.IsFinite(radius))
        {
            throw new NotSupportedException(
                "Python Optiland ChebyshevPolynomialGeometry with a finite base radius is not supported yet.");
        }

        return new ChebyshevGeometry(
            ReadPolynomialCoefficients(geometry),
            GetDouble(geometry, "norm_x", 1),
            GetDouble(geometry, "norm_y", 1));
    }

    private static ZernikeGeometry ReadZernikeGeometry(JsonElement geometry)
    {
        var radius = GetDouble(geometry, "radius", double.NaN);
        if (double.IsFinite(radius))
        {
            throw new NotSupportedException(
                "Python Optiland ZernikePolynomialGeometry with a finite base radius is not supported yet.");
        }

        var zernikeType = GetString(geometry, "zernike_type", "standard");
        if (!zernikeType.Equals("fringe", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Python Optiland ZernikePolynomialGeometry zernike_type '{zernikeType}' is not supported yet.");
        }

        return new ZernikeGeometry(
            ReadFringeZernikeCoefficients(geometry),
            GetDouble(geometry, "norm_radius", 1));
    }

    private static string ReadMaterial(Optic optic, JsonElement material)
    {
        var type = GetString(material, "type", "IdealMaterial");
        ValidateHomogeneousPropagation(material);
        if (type == "Material")
        {
            var name = GetString(material, "name", "Air");
            var reference = GetString(material, "reference", string.Empty);
            var preferred = string.IsNullOrWhiteSpace(reference)
                ? Array.Empty<string>()
                : new[] { reference };
            return optic.Materials.Resolve(name, preferred).Name;
        }

        if (type == "AbbeMaterial")
        {
            var index = GetDouble(material, "index", 1.5);
            var abbe = GetDouble(material, "abbe", 50);
            var name = $"Python Abbe {index:0.######}/{abbe:0.###}";
            optic.Materials.Register(new AbbeMaterial(name, index, abbe));
            return name;
        }

        if (type == "IdealMaterial")
        {
            var index = GetDouble(material, "index", 1);
            var extinction = GetDouble(material, "absorp", 0);
            if (Math.Abs(index - 1) < 1e-12 && Math.Abs(extinction) < 1e-12)
            {
                return "Air";
            }

            var name = $"Python Ideal n={index:0.######}";
            optic.Materials.Register(new ConstantIndexMaterial(name, index, extinction));
            return name;
        }

        throw new NotSupportedException($"Python Optiland material '{type}' is not supported yet.");
    }

    private static void ValidateHomogeneousPropagation(JsonElement material)
    {
        if (!material.TryGetProperty("propagation_model", out var propagation)
            || propagation.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        var propagationClass = GetString(propagation, "class", string.Empty);
        if (!propagationClass.Equals("HomogeneousPropagation", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Python Optiland propagation model '{propagationClass}' is not supported yet.");
        }
    }

    private static ParsedInteraction ReadInteractionModel(JsonElement interaction)
    {
        var type = GetString(interaction, "type", "RefractiveReflectiveModel");
        var isReflective = GetBoolean(interaction, "is_reflective");
        if (interaction.TryGetProperty("bsdf", out var bsdf)
            && bsdf.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            throw new NotSupportedException("Python Optiland BSDF import is not supported yet.");
        }

        var coating = interaction.TryGetProperty("coating", out var coatingElement)
            ? ReadCoating(coatingElement)
            : new NoneCoatingModel();
        return type switch
        {
            "RefractiveReflectiveModel" => new ParsedInteraction(
                new RefractiveReflectiveInteractionModel(isReflective),
                isReflective,
                coating),
            "ThinLensInteractionModel" => ReadThinLensInteraction(interaction, isReflective, coating),
            "PhaseInteractionModel" => ReadPhaseInteraction(interaction, isReflective, coating),
            "DiffractiveInteractionModel" => new ParsedInteraction(
                new DiffractiveInteractionModel(isReflective),
                isReflective,
                coating),
            _ => throw new NotSupportedException($"Python Optiland interaction model '{type}' is not supported yet.")
        };
    }

    private static ParsedInteraction ReadThinLensInteraction(
        JsonElement interaction,
        bool isReflective,
        ICoatingModel coating)
    {
        return new ParsedInteraction(
            new ThinLensInteractionModel(GetDouble(interaction, "focal_length", 50), isReflective),
            isReflective,
            coating);
    }

    private static ParsedInteraction ReadPhaseInteraction(
        JsonElement interaction,
        bool isReflective,
        ICoatingModel coating)
    {
        if (!interaction.TryGetProperty("phase_profile", out var profile)
            || profile.ValueKind != JsonValueKind.Object)
        {
            throw new NotSupportedException("Python Optiland PhaseInteractionModel requires a phase_profile dictionary.");
        }

        return new ParsedInteraction(
            new PhaseInteractionModel(ReadPhaseProfile(profile), isReflective),
            isReflective,
            coating);
    }

    private static IPhaseProfile ReadPhaseProfile(JsonElement profile)
    {
        return GetString(profile, "phase_type", string.Empty) switch
        {
            "constant" => new ConstantPhaseProfile(GetDouble(profile, "phase", 0)),
            "linear_grating" => new LinearGratingPhaseProfile(
                GetDouble(profile, "period", 1),
                GetDouble(profile, "angle", 0),
                (int)GetDouble(profile, "order", 1),
                GetDouble(profile, "efficiency", 1)),
            "radial" => new RadialPhaseProfile(ReadDoubleArray(profile, "coefficients")),
            "grid" => new GridPhaseProfile(
                ReadDoubleArray(profile, "x_coords"),
                ReadDoubleArray(profile, "y_coords"),
                ReadDoubleMatrix(profile, "phase_grid")),
            var type => throw new NotSupportedException(
                $"Python Optiland phase profile '{type}' is not supported yet.")
        };
    }

    private static ICoatingModel ReadCoating(JsonElement coating)
    {
        if (coating.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new NoneCoatingModel();
        }

        var type = GetString(coating, "type", string.Empty);
        return type switch
        {
            "SimpleCoating" => new SimpleCoatingModel(
                GetDouble(coating, "transmittance", 1),
                GetDouble(coating, "reflectance", 0)),
            "" => new NoneCoatingModel(),
            _ => throw new NotSupportedException($"Python Optiland coating '{type}' is not supported yet.")
        };
    }

    private static IPhysicalAperture? ReadPhysicalAperture(JsonElement aperture)
    {
        if (aperture.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return GetString(aperture, "type", string.Empty) switch
        {
            "RadialAperture" => ReadRadialAperture(aperture),
            "OffsetRadialAperture" => new OffsetRadialAperture(
                GetDouble(aperture, "r_max", 1),
                GetDouble(aperture, "r_min", 0),
                GetDouble(aperture, "offset_x", 0),
                GetDouble(aperture, "offset_y", 0)),
            "RectangularAperture" => ReadRectangularAperture(aperture),
            "EllipticalAperture" => new EllipticalAperture(
                GetDouble(aperture, "a", 1),
                GetDouble(aperture, "b", 1),
                GetDouble(aperture, "offset_x", 0),
                GetDouble(aperture, "offset_y", 0)),
            "PolygonAperture" => ReadPolygonAperture(aperture),
            "FileAperture" => ReadFileAperture(aperture),
            "UnionAperture" => ReadBooleanAperture(aperture, (left, right) => new UnionAperture(left, right)),
            "IntersectionAperture" => ReadBooleanAperture(
                aperture,
                (left, right) => new IntersectionAperture(left, right)),
            "DifferenceAperture" => ReadBooleanAperture(
                aperture,
                (left, right) => new DifferenceAperture(left, right)),
            "" => null,
            var type => throw new NotSupportedException($"Python Optiland physical aperture '{type}' is not supported yet.")
        };
    }

    private static IPhysicalAperture ReadRadialAperture(JsonElement aperture)
    {
        var innerRadius = GetDouble(aperture, "r_min", 0);
        var outerRadius = GetDouble(aperture, "r_max", 1);
        return innerRadius > 0
            ? new AnnularAperture(outerRadius, innerRadius)
            : new CircularAperture(outerRadius);
    }

    private static RectangularAperture ReadRectangularAperture(JsonElement aperture)
    {
        var xMin = GetDouble(aperture, "x_min", -1);
        var xMax = GetDouble(aperture, "x_max", 1);
        var yMin = GetDouble(aperture, "y_min", -1);
        var yMax = GetDouble(aperture, "y_max", 1);
        return new RectangularAperture(
            Math.Abs(xMax - xMin) / 2.0,
            Math.Abs(yMax - yMin) / 2.0,
            (xMin + xMax) / 2.0,
            (yMin + yMax) / 2.0);
    }

    private static PolygonAperture ReadPolygonAperture(JsonElement aperture)
    {
        return new PolygonAperture(ReadPolygonVertices(aperture));
    }

    private static FileAperture ReadFileAperture(JsonElement aperture)
    {
        var delimiter = aperture.TryGetProperty("delimiter", out var delimiterElement)
            && delimiterElement.ValueKind == JsonValueKind.String
                ? delimiterElement.GetString()
                : null;
        return new FileAperture(
            ReadPolygonVertices(aperture),
            GetString(aperture, "filepath", string.Empty),
            delimiter,
            (int)GetDouble(aperture, "skip_header", 0));
    }

    private static IReadOnlyList<(double X, double Y)> ReadPolygonVertices(JsonElement aperture)
    {
        var x = ReadDoubleArray(aperture, "x");
        var y = ReadDoubleArray(aperture, "y");
        return x.Zip(y, (xValue, yValue) => (xValue, yValue)).ToArray();
    }

    private static IPhysicalAperture ReadBooleanAperture(
        JsonElement aperture,
        Func<IPhysicalAperture, IPhysicalAperture, IPhysicalAperture> factory)
    {
        return factory(
            ReadPhysicalAperture(aperture.GetProperty("a"))
                ?? throw new NotSupportedException("Python Optiland boolean aperture operand 'a' is missing."),
            ReadPhysicalAperture(aperture.GetProperty("b"))
                ?? throw new NotSupportedException("Python Optiland boolean aperture operand 'b' is missing."));
    }

    private static CoordinateSystem? ReadCoordinateSystem(JsonElement geometry)
    {
        if (!geometry.TryGetProperty("cs", out var cs))
        {
            return null;
        }

        var z = GetDouble(cs, "z", 0);
        if (!double.IsFinite(z))
        {
            return null;
        }

        return new CoordinateSystem(
            new Vector3D(GetDouble(cs, "x", 0), GetDouble(cs, "y", 0), z),
            RadiansToDegrees(GetDouble(cs, "rx", 0)),
            RadiansToDegrees(GetDouble(cs, "ry", 0)),
            RadiansToDegrees(GetDouble(cs, "rz", 0)));
    }
}
