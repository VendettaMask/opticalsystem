using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

var before = Read(args[0]);
var after = Read(args[1]);
var mismatches = before.Methods.Keys.Union(after.Methods.Keys)
    .Where(key => !before.Methods.TryGetValue(key, out var a) || !after.Methods.TryGetValue(key, out var b) || a != b).ToArray();
var result = new
{
    Before = new { before.Path, before.Sha256, before.InformationalVersion, MethodsSha256 = Hash(JsonSerializer.SerializeToUtf8Bytes(before.Methods)), before.EmbeddedResourcesSha256 },
    After = new { after.Path, after.Sha256, after.InformationalVersion, MethodsSha256 = Hash(JsonSerializer.SerializeToUtf8Bytes(after.Methods)), after.EmbeddedResourcesSha256 },
    ComparedMethods = before.Methods.Count,
    MismatchedMethods = mismatches,
    EmbeddedResourcesMatch = before.EmbeddedResourcesSha256 == after.EmbeddedResourcesSha256
};
File.WriteAllText(args[2], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
if (mismatches.Length != 0 || !result.EmbeddedResourcesMatch) throw new InvalidDataException("Core executable content differs.");
Console.WriteLine($"{before.Methods.Count} method definitions and embedded resources match. Informational version: {before.InformationalVersion} -> {after.InformationalVersion}.");

static Audit Read(string path)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var reader = pe.GetMetadataReader();
    var methods = new Dictionary<string, string>();
    foreach (var handle in reader.MethodDefinitions)
    {
        var method = reader.GetMethodDefinition(handle);
        var body = method.RelativeVirtualAddress == 0 ? null : pe.GetMethodBody(method.RelativeVirtualAddress);
        var signature = Hash(reader.GetBlobBytes(method.Signature));
        var serialized = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Signature = signature,
            Attributes = (int)method.Attributes,
            ImplementationAttributes = (int)method.ImplAttributes,
            Body = body is null ? null : Hash(body.GetILBytes()!),
            body?.MaxStack,
            body?.LocalVariablesInitialized,
            LocalSignature = body is null ? 0 : MetadataTokens.GetToken(body.LocalSignature),
            Exceptions = body?.ExceptionRegions.Select(region => new
            {
                region.Kind,
                region.TryOffset,
                region.TryLength,
                region.HandlerOffset,
                region.HandlerLength,
                region.FilterOffset,
                CatchType = MetadataTokens.GetToken(region.CatchType)
            }).ToArray()
        });
        methods.Add($"{MetadataTokens.GetToken(handle):X8}:{reader.GetString(method.Name)}", Hash(serialized));
    }
    var version = "";
    foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
    {
        var attribute = reader.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
        var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference) continue;
        var type = reader.GetTypeReference((TypeReferenceHandle)parent);
        if (reader.GetString(type.Name) != "AssemblyInformationalVersionAttribute") continue;
        var value = reader.GetBlobReader(attribute.Value);
        _ = value.ReadUInt16();
        version = value.ReadSerializedString()!;
    }
    var directory = pe.PEHeaders.CorHeader!.ResourcesDirectory;
    var resources = directory.Size == 0 ? [] : pe.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, directory.Size).ToArray();
    return new(path, Hash(File.ReadAllBytes(path)), version, methods, Hash(resources));
}

static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
internal sealed record Audit(string Path, string Sha256, string InformationalVersion,
    Dictionary<string, string> Methods, string EmbeddedResourcesSha256);
