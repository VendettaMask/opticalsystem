using System.Text.Json;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class BoundedProjectionTests
{
    [Fact]
    public void PlanarProjectionChecksBoundsInfeasibilityAndCancellation()
    {
        var point = BoundedPlanarProjection.Solve([(0, new double[] { 1, 1 })], [1, 1]);
        Assert.NotNull(point); Assert.InRange(Math.Abs(point[0]), 0, 1e-12); Assert.InRange(Math.Abs(point[1]), 0, 1e-12);
        Assert.Null(BoundedPlanarProjection.Solve([(2, new double[] { 1, 0 })], [0, 0]));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => BoundedPlanarProjection.Solve([], [0, 0], stop.Token));
        Assert.Throws<ArgumentException>(() => BoundedPlanarProjection.Solve([], [double.NaN, 0]));
    }

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
        var recovered = BoundedPlanarProjection.Solve(rows, preferred);
        Assert.NotNull(recovered);
        Assert.All(rows, row => Assert.True(row.Offset + row.Normal.Zip(recovered, (a, b) => a * b).Sum() <= 1e-10));
        Assert.InRange(recovered[0], -1, 1); Assert.InRange(recovered[1], -1, 1);
    }
}
