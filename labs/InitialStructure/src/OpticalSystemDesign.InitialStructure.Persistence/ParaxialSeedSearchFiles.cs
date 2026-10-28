using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

public static class ParaxialSeedSearchFiles
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public static Task SaveAsync(ParaxialSeedSearchResult result, string path, CancellationToken token = default) =>
        BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "first-order seed preparation",
            (stream, cancellation) => JsonSerializer.SerializeAsync(stream,
                new { SchemaVersion = 1, Result = result, Sha256 = ContentFingerprint.Compute(result) }, Json, cancellation), token);
}
