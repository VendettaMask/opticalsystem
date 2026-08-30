using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Theming;

internal sealed class IsekaiThemeIconPack : IThemeIconPack
{
    private const string CatalogResourceName =
        "OptilandWorkbench.App.Assets.Icons.game-icons-isekai.json";

    private static readonly Lazy<IReadOnlyDictionary<string, ImportedGameIcon>> Catalog = new(Load);

    public static IsekaiThemeIconPack Instance { get; } = new();

    public string Id => "GameIconsFantasy";

    internal static IReadOnlyCollection<string> Names => Catalog.Value.Keys.ToArray();

    private IsekaiThemeIconPack()
    {
    }

    public bool TryResolve(string iconName, out IconDefinition definition)
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

    internal static bool TryGetAttribution(string iconName, out GameIconAttribution attribution)
    {
        if (Catalog.Value.TryGetValue(iconName, out var imported))
        {
            attribution = imported.Attribution;
            return true;
        }

        attribution = default;
        return false;
    }

    private static IReadOnlyDictionary<string, ImportedGameIcon> Load()
    {
        using var stream = typeof(IsekaiThemeIconPack).Assembly
            .GetManifestResourceStream(CatalogResourceName);
        if (stream is null)
        {
            return new Dictionary<string, ImportedGameIcon>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(stream);
        var icons = new Dictionary<string, ImportedGameIcon>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.GetProperty("icons").EnumerateObject())
        {
            var item = property.Value;
            var primitives = item.GetProperty("paths")
                .EnumerateArray()
                .Select(path => (IconPrimitive) new FilledPathPrimitive(path.GetString() ?? string.Empty))
                .ToArray();
            var transform = CreateTransform(
                item.GetProperty("rotation").GetDouble(),
                item.GetProperty("scaleX").GetDouble(),
                item.GetProperty("scaleY").GetDouble());
            var definition = new IconDefinition(
                primitives,
                Array.Empty<IconPrimitive>(),
                transform,
                PenLineCap.Square,
                PenLineJoin.Miter,
                1);
            var attribution = new GameIconAttribution(
                item.GetProperty("author").GetString() ?? string.Empty,
                item.GetProperty("source").GetString() ?? string.Empty);
            icons[property.Name] = new ImportedGameIcon(definition, attribution);
        }

        return icons;
    }

    private static Matrix CreateTransform(double rotationDegrees, double scaleX, double scaleY)
    {
        const double center = 256;
        const double viewBoxScale = 24d / 512d;
        return Matrix.CreateTranslation(-center, -center) *
               Matrix.CreateScale(scaleX, scaleY) *
               Matrix.CreateRotation(rotationDegrees * Math.PI / 180d) *
               Matrix.CreateTranslation(center, center) *
               Matrix.CreateScale(viewBoxScale, viewBoxScale);
    }

    private sealed record ImportedGameIcon(
        IconDefinition Definition,
        GameIconAttribution Attribution);
}

internal readonly record struct GameIconAttribution(string Author, string Source);
