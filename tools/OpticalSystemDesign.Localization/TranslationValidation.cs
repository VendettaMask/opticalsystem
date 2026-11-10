using System.Text.RegularExpressions;

namespace OpticalSystemDesign.Localization;

public static class TranslationValidation
{
    private static readonly Regex Slots = new(@"(?<!\{)\{(\d+)(?:,[^{}:]+)?(?::[^{}]+)?\}(?!\})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Numbers = new(@"(?<![A-Za-z_])\d+(?:\.\d+)?(?:[Ee][+-]?\d+)?", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Units = new(@"(?<![A-Za-z])(?:W/m²/nm|W/m²|DN/e⁻|e⁻/s|e⁻|sr⁻¹|μm²|µm|μm|mm|nm|DN|DPI|MiB|KB|px|cycles/pixel|cycles/mm)(?![A-Za-z])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Links = new(@"https?://[^\s]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<string> Validate(TranslationEntry entry)
    {
        var errors = new List<string>();
        foreach (var (language, value) in new[] { ("zh-CN", entry.Chinese), ("en-US", entry.English), ("ja-JP", entry.Japanese) })
        {
            if (string.IsNullOrWhiteSpace(value)) { errors.Add($"{language}: empty translation"); continue; }
            if (language == "en-US" && Slots.Replace(value, "").Any(c => c is >= '\u4e00' and <= '\u9fff'))
                errors.Add($"{language}: untranslated Chinese UI text");
            if (!Tokens(Slots, entry.Source, true).SequenceEqual(Tokens(Slots, value, true))) errors.Add($"{language}: placeholder mismatch");
            var sourceText = Slots.Replace(Links.Replace(entry.Source, ""), "");
            var translatedText = Slots.Replace(Links.Replace(value, ""), "");
            if (!ContainsAll(Tokens(Numbers, sourceText), Tokens(Numbers, translatedText))) errors.Add($"{language}: numeric literal mismatch");
            if (!ContainsAll(Tokens(Units, sourceText), Tokens(Units, translatedText))) errors.Add($"{language}: unit symbol mismatch");
            if (!Tokens(Links, entry.Source).SequenceEqual(Tokens(Links, value))) errors.Add($"{language}: URL mismatch");
            if (entry.Source.StartsWith("n = ", StringComparison.Ordinal) || entry.Source.StartsWith("n_ref = ", StringComparison.Ordinal)
                || entry.Source.StartsWith("n² = ", StringComparison.Ordinal))
            {
                var end = entry.Source.IndexOfAny(['；', '。']);
                var formula = end < 0 ? entry.Source : entry.Source[..end];
                if (!value.Contains(formula, StringComparison.Ordinal)) errors.Add($"{language}: formula mismatch");
            }
        }
        return errors;
    }

    private static string[] Tokens(Regex regex, string value, bool slots = false) => regex.Matches(value)
        .Select(x => slots ? x.Groups[1].Value : x.Value).Order(StringComparer.Ordinal).ToArray();

    // Chinese word numerals may legitimately become digits (三维 -> 3D). Every existing
    // numeric/unit token must survive; new translated word numerals are not source values.
    private static bool ContainsAll(string[] expected, string[] actual) => expected.GroupBy(x => x, StringComparer.Ordinal)
        .All(group => actual.Count(x => x == group.Key) >= group.Count());
}
