using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptilandWorkbench.InitialStructure.Engine;

public static class ContentFingerprint
{
    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Compute<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
