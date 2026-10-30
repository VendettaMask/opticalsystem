using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

public static class LensLibraryFiles
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static Task SaveAsync(LensLibraryDocument library, string path, CancellationToken token = default)
    {
        LensLibraryService.Validate(library);
        return BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "local lens library",
            (stream, cancellation) => JsonSerializer.SerializeAsync(stream,
                new Envelope(library, ContentFingerprint.Compute(library)), Json, cancellation), token);
    }

    public static async Task<LensLibraryDocument> LoadAsync(string path, CancellationToken token = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes, "local lens library", token);
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(bytes, Json) ?? throw new InvalidDataException("Empty lens library."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid lens library JSON.", error); }
        if (envelope.Library is null || envelope.Sha256 != ContentFingerprint.Compute(envelope.Library)) throw new InvalidDataException("Lens library checksum mismatch.");
        LensLibraryService.Validate(envelope.Library);
        return envelope.Library;
    }
    private sealed record Envelope(LensLibraryDocument Library, string Sha256);
}
