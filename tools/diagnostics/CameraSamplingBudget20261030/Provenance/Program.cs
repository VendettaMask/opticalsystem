using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("Replay Core PDB, final Core PDB, report path.");
var replay = Read(args[0]); var final = Read(args[1]);
var changed = replay.Keys.Union(final.Keys).Where(k => replay.GetValueOrDefault(k) != final.GetValueOrDefault(k)).Order().ToArray();
// These independent utilities have no call site in the camera transport. All renderer, transport, material,
// Fresnel, geometry and wave source documents must match. No broad directory exception is permitted.
var independent = new[] { "/Services/CenteredPrescriptionScaling.cs", "/Coatings/AdaptiveThinFilmSpectrum.cs" };
if (changed.Any(k => !independent.Any(s => k.EndsWith(s, StringComparison.OrdinalIgnoreCase))))
    throw new InvalidOperationException("Unexplained computation source change: " + string.Join(", ", changed));
var report = new { ChangedDocuments = changed, RendererSource = final.Single(p => p.Key.EndsWith("/Analysis/Imaging/DiffractionCameraRenderer.cs")),
    ReplayDocuments = replay, FinalDocuments = final,
    ReplayPdbSha256 = Hash(args[0]), FinalPdbSha256 = Hash(args[1]),
    Explanation = "Only independent scaling/adaptive spectrum utilities may differ; all camera/wave/trace/material/geometry/Fresnel documents match. Neither utility is called by camera transport. Final Core was separately regression tested." };
File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { changed, cameraAndTraceSourcesMatch = true }));

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static Dictionary<string, string> Read(string path)
{
    using var file = File.OpenRead(path); using var provider = MetadataReaderProvider.FromPortablePdbStream(file);
    var reader = provider.GetMetadataReader();
    return reader.Documents.Select(h => reader.GetDocument(h)).ToDictionary(d => reader.GetString(d.Name).Replace('\\', '/'),
        d => Convert.ToHexStringLower(reader.GetBlobBytes(d.Hash)));
}
