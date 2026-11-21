using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Core.Serialization;

public static partial class ComponentSnapshotFactory
{
    private static ComponentSnapshot FromModelGlass(ModelGlassMaterial model) => new("model_glass", new()
    {
        ["nd"] = model.Nd, ["vd"] = model.Vd, ["dpgf"] = model.DPgF,
        ["varyNd"] = model.VaryNd ? 1 : 0, ["varyVd"] = model.VaryVd ? 1 : 0, ["varyDPgF"] = model.VaryDPgF ? 1 : 0
    }, new() { ["name"] = model.Name, ["protocol"] = ModelGlassMaterial.Protocol });

    internal static ModelGlassMaterial ToModelGlass(ComponentSnapshot snapshot)
    {
        if (snapshot.Numbers.Count != 6 || new[] { "nd", "vd", "dpgf", "varyNd", "varyVd", "varyDPgF" }.Any(key => !snapshot.Numbers.ContainsKey(key))
            || snapshot.Text.Count != 2 || !snapshot.Text.ContainsKey("name") || snapshot.Text.GetValueOrDefault("protocol") != ModelGlassMaterial.Protocol
            || snapshot.Children is { Count: > 0 } || new[] { "varyNd", "varyVd", "varyDPgF" }.Any(key => snapshot.Numbers[key] is not (0 or 1)))
            throw new InvalidDataException("Invalid model-glass protocol, descriptors or variable flags.");
        return new(snapshot.Text["name"], snapshot.Numbers["nd"], snapshot.Numbers["vd"], snapshot.Numbers["dpgf"],
            snapshot.Numbers["varyNd"] == 1, snapshot.Numbers["varyVd"] == 1, snapshot.Numbers["varyDPgF"] == 1);
    }
}
