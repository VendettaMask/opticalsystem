using Avalonia.Media;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

// Business classification colours, independent of wavelength and calculation/status semantics.
// Merit families use OperandHelpTaxonomy; tolerance families use the typed perturbation kind.
internal static class MeritOperandRowPalette
{
    internal static MeritOperandRowCategory CategoryFor(string? operandType)
    {
        var code = Normalize(operandType);
        var family = code switch
        {
            "FNUM" => "pupil",
            "RADI" => "curvature",
            "THIC" => "thickness",
            "RWFE" => "wavefront",
            "AXCL" or "LACL" or "SPCH" => "chromatic",
            "BLNK" => "comment",
            "DMFS" => "section",
            _ => OperandHelpTaxonomy.FamilyFor(code).Id
        };
        return family switch
        {
            "focal" or "pupil" or "conjugate" => C("first-order", "一阶 / 焦距与光瞳", "#39988D"),
            "system" or "configuration" => C("system", "系统 / 多重结构", "#789281"),
            "real-ray" or "paraxial-ray" or "global-ray" or "centroid" or "ray-fan" => C("ray", "光线 / 坐标", "#548CCB"),
            "ray-angle" or "ray-check" or "path" => C("ray-path", "光线 / 角度与路径", "#4B9FBA"),
            "spot" => C("spot", "像质 / 点列", "#727ACA"),
            "wavefront" => C("wavefront", "像质 / 波前与光程差", "#A17BC5"),
            "ray-aberration" => C("ray-aberration", "像质 / 横向与角度像差", "#529EAA"),
            "chromatic" => C("chromatic", "像质 / 色差", "#C27B9E"),
            "seidel" or "distortion" => C("aberration", "像质 / 三阶像差与畸变", "#B58EBC"),
            "geometric-mtf" or "diffraction-mtf" or "psf" => C("transfer", "像质 / MTF 与 PSF", "#857CC1"),
            "energy" or "image-analysis" or "illumination" => C("energy", "像质 / 能量与照度", "#6BADA0"),
            "aperture" => C("aperture", "制造 / 口径与净孔径", "#9AA65A"),
            "thickness" => C("thickness", "制造 / 中心厚度与间隔", "#C0A05E"),
            "edge-thickness" => C("edge-thickness", "制造 / 边缘厚度", "#C58B5D"),
            "curvature" or "global-surface" or "surface-point" or "surface-statistics" or "phase" or "parameters" or "manufacturing"
                => C("surface", "制造 / 面形与参数", "#B9A073"),
            "glass" or "glass-properties" or "grin-point" or "grin-profile" => C("material", "材料 / 折射率与色散", "#7AA27A"),
            "coating" or "paraxial-gaussian" or "skew-gaussian" or "physical-optics" or "ghost" or "hologram"
                => C("beam", "专项 / 光束与镀膜", "#5AA6A5"),
            "math" or "row-constraint" => C("math", "数学 / 行运算与约束", "#879CA8"),
            "section" => C("section", "控制 / 评价函数分段", "#BEA15E"),
            "flow" or "tolerance" => C("control", "控制 / 流程与扩展", "#A49C88"),
            "comment" => C("comment", "注释 / 空行", "#92908A"),
            "unused" or "unverified" => C("reserved", "保留 / 定义待核实", "#9A929F"),
            _ => C("unclassified", "未知 / 中性回退", "#92969C")
        };
    }

    internal static MeritOperandRowCategory ToleranceCategoryFor(ToleranceOperandKind kind) => kind switch
    {
        ToleranceOperandKind.FringePower or ToleranceOperandKind.FringeIrregularity
            => C("tolerance-shape", "公差 / 曲率与面形", "#AB70A8"),
        ToleranceOperandKind.Radius or ToleranceOperandKind.Conic or ToleranceOperandKind.AsphereCoefficient
            => C("tolerance-shape", "公差 / 曲率与面形", "#A17BC5"),
        ToleranceOperandKind.Thickness => CategoryFor("THIC"),
        ToleranceOperandKind.DecenterX or ToleranceOperandKind.DecenterY
            or ToleranceOperandKind.ElementDecenterX or ToleranceOperandKind.ElementDecenterY
            => C("tolerance-decenter", "公差 / 偏心", "#548CCB"),
        ToleranceOperandKind.TiltX or ToleranceOperandKind.TiltY
            or ToleranceOperandKind.ElementTiltX or ToleranceOperandKind.ElementTiltY
            => C("tolerance-tilt", "公差 / 倾斜", "#4B9FBA"),
        ToleranceOperandKind.RefractiveIndex or ToleranceOperandKind.AbbeNumber => CategoryFor("INDX"),
        ToleranceOperandKind.Compensator => C("tolerance-compensator", "公差 / 补偿", "#879CA8"),
        _ => CategoryFor(null)
    };

    internal static MeritOperandRowVisual ResolveToleranceVisual(ToleranceOperandKind kind, ThemeDefinition theme,
        bool hovered = false, bool selected = false, bool enabled = true) =>
        ResolveVisual(ToleranceOperandEditorRow.CodeFor(kind), ToleranceCategoryFor(kind), theme,
            shadeCount: 7, shadeStep: 0.018, hasError: false, hovered, selected, enabled);

    internal static Color Resolve(string? operandType, bool hasError = false) =>
        ResolveVisual(operandType, ThemeRegistry.FromSettings("Light"), hasError).Background;

    internal static MeritOperandRowVisual ResolveVisual(
        string? operandType, ThemeDefinition theme, bool hasError = false,
        bool hovered = false, bool selected = false, bool enabled = true)
        => ResolveVisual(Normalize(operandType), CategoryFor(operandType), theme,
            shadeCount: 4, shadeStep: 0.025, hasError, hovered, selected, enabled);

    private static MeritOperandRowVisual ResolveVisual(string code, MeritOperandRowCategory category,
        ThemeDefinition theme, uint shadeCount, double shadeStep, bool hasError, bool hovered, bool selected, bool enabled)
    {
        var palette = theme.Palette!;
        // An explicitly specified, process-independent hash gives a code its fixed shade.
        // This is not row-index striping; insertion, sorting and recycling cannot change it.
        uint shade = 2166136261;
        foreach (var ch in code) shade = unchecked((shade ^ ch) * 16777619);
        var neutral = category.Id is "comment" or "unclassified";
        var baseStrength = theme.SettingsValue == "Light" ? 0.34 : theme.IsDarkLike ? 0.30 : 0.15;
        var strength = neutral ? 0.04 : baseStrength + (shade % shadeCount) * shadeStep;
        if (selected) strength += theme.IsDarkLike ? 0.16 : 0.13;
        if (hovered) strength += 0.065;
        var background = Mix(palette.Surface, category.Tint, strength);
        if (hasError)
        {
            // Error red is an override, never the ordinary colour of an arithmetic operand.
            background = Mix(palette.Surface, palette.TextError,
                (theme.IsDarkLike ? 0.32 : 0.13) + (selected ? 0.10 : 0) + (hovered ? 0.04 : 0));
        }
        if (!enabled) background = Mix(palette.Surface, background, 0.35);
        // Keep the theme's real text (including Isekai old gold), without losing AA contrast.
        for (var attempt = 0; attempt < 32 && Contrast(palette.TextPrimary, background) < 4.5; attempt++)
            background = Mix(palette.Surface, background, 0.90);
        if (Contrast(palette.TextPrimary, background) < 4.5) background = palette.Surface;
        return new(background, enabled ? palette.TextPrimary : palette.TextDisabled);
    }

    private static MeritOperandRowCategory C(string id, string name, string tint) => new(id, name, Color.Parse(tint));
    private static string Normalize(string? code) => code?.Trim().ToUpperInvariant() ?? string.Empty;

    internal static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    internal static double Contrast(Color first, Color second)
    {
        static double L(Color color)
        {
            static double Linear(byte c) => c <= 10 ? c / 3294.6 : Math.Pow((c / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        var a = L(first) + 0.05;
        var b = L(second) + 0.05;
        return Math.Max(a, b) / Math.Min(a, b);
    }
}

internal sealed record MeritOperandRowCategory(string Id, string Name, Color Tint);
internal sealed record MeritOperandRowVisual(Color Background, Color Foreground);
