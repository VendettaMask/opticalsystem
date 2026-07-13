using System.Text;
using System.Text.Json;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;

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
        ReadAperture(root, optic);
        ReadFields(root, optic);
        ReadWavelengths(root, optic);

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

        optic.SurfaceGroup.Replace(parsedSurfaces.Select(item => item.Surface));
        for (var index = 0; index < parsedSurfaces.Count; index++)
        {
            var parsed = parsedSurfaces[index];
            var surface = optic.SurfaceGroup.Items[index];
            surface.Geometry = parsed.Geometry;
            surface.CoatingModel = parsed.Coating;
            surface.PhysicalAperture = parsed.Aperture;
            if (parsed.CoordinateSystem is not null && index > 0)
            {
                surface.CoordinateSystem = parsed.CoordinateSystem;
            }
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
            ["apodization"] = null,
            ["pickups"] = Array.Empty<object>(),
            ["solves"] = new Dictionary<string, object?> { ["solves"] = Array.Empty<object>() },
            ["surface_group"] = new Dictionary<string, object?>
            {
                ["surfaces"] = optic.SurfaceGroup.Items.Select((surface, index) => WriteSurface(optic, surface, index)).ToArray()
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

    private static void ReadAperture(JsonElement root, Optic optic)
    {
        if (!root.TryGetProperty("aperture", out var aperture) || aperture.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var type = GetString(aperture, "type", "EPD");
        optic.Aperture.Kind = type.ToLowerInvariant() switch
        {
            "imagefno" => ApertureKind.FNumber,
            "objectna" => ApertureKind.NumericalAperture,
            _ => ApertureKind.EntrancePupilDiameter
        };
        optic.Aperture.Value = GetDouble(aperture, "value", optic.Aperture.Value);
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
            if (!fieldType.Equals("AngleField", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Python Optiland field type '{fieldType}' is not supported yet.");
            }
        }

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
                    Weight = 1
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
        var semiDiameter = aperture switch
        {
            CircularAperture circular => circular.Radius,
            RectangularAperture rectangular => Math.Max(rectangular.HalfWidth, rectangular.HalfHeight),
            _ => 1.0
        };
        var hasInteraction = source.TryGetProperty("interaction_model", out var interaction)
            && interaction.ValueKind == JsonValueKind.Object;
        if (hasInteraction)
        {
            var interactionType = GetString(interaction, "type", "RefractiveReflectiveModel");
            if (!interactionType.Equals("RefractiveReflectiveModel", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Python Optiland interaction model '{interactionType}' is not supported yet.");
            }

            if (interaction.TryGetProperty("bsdf", out var bsdf)
                && bsdf.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                throw new NotSupportedException("Python Optiland BSDF import is not supported yet.");
            }
        }

        var reflective = hasInteraction && GetBoolean(interaction, "is_reflective");
        var coating = hasInteraction && interaction.TryGetProperty("coating", out var coatingElement)
            ? ReadCoating(coatingElement)
            : new NoneCoatingModel();
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
            IsReflective = reflective
        };
        surface.Geometry = parsedGeometry;
        surface.CoatingModel = coating;
        return new ParsedSurface(surface, parsedGeometry, coating, aperture, ReadCoordinateSystem(geometry));
    }

    private static IGeometry ReadGeometry(JsonElement geometry)
    {
        var geometryType = GetString(geometry, "type", "Plane");
        var radius = GetDouble(geometry, "radius", 0);
        var conic = GetDouble(geometry, "conic", 0);
        return geometryType switch
        {
            "Plane" => new PlaneGeometry(),
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
            var type => throw new NotSupportedException($"Python Optiland geometry '{type}' is not supported yet.")
        };
    }

    private static string ReadMaterial(Optic optic, JsonElement material)
    {
        var type = GetString(material, "type", "IdealMaterial");
        if (type == "Material")
        {
            return GetString(material, "name", "Air");
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
            "RadialAperture" => new CircularAperture(GetDouble(aperture, "r_max", 1)),
            "RectangularAperture" => new RectangularAperture(
                Math.Max(Math.Abs(GetDouble(aperture, "x_min", -1)), Math.Abs(GetDouble(aperture, "x_max", 1))),
                Math.Max(Math.Abs(GetDouble(aperture, "y_min", -1)), Math.Abs(GetDouble(aperture, "y_max", 1)))),
            "" => null,
            var type => throw new NotSupportedException($"Python Optiland physical aperture '{type}' is not supported yet.")
        };
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
            ["object_space_telecentric"] = false
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
                ["vx"] = 0.0,
                ["vy"] = 0.0
            }).ToArray(),
            ["telecentric"] = false,
            ["field_definition"] = new Dictionary<string, object?> { ["field_type"] = "AngleField" },
            ["object_space_telecentric"] = false
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

    private static object WriteSurface(Optic optic, OpticalSurface surface, int index)
    {
        if (surface.InteractionModel is not RefractiveReflectiveInteractionModel interaction)
        {
            throw new NotSupportedException($"Interaction '{surface.InteractionModel.Kind}' cannot be exported to Python Optiland JSON yet.");
        }

        if (surface.ScatteringModel is not null)
        {
            throw new NotSupportedException("BSDF/scattering surfaces cannot be exported to Python Optiland JSON yet.");
        }

        var geometry = WriteGeometry(surface, index == 0);
        var material = WriteMaterial(optic.Materials.Resolve(surface.MaterialAfterName));
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
            ["interaction_model"] = new Dictionary<string, object?>
            {
                ["type"] = "RefractiveReflectiveModel",
                ["is_reflective"] = interaction.IsReflective || surface.IsReflective,
                ["coating"] = WriteCoating(surface.CoatingModel),
                ["bsdf"] = null
            },
            ["comment"] = surface.Label
        };
    }

    private static object WriteGeometry(OpticalSurface surface, bool isObject)
    {
        var cs = WriteCoordinateSystem(surface.CoordinateSystem, isObject);
        return surface.Geometry switch
        {
            PlaneGeometry => new Dictionary<string, object?>
            {
                ["type"] = "Plane",
                ["cs"] = cs,
                ["radius"] = PositiveInfinitySentinel
            },
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
            _ => throw new NotSupportedException($"Geometry '{surface.Geometry.Kind}' cannot be exported to Python Optiland JSON yet.")
        };
    }

    private static Dictionary<string, object?> WriteCoordinateSystem(CoordinateSystem coordinate, bool isObject)
    {
        return new Dictionary<string, object?>
        {
            ["x"] = isObject ? 0 : coordinate.Origin.X,
            ["y"] = isObject ? 0 : coordinate.Origin.Y,
            ["z"] = isObject ? NegativeInfinitySentinel : coordinate.Origin.Z,
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
            RectangularAperture rectangular => new Dictionary<string, object?>
            {
                ["type"] = "RectangularAperture",
                ["x_min"] = -rectangular.HalfWidth,
                ["x_max"] = rectangular.HalfWidth,
                ["y_min"] = -rectangular.HalfHeight,
                ["y_max"] = rectangular.HalfHeight
            },
            _ => throw new NotSupportedException($"Physical aperture '{aperture.Kind}' cannot be exported to Python Optiland JSON yet.")
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
            .Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number)
                ? number
                : 0.0)
            .ToArray();
    }

    private static double GeometryRadius(IGeometry geometry)
    {
        return geometry switch
        {
            PlaneGeometry => 0,
            StandardGeometry standard => standard.Radius,
            EvenAsphereGeometry even => even.Base.Radius,
            OddAsphereGeometry odd => odd.Base.Radius,
            BiconicGeometry biconic => biconic.RadiusX,
            _ => 0
        };
    }

    private static double GeometryConic(IGeometry geometry)
    {
        return geometry switch
        {
            StandardGeometry standard => standard.Conic,
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
        ICoatingModel Coating,
        IPhysicalAperture? Aperture,
        CoordinateSystem? CoordinateSystem);
}
