using OpticalSystemDesign.Core.Materials;

var replace = args.Length == 6 && args[0] == "--replace";
if (args.Length != 2 && !replace)
{
    Console.Error.WriteLine("Usage: OpticalSystemDesign.GlassCatalogConverter <Glasscat directory> <output.ogdb>");
    Console.Error.WriteLine("   or: --replace <input.ogdb> <source.agf> <catalog name> <replaced catalog names,comma-separated> <output.ogdb>");
    return 2;
}

var outputPath = Path.GetFullPath(replace ? args[5] : args[1]);
List<OpticalGlassCatalogDocument> catalogs;
string sourceDescription;
if (replace)
{
    var original = OpticalSystemDesignGlassCatalogStore.LoadBundle(args[1]);
    var replacement = await ZemaxAgfCatalogReader.ImportFileAsync(args[2]);
    replacement.CatalogName = args[3].Trim().ToUpperInvariant();
    var retired = args[4].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Append(replacement.CatalogName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (replacement.Glasses.Select(glass => glass.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != replacement.Glasses.Count)
        throw new InvalidDataException("The replacement catalog contains duplicate glass names.");
    if (!original.Catalogs.Any(catalog => retired.Contains(catalog.CatalogName)))
        throw new InvalidDataException("No replaced catalog was found in the input bundle.");
    catalogs = original.Catalogs.Where(catalog => !retired.Contains(catalog.CatalogName)).ToList();
    var retained = catalogs.Count;
    catalogs.Add(replacement);
    catalogs = catalogs.OrderBy(catalog => catalog.CatalogName, StringComparer.OrdinalIgnoreCase).ToList();
    sourceDescription = $"Zemax Glasscat: {retained} retained AGF catalogs; {replacement.CatalogName} from {Path.GetFileName(args[2])} ({replacement.Comment})";
}
else
{
    var sourceDirectory = Path.GetFullPath(args[0]);
    var sourcePaths = Directory.EnumerateFiles(sourceDirectory, "*.agf", SearchOption.TopDirectoryOnly)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    if (sourcePaths.Length == 0) throw new InvalidDataException($"No Zemax AGF files were found in '{sourceDirectory}'.");
    catalogs = new List<OpticalGlassCatalogDocument>(sourcePaths.Length);
    foreach (var sourcePath in sourcePaths) catalogs.Add(await ZemaxAgfCatalogReader.ImportFileAsync(sourcePath));
    sourceDescription = $"Zemax Glasscat ({sourcePaths.Length} AGF catalogs)";
}
foreach (var catalog in catalogs) ExternalGlassCatalogDatabase.Register(catalog);

var invalidMaterials = new List<string>();
var registry = new MaterialRegistry();
foreach (var catalog in catalogs)
{
    foreach (var glass in catalog.Glasses
        .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last()))
    {
        try
        {
            var material = (CatalogGlassMaterial)registry.Resolve($"{catalog.CatalogName}:{glass.Name}");
            var minimum = material.MinimumWavelengthNanometers;
            var maximum = material.MaximumWavelengthNanometers;
            var wavelength = minimum > 0 && maximum > minimum
                ? (minimum + maximum) / 2.0
                : 587.5618;
            var refractiveIndex = material.RefractiveIndex(wavelength);
            if (!double.IsFinite(refractiveIndex) || refractiveIndex <= 0)
            {
                invalidMaterials.Add($"{catalog.CatalogName}:{glass.Name} produced n={refractiveIndex} at {wavelength} nm");
            }
        }
        catch (Exception exception)
        {
            invalidMaterials.Add($"{catalog.CatalogName}:{glass.Name}: {exception.Message}");
        }
    }
}

if (invalidMaterials.Count > 0)
{
    throw new InvalidDataException(
        $"{invalidMaterials.Count} glasses failed validation:{Environment.NewLine}" +
        string.Join(Environment.NewLine, invalidMaterials.Take(20)));
}

var outputDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

await OpticalSystemDesignGlassCatalogStore.SaveBundleAsync(
    new OpticalGlassCatalogBundle
    {
        SourceDescription = sourceDescription,
        Catalogs = catalogs
    },
    outputPath);

var rawGlassCount = catalogs.Sum(catalog => catalog.Glasses.Count);
var uniqueGlassCount = catalogs.Sum(catalog => catalog.Glasses
    .Select(glass => glass.Name)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Count());
Console.WriteLine($"Catalogs: {catalogs.Count}");
Console.WriteLine($"Glass records: {rawGlassCount}");
Console.WriteLine($"Unique catalog glasses: {uniqueGlassCount}");
Console.WriteLine($"Output: {outputPath}");
Console.WriteLine($"Bytes: {new FileInfo(outputPath).Length}");
return 0;
