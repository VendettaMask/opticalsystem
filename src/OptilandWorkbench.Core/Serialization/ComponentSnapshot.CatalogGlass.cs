using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Serialization;

public static partial class ComponentSnapshotFactory
{
    private static readonly JsonSerializerOptions GlassMetadataOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private static ComponentSnapshot FromCatalogGlass(CatalogGlassMaterial glass)
    {
        if (glass.PropagationModel is not HomogeneousPropagationModel)
        {
            throw new NotSupportedException("Catalog glass snapshots require a homogeneous propagation model.");
        }

        // Store the resolved dispersion, not a catalog lookup key: different catalogs
        // can contain the same glass name with different coefficients.
        var numbers = new Dictionary<string, double>
        {
            ["minimumWavelengthNanometers"] = glass.MinimumWavelengthNanometers,
            ["maximumWavelengthNanometers"] = glass.MaximumWavelengthNanometers
        };
        Coefficients(glass.Coefficients, numbers);
        Coefficients(glass.RefractiveIndexWavelengthsNanometers, numbers, "nw");
        Coefficients(glass.RefractiveIndices, numbers, "n");
        Coefficients(glass.ExtinctionWavelengthsNanometers, numbers, "kw");
        Coefficients(glass.ExtinctionCoefficients, numbers, "k");
        var text = new Dictionary<string, string>
        {
            ["name"] = glass.Name,
            ["manufacturer"] = glass.Manufacturer,
            ["formula"] = glass.Formula
        };
        if (glass.ZemaxData is not null)
        {
            text["zemaxData"] = JsonSerializer.Serialize(glass.ZemaxData, GlassMetadataOptions);
        }

        return new ComponentSnapshot("catalog_glass", numbers, text);
    }

    private static CatalogGlassMaterial ToCatalogGlass(ComponentSnapshot snapshot, string name)
    {
        return new CatalogGlassMaterial(
            name,
            snapshot.Text["manufacturer"],
            snapshot.Text["formula"],
            snapshot.Numbers["minimumWavelengthNanometers"],
            snapshot.Numbers["maximumWavelengthNanometers"],
            ReadCoefficients(snapshot.Numbers),
            ReadCoefficients(snapshot.Numbers, "nw"),
            ReadCoefficients(snapshot.Numbers, "n"),
            ReadCoefficients(snapshot.Numbers, "kw"),
            ReadCoefficients(snapshot.Numbers, "k"),
            zemaxData: snapshot.Text.TryGetValue("zemaxData", out var metadata)
                ? JsonSerializer.Deserialize<OpticalGlassDefinition>(metadata, GlassMetadataOptions)
                : null);
    }
}
