using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptilandWorkbench.Core.Materials;

public sealed class OpticalGlassCatalogDocument
{
    public int SchemaVersion { get; set; } = 1;

    public string CatalogName { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public List<string> HeaderComments { get; set; } = new();

    public List<OpticalGlassDefinition> Glasses { get; set; } = new();
}

public sealed class OpticalGlassCatalogBundle
{
    public int SchemaVersion { get; set; } = 1;

    public string SourceDescription { get; set; } = string.Empty;

    public List<OpticalGlassCatalogDocument> Catalogs { get; set; } = new();
}

public sealed class OpticalGlassDefinition
{
    public string Name { get; set; } = string.Empty;

    public int DispersionFormulaNumber { get; set; }

    public string MilNumber { get; set; } = string.Empty;

    public double ReferenceIndexD { get; set; }

    public double ReferenceAbbeNumber { get; set; }

    public bool ExcludeSubstitution { get; set; }

    public int Status { get; set; }

    public int MeltFrequency { get; set; }

    public string Comment { get; set; } = string.Empty;

    public double? ThermalExpansionLow { get; set; }

    public double? ThermalExpansionHigh { get; set; }

    public double? Density { get; set; }

    public double? RelativePartialDispersionDeviation { get; set; }

    public bool IgnoreThermalExpansion { get; set; }

    public List<double> DispersionCoefficients { get; set; } = new();

    public List<double> ThermalCoefficients { get; set; } = new();

    public List<double> MechanicalData { get; set; } = new();

    public List<double> OtherData { get; set; } = new();

    public double MinimumWavelengthMicrometers { get; set; }

    public double MaximumWavelengthMicrometers { get; set; }

    public List<OpticalGlassTransmission> InternalTransmissions { get; set; } = new();

    public List<OpticalGlassStressData> StressData { get; set; } = new();

    public List<string> UnrecognizedRecords { get; set; } = new();
}

public sealed record OpticalGlassTransmission(
    double WavelengthMicrometers,
    double Transmission,
    double ThicknessMillimeters);

public sealed record OpticalGlassStressData(
    double WavelengthMicrometers,
    double StressOpticalCoefficient,
    double NegativeK11,
    double NegativeK12);

public static class ZemaxAgfCatalogReader
{
    private static readonly HashSet<string> RecordNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CC", "NM", "GC", "ED", "CD", "TD", "MD", "OD", "LD", "IT", "BD"
    };

    public static OpticalGlassCatalogDocument Import(string text, string catalogName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        var document = new OpticalGlassCatalogDocument
        {
            CatalogName = NormalizeCatalogName(catalogName)
        };
        OpticalGlassDefinition? current = null;

        foreach (var line in LogicalLines(text, document.HeaderComments))
        {
            var separator = line.IndexOfAny([' ', '\t']);
            var record = (separator < 0 ? line : line[..separator]).ToUpperInvariant();
            var payload = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();
            var tokens = payload.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            switch (record)
            {
                case "CC":
                    document.Comment = payload;
                    break;
                case "NM":
                    current = ReadName(tokens);
                    document.Glasses.Add(current);
                    break;
                case "GC":
                    RequireCurrent(current, record).Comment = payload;
                    break;
                case "ED":
                    ReadExtraData(RequireCurrent(current, record), tokens);
                    break;
                case "CD":
                    RequireCurrent(current, record).DispersionCoefficients = ReadNumbers(tokens);
                    break;
                case "TD":
                    RequireCurrent(current, record).ThermalCoefficients = ReadNumbers(tokens, double.NaN);
                    break;
                case "MD":
                    RequireCurrent(current, record).MechanicalData = ReadNumbers(tokens, double.NaN);
                    break;
                case "OD":
                    RequireCurrent(current, record).OtherData = ReadNumbers(tokens, missingValue: -1);
                    break;
                case "LD":
                    ReadWavelengthRange(RequireCurrent(current, record), tokens);
                    break;
                case "IT":
                    ReadTransmission(RequireCurrent(current, record), tokens);
                    break;
                case "BD":
                    ReadStressData(RequireCurrent(current, record), tokens);
                    break;
                default:
                    if (current is not null)
                    {
                        current.UnrecognizedRecords.Add(line);
                    }
                    else
                    {
                        document.HeaderComments.Add(line);
                    }
                    break;
            }
        }

        if (document.Glasses.Count == 0)
        {
            throw new InvalidDataException("The Zemax AGF catalog does not contain any NM glass records.");
        }

        return document;
    }

    public static async Task<OpticalGlassCatalogDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Import(Decode(bytes), Path.GetFileNameWithoutExtension(path));
    }

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static IEnumerable<string> LogicalLines(string text, ICollection<string> headerComments)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith('!'))
            {
                headerComments.Add(trimmed[1..].Trim());
                continue;
            }

            var record = trimmed.Length >= 2 ? trimmed[..2] : trimmed;
            var startsRecord = RecordNames.Contains(record) ||
                (trimmed.Length > 2 && char.IsLetter(trimmed[0]) && char.IsLetter(trimmed[1]) && char.IsWhiteSpace(trimmed[2]));
            if (startsRecord || lines.Count == 0)
            {
                lines.Add(trimmed);
            }
            else
            {
                lines[^1] = $"{lines[^1]} {trimmed}";
            }
        }

        return lines;
    }

    private static OpticalGlassDefinition ReadName(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 5)
        {
            throw new InvalidDataException("Zemax AGF NM requires at least name, formula, MIL, Nd and Vd.");
        }

        var formula = RequiredInt(tokens[1], "NM formula");
        if (formula is < 1 or > 13)
        {
            throw new NotSupportedException($"Zemax AGF dispersion formula {formula} is not supported.");
        }

        return new OpticalGlassDefinition
        {
            Name = tokens[0],
            DispersionFormulaNumber = formula,
            MilNumber = tokens[2],
            ReferenceIndexD = RequiredDouble(tokens[3], "NM Nd"),
            ReferenceAbbeNumber = RequiredDouble(tokens[4], "NM Vd"),
            ExcludeSubstitution = OptionalInt(tokens, 5) != 0,
            Status = OptionalInt(tokens, 6),
            MeltFrequency = tokens.Count > 7 && !IsMissing(tokens[7]) ? OptionalInt(tokens, 7) : 0
        };
    }

    private static void ReadExtraData(OpticalGlassDefinition glass, IReadOnlyList<string> tokens)
    {
        glass.ThermalExpansionLow = OptionalDouble(tokens, 0);
        glass.ThermalExpansionHigh = OptionalDouble(tokens, 1);
        glass.Density = OptionalDouble(tokens, 2);
        glass.RelativePartialDispersionDeviation = OptionalDouble(tokens, 3);
        glass.IgnoreThermalExpansion = OptionalInt(tokens, 4) != 0;
    }

    private static void ReadWavelengthRange(OpticalGlassDefinition glass, IReadOnlyList<string> tokens)
    {
        glass.MinimumWavelengthMicrometers = RequiredDouble(tokens, 0, "LD minimum wavelength");
        glass.MaximumWavelengthMicrometers = RequiredDouble(tokens, 1, "LD maximum wavelength");
    }

    private static void ReadTransmission(OpticalGlassDefinition glass, IReadOnlyList<string> tokens)
    {
        glass.InternalTransmissions.Add(new OpticalGlassTransmission(
            RequiredDouble(tokens, 0, "IT wavelength"),
            OptionalDouble(tokens, 1) ?? double.NaN,
            OptionalDouble(tokens, 2) ?? double.NaN));
    }

    private static void ReadStressData(OpticalGlassDefinition glass, IReadOnlyList<string> tokens)
    {
        glass.StressData.Add(new OpticalGlassStressData(
            RequiredDouble(tokens, 0, "BD wavelength"),
            RequiredDouble(tokens, 1, "BD K"),
            OptionalDouble(tokens, 2) ?? double.NaN,
            OptionalDouble(tokens, 3) ?? double.NaN));
    }

    private static List<double> ReadNumbers(IReadOnlyList<string> tokens, double missingValue = 0) =>
        tokens.Select(token => IsMissing(token) ? missingValue : RequiredDouble(token, "numeric record")).ToList();

    private static OpticalGlassDefinition RequireCurrent(OpticalGlassDefinition? current, string record) =>
        current ?? throw new InvalidDataException($"Zemax AGF {record} appears before the first NM record.");

    private static int OptionalInt(IReadOnlyList<string> tokens, int index) =>
        index < tokens.Count && !IsMissing(tokens[index]) ? RequiredInt(tokens[index], $"operand {index}") : 0;

    private static double? OptionalDouble(IReadOnlyList<string> tokens, int index) =>
        index < tokens.Count && !IsMissing(tokens[index]) ? RequiredDouble(tokens[index], $"operand {index}") : null;

    private static bool IsMissing(string token) => token is "-" or "_";

    private static int RequiredInt(string token, string field)
    {
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
        {
            return checked((int)floating);
        }

        throw new InvalidDataException($"Zemax AGF {field} value '{token}' is not an integer.");
    }

    private static double RequiredDouble(IReadOnlyList<string> tokens, int index, string field) =>
        index < tokens.Count
            ? RequiredDouble(tokens[index], field)
            : throw new InvalidDataException($"Zemax AGF {field} is missing.");

    private static double RequiredDouble(string token, string field) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"Zemax AGF {field} value '{token}' is not numeric.");

    private static string NormalizeCatalogName(string catalogName) =>
        Path.GetFileNameWithoutExtension(catalogName.Trim()).ToUpperInvariant();
}

public static class OptilandGlassCatalogStore
{
    private static readonly byte[] BundleMagic = "OGDB\u0001\r\n\u001a"u8.ToArray();
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public const string Extension = ".ogcat";

    public const string BundleExtension = ".ogdb";

    public static async Task SaveAsync(
        OpticalGlassCatalogDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<OpticalGlassCatalogDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<OpticalGlassCatalogDocument>(
            stream,
            Options,
            cancellationToken).ConfigureAwait(false);
        if (document is null || document.SchemaVersion != 1 || document.Glasses.Count == 0)
        {
            throw new InvalidDataException($"Optiland glass catalog '{path}' is empty or has an unsupported schema.");
        }

        return document;
    }

    public static OpticalGlassCatalogDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<OpticalGlassCatalogDocument>(stream, Options);
        if (document is null || document.SchemaVersion != 1 || document.Glasses.Count == 0)
        {
            throw new InvalidDataException($"Optiland glass catalog '{path}' is empty or has an unsupported schema.");
        }

        return document;
    }

    public static async Task SaveBundleAsync(
        OpticalGlassCatalogBundle bundle,
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await stream.WriteAsync(BundleMagic, cancellationToken).ConfigureAwait(false);
        await using var compressed = new GZipStream(stream, CompressionLevel.SmallestSize, leaveOpen: false);
        await JsonSerializer.SerializeAsync(compressed, bundle, Options, cancellationToken).ConfigureAwait(false);
    }

    public static OpticalGlassCatalogBundle LoadBundle(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadBundle(stream);
    }

    public static OpticalGlassCatalogBundle LoadBundle(Stream stream)
    {
        Span<byte> magic = stackalloc byte[BundleMagic.Length];
        stream.ReadExactly(magic);
        if (!magic.SequenceEqual(BundleMagic))
        {
            throw new InvalidDataException("The file is not an Optiland glass-catalog database.");
        }

        using var compressed = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        var bundle = JsonSerializer.Deserialize<OpticalGlassCatalogBundle>(compressed, Options);
        if (bundle is null || bundle.SchemaVersion != 1 || bundle.Catalogs.Count == 0 ||
            bundle.Catalogs.Any(catalog => catalog.SchemaVersion != 1 || catalog.Glasses.Count == 0))
        {
            throw new InvalidDataException("The Optiland glass-catalog database is empty or has an unsupported schema.");
        }

        return bundle;
    }
}

public static class ExternalGlassCatalogDatabase
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, OpticalGlassCatalogDocument> Catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Names
    {
        get
        {
            lock (Gate)
            {
                return Catalogs.Values
                    .SelectMany(catalog => catalog.Glasses.Select(glass => $"{catalog.CatalogName}:{glass.Name}"))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public static IReadOnlyList<string> Manufacturers
    {
        get
        {
            lock (Gate)
            {
                return Catalogs.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
    }

    public static void Register(OpticalGlassCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (Gate)
        {
            Catalogs[document.CatalogName] = document;
        }
    }

    public static void RegisterIfMissing(OpticalGlassCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (Gate)
        {
            Catalogs.TryAdd(document.CatalogName, document);
        }
    }

    public static bool TryResolve(
        string name,
        IReadOnlyList<string>? preferredManufacturers,
        out CatalogGlassMaterial material)
    {
        lock (Gate)
        {
            var separator = name.IndexOf(':');
            if (separator > 0 && separator < name.Length - 1 &&
                TryFind(name[..separator], name[(separator + 1)..], out var qualifiedCatalog, out var qualifiedGlass))
            {
                material = CreateMaterial(qualifiedCatalog, qualifiedGlass);
                return true;
            }

            foreach (var preferred in preferredManufacturers ?? Array.Empty<string>())
            {
                if (TryFind(preferred, name, out var preferredCatalog, out var preferredGlass))
                {
                    material = CreateMaterial(preferredCatalog, preferredGlass);
                    return true;
                }
            }

            var matches = Catalogs.Values
                .Select(catalog => (Catalog: catalog, Glass: catalog.Glasses.LastOrDefault(glass =>
                    glass.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
                .Where(match => match.Glass is not null)
                .ToArray();
            if (matches.Length == 1)
            {
                material = CreateMaterial(matches[0].Catalog, matches[0].Glass!);
                return true;
            }
        }

        material = null!;
        return false;
    }

    public static IReadOnlyList<string> MatchingManufacturers(string name)
    {
        lock (Gate)
        {
            return Catalogs.Values
                .Where(catalog => catalog.Glasses.Any(glass => glass.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Select(catalog => catalog.CatalogName)
                .ToArray();
        }
    }

    private static bool TryFind(
        string catalogName,
        string glassName,
        out OpticalGlassCatalogDocument catalog,
        out OpticalGlassDefinition glass)
    {
        var normalized = Path.GetFileNameWithoutExtension(catalogName.Trim());
        if (Catalogs.TryGetValue(normalized, out catalog!))
        {
            glass = catalog.Glasses.LastOrDefault(item =>
                item.Name.Equals(glassName, StringComparison.OrdinalIgnoreCase))!;
            return glass is not null;
        }

        glass = null!;
        return false;
    }

    private static CatalogGlassMaterial CreateMaterial(
        OpticalGlassCatalogDocument catalog,
        OpticalGlassDefinition glass)
    {
        var transmission = glass.InternalTransmissions
            .Where(sample => sample.WavelengthMicrometers > 0 && sample.ThicknessMillimeters > 0 &&
                double.IsFinite(sample.ThicknessMillimeters))
            .OrderBy(sample => sample.WavelengthMicrometers)
            .ToArray();
        var extinction = transmission.Select(sample =>
        {
            var clampedTransmission = Math.Clamp(sample.Transmission, 1e-300, 1);
            return -Math.Log(clampedTransmission) * (sample.WavelengthMicrometers / 1000.0) /
                (4.0 * Math.PI * sample.ThicknessMillimeters);
        }).ToArray();
        return new CatalogGlassMaterial(
            glass.Name,
            catalog.CatalogName,
            $"zemax formula {glass.DispersionFormulaNumber}",
            glass.MinimumWavelengthMicrometers * 1000.0,
            glass.MaximumWavelengthMicrometers * 1000.0,
            glass.DispersionCoefficients,
            extinctionWavelengthsNanometers: transmission.Select(sample => sample.WavelengthMicrometers * 1000.0).ToArray(),
            extinctionCoefficients: extinction,
            zemaxData: glass);
    }
}
