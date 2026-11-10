using System.Text.Encodings.Web;
using System.Text.Json;
using OpticalSystemDesign.Localization;

var command = args.ElementAtOrDefault(0) ?? "check";
var root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? ".");
var folder = Path.Combine(root, "localization");
var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var files = Directory.EnumerateFiles(Path.Combine(root, "src/OpticalSystemDesign.App"), "*.cs", SearchOption.AllDirectories)
    .Where(x => !x.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
    .Order(StringComparer.Ordinal).Select(path => (Path: Path.GetRelativePath(root, path).Replace('\\', '/'), Text: File.ReadAllText(path))).ToArray();
var seedFiles = files.Concat(Directory.EnumerateFiles(Path.Combine(root, "src/OpticalSystemDesign.App/Controls"), "ClipboardExceptionBoundary.cs")
    .Select(path => (Path: path, Text: File.ReadAllText(path))));
var entries = LocalizationExtractor.ReadSeeds(seedFiles).GroupBy(x => x.Source, StringComparer.Ordinal)
    .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
foreach (var name in new[] { "optical-terms.json", "ui-translations.json" })
    if (File.Exists(Path.Combine(folder, name)))
        foreach (var entry in JsonSerializer.Deserialize<TranslationEntry[]>(File.ReadAllText(Path.Combine(folder, name)))!) entries[entry.Source] = entry;
foreach (var name in new[] { "optical-terms.tsv", "ui-translations.tsv", "ui-presentation-translations.tsv" })
    if (File.Exists(Path.Combine(folder, name)))
        foreach (var line in File.ReadAllLines(Path.Combine(folder, name)).Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#')))
        {
            var columns = line.Split('\t').Select(x => x.Replace("\\r", "\r").Replace("\\n", "\n")).ToArray();
            if (columns.Length is < 3 or > 4) throw new InvalidDataException($"Invalid TSV entry: {line}");
            var entry = new TranslationEntry(columns[0], columns.Length == 4 ? columns[3] : columns[0], columns[1], columns[2]);
            entries[entry.Source] = entry;
        }
var occurrences = LocalizationExtractor.Extract(files.Where(x => !Path.GetFileName(x.Path).StartsWith("UiLocalization", StringComparison.Ordinal)))
    .OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Line).ToArray();
// Known structured labels are translated by the retained local formatting adapters.
var invariantPath = Path.Combine(folder, "invariant-text.tsv");
var invariants = File.Exists(invariantPath) ? File.ReadAllLines(invariantPath).Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
    .Select(x => x.Split('\t')).ToDictionary(x => x[0].Replace("\\n", "\n"), x => x.Length == 2 && !string.IsNullOrWhiteSpace(x[1])
        ? x[1] : throw new InvalidDataException("Invariants require an explicit reason."), StringComparer.Ordinal) : [];
// Reuse existing translations without guessing between ambiguous labels.
foreach (var group in entries.Values.SelectMany(entry => new[] { entry.Chinese, entry.English, entry.Japanese }.Distinct()
             .Select(value => (Value: value, Entry: entry))).GroupBy(x => x.Value, StringComparer.Ordinal).ToArray())
    if (!invariants.ContainsKey(group.Key) && !entries.ContainsKey(group.Key)
        && group.Select(x => (x.Entry.Chinese, x.Entry.English, x.Entry.Japanese)).Distinct().Count() == 1
        && occurrences.Any(x => x.Source == group.Key))
        entries[group.Key] = group.First().Entry with { Source = group.Key };
for (var pass = 0; pass < 3; pass++)
foreach (var occurrence in occurrences.Where(x => !entries.ContainsKey(x.Source) && !invariants.ContainsKey(x.Source)))
{
    var source = occurrence.Source;
    var accelerator = source.LastIndexOf("(_", StringComparison.Ordinal);
    if (accelerator > 0 && source.EndsWith(')') && entries.TryGetValue(source[..accelerator], out var original))
        entries[source] = new(source, original.Chinese + source[accelerator..], original.English + source[accelerator..], original.Japanese + source[accelerator..]);
    else if (source.EndsWith('：') && entries.TryGetValue(source[..^1], out original))
        entries[source] = new(source, original.Chinese + "：", original.English + ":", original.Japanese + "：");
    else if (source.EndsWith("{0}", StringComparison.Ordinal) && source.Length > 3
        && source[^4] is '：' or ':' or ' ' && entries.TryGetValue(source[..^3], out original))
        entries[source] = new(source, original.Chinese + "{0}", original.English + "{0}", original.Japanese + "{0}");
    else if (source.Trim() is var trimmed && source != trimmed && entries.TryGetValue(trimmed, out original))
    {
        var begin = source[..(source.Length - source.TrimStart().Length)];
        var end = source[source.TrimEnd().Length..];
        entries[source] = new(source, begin + original.Chinese + end, begin + original.English + end, begin + original.Japanese + end);
    }
}
var missing = occurrences.Where(x => !entries.ContainsKey(x.Source) && !invariants.ContainsKey(x.Source)).GroupBy(x => x.Source, StringComparer.Ordinal)
    .Select(x => new { Source = x.Key, Occurrences = x.ToArray() }).OrderBy(x => x.Source, StringComparer.Ordinal).ToArray();
Directory.CreateDirectory(folder);
void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, options) + "\n");
if (command == "extract")
{
    var output = args.ElementAtOrDefault(2) ?? Path.Combine(folder, "extracted.json");
    Write(output, new { Schema = 1, Occurrences = occurrences, Missing = missing });
    Console.WriteLine($"Extracted {occurrences.Length} UI occurrences, {entries.Count} translations, {missing.Length} missing.");
    return 0;
}
var errors = new List<string>();
foreach (var entry in entries.Values)
{
    errors.AddRange(TranslationValidation.Validate(entry).Select(error => $"{error}: {entry.Source}"));
}
var glossaryPath = Path.Combine(folder, "optical-terms.tsv");
if (File.Exists(glossaryPath))
    foreach (var line in File.ReadAllLines(glossaryPath).Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#')))
    {
        var term = line.Split('\t');
        if (!entries.TryGetValue(term[0], out var actual) || actual.English != term[1] || actual.Japanese != term[2])
            errors.Add($"Optical term override conflicts with the glossary: {term[0]}");
    }
var generated = JsonSerializer.Serialize(entries.Values.OrderBy(x => x.Source, StringComparer.Ordinal).ToArray(), options) + "\n";
var catalogPath = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/Localization/ui.catalog.json");
if (command == "sync")
{
    errors.AddRange(missing.Select(x => $"Untranslated UI: {x.Source} ({x.Occurrences[0].Path}:{x.Occurrences[0].Line})"));
    if (errors.Count == 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        File.WriteAllText(catalogPath, generated);
        Write(Path.Combine(Path.GetDirectoryName(catalogPath)!, "ui.invariants.json"), invariants.Keys.Order(StringComparer.Ordinal).ToArray());
    }
}
else if (command == "check")
{
    if (!File.Exists(catalogPath) || File.ReadAllText(catalogPath).Replace("\r\n", "\n") != generated.Replace("\r\n", "\n")) errors.Add("Local catalog is stale. Run sync.");
    var invariantCatalog = Path.Combine(Path.GetDirectoryName(catalogPath)!, "ui.invariants.json");
    var invariantJson = JsonSerializer.Serialize(invariants.Keys.Order(StringComparer.Ordinal).ToArray(), options) + "\n";
    if (!File.Exists(invariantCatalog) || File.ReadAllText(invariantCatalog).Replace("\r\n", "\n") != invariantJson.Replace("\r\n", "\n")) errors.Add("Local data declarations are stale. Run sync.");
    errors.AddRange(missing.Select(x => $"Untranslated UI: {x.Source} ({x.Occurrences[0].Path}:{x.Occurrences[0].Line})"));
}
else throw new ArgumentException("Use extract, sync or check.");
Console.WriteLine($"{command}: {entries.Count} local translations, {occurrences.Length} UI occurrences, {missing.Length} missing, {errors.Count} errors.");
foreach (var error in errors) Console.Error.WriteLine(error);
return errors.Count == 0 ? 0 : 1;
