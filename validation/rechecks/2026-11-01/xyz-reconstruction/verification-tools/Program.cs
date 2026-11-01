using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;

var repo=Path.GetFullPath(args[0]);var root=Path.GetFullPath(args[1]);var frozen=Path.GetFullPath(args[2]);
using var mapJson=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"source-snapshot-map.json")));
var map=mapJson.RootElement.EnumerateArray().ToDictionary(p=>Path.GetFullPath(Path.Combine(repo,p.GetProperty("OriginalPath").GetString()!)).Replace('\\','/'),p=>p.GetProperty("SnapshotPath").GetString()!,StringComparer.OrdinalIgnoreCase);
var results=new List<object>();int matched=0;var failures=new List<string>();
foreach(var pdb in new[]{"tests/OpticalSystemDesign.Tests.pdb","tests/OpticalSystemDesign.App.pdb","driver/OpticalSystemDesign.Core.pdb","driver/OpticalSystemDesign.Application.pdb","driver/XyzReconstruction20261101.pdb","white/CommercialBsdf20261031.pdb"})
{
    var entries=new List<object>();using var stream=File.OpenRead(Path.Combine(frozen,pdb));using var provider=MetadataReaderProvider.FromPortablePdbStream(stream);var reader=provider.GetMetadataReader();
    foreach(var handle in reader.Documents)
    {
        var document=reader.GetDocument(handle);var name=reader.GetString(document.Name).Replace('\\','/');if(!map.TryGetValue(name,out var snapshot))continue;
        var bytes=File.ReadAllBytes(Path.Combine(repo,snapshot));var algorithm=reader.GetGuid(document.HashAlgorithm);
        var actual=algorithm==new Guid("8829d00f-11b8-4213-878b-770e8597ac16")?SHA256.HashData(bytes):algorithm==new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460")?SHA1.HashData(bytes):throw new InvalidDataException("Unknown PDB document hash");
        var expected=reader.GetBlobBytes(document.Hash);bool same=actual.SequenceEqual(expected);if(!same)failures.Add(name);matched++;
        entries.Add(new{OriginalPath=Path.GetRelativePath(repo,name).Replace('\\','/'),SnapshotPath=snapshot,MatchesCompiledDocumentChecksum=same,Algorithm=algorithm,CompiledChecksum=Convert.ToHexString(expected).ToLowerInvariant()});
    }
    results.Add(new{Pdb=pdb,Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(frozen,pdb)))).ToLowerInvariant(),Documents=entries});
}
File.WriteAllText(Path.Combine(root,"compiled-source-audit.json"),JsonSerializer.Serialize(new{MatchedDocuments=matched,Failures=failures,Assemblies=results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Compiled source checks: {matched}; mismatches: {failures.Count}");if(failures.Count>0)Environment.ExitCode=1;
