using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;

var repo=Path.GetFullPath(args[0]);var root=Path.GetFullPath(args[1]);var execution=Path.GetFullPath(args[2]);
using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"source-snapshot-map.json")));
var map=json.RootElement.EnumerateArray().ToDictionary(p=>Path.GetFullPath(Path.Combine(repo,p.GetProperty("OriginalPath").GetString()!)).Replace('\\','/'),p=>p.GetProperty("SnapshotPath").GetString()!,StringComparer.OrdinalIgnoreCase);
var assemblies=new List<object>();var failures=new List<string>();int count=0;
foreach(var name in new[]{"OpticalSystemDesign.Tests","OpticalSystemDesign.Core","OpticalSystemDesign.Application","OpticalSystemDesign.App"})
{
    var entries=new List<object>();using var stream=File.OpenRead(Path.Combine(execution,name+".pdb"));using var provider=MetadataReaderProvider.FromPortablePdbStream(stream);var reader=provider.GetMetadataReader();
    foreach(var handle in reader.Documents)
    {
        var document=reader.GetDocument(handle);var path=reader.GetString(document.Name).Replace('\\','/');if(!map.TryGetValue(path,out var snapshot))continue;
        var bytes=File.ReadAllBytes(Path.Combine(repo,snapshot));var algorithm=reader.GetGuid(document.HashAlgorithm);
        var actual=algorithm==new Guid("8829d00f-11b8-4213-878b-770e8597ac16")?SHA256.HashData(bytes):algorithm==new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460")?SHA1.HashData(bytes):throw new InvalidDataException("Unknown document hash");
        var expected=reader.GetBlobBytes(document.Hash);bool matches=actual.SequenceEqual(expected);if(!matches)failures.Add(path);count++;
        entries.Add(new{OriginalPath=Path.GetRelativePath(repo,path).Replace('\\','/'),SnapshotPath=snapshot,MatchesCompiledDocumentChecksum=matches,Checksum=Convert.ToHexString(expected).ToLowerInvariant()});
    }
    assemblies.Add(new{Name=name,Documents=entries});
}
File.WriteAllText(Path.Combine(root,"compiled-source-audit.json"),JsonSerializer.Serialize(new{MatchedDocuments=count,Failures=failures,Assemblies=assemblies},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Compiled source checks: {count}; mismatches: {failures.Count}");if(failures.Count>0)Environment.ExitCode=1;
