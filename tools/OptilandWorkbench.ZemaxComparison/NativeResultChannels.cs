using System.Text.Json;

namespace OptilandWorkbench.ZemaxComparison;

public static class NativeResultChannels
{
    public static Dictionary<string, int> Count(JsonElement native)
    {
        string[] names = ["dataSeries", "dataGrids", "dataGridsRgb", "dataSeriesRgb", "dataScatterPoints", "dataScatterPointsRgb", "rayData", "spotMetrics"];
        return names.ToDictionary(name => name, name => native.TryGetProperty(name, out var values) ? values.GetArrayLength() : 0);
    }
}
