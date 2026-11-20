using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Services;

internal interface IProjectArchiveParticipant
{
    Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken cancellationToken);
}

internal static class ProjectArchiveArtifacts
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }, MaxDepth = 64, WriteIndented = false
    };
    internal static async Task<ProjectArchiveFileDto> WriteAsync<T>(string directory, string relative, string kind, T value,
        bool result = false, CancellationToken token = default)
    {
        var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await BoundedApplicationFile.WriteAllTextAtomicAsync(path, JsonSerializer.Serialize(value, Json),
            result || kind is not ("workspace" or "tolerances" or "optimization-input")
                ? BoundedApplicationFile.MaximumExportBytes : BoundedApplicationFile.MaximumSettingsBytes, "Project archive data", token);
        return File(relative, kind, path, result);
    }
    internal static ProjectArchiveFileDto File(string relative, string kind, string path, bool result = false) =>
        new(relative, kind, path, new FileInfo(path).Length, !result && kind != "workspace", result);
    internal static T Read<T>(string path) => JsonSerializer.Deserialize<T>(BoundedApplicationFile.ReadAllText(path,
        BoundedApplicationFile.MaximumExportBytes, "Project archive data"), Json) ?? throw new InvalidDataException("Archive data is empty.");
}
