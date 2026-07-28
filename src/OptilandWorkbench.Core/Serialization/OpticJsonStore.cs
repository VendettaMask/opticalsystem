using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptilandWorkbench.Core.Serialization;

public static class OpticJsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static async Task SaveAsync(Optic optic, string path, CancellationToken cancellationToken = default)
    {
        var snapshot = optic.ToSnapshot();
        OpticSnapshotValidator.Validate(snapshot);
        var json = JsonSerializer.Serialize(snapshot, Options);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public static async Task<Optic> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        if (PythonOptilandJsonStore.LooksLike(json))
        {
            return PythonOptilandJsonStore.Deserialize(json, Path.GetFileNameWithoutExtension(path));
        }

        var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(json, Options)
            ?? throw new InvalidDataException("The selected file is not a valid optic JSON document.");

        return Optic.FromSnapshot(snapshot);
    }
}
