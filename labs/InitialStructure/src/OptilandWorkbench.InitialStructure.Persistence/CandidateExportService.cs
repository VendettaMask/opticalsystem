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
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The export path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument([optic], 0),
                temporaryPath,
                cancellationToken);
            var restored = await StarOptProjectStore.LoadAsync(temporaryPath, cancellationToken);
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

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
            return fullPath;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
