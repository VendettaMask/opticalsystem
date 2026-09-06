using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

// Test-only access to frozen historical inputs; product projects never reference this directory.
internal static class FrozenHistoryFixture
{
    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static string PathFor(string fileName) => Path.Combine(
        AppContext.BaseDirectory, "Validation", "History", "optiland-0.5.8", "fixtures", fileName);

    public static Optic FiniteSystem() => Optic.FromSnapshot(
        JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(InputPath("finite-system.optic.json")), Options)!);

    public static Optic Component(string category, JsonElement referenceCase)
    {
        using var inputs = JsonDocument.Parse(File.ReadAllText(InputPath("components.json")));
        var key = category + "/" + referenceCase.GetProperty("name").GetString();
        return Optic.FromSnapshot(inputs.RootElement.GetProperty(key).Deserialize<OpticSnapshot>(Options)!);
    }

    private static string InputPath(string name) => Path.Combine(
        AppContext.BaseDirectory, "Validation", "History", "optiland-0.5.8", "inputs", name);
}
