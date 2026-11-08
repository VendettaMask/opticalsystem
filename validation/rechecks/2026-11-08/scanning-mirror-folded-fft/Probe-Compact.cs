using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true, WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter() }
};
var job = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var snapshotBytes = File.ReadAllBytes(job.GetProperty("snapshotPath").GetString()!);
var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(snapshotBytes, options)!);
var method = typeof(DiffractionEngine).GetMethod("ComputeStreamedFocalFftPsf",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))).ToLowerInvariant();
foreach (var tier in args.Skip(2).Select(int.Parse))
{
    var timer = Stopwatch.StartNew();
    Console.WriteLine($"Starting compact {tier}, Core {hash}");
    var (psf, diagnostics) = ((PsfResult, StreamedFourierPupilDiagnostics))method.Invoke(null,
        [optic, (0d, 0d), optic.Wavelengths[0], tier, 128, .25d, 100_000_000L, true])!;
    var rows = psf.Values.Cast<double>().Chunk(128)
        .Select(row => row.Select(value => value / (100 * psf.UnaberratedPeakRatio)).ToArray()).ToArray();
    var result = new
    {
        tier,
        coreSha256 = hash,
        sourceSnapshotSha256 = Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant(),
        phase = psf.PupilPhaseSampling, diagnostics, psf.TangentialWorkingFNumber, psf.SagittalWorkingFNumber,
        axis = Enumerable.Range(-psf.CroppedImageCenterIndex!.Value, 128)
            .Select(i => i * psf.SampleSpacingMicrometers).ToArray(),
        image = rows,
        wallSeconds = timer.Elapsed.TotalSeconds
    };
    File.WriteAllText($"{args[1]}-{tier}.json", JsonSerializer.Serialize(result, options));
    Console.WriteLine($"Finished compact {tier}, phase {psf.PupilPhaseSampling}, wall {timer.Elapsed.TotalSeconds:F3}s");
}
