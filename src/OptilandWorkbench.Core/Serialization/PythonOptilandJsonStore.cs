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

namespace OptilandWorkbench.Core.Serialization;

public static class PythonOptilandJsonStore
{
    private const string PositiveInfinitySentinel = "__optiland_positive_infinity__";
    private const string NegativeInfinitySentinel = "__optiland_negative_infinity__";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> PythonCatalogMaterialNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "F2",
        "N-F2",
        "N-SK15",
        "K10",
        "SK16"
    };

    public static bool LooksLike(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(NormalizePythonNumericTokens(json));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("version", out _)
                && root.TryGetProperty("surface_group", out _)
                && root.TryGetProperty("fields", out _)
                && root.TryGetProperty("wavelengths", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static Optic Deserialize(string json, string name = "Imported Python Optiland")
    {
        using var document = JsonDocument.Parse(NormalizePythonNumericTokens(json));
        var root = document.RootElement;
        if (!root.TryGetProperty("surface_group", out var surfaceGroup)
            || !surfaceGroup.TryGetProperty("surfaces", out var surfaceArray))
        {
            throw new InvalidDataException("The document is not a Python Optiland optic dictionary.");
        }

        var optic = new Optic(name);
        ValidateUnsupportedRootContracts(root);
        ReadAperture(root, optic);
        ReadFields(root, optic);
        ReadWavelengths(root, optic);
        optic.Apodization = ReadApodization(root);

        var parsedSurfaces = new List<ParsedSurface>();
        var surfaceNumber = 0;
        foreach (var surfaceElement in surfaceArray.EnumerateArray())
        {
            parsedSurfaces.Add(ReadSurface(optic, surfaceElement, surfaceNumber++));
        }

        if (parsedSurfaces.Count < 2)
        {
            throw new InvalidDataException("A Python Optiland document must contain at least object and image surfaces.");
        }

        var objectCoordinate = parsedSurfaces[0].CoordinateSystem;
        var firstSurfaceCoordinate = parsedSurfaces[1].CoordinateSystem;
        var coordinateOffset = objectCoordinate?.Origin.Z ?? 0;
        if (objectCoordinate is not null && firstSurfaceCoordinate is not null)
        {
            parsedSurfaces[0].Surface.Thickness = firstSurfaceCoordinate.Origin.Z - objectCoordinate.Origin.Z;
        }

        optic.SurfaceGroup.Replace(parsedSurfaces.Select(item => item.Surface), syncComposition: false);
        IMaterial previousMaterial = optic.Materials.Resolve("Air");
        for (var index = 0; index < parsedSurfaces.Count; index++)
        {
            var parsed = parsedSurfaces[index];
            var surface = optic.SurfaceGroup.Items[index];
            var materialAfter = parsed.Interaction is RefractiveReflectiveInteractionModel { IsReflective: true }
                ? previousMaterial.Clone()
                : optic.Materials.Resolve(surface.Material);
            surface.Geometry = parsed.Geometry;
            surface.MaterialBefore = previousMaterial.Clone();
            surface.MaterialAfter = materialAfter;
            surface.InteractionModel = parsed.Interaction;
            surface.CoatingModel = parsed.Coating;
            surface.PhysicalAperture = parsed.Aperture;
            if (parsed.CoordinateSystem is not null && index > 0)
            {
                var coordinate = parsed.CoordinateSystem;
                surface.CoordinateSystem = new CoordinateSystem(
                    new Vector3D(
                        coordinate.Origin.X,
                        coordinate.Origin.Y,
                        coordinate.Origin.Z - coordinateOffset),
                    coordinate.RotationXDegrees,
                    coordinate.RotationYDegrees,
                    coordinate.RotationZDegrees);
            }

            previousMaterial = materialAfter.Clone();
        }

        if (root.TryGetProperty("aperture", out var rootAperture)
            && rootAperture.ValueKind == JsonValueKind.Object
            && rootAperture.TryGetProperty("type", out var apertureType)
            && apertureType.GetString()?.Equals("float_by_stop_size", StringComparison.OrdinalIgnoreCase) == true)
        {
            optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
            optic.Aperture.Value = optic.SurfaceGroup.ApertureRadius() * 2.0;
        }

        FitVisualSemiDiameters(optic, parsedSurfaces);
        return optic;
    }

    public static string Serialize(Optic optic)
    {
        var root = new Dictionary<string, object?>
        {
            ["version"] = 1.0,
            ["aperture"] = WriteAperture(optic),
            ["fields"] = WriteFields(optic),
            ["wavelengths"] = WriteWavelengths(optic),
            ["apodization"] = WriteApodization(optic.Apodization),
            ["pickups"] = Array.Empty<object>(),
            ["solves"] = new Dictionary<string, object?> { ["solves"] = Array.Empty<object>() },
            ["surface_group"] = new Dictionary<string, object?>
            {
                ["surfaces"] = optic.SurfaceGroup.Items.Select((surface, index) =>
                    WriteSurface(
                        optic,
                        surface,
                        index,
                        optic.SurfaceGroup.Items.FirstOrDefault()?.Thickness ?? 0)).ToArray()
            }
        };

        return JsonSerializer.Serialize(root, Options)
            .Replace($"\"{PositiveInfinitySentinel}\"", "Infinity", StringComparison.Ordinal)
            .Replace($"\"{NegativeInfinitySentinel}\"", "-Infinity", StringComparison.Ordinal);
    }

    public static async Task SaveAsync(Optic optic, string path, CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(path, Serialize(optic), cancellationToken);
    }

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
            "epd" or "float_by_stop_size" => ApertureKind.EntrancePupilDiameter,
            "imagefno" => ApertureKind.FNumber,
            "objectna" => ApertureKind.NumericalAperture,
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
        if (parsedInteraction.Interaction is PhaseInteractionModel && parsedGeometry is not PlaneGeometry)
        {
            throw new NotSupportedException("Python Optiland phase interactions require Plane geometry.");
        }
        if (parsedInteraction.Interaction is DiffractiveInteractionModel && parsedGeometry is not IGratingGeometry)
        {
            throw new NotSupportedException("Python Optiland diffractive interactions require grating geometry.");
        }
        if (parsedGeometry is IGratingGeometry && parsedInteraction.Interaction is not DiffractiveInteractionModel)
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
            Thickness = type == "ObjectSurface" ? 0 : GetDouble(source, "thickness", 0),
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
            "BiconicGeometry" => new BiconicGeometry(
                GetDouble(geometry, "radius_x", radius),
                GetDouble(geometry, "radius_y", radius),
                GetDouble(geometry, "conic_x", conic),
                GetDouble(geometry, "conic_y", conic)),
            "ToroidalGeometry" => ReadToroidalGeometry(geometry),
            "PolynomialGeometry" => ReadPolynomialGeometry(geometry),
            "ChebyshevPolynomialGeometry" => ReadChebyshevGeometry(geometry),
            "ZernikePolynomialGeometry" => ReadZernikeGeometry(geometry),
            var type => throw new NotSupportedException($"Python Optiland geometry '{type}' is not supported yet.")
        };
    }

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

    private static object WriteAperture(Optic optic)
    {
        var type = optic.Aperture.Kind switch
        {
            ApertureKind.FNumber => "imageFNO",
            ApertureKind.NumericalAperture => "objectNA",
            _ => "EPD"
        };
        return new Dictionary<string, object?>
        {
            ["type"] = type,
            ["value"] = optic.Aperture.Value,
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

        return new Dictionary<string, object?>
        {
            ["type"] = "Surface",
            ["thickness"] = surface.Thickness,
            ["geometry"] = geometry,
            ["material_post"] = material,
            ["is_stop"] = surface.IsStop,
            ["aperture"] = WritePhysicalAperture(surface.PhysicalAperture),
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
            BiconicGeometry biconic => new Dictionary<string, object?>
            {
                ["type"] = "BiconicGeometry",
                ["cs"] = cs,
                ["radius_x"] = biconic.RadiusX,
                ["radius_y"] = biconic.RadiusY,
                ["conic_x"] = biconic.ConicX,
                ["conic_y"] = biconic.ConicY
            },
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

    private static void FitVisualSemiDiameters(Optic optic, IReadOnlyList<ParsedSurface> parsedSurfaces)
    {
        var maxima = new double[optic.SurfaceGroup.Items.Count];
        var fields = new[] { 0.0, 0.5, 1.0 };
        var pupils = new[]
        {
            (0.0, 0.0),
            (1.0, 0.0),
            (-1.0, 0.0),
            (0.0, 1.0),
            (0.0, -1.0),
            (Math.Sqrt(0.5), Math.Sqrt(0.5)),
            (-Math.Sqrt(0.5), Math.Sqrt(0.5)),
            (Math.Sqrt(0.5), -Math.Sqrt(0.5)),
            (-Math.Sqrt(0.5), -Math.Sqrt(0.5))
        };

        try
        {
            foreach (var field in fields)
            {
                foreach (var wavelength in optic.Wavelengths)
                {
                    foreach (var pupil in pupils)
                    {
                        var trace = optic.TraceGeneric(0, field, pupil.Item1, pupil.Item2, wavelength.Micrometers);
                        foreach (var sample in trace.RayHistories.Single())
                        {
                            var radius = Math.Sqrt((sample.Position.X * sample.Position.X) + (sample.Position.Y * sample.Position.Y));
                            maxima[sample.SurfaceNumber] = Math.Max(maxima[sample.SurfaceNumber], radius);
                        }
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
        }

        var fallback = Math.Max(0.5, optic.Paraxial.EstimateEntrancePupilDiameter() * 0.6);
        for (var index = 0; index < optic.SurfaceGroup.Items.Count; index++)
        {
            if (parsedSurfaces[index].Aperture is null)
            {
                optic.SurfaceGroup.Items[index].SemiDiameter = maxima[index] > 0 ? maxima[index] * 1.15 : fallback;
                optic.SurfaceGroup.Items[index].PhysicalAperture = null;
            }
        }
    }

    private static string NormalizePythonNumericTokens(string json)
    {
        var output = new StringBuilder(json.Length + 32);
        var inString = false;
        var escaped = false;
        for (var index = 0; index < json.Length; index++)
        {
            var character = json[index];
            if (inString)
            {
                output.Append(character);
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                output.Append(character);
                continue;
            }

            if (MatchesToken(json, index, "-Infinity"))
            {
                output.Append("\"-Infinity\"");
                index += "-Infinity".Length - 1;
            }
            else if (MatchesToken(json, index, "Infinity"))
            {
                output.Append("\"Infinity\"");
                index += "Infinity".Length - 1;
            }
            else if (MatchesToken(json, index, "NaN"))
            {
                output.Append("\"NaN\"");
                index += "NaN".Length - 1;
            }
            else
            {
                output.Append(character);
            }
        }

        return output.ToString();
    }

    private static bool MatchesToken(string source, int index, string token)
    {
        if (index + token.Length > source.Length
            || !source.AsSpan(index, token.Length).Equals(token, StringComparison.Ordinal))
        {
            return false;
        }

        var beforeIsIdentifier = index > 0 && (char.IsLetterOrDigit(source[index - 1]) || source[index - 1] == '_');
        var end = index + token.Length;
        var afterIsIdentifier = end < source.Length && (char.IsLetterOrDigit(source[end]) || source[end] == '_');
        return !beforeIsIdentifier && !afterIsIdentifier;
    }

    private static IReadOnlyList<double> WithLeadingZero(IReadOnlyList<double> coefficients)
    {
        return new[] { 0.0 }.Concat(coefficients).ToArray();
    }

    private static IReadOnlyList<double> ReadHighOrderAsphereCoefficients(JsonElement geometry, string geometryType)
    {
        var coefficients = ReadDoubleArray(geometry, "coefficients");
        if (coefficients.Length == 0)
        {
            return Array.Empty<double>();
        }

        if (Math.Abs(coefficients[0]) > 1e-14)
        {
            throw new NotSupportedException(
                $"Python Optiland geometry '{geometryType}' with a nonzero first asphere coefficient is not supported yet.");
        }

        return coefficients.Skip(1).ToArray();
    }

    private static double[] ReadDoubleArray(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<double>();
        }

        return array.EnumerateArray()
            .Select(item => ReadDoubleValue(item, 0.0))
            .ToArray();
    }

    private static double[,] ReadDoubleMatrix(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return new double[0, 0];
        }

        var parsedRows = rows.EnumerateArray()
            .Select(row => row.ValueKind == JsonValueKind.Array
                ? row.EnumerateArray().Select(item => ReadDoubleValue(item, 0)).ToArray()
                : Array.Empty<double>())
            .ToArray();
        var columns = parsedRows.Length == 0 ? 0 : parsedRows[0].Length;
        if (parsedRows.Any(row => row.Length != columns))
        {
            throw new InvalidDataException("Python Optiland phase_grid rows must have equal lengths.");
        }

        var output = new double[parsedRows.Length, columns];
        for (var row = 0; row < parsedRows.Length; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                output[row, column] = parsedRows[row][column];
            }
        }

        return output;
    }

    private static double[][] WriteDoubleMatrix(double[,] matrix)
    {
        return Enumerable.Range(0, matrix.GetLength(0))
            .Select(row => Enumerable.Range(0, matrix.GetLength(1))
                .Select(column => matrix[row, column])
                .ToArray())
            .ToArray();
    }

    private static IReadOnlyDictionary<(int X, int Y), double> ReadPolynomialCoefficients(JsonElement geometry)
    {
        var coefficients = new Dictionary<(int X, int Y), double>();
        if (!geometry.TryGetProperty("coefficients", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return coefficients;
        }

        var xOrder = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Array)
            {
                var yOrder = 0;
                foreach (var item in row.EnumerateArray())
                {
                    var coefficient = ReadDoubleValue(item, 0.0);
                    if (Math.Abs(coefficient) > 0)
                    {
                        coefficients[(xOrder, yOrder)] = coefficient;
                    }

                    yOrder++;
                }
            }

            xOrder++;
        }

        return coefficients;
    }

    private static double[][] WritePolynomialCoefficients(IReadOnlyDictionary<(int X, int Y), double> coefficients)
    {
        if (coefficients.Count == 0)
        {
            return Array.Empty<double[]>();
        }

        var maxX = coefficients.Keys.Max(key => key.X);
        var maxY = coefficients.Keys.Max(key => key.Y);
        var matrix = Enumerable.Range(0, maxX + 1)
            .Select(_ => new double[maxY + 1])
            .ToArray();
        foreach (var item in coefficients)
        {
            matrix[item.Key.X][item.Key.Y] = item.Value;
        }

        return matrix;
    }

    private static IReadOnlyDictionary<(int RadialOrder, int AzimuthalFrequency), double> ReadFringeZernikeCoefficients(JsonElement geometry)
    {
        var coefficients = ReadDoubleArray(geometry, "coefficients");
        var indices = FringeZernikeIndices(coefficients.Length);
        var result = new Dictionary<(int RadialOrder, int AzimuthalFrequency), double>();
        for (var index = 0; index < coefficients.Length; index++)
        {
            if (Math.Abs(coefficients[index]) > 0)
            {
                result[(indices[index].RadialOrder, indices[index].AzimuthalFrequency)] = coefficients[index];
            }
        }

        return result;
    }

    private static double[] WriteFringeZernikeCoefficients(
        IReadOnlyDictionary<(int RadialOrder, int AzimuthalFrequency), double> coefficients)
    {
        if (coefficients.Count == 0)
        {
            return Array.Empty<double>();
        }

        var numbered = coefficients
            .Select(item => new
            {
                Number = FringeZernikeNumber(item.Key.RadialOrder, item.Key.AzimuthalFrequency),
                item.Value
            })
            .ToArray();
        if (numbered.Any(item => item.Number is null))
        {
            throw new NotSupportedException("Invalid Zernike coefficient indices cannot be exported to Python Optiland JSON yet.");
        }

        var values = new double[numbered.Max(item => item.Number!.Value)];
        foreach (var item in numbered)
        {
            values[item.Number!.Value - 1] = item.Value;
        }

        return values;
    }

    private static (int RadialOrder, int AzimuthalFrequency)[] FringeZernikeIndices(int count)
    {
        if (count == 0)
        {
            return Array.Empty<(int RadialOrder, int AzimuthalFrequency)>();
        }

        var numbersPresent = new bool[count + 1];
        numbersPresent[0] = true;
        var indices = new List<(int Number, int RadialOrder, int AzimuthalFrequency)>();
        var radialOrder = 0;
        var azimuthalFrequency = 0;
        while (numbersPresent.Any(present => !present))
        {
            var number = FringeZernikeNumber(radialOrder, azimuthalFrequency);
            if (number is not null)
            {
                indices.Add((number.Value, radialOrder, azimuthalFrequency));
                if (number.Value <= count)
                {
                    numbersPresent[number.Value] = true;
                }
            }

            if (azimuthalFrequency == radialOrder)
            {
                radialOrder++;
                azimuthalFrequency = -radialOrder;
            }
            else
            {
                azimuthalFrequency++;
            }
        }

        return indices
            .OrderBy(item => item.Number)
            .Take(count)
            .Select(item => (item.RadialOrder, item.AzimuthalFrequency))
            .ToArray();
    }

    private static int? FringeZernikeNumber(int radialOrder, int azimuthalFrequency)
    {
        if (radialOrder < 0
            || Math.Abs(azimuthalFrequency) > radialOrder
            || ((radialOrder - azimuthalFrequency) % 2) != 0)
        {
            return null;
        }

        var absoluteM = Math.Abs(azimuthalFrequency);
        return (int)(
            Math.Pow(1 + ((radialOrder + absoluteM) / 2.0), 2)
            - (2 * absoluteM)
            + ((1 - Math.Sign(azimuthalFrequency)) / 2.0));
    }

    private static double ReadDoubleValue(JsonElement value, double fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() switch
            {
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                "NaN" => double.NaN,
                var text when double.TryParse(text, out var parsed) => parsed,
                _ => fallback
            };
        }

        return fallback;
    }

    private static double GeometryRadius(IGeometry geometry)
    {
        return geometry switch
        {
            PlaneGeometry => 0,
            PlaneGratingGeometry => 0,
            StandardGratingGeometry grating => grating.Base.Radius,
            StandardGeometry standard => standard.Radius,
            EvenAsphereGeometry even => even.Base.Radius,
            OddAsphereGeometry odd => odd.Base.Radius,
            BiconicGeometry biconic => biconic.RadiusX,
            ToroidalGeometry toroidal => toroidal.SagittalRadius,
            PolynomialGeometry => double.PositiveInfinity,
            ChebyshevGeometry => double.PositiveInfinity,
            ZernikeGeometry => double.PositiveInfinity,
            _ => 0
        };
    }

    private static double GeometryConic(IGeometry geometry)
    {
        return geometry switch
        {
            StandardGeometry standard => standard.Conic,
            StandardGratingGeometry grating => grating.Base.Conic,
            EvenAsphereGeometry even => even.Base.Conic,
            OddAsphereGeometry odd => odd.Base.Conic,
            BiconicGeometry biconic => biconic.ConicX,
            _ => 0
        };
    }

    private static double GetDouble(JsonElement source, string propertyName, double fallback)
    {
        if (!source.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        return ReadDoubleValue(value, fallback);
    }

    private static string GetString(JsonElement source, string propertyName, string fallback)
    {
        return source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static bool GetBoolean(JsonElement source, string propertyName)
    {
        return source.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    private sealed record ParsedSurface(
        OpticalSurface Surface,
        IGeometry Geometry,
        IInteractionModel Interaction,
        ICoatingModel Coating,
        IPhysicalAperture? Aperture,
        CoordinateSystem? CoordinateSystem);

    private sealed record ParsedInteraction(
        IInteractionModel Interaction,
        bool IsReflective,
        ICoatingModel Coating);
}
