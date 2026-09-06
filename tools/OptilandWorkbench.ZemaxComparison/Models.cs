using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptilandWorkbench.ZemaxComparison;

public enum SupportStatus { Comparable, PartiallyComparable, WorkbenchOnly, ZemaxOnly, UnsupportedByZosApi, NotApplicableToModel, LicenseUnavailable, Failed, AdapterNotImplemented, PhysicalDefinitionMismatch }
public enum Conclusion { Pass, Close, Difference, Incomparable, Skipped, Error }
public enum CaptureStatus { Captured, Skipped, Failed, LicenseUnavailable, TimedOut, Cancelled, ScreenshotOnly }
public enum ResultKind { Scalar, Series1D, Grid2D, TextReport, Image, ComplexField, Scatter }
public sealed record Axis(string Quantity, string Unit);
public sealed record ScalarResult(string Id, double Value, string Unit);
public sealed record Series1DResult(string Id, string DisplayName, double[] X, double[] Y, Axis XAxis, Axis YAxis);
public sealed record Grid2DResult(string Id, double[] X, double[] Y, double?[][] Z, Axis XAxis, Axis YAxis, Axis ValueAxis)
{
    public bool[][] InvalidMask => Z.Select(row => row.Select(v => !v.HasValue || !double.IsFinite(v.Value)).ToArray()).ToArray();
}
public sealed record TextReportResult(Dictionary<string, string> Fields, string RawText, string[] ParsingWarnings);
public sealed record ImageResult(string File, int Width, int Height, string Semantics, string Normalization);
public sealed record ComplexFieldResult(double?[][] Real, double?[][] Imaginary, double?[][] Amplitude, double?[][] Phase, string Unit, string Convention);
public sealed record NumericResult
{
    public List<ScalarResult> Scalars { get; init; } = [];
    public List<Series1DResult> Series { get; init; } = [];
    public List<Grid2DResult> Grids { get; init; } = [];
    public List<TextReportResult> Reports { get; init; } = [];
    public List<ImageResult> Images { get; init; } = [];
    public List<ComplexFieldResult> ComplexFields { get; init; } = [];
    public List<string> Transformations { get; init; } = [];
    public string Semantics { get; init; } = "";
}
public sealed record CanonicalAnalysisRequest
{
    public required string CanonicalAnalysisKey { get; init; }
    public int Configuration { get; init; } = 1;
    public int Field { get; init; } = 1;
    public int Wavelength { get; init; } = 1;
    public int Surface { get; init; } = -1;
    public int PupilSampling { get; init; } = 64;
    public int ImageSampling { get; init; } = 64;
    public int RayCount { get; init; } = 20;
    public int GridSize { get; init; } = 64;
    public double ImageDeltaMicrometers { get; init; } = 0.25;
    public double MaximumFrequency { get; init; } = 50;
    public double FocusMinimum { get; init; } = -0.1;
    public double FocusMaximum { get; init; } = 0.1;
    public string Reference { get; init; } = "ChiefRay";
    public bool Polarization { get; init; }
    public required string Apodization { get; init; }
    public bool DeleteVignetted { get; init; } = true;
    public bool UseRayAiming { get; init; }
    public string Normalization { get; init; } = "NativePhysical";
    public string CoordinateConvention { get; init; } = "LocalSurfaceXY; row=y ascending; column=x ascending";
    public string OutputUnits { get; init; } = "TypedAxes";
    public string SettingsOrigin { get; init; } = "CapturedSettings";
    public required Dictionary<string, string> WorkbenchSettings { get; init; }
    public Dictionary<string, object> ZemaxSettings { get; init; } = [];
    public Dictionary<string, string> ZemaxCfgSettings { get; init; } = [];
    public string WavelengthScope { get; init; } = "Selected";
    public int WavelengthCount { get; init; } = 1;
    public string FieldDefinition { get; init; } = "Angle";
    public double MaximumFieldRadius { get; init; }
    public int SurfaceCount { get; init; }
    public int FieldCount { get; init; } = 1;
    public double[][] DefinedFields { get; init; } = [];
    public string FieldScanDirection { get; init; } = "+y";
    public double PrimaryWavelengthMicrometers { get; init; }
    public string ZemaxSettingsMode { get; init; } = "TypedProperties";
    public string? SourceImagePath { get; init; }
    public string? SourceImageSha256 { get; init; }
    public string Fingerprint => JsonFiles.CanonicalHash(JsonSerializer.SerializeToElement(this, JsonFiles.RequestOptions));
}
public sealed record Tolerances(double Absolute, double Relative, double Nrmse, double CloseNrmse, double MinimumCoverage = 0.95);
public sealed record ComparisonMetric(string Id, string Unit, int Count, double MaxAbsolute, double MeanAbsolute, double Rmse,
    double Nrmse, double MaxRelative, double P50, double P90, double P95, double? Pearson,
    double WorstX, double? WorstY, double Coverage, Conclusion Conclusion,
    Dictionary<string, double?>? Extra = null);
public sealed record AnalysisRun
{
    public required string Key { get; init; }
    public required string Directory { get; init; }
    public SupportStatus Support { get; set; }
    public CaptureStatus ZemaxStatus { get; set; } = CaptureStatus.Skipped;
    public CaptureStatus WorkbenchStatus { get; set; } = CaptureStatus.Skipped;
    public Conclusion Conclusion { get; set; } = Conclusion.Skipped;
    public string Reason { get; set; } = "";
    public CanonicalAnalysisRequest? Request { get; set; }
    public Dictionary<string, Tolerances> Tolerances { get; set; } = [];
    public List<ComparisonMetric> Metrics { get; set; } = [];
    public List<string> Normalizations { get; set; } = [];
    public string ScreenshotStatus { get; set; } = "NotRequested";
    public long ElapsedMilliseconds { get; set; }
}
public static class JsonFiles
{
    public static JsonSerializerOptions Options { get; } = Create();
    public static JsonSerializerOptions RequestOptions { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers = { info => { if (info.Type == typeof(CanonicalAnalysisRequest))
                foreach (var p in info.Properties.Where(p => p.Name == "fingerprint").ToArray()) info.Properties.Remove(p); } }
        };
        return options;
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string CanonicalHash(JsonElement value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(value, writer);
        return Hash(buffer.ToArray());
        static void Write(JsonElement node, Utf8JsonWriter writer)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var p in node.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { writer.WritePropertyName(p.Name); Write(p.Value, writer); }
                writer.WriteEndObject();
            }
            else if (node.ValueKind == JsonValueKind.Array) { writer.WriteStartArray(); foreach (var item in node.EnumerateArray()) Write(item, writer); writer.WriteEndArray(); }
            else node.WriteTo(writer);
        }
    }
    public static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException(path);
    public static string Slug(string key) => string.Join('-', key.ToLowerInvariant().Split(
        [' ', '/', '\\', '.', ':', '<', '>', '"', '|', '?', '*'], StringSplitOptions.RemoveEmptyEntries));
}
