using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Theming;

internal sealed class PixelThemeIconPack : IThemeIconPack
{
    private const string CatalogResourceName =
        "OptilandWorkbench.App.Assets.Icons.farm-fresh-icons.json";

    private static readonly Lazy<IReadOnlyDictionary<string, ImportedPixelIcon>> Catalog = new(Load);

    public static PixelThemeIconPack Instance { get; } = new();

    public string Id => "FarmFresh32";

    internal static IReadOnlyCollection<string> Names => Catalog.Value.Keys.ToArray();

    private PixelThemeIconPack()
    {
    }

    public bool TryResolve(string iconName, out IconDefinition definition) =>
        TryResolveImported(iconName, out definition);

    internal static bool TryGetSource(string iconName, out string source)
    {
        if (Catalog.Value.TryGetValue(iconName, out var imported))
        {
            source = imported.Source;
            return true;
        }

        source = string.Empty;
        return false;
    }

    private static bool TryResolveImported(string iconName, out IconDefinition definition)
    {
        var icons = Catalog.Value;
        if (icons.TryGetValue(iconName, out var imported) ||
            icons.TryGetValue("circle-question-mark", out imported))
        {
            definition = imported.Definition;
            return true;
        }

        definition = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, ImportedPixelIcon> Load()
    {
        using var stream = typeof(PixelThemeIconPack).Assembly
            .GetManifestResourceStream(CatalogResourceName);
        if (stream is null)
        {
            return new Dictionary<string, ImportedPixelIcon>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(stream);
        var icons = new Dictionary<string, ImportedPixelIcon>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.GetProperty("icons").EnumerateObject())
        {
            var source = property.Value.GetString() ?? string.Empty;
            var resourceName = $"OptilandWorkbench.App.Assets.Icons.FarmFresh32.{source}";
            using var imageStream = typeof(PixelThemeIconPack).Assembly
                .GetManifestResourceStream(resourceName);
            if (imageStream is null)
            {
                continue;
            }

            using var imageBytes = new MemoryStream();
            imageStream.CopyTo(imageBytes);
            var bytes = imageBytes.ToArray();
            var image = new Lazy<IImage>(() =>
            {
                using var bitmapStream = new MemoryStream(bytes, writable: false);
                return new Bitmap(bitmapStream);
            });
            var definition = new IconDefinition(
                Array.Empty<IconPrimitive>(),
                Array.Empty<IconPrimitive>(),
                Matrix.Identity,
                PenLineCap.Square,
                PenLineJoin.Miter,
                1,
                true,
                image);

            icons[property.Name] = new ImportedPixelIcon(
                definition,
                source);
        }

        return icons;
    }

    private sealed record ImportedPixelIcon(IconDefinition Definition, string Source);
}
