using System.Text.Json;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class BoundedProjectionTests
{
    [Fact]
    public void SavedNarrowConeHasAFeasibleWitnessButLegacyProjectionExhausts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName,
            "validation/rechecks/2026-10-28/two-element-selection-consistency/feasible-cone.json")));
        var rows = data.RootElement.GetProperty("constraints").EnumerateArray().Select(r =>
            (Offset: r.GetProperty("offset").GetDouble(), Normal: r.GetProperty("normal").EnumerateArray().Select(v => v.GetDouble()).ToArray())).ToArray();
        var preferred = data.RootElement.GetProperty("preferred").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var witness = data.RootElement.GetProperty("witness").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        Assert.All(rows, row => Assert.True(row.Offset + row.Normal.Zip(witness, (a, b) => a * b).Sum() <= 1e-12));
        Assert.Null(LinearFeasibilityProjection.Solve(rows, 2, 4096, preferredPoint: preferred));
    }
}
