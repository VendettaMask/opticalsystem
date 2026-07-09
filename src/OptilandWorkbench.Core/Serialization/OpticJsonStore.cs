using System.Text.Json;

namespace OptilandWorkbench.Core.Serialization;

public static class OpticJsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static async Task SaveAsync(Optic optic, string path, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(optic.ToSnapshot(), Options);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public static async Task<Optic> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(json, Options)
            ?? throw new InvalidDataException("The selected file is not a valid optic JSON document.");

        return Optic.FromSnapshot(snapshot);
    }
}
