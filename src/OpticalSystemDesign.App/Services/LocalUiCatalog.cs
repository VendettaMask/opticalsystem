using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpticalSystemDesign.App.Services;

internal sealed record LocalUiTranslation(string Source, string Chinese, string English, string Japanese)
{
    public string In(string language) => language switch { UiLocalization.English => English, UiLocalization.Japanese => Japanese, _ => Chinese };
}

/// <summary>Immutable shipped resources. No network, model, user-file lookup, or numeric conversion.</summary>
internal sealed class LocalUiCatalog
{
    internal const string ResourceName = "OpticalSystemDesign.App.Localization.ui.catalog.json";
    private readonly IReadOnlyDictionary<string, LocalUiTranslation> _entries;
    private readonly IReadOnlyDictionary<string, LocalUiTranslation[]> _aliases;
    private readonly HashSet<string> _invariants;
    private readonly (LocalUiTranslation Entry, Regex Pattern, string Prefix, int[] Indices)[] _templates;
    private static readonly Regex Slots = new(@"(?<!\{)\{(\d+)(?:,[^{}:]+)?(?::[^{}]+)?\}(?!\})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private LocalUiCatalog(LocalUiTranslation[] entries, IEnumerable<string>? invariants = null)
    {
        _invariants = new HashSet<string>(invariants ?? [], StringComparer.Ordinal);
        var dictionary = new Dictionary<string, LocalUiTranslation>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Source) || new[] { entry.Chinese, entry.English, entry.Japanese }.Any(string.IsNullOrWhiteSpace)
                || !dictionary.TryAdd(entry.Source, entry)) throw new InvalidDataException("Invalid or duplicate local UI translation.");
            var expected = PlaceholderIndices(entry.Source);
            foreach (var value in new[] { entry.Chinese, entry.English, entry.Japanese })
                if (!expected.SequenceEqual(PlaceholderIndices(value))) throw new InvalidDataException($"UI placeholder mismatch: {entry.Source}");
        }
        _entries = dictionary;
        _aliases = entries.SelectMany(entry => new[] { entry.Chinese, entry.English, entry.Japanese }.Distinct()
            .Select(value => (Value: value, Entry: entry))).GroupBy(x => x.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(x => x.Entry).ToArray(), StringComparer.Ordinal);
        var templates = new List<(LocalUiTranslation, Regex, string, int[])>();
        foreach (var entry in entries)
        foreach (var sourceTemplate in new[] { entry.Source, entry.Chinese, entry.English, entry.Japanese }.Distinct())
        {
            var matches = Slots.Matches(sourceTemplate);
            if (matches.Count == 0) continue;
            // Only fixed UI sentences are candidates. A bare value or unit is never a template.
            var fixedText = Slots.Replace(sourceTemplate, "");
            if (!fixedText.Any(c => c is >= '\u4e00' and <= '\u9fff') && fixedText.Count(char.IsAsciiLetter) < 3) continue;
            var pattern = "\\A";
            var offset = 0;
            var indices = new List<int>();
            foreach (Match match in matches)
            {
                pattern += EscapeLiteral(sourceTemplate[offset..match.Index]) + "(.*?)";
                indices.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
                offset = match.Index + match.Length;
            }
            pattern += EscapeLiteral(sourceTemplate[offset..]) + "\\z";
            templates.Add((entry, new Regex(pattern, RegexOptions.CultureInvariant | (sourceTemplate.Contains('\n') ? RegexOptions.Singleline : RegexOptions.None), TimeSpan.FromMilliseconds(20)),
                sourceTemplate[..matches[0].Index].Split('\n')[0].TrimEnd('\r'), indices.ToArray()));
        }
        _templates = templates.OrderByDescending(x => x.Item3.Length).ToArray();
    }

    public static LocalUiCatalog Load()
    {
        using var stream = typeof(LocalUiCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The bundled local UI catalog is missing.");
        using var invariantStream = typeof(LocalUiCatalog).Assembly.GetManifestResourceStream("OpticalSystemDesign.App.Localization.ui.invariants.json")
            ?? throw new InvalidDataException("The bundled UI data declarations are missing.");
        return new(JsonSerializer.Deserialize<LocalUiTranslation[]>(stream)
            ?? throw new InvalidDataException("The bundled local UI catalog is empty."), JsonSerializer.Deserialize<string[]>(invariantStream));
    }

    internal static LocalUiCatalog Read(Stream stream) => new(JsonSerializer.Deserialize<LocalUiTranslation[]>(stream)
        ?? throw new InvalidDataException("The bundled local UI catalog is empty."));

    internal IReadOnlyCollection<LocalUiTranslation> Entries => _entries.Values.ToArray();
    internal static int[] PlaceholderIndices(string text) => Slots.Matches(text).Select(x => int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
    private static string EscapeLiteral(string text) => Regex.Escape(text.Replace("\r\n", "\n", StringComparison.Ordinal))
        .Replace(@"\n", @"\r?\n", StringComparison.Ordinal);

    internal bool TryGet(string source, string language, out string result)
    {
        if (_invariants.Contains(source)) { result = source; return true; }
        if (_entries.TryGetValue(source, out var entry)) { result = entry.In(language); return true; }
        if (_aliases.TryGetValue(source, out var aliases))
        {
            var translations = aliases.Select(value => value.In(language)).Distinct(StringComparer.Ordinal).Take(2).ToArray();
            if (translations.Length == 1) { result = translations[0]; return true; }
        }
        result = source;
        return false;
    }

    internal bool TryTemplate(string source, string language, out string result)
    {
        result = source;
        if (source.Length > 32768) return false;
        foreach (var (entry, pattern, prefix, indices) in _templates)
        {
            if (!source.StartsWith(prefix, StringComparison.Ordinal)) continue;
            Match match;
            try { match = pattern.Match(source); } catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            var values = new Dictionary<int, string>();
            var valid = true;
            for (var index = 0; index < indices.Length; index++)
            {
                var value = match.Groups[index + 1].Value;
                if (values.TryGetValue(indices[index], out var previous) && previous != value) { valid = false; break; }
                values[indices[index]] = value;
            }
            if (!valid) continue;
            // Captures already contain the original formatting: never parse/reformat/translate them.
            result = Slots.Replace(entry.In(language), slot => values[int.Parse(slot.Groups[1].Value, CultureInfo.InvariantCulture)]);
            return true;
        }
        return false;
    }
}
