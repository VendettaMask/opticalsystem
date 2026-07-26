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
    private const string PositiveInfinitySentinel = "__optiland_positive_infinity__";
    private const string NegativeInfinitySentinel = "__optiland_negative_infinity__";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    internal static string PositiveInfinity => PositiveInfinitySentinel;

    internal static string NegativeInfinity => NegativeInfinitySentinel;

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
}
