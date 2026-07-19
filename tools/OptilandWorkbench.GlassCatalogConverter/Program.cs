using OptilandWorkbench.Core.Materials;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: OptilandWorkbench.GlassCatalogConverter <Glasscat directory> <output.ogdb>");
    return 2;
}

var sourceDirectory = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var sourcePaths = Directory
    .EnumerateFiles(sourceDirectory, "*.agf", SearchOption.TopDirectoryOnly)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (sourcePaths.Length == 0)
{
    throw new InvalidDataException($"No Zemax AGF files were found in '{sourceDirectory}'.");
}

var catalogs = new List<OpticalGlassCatalogDocument>(sourcePaths.Length);
foreach (var sourcePath in sourcePaths)
{
    var catalog = await ZemaxAgfCatalogReader.ImportFileAsync(sourcePath);
    catalogs.Add(catalog);
    ExternalGlassCatalogDatabase.Register(catalog);
}

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

await OptilandGlassCatalogStore.SaveBundleAsync(
    new OpticalGlassCatalogBundle
    {
        SourceDescription = $"Zemax Glasscat ({sourcePaths.Length} AGF catalogs)",
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
