using System.Security.Cryptography;
using System.Text;
using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.Application.Services;

public static class ZemaxStockCatalogReader
{
    private const uint SupportedVersion = 1001;
    private const int RecordHeaderSize = 144;
    private const string ShapeCodes = "?EBPM";

    private static readonly IReadOnlyDictionary<string, string> VendorNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EDMUND OPTICS"] = "Edmund Optics",
            ["THORLABS"] = "Thorlabs",
            ["NEWPORT CORP"] = "Newport",
            ["OPTOSIGMA"] = "OptoSigma",
            ["SIGMA KOKI"] = "Sigma Koki",
            ["CVI MELLES GRIOT"] = "CVI Melles Griot",
            ["ISP OPTICS"] = "ISP Optics",
            ["EKSMA OPTICS"] = "EKSMA Optics",
            ["DAHENG OPTICS"] = "Daheng Optics",
            ["DIAS INFRARED"] = "DIAS Infrared",
            ["ARCHER OPTX"] = "Archer Optx",
            ["BERNHARD HALLE"] = "Bernhard Halle",
            ["BEFORT-WETZLAR"] = "Befort Wetzlar",
            ["LIGHT PATH"] = "LightPath",
            ["LINOS PHOTONICS"] = "LINOS Photonics",
            ["OPTICS FOR RESEARCH"] = "Optics for Research",
            ["QIOptiq_POLYMER"] = "Qioptiq Polymer",
            ["ROSS OPTICAL"] = "Ross Optical",
            ["SPECIAL OPTICS"] = "Special Optics",
            ["FOCUSLIGHT MICROOPTICS"] = "Focuslight MicroOptics"
        };

    private static readonly IReadOnlyDictionary<string, string> VendorUrls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANTERYON"] = "https://www.anteryon.com/",
            ["ARCHER OPTX"] = "https://www.archeroptx.com/",
            ["ASPHERICON"] = "https://www.asphericon.com/",
            ["BEFORT-WETZLAR"] = "https://www.befort-optic.com/",
            ["BERNHARD HALLE"] = "https://www.b-halle.de/",
            ["COMAR"] = "https://www.comaroptics.com/",
            ["CVI MELLES GRIOT"] = "https://www.cvimellesgriot.com/",
            ["DAHENG OPTICS"] = "https://www.cdhcorp.com.cn/",
            ["DIAS INFRARED"] = "https://www.dias-infrared.de/",
            ["DIVERSEOPTICS"] = "https://www.diverseoptics.com/",
            ["EALING"] = "https://www.ealingcatalog.com/",
            ["EDMUND OPTICS"] = "https://www.edmundoptics.com/",
            ["EKSMA OPTICS"] = "https://eksmaoptics.com/",
            ["ESCO"] = "https://www.escoproducts.com/",
            ["FOCUSLIGHT"] = "https://www.focuslight.com/",
            ["FOCUSLIGHT MICROOPTICS"] = "https://www.focuslight.com/",
            ["GELTECH"] = "https://www.lightpath.com/",
            ["ISP OPTICS"] = "https://www.ispoptics.com/",
            ["LIGHT PATH"] = "https://www.lightpath.com/",
            ["LIMO"] = "https://www.focuslight.com/",
            ["LINOS PHOTONICS"] = "https://www.qioptiq-shop.com/",
            ["NEWPORT CORP"] = "https://www.newport.com/",
            ["NSG"] = "https://www.nsg.com/",
            ["OPTICS FOR RESEARCH"] = "https://www.ofr.com/",
            ["OPTOSIGMA"] = "https://www.optosigma.com/",
            ["OPTOTUNE"] = "https://www.optotune.com/",
            ["QIOptiq_POLYMER"] = "https://www.qioptiq.com/",
            ["ROSS OPTICAL"] = "https://www.rossoptical.com/",
            ["RPO"] = "https://www.rpoptics.com/",
            ["SIGMA KOKI"] = "https://www.sigma-koki.com/",
            ["SPECIAL OPTICS"] = "https://www.specialoptics.com/",
            ["THORLABS"] = "https://www.thorlabs.com/",
            ["VIAOPTIC"] = "https://www.viaoptic.de/en/"
        };

    public static IReadOnlyList<CommercialLensEntryDto> ReadDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<CommercialLensEntryDto>();
        }

        var entries = new List<CommercialLensEntryDto>();
        foreach (var path in Directory.EnumerateFiles(directory)
                     .Where(path => Path.GetExtension(path).Equals(".zmf", StringComparison.OrdinalIgnoreCase))
                     .Where(path => StockLensCatalogPolicy.IncludesCatalog(Path.GetFileNameWithoutExtension(path)))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                entries.AddRange(ReadFile(path));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // A damaged or unsupported vendor file must not hide the remaining installed catalogs.
            }
        }

        return entries;
    }

    public static IReadOnlyList<CommercialLensEntryDto> ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: false);
        if (stream.Length < sizeof(uint) || reader.ReadUInt32() != SupportedVersion)
        {
            throw new InvalidDataException("只支持版本 1001 的 Zemax ZMF 库。");
        }

        var catalogKey = Path.GetFileNameWithoutExtension(path);
        var manufacturer = DisplayVendorName(catalogKey);
        var productUrl = VendorUrls.GetValueOrDefault(catalogKey, string.Empty);
        var verifiedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        var entries = new List<CommercialLensEntryDto>();
        var recordIndex = 0;
        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < RecordHeaderSize)
            {
                throw new InvalidDataException($"ZMF 库 {Path.GetFileName(path)} 的记录头不完整。");
            }

            var name = DecodeName(reader.ReadBytes(100));
            var lensVersion = reader.ReadUInt32();
            var elementCount = reader.ReadUInt32();
            var shapeIndex = reader.ReadUInt32();
            var aspheric = reader.ReadUInt32();
            var grin = reader.ReadUInt32();
            var toroidal = reader.ReadUInt32();
            var descriptionLength = reader.ReadUInt32();
            var effectiveFocalLength = reader.ReadDouble();
            var entrancePupilDiameter = reader.ReadDouble();
            if (descriptionLength > stream.Length - stream.Position)
            {
                throw new InvalidDataException($"ZMF 库 {Path.GetFileName(path)} 的记录正文长度无效。");
            }

            stream.Seek(descriptionLength, SeekOrigin.Current);
            if (string.IsNullOrWhiteSpace(name))
            {
                recordIndex++;
                continue;
            }

            var shapeCode = shapeIndex < ShapeCodes.Length
                ? ShapeCodes[(int)shapeIndex].ToString()
                : "?";
            var surfaceType = SurfaceType(aspheric, grin, toroidal);
            entries.Add(new CommercialLensEntryDto(
                StableId(catalogKey, name, recordIndex),
                manufacturer,
                name,
                name,
                "本机 Zemax Stockcat 目录",
                productUrl,
                string.Empty,
                LensType(shapeCode, surfaceType, elementCount),
                shapeCode,
                surfaceType,
                checked((int)elementCount),
                FiniteOrZero(effectiveFocalLength),
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "本机 ZMF 目录记录；未导出处方",
                null,
                $"来自本机 {Path.GetFileName(path)}，目录版本 {lensVersion}；仅读取目录头字段，未解码或复制处方正文。",
                verifiedAt,
                FiniteOrZero(entrancePupilDiameter)));
            recordIndex++;
        }

        return entries;
    }

    private static string DisplayVendorName(string catalogKey) =>
        VendorNames.GetValueOrDefault(catalogKey, catalogKey);

    private static string DecodeName(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        return Encoding.Latin1.GetString(bytes, 0, length >= 0 ? length : bytes.Length).Trim();
    }

    private static string SurfaceType(uint aspheric, uint grin, uint toroidal) =>
        grin > 0 ? "G" : toroidal > 0 ? "T" : aspheric > 0 ? "A" : "S";

    private static string LensType(string shapeCode, string surfaceType, uint elements)
    {
        var surface = surfaceType switch
        {
            "G" => "GRIN",
            "T" => "环曲面",
            "A" => "非球面",
            _ => "球面"
        };
        var shape = shapeCode switch
        {
            "E" => "等曲率",
            "B" => "双面曲率",
            "P" => "平面型",
            "M" => "弯月型",
            _ => "其他形状"
        };
        return $"{surface} · {shape} · {elements} 元件";
    }

    private static string StableId(string catalog, string name, int index)
    {
        var value = Encoding.UTF8.GetBytes($"{catalog}\n{name}\n{index}");
        return $"zemax-zmf-{Convert.ToHexString(SHA256.HashData(value))[..20].ToLowerInvariant()}";
    }

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;
}
