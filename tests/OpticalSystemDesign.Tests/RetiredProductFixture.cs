using System.Text.Json;

namespace OpticalSystemDesign.Tests;

// Test-only negative branding guard and original historical reference identity.
internal static class RetiredProductFixture
{
    internal static string Brand { get; } = ReadBrand();

    private static string ReadBrand()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "retired-product.json")));
        return document.RootElement.GetProperty("Brand").GetString()!;
    }
}
