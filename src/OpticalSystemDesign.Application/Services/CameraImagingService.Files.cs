using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    internal const string SensorFormat = CameraSensorProfile.FormatIdentifier;
    private const int MaximumSensorFileBytes = 1024 * 1024;
    private const string SpectralScope = "CameraLabV2-QE: three discrete input bands at 650/550/450 nm; "
        + "imported absolute total QE including CFA, linearly interpolated without extrapolation; "
        + "each CFA type sums electrons from all input bands. Scalar R/G/B QE is replaced, not multiplied. "
        + "User-specified per-band image-plane irradiance; sRGB input; no continuous scene spectrum reconstruction, "
        + "spectral quadrature, pixel-area integration, angular QE, measured calibration verification or vendor ISP. "
        + "Not an ImSym/Speos numerical comparison or EMVA certification.";
    private static readonly JsonSerializerOptions SensorJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public async Task<CameraSensorProfile> ImportSensorAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, MaximumSensorFileBytes, "Camera sensor profile", cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = bytes.AsMemory();
        if (payload.Length >= 3 && payload.Span[0] == 0xEF && payload.Span[1] == 0xBB && payload.Span[2] == 0xBF) payload = payload[3..];
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            CheckDuplicateProperties(document.RootElement);
            RequireProperties(document.RootElement, new[] { "Format", "SchemaVersion", "Name", "Source", "Sensor" });
            var sensor = Property(document.RootElement, "Sensor");
            RequireProperties(sensor, JsonSerializer.SerializeToElement(new CameraSensorParameters(), SensorJsonOptions)
                .EnumerateObject().Select(p => p.Name));
            if (TryProperty(document.RootElement, "SpectralResponse", out var response) && response.ValueKind != JsonValueKind.Null)
                RequireCurveProperties(response);
            var profile = JsonSerializer.Deserialize<CameraSensorProfile>(payload.Span, SensorJsonOptions)
                ?? throw new InvalidDataException("传感器参数文件为空。");
            return ValidateProfile(profile) with { Origin = Origin(path, bytes) };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("传感器参数导入失败：请检查格式、完整字段和单位。" + ex.Message, ex); }
    }

    public async Task<CameraResponseCurve> ImportResponseAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, MaximumSensorFileBytes, "Camera spectral response", cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var samples = new List<CameraResponseSample>(); var headerSeen = false; var scale = 1d;
        foreach (var row in CameraSpectralCsvReader.Read(bytes, cancellationToken))
        {
            if (!headerSeen)
            {
                scale = row.Header switch
                {
                    "wavelength_nm,qe_r,qe_g,qe_b" => 1,
                    "wavelength_nm,qe_r_percent,qe_g_percent,qe_b_percent" => 0.01,
                    _ => throw new InvalidDataException("CSV 表头应为 wavelength_nm,qe_r,qe_g,qe_b；百分数请使用 qe_r_percent,qe_g_percent,qe_b_percent。")
                };
                headerSeen = true; continue;
            }
            if (samples.Count >= SensorSpectralResponse.MaximumSamples || row.Cells.Length != 4)
                throw new InvalidDataException($"CSV 第 {row.LineNumber} 行：需要四列，最多 4096 个采样点。");
            if (!row.TryNumbers(out var numbers, requireFinite: true))
                throw new InvalidDataException($"CSV 第 {row.LineNumber} 行：请使用有限数值和小数点。");
            samples.Add(new(numbers[0], numbers[1] * scale, numbers[2] * scale, numbers[3] * scale));
        }
        try
        {
            return FreezeResponse(new(Path.GetFileNameWithoutExtension(path),
                scale == 1 ? "CSV absolute QE, fraction" : "CSV absolute QE, percent converted to fraction",
                samples, Origin(path, bytes)))!;
        }
        catch (ArgumentException ex) { throw new InvalidDataException("光谱响应导入失败：" + ex.Message, ex); }
    }

    public Task SaveSensorAsync(CameraSensorProfile profile, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        profile = ValidateProfile(profile);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, SensorJsonOptions);
        return BoundedFile.WriteAllBytesAtomicAsync(path, bytes, MaximumSensorFileBytes, "Camera sensor profile", cancellationToken);
    }

    private static CameraSensorProfile ValidateProfile(CameraSensorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Format != SensorFormat || profile.SchemaVersion != 1)
            throw new InvalidDataException("不支持的传感器格式或版本；请使用参数模板。");
        Label(profile.Name, 256, "Sensor name", allowEmpty: false); Label(profile.Source, 2048, "Sensor source");
        ValidateOrigin(profile.Origin);
        if (profile.Sensor is null) throw new InvalidDataException("传感器参数不能为空。");
        CameraSensorPipeline.Validate(ToCore(profile.Sensor), new());
        return profile with { SpectralResponse = FreezeResponse(profile.SpectralResponse) };
    }

    private static CameraResponseCurve? FreezeResponse(CameraResponseCurve? curve)
    {
        if (curve is null) return null;
        Label(curve.Name, 256, "Curve name", allowEmpty: false); Label(curve.Source, 2048, "Curve source");
        ValidateOrigin(curve.Origin);
        if (curve.Samples is null || curve.Samples.Count > SensorSpectralResponse.MaximumSamples)
            throw new ArgumentException("QE 曲线采样为空或过多。");
        var samples = curve.Samples.ToArray();
        var core = new SensorSpectralResponse(samples.Select(x => x is null
            ? throw new ArgumentException("QE curve contains a null sample.")
            : new SensorResponseSample(x.WavelengthNanometers, x.RedQe, x.GreenQe, x.BlueQe)));
        return curve with { Samples = Array.AsReadOnly(samples) };
    }

    private static SensorSpectralResponse? ToCore(CameraResponseCurve? curve) => curve is null ? null
        : new(curve.Samples.Select(x => new SensorResponseSample(x.WavelengthNanometers, x.RedQe, x.GreenQe, x.BlueQe)));

    private static string ResponseCsv(CameraResponseCurve curve)
    {
        var output = new StringBuilder("wavelength_nm,qe_r,qe_g,qe_b\n");
        foreach (var sample in curve.Samples)
            output.Append(CultureInfo.InvariantCulture, $"{sample.WavelengthNanometers:G17},{sample.RedQe:G17},{sample.GreenQe:G17},{sample.BlueQe:G17}\n");
        return output.ToString();
    }

    private static CameraFileOrigin Origin(string path, byte[] bytes) => new(Path.GetFileName(path), Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private static void ValidateOrigin(CameraFileOrigin? origin)
    {
        if (origin is null) return;
        Label(origin.FileName, 256, "Origin filename", allowEmpty: false);
        if (origin.Sha256 is null || origin.Sha256.Length != 64 || origin.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Origin SHA-256 must contain 64 hexadecimal characters.");
    }
    private static void Label(string? text, int maximum, string name, bool allowEmpty = true)
    { if (text is null || text.Length > maximum || !allowEmpty && string.IsNullOrWhiteSpace(text)) throw new ArgumentException(name + " is invalid."); }
    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        value = default; return false;
    }
    private static JsonElement Property(JsonElement element, string name) => TryProperty(element, name, out var value)
        ? value : throw new InvalidDataException("缺少字段：" + name);
    private static void RequireProperties(JsonElement element, IEnumerable<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("参数对象格式错误。");
        foreach (var name in names) _ = Property(element, name);
    }
    private static void RequireCurveProperties(JsonElement curve)
    {
        RequireProperties(curve, new[] { "Name", "Source", "Samples" });
        var samples = Property(curve, "Samples");
        if (samples.ValueKind != JsonValueKind.Array || samples.GetArrayLength() > SensorSpectralResponse.MaximumSamples)
            throw new InvalidDataException("光谱采样数组无效或过大。");
        foreach (var sample in samples.EnumerateArray())
            RequireProperties(sample, new[] { "WavelengthNanometers", "RedQe", "GreenQe", "BlueQe" });
    }
    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException("JSON 字段重复：" + property.Name);
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) CheckDuplicateProperties(item);
    }
}
