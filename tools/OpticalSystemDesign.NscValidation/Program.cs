using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;

if (args.Length == 3 && args[0] == "--audit")
{
    var root = Path.GetFullPath(args[1]);
    var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(p => Path.GetExtension(p).Equals(".zmx", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
    var rows = files.Select(path =>
    {
        var result = ZemaxNonSequentialReader.Read(path);
        if (!File.ReadAllBytes(path).SequenceEqual(result.OriginalBytes())) throw new InvalidDataException("Source preservation failed.");
        return new
        {
            File = Path.GetRelativePath(root, path).Replace('\\', '/'), result.SourceSha256, result.Mode, result.CanApply,
            Records = result.Records.Count, Objects = result.Objects.Count, MappedObjects = result.Objects.Count(o => o.MappedObject is not null),
            result.Assets,
            Diagnostics = result.Diagnostics.GroupBy(d => new { d.Code, d.Blocking }).Select(g => new
            {
                g.Key.Code, g.Key.Blocking, Count = g.Count(),
                Details = g.Select(d => new { d.Line, d.ObjectNumber, d.Message }).ToArray()
            }).ToArray()
        };
    }).ToArray();
    var report = new { Scope = "NSC text import and source preservation", Files = rows.Length,
        Applicable = rows.Count(r => r.CanApply), MappedObjects = rows.Sum(r => r.MappedObjects), Results = rows };
    await BoundedFile.WriteAllTextAtomicAsync(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
        BoundedFile.MaximumExportBytes, "NSC import audit");
    Console.WriteLine($"{report.Applicable}/{report.Files} files applicable; {report.MappedObjects} objects mapped; all original bytes preserved.");
    return;
}

if (args.Length != 2) throw new ArgumentException("Expected existing source ZMX and output receipt path.");
var result = ZemaxNonSequentialReader.Read(args[0]);
if (!File.ReadAllBytes(args[0]).SequenceEqual(result.OriginalBytes())) throw new InvalidDataException("Source preservation failed.");
var receipt = new
{
    Scope = "Real original ZMX intake; not native numerical parity or complete executable NSC import",
    result.FileName, result.SourceSha256, OriginalBytes = result.OriginalBytes().Length, result.Mode, Records = result.Records.Count,
    Objects = result.Objects.Select(o => new { o.Number, o.Surface, o.Type, o.ReferenceObject, o.InsideObject, Records = o.Records.Count, BasicGeometryMapped = o.MappedObject is not null }),
    result.Assets, result.CanApply, Diagnostics = result.Diagnostics.GroupBy(d => d.Code).Select(g => new { Code = g.Key, Count = g.Count() }),
    NativeOpticStudioComparison = "Not performed by the intake tool"
};
await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{result.Objects.Count} objects; {result.Records.Count} records; executable={result.CanApply}; original bytes preserved.");
