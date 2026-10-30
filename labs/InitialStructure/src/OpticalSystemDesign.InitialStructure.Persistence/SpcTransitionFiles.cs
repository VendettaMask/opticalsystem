using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

/// <summary>Separate experimental files, never qualified-design records or autonomous checkpoints.</summary>
public static class SpcTransitionFiles
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static Task SaveAsync(SpcTransitionState state, string path, CancellationToken token = default)
    {
        _ = SpcTransitionCompiler.Compile(state);
        return BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "experimental SPC transition",
            (stream, cancellation) => JsonSerializer.SerializeAsync(stream,
                new Envelope(SpcTransitionState.Identity, state, ContentFingerprint.Compute(state)), Json, cancellation), token);
    }

    public static async Task<SpcTransitionState> LoadAsync(string path, CancellationToken token = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes, "experimental SPC transition", token);
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(bytes, Json) ?? throw new InvalidDataException("Empty SPC transition."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid SPC transition JSON.", error); }
        if (envelope.Algorithm != SpcTransitionState.Identity || envelope.State is null || envelope.Sha256 != ContentFingerprint.Compute(envelope.State))
            throw new InvalidDataException("SPC transition identity/checksum mismatch.");
        _ = SpcTransitionCompiler.Compile(envelope.State);
        return envelope.State;
    }

    private sealed record Envelope(string Algorithm, SpcTransitionState State, string Sha256);
}
