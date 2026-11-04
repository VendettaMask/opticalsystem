using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;

if (args.Length != 2) throw new ArgumentException("Expected existing source ZMX and output receipt path.");
var result = ZemaxNonSequentialReader.Read(args[0]);
if (!File.ReadAllBytes(args[0]).SequenceEqual(result.OriginalBytes())) throw new InvalidDataException("Source preservation failed.");
var receipt = new
{
    Scope = "Real original ZMX intake; not native numerical parity or complete executable NSC import",
    result.FileName, result.SourceSha256, OriginalBytes = result.OriginalBytes().Length, result.Mode, Records = result.Records.Count,
    Objects = result.Objects.Select(o => new { o.Number, o.Surface, o.Type, o.ReferenceObject, o.InsideObject, Records = o.Records.Count, BasicGeometryMapped = o.MappedObject is not null }),
    result.Assets, result.CanApply, Diagnostics = result.Diagnostics.GroupBy(d => d.Code).Select(g => new { Code = g.Key, Count = g.Count() }),
    NativeOpticStudioComparison = "Unavailable: installation/API runtime and licensed archive extraction not present"
};
await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{result.Objects.Count} objects; {result.Records.Count} records; executable={result.CanApply}; original bytes preserved.");
