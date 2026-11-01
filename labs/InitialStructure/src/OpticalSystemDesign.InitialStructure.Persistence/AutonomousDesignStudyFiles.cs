using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

public static class AutonomousDesignStudyFiles
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public const int MaximumEntries = 16;
    public static Task SaveStudyAsync(AutonomousDesignStudy study, string path, CancellationToken token = default)
    {
        _ = AutonomousDesignReview.Build(study);
        return Write(study, path, token);
    }
    public static async Task<AutonomousDesignStudy> LoadStudyAsync(string path, CancellationToken token = default)
    {
        var result = await Read<AutonomousDesignStudy>(path, token);
        _ = AutonomousDesignReview.Build(result);
        return result;
    }
    public static Task SavePortfolioAsync(AutonomousDesignPortfolio portfolio, string path, CancellationToken token = default)
    {
        Validate(portfolio);
        return Write(portfolio, path, token);
    }
    public static async Task<AutonomousDesignPortfolio> LoadPortfolioAsync(string path, CancellationToken token = default)
    {
        var result = await Read<AutonomousDesignPortfolio>(path, token);
        Validate(result);
        return result;
    }
    public static void Validate(AutonomousDesignPortfolio portfolio)
    {
        if (portfolio.Version != 1 || portfolio.Algorithm != "AutonomousDesignPortfolioV1" || portfolio.Entries.IsDefault
            || portfolio.Entries.Length > MaximumEntries
            || portfolio.Entries.Select(s => s.SourceFingerprint).Distinct(StringComparer.Ordinal).Count() != portfolio.Entries.Length)
            throw new InvalidDataException("Invalid design portfolio version, count or duplicate evidence.");
        foreach (var study in portfolio.Entries) _ = AutonomousDesignReview.Build(study);
    }
    private static Task Write<T>(T value, string path, CancellationToken token) =>
        BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "design evidence",
            (stream, ct) => JsonSerializer.SerializeAsync(stream, new Envelope<T>(1, value, ContentFingerprint.Compute(value)), Json, ct), token);
    private static async Task<T> Read<T>(string path, CancellationToken token)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes, "design evidence", token);
        Envelope<T> envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope<T>>(bytes, Json) ?? throw new InvalidDataException("Empty design evidence."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid design evidence JSON.", error); }
        if (envelope.Version != 1 || envelope.Value is null || envelope.Sha256 != ContentFingerprint.Compute(envelope.Value))
            throw new InvalidDataException("Design evidence version or checksum mismatch.");
        return envelope.Value;
    }
    private sealed record Envelope<T>(int Version, T Value, string Sha256);
}
