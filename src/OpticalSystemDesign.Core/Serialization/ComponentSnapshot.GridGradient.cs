using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;

namespace OpticalSystemDesign.Core.Serialization;

public static partial class ComponentSnapshotFactory
{
    private static ComponentSnapshot FromGridGradient(GradientIndexMaterial material, GridGradientIndexProfile grid)
    {
        if (material.Variables.Count != 0) throw new NotSupportedException("Grid-gradient node optimization is not implemented.");
        var n = new Dictionary<string, double> { ["nx"] = grid.Nx, ["ny"] = grid.Ny, ["nz"] = grid.Nz,
            ["originX"] = grid.Origin.X, ["originY"] = grid.Origin.Y, ["originZ"] = grid.Origin.Z,
            ["spacingX"] = grid.Spacing.X, ["spacingY"] = grid.Spacing.Y, ["spacingZ"] = grid.Spacing.Z };
        for (var i = 0; i < grid.Values.Count; i++) n.Add($"v{i}", grid.Values[i]);
        var options = material.IntegrationOptions;
        var values = new[] { material.MaximumPathLength, options.MaximumStep, options.MinimumStep, options.PositionTolerance,
            options.DirectionTolerance, options.OpticalPathTolerance, options.RelativeTolerance, options.SurfaceTolerance, options.MaximumAttempts };
        foreach (var pair in GradientOptionKeys.Zip(values)) n.Add(pair.First, pair.Second);
        return new("gradient_index", n, new() { ["name"] = material.Name, ["profile"] = "grid_gradient", ["formula"] = grid.Formula,
            ["sourceName"] = grid.SourceName, ["sourceSha256"] = grid.SourceSha256 });
    }
    private static GradientIndexMaterial ToGridGradient(ComponentSnapshot snapshot)
    {
        var n = snapshot.Numbers; var t = snapshot.Text;
        if (n is null || t is null || snapshot.Children is { Count: > 0 }
            || !t.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["name", "profile", "formula", "sourceName", "sourceSha256"])
            || t["formula"] != "Schott" || n.Values.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Invalid complete grid-gradient snapshot.");
        var keys = new[] { "nx", "ny", "nz", "originX", "originY", "originZ", "spacingX", "spacingY", "spacingZ" };
        if (keys.Concat(GradientOptionKeys).Any(k => !n.ContainsKey(k))) throw new InvalidDataException("Incomplete grid-gradient snapshot.");
        foreach (var key in new[] { "nx", "ny", "nz" }) if (n[key] != Math.Truncate(n[key]) || n[key] is < 6 or > 128) throw new InvalidDataException("Invalid grid dimensions.");
        var count = (long)n["nx"] * (int)n["ny"] * (int)n["nz"];
        if (count > 80_000 || n.Count != count * 11 + keys.Length + GradientOptionKeys.Length) throw new InvalidDataException("Invalid grid-gradient value count.");
        var nodes = new double[(int)count * 11];
        for (var i = 0; i < nodes.Length; i++) if (!n.TryGetValue($"v{i}", out nodes[i])) throw new InvalidDataException("Missing grid-gradient node value.");
        var attempts = n["maximumAttempts"]; if (attempts != Math.Truncate(attempts) || attempts is < 1 or > 1_000_000) throw new InvalidDataException("Invalid GRIN integration budget.");
        var grid = new GridGradientIndexProfile((int)n["nx"], (int)n["ny"], (int)n["nz"], new(n["originX"], n["originY"], n["originZ"]),
            new(n["spacingX"], n["spacingY"], n["spacingZ"]), nodes, t["sourceName"], t["sourceSha256"]);
        return new(t["name"], grid, n["maximumPathLength"], new GradientIndexIntegrationOptions(n["maximumStep"], n["minimumStep"],
            n["positionTolerance"], n["directionTolerance"], n["opticalPathTolerance"], n["relativeTolerance"], n["surfaceTolerance"], (int)attempts));
    }
}
