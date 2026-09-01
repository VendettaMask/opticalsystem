using Avalonia.Media;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Theming;

internal sealed class PixelThemeIconPack : IThemeIconPack
{
    private static readonly Lazy<IReadOnlyDictionary<string, IconDefinition>> Definitions = new(Load);

    public static PixelThemeIconPack Instance { get; } = new();

    public string Id => "PixelLucide";

    private PixelThemeIconPack()
    {
    }

    public bool TryResolve(string iconName, out IconDefinition definition) =>
        Definitions.Value.TryGetValue(iconName, out definition!) ||
        Definitions.Value.TryGetValue("circle-question-mark", out definition!);

    private static IReadOnlyDictionary<string, IconDefinition> Load()
    {
        var definitions = new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var iconName in LocalIconLibrary.Names)
        {
            if (!LocalIconLibrary.TryGetStandard(iconName, out var standard))
            {
                continue;
            }

            definitions[iconName] = new IconDefinition(
                standard.Primitives,
                standard.AccentPrimitives,
                standard.ContentTransform,
                PenLineCap.Square,
                PenLineJoin.Miter,
                1.35,
                true);
        }

        return definitions;
    }
}
