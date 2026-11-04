using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Media;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Tests;

public sealed class MeritOperandRowPaletteTests
{
    [Theory]
    [InlineData("AXCL", "chromatic")]
    [InlineData("REAY", "ray")]
    [InlineData("EFFL", "first-order")]
    [InlineData("DIFF", "math")]
    [InlineData("BLNK", "comment")]
    [InlineData("DMFS", "section")]
    [InlineData("MNCA", "aperture")]
    [InlineData("MXCA", "aperture")]
    [InlineData("MNEA", "edge-thickness")]
    [InlineData("OPDX", "wavefront")]
    [InlineData("RWFE", "wavefront")]
    [InlineData("THIC", "thickness")]
    public void FamiliarCodesKeepTheirPhysicalClassification(string code, string family)
    {
        Assert.Equal(family, MeritOperandRowPalette.CategoryFor(code).Id);
        Assert.Equal(MeritOperandRowPalette.Resolve(code), MeritOperandRowPalette.Resolve("  " + code.ToLowerInvariant() + " "));
    }

    [Fact]
    public void EntireCatalogueIsMappedWithoutPrefixGuessesAndReservedCodesStayLabelled()
    {
        foreach (var type in MeritFunctionCatalog.Types)
            Assert.NotEqual("unclassified", MeritOperandRowPalette.CategoryFor(type.Code).Id);
        Assert.Equal("reserved", MeritOperandRowPalette.CategoryFor("TRAN").Id);
        Assert.Equal("unclassified", MeritOperandRowPalette.CategoryFor("RW_NOT_AN_OPERAND").Id);
        Assert.Equal(MeritOperandRowPalette.Resolve(null), MeritOperandRowPalette.Resolve("UNKNOWN"));
        foreach (var theme in ThemeRegistry.ConcreteThemes)
        {
            var examples = new[] { "AXCL", "REAY", "EFFL", "DIFF", "BLNK", "DMFS", "MNCA", "MXCA", "MNEA", "OPDX" };
            Assert.Equal(examples.Length, examples.Select(code => MeritOperandRowPalette.ResolveVisual(code, theme).Background).Distinct().Count());
        }
        ExportMapping();
    }

    [Fact]
    public void EveryEnabledCodeAndInteractionKeepsThemeTextReadableAndErrorSeparate()
    {
        // Independent WCAG luminance calculation, including actual theme text rather than generic white.
        foreach (var theme in ThemeRegistry.ConcreteThemes)
        foreach (var code in MeritFunctionCatalog.Types.Select(t => t.Code).Append("UNKNOWN"))
        foreach (var selected in new[] { false, true })
        foreach (var hovered in new[] { false, true })
        foreach (var error in new[] { false, true })
        {
            var visual = MeritOperandRowPalette.ResolveVisual(code, theme, error, hovered, selected);
            Assert.Equal(theme.Palette!.TextPrimary, visual.Foreground);
            Assert.True(Contrast(visual.Foreground, visual.Background) >= 4.5,
                $"{theme.SettingsValue}/{code}/selected={selected}/hover={hovered}/error={error}: {visual}");
            if (code is "DIVI" or "DIVB")
                Assert.NotEqual(MeritOperandRowPalette.ResolveVisual(code, theme).Background,
                    MeritOperandRowPalette.ResolveVisual(code, theme, hasError: true).Background);
        }
    }

    private static double Contrast(Color a, Color b)
    {
        static double L(Color color)
        {
            static double Linear(byte c) { var x = c / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        return (Math.Max(L(a), L(b)) + 0.05) / (Math.Min(L(a), L(b)) + 0.05);
    }

    private static void ExportMapping()
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_OPERAND_COLORS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var map = MeritFunctionCatalog.Types.OrderBy(t => t.Code, StringComparer.Ordinal).Select(t => new
        {
            code = t.Code,
            category = MeritOperandRowPalette.CategoryFor(t.Code).Id,
            categoryName = MeritOperandRowPalette.CategoryFor(t.Code).Name,
            themes = ThemeRegistry.ConcreteThemes.ToDictionary(theme => theme.SettingsValue, theme =>
            {
                var visual = MeritOperandRowPalette.ResolveVisual(t.Code, theme);
                return new { background = Hex(visual.Background), foreground = Hex(visual.Foreground) };
            })
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "operand-color-map.json"),
            JsonSerializer.Serialize(new { version = 1, semantics = "Presentation only; colour does not indicate calculation support or acceptance.", operands = map },
                new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        var html = new StringBuilder("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>操作数行色映射</title><style>body{font:15px system-ui;margin:24px;background:#fafafa;color:#222}table{border-collapse:collapse}th,td{padding:9px 16px;border:1px solid #ccc;text-align:left}thead{position:sticky;top:0;background:#fff}code{font:14px monospace}input{font:inherit;padding:8px;width:32em;max-width:90vw}td span{display:block;font:12px monospace;margin-top:4px}</style><h1>操作数行色映射</h1><p>按语义分色系、按代码固定色阶；下列为正常行底色。同一代码不随行号、排序、重启而变。红色仅用于错误；颜色不表示可计算状态或验收通过。</p><input aria-label=\"筛选操作数或类别\" placeholder=\"输入操作数代码或类别筛选\" oninput=\"document.querySelectorAll('tbody tr').forEach(r=>r.hidden=!r.textContent.toLowerCase().includes(this.value.toLowerCase()))\"><p>实际原生控件预览见同目录 merit-rows-*.png。</p><table><thead><tr><th>Code</th><th>语义类别</th>");
        foreach (var theme in ThemeRegistry.ConcreteThemes) html.Append("<th>").Append(theme.SettingsValue).Append("</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var item in map)
        {
            html.Append("<tr><th><code>").Append(item.code).Append("</code></th><td>").Append(WebUtility.HtmlEncode(item.categoryName)).Append("</td>");
            foreach (var pair in item.themes)
                html.Append("<td style=\"background:").Append(pair.Value.background).Append(";color:").Append(pair.Value.foreground)
                    .Append("\"><code>").Append(item.code).Append("</code><span>").Append(pair.Value.background).Append("</span></td>");
            html.Append("</tr>");
        }
        File.WriteAllText(Path.Combine(directory, "operand-color-map.html"), html.Append("</tbody></table></html>").ToString(), Encoding.UTF8);
    }
}
