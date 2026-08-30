using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Persistence;

public sealed class CandidateExportService
{
    private static readonly JsonSerializerOptions FingerprintOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public async Task<string> ExportStarOptAsync(
        CandidateSnapshot candidate,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RunDirectoryStore.ValidateCandidateSet([candidate], nameof(candidate));
        var fullPath = Path.GetFullPath(path);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetExtension(fullPath),
                StarOptProjectStore.Extension))
        {
            fullPath += StarOptProjectStore.Extension;
        }

        var optic = Optic.FromSnapshot(candidate.Optic);
        OpticSnapshotValidator.Validate(optic.ToSnapshot());
        try
        {
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument([optic], 0),
                fullPath,
                cancellationToken);
            var restored = await StarOptProjectStore.LoadAsync(fullPath, cancellationToken);
            var restoredSnapshot = restored.Configurations[restored.ActiveConfigurationIndex].ToSnapshot();
            OpticSnapshotValidator.Validate(restoredSnapshot);
            var expectedHash = SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(candidate.Optic, FingerprintOptions));
            var actualHash = SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(restoredSnapshot, FingerprintOptions));
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new InvalidDataException(
                    "The exported STAROPT project did not reproduce the selected candidate snapshot.");
            }

            return fullPath;
        }
        catch
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            throw;
        }
    }
}
