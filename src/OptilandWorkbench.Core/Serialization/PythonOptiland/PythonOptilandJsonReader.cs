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
            optic.Aperture.Kind = ApertureKind.FloatByStopSize;
            optic.Aperture.Value = optic.SurfaceGroup.ApertureRadius();
        }

        FitVisualSemiDiameters(optic, parsedSurfaces);
        return optic;
    }
}
