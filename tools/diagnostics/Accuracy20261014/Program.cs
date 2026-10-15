using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Optimization;

if (args.Length != 3) throw new ArgumentException("Usage: <repository> <capture-manifest.json> <report.json>");
var root = Path.GetFullPath(args[0]);
using var manifest = JsonDocument.Parse(File.ReadAllText(args[1]));
var casesElement = manifest.RootElement.ValueKind == JsonValueKind.Array ? manifest.RootElement : manifest.RootElement.GetProperty("Cases");
var cases = casesElement.Deserialize<Case[]>()!;
var records = new List<object>();
foreach (var item in cases)
{
    var path = Path.Combine(root, item.Source);
    var bytes = File.ReadAllBytes(path);
    var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
    if (hash != item.SourceSha256) throw new InvalidDataException("Source identity mismatch: " + item.Name);
    var optic = OpticalFormatCatalog.Import(File.ReadAllText(path), ".zmx");
    using var nativeEnvironment = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, item.Capture, "environment.json")));
    var environment = nativeEnvironment.RootElement;
    if (!environment.GetProperty("validLicense").GetBoolean() || environment.GetProperty("major").GetInt32() != 26
        || environment.GetProperty("minor").GetInt32() != 1) throw new InvalidDataException("Unlicensed/wrong-version capture");
    var values = new List<Comparison>();
    foreach (var file in new[] { "curvature-operands.json", "operands.json", "signed-curvature-operands.json" })
    {
        if (!File.Exists(Path.Combine(root, item.Capture, file))) continue;
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, item.Capture, file)));
        foreach (var row in capture.RootElement.EnumerateArray())
        {
            var code = row.GetProperty("code").GetString()!;
            var wave = row.GetProperty("wave").GetInt32();
            var curvature = code is "FCGS" or "FCGT";
            var density = curvature ? 0 : row.GetProperty("density").GetInt32();
            var field = curvature ? 0 : row.GetProperty("field").GetInt32();
            var hy = curvature ? row.GetProperty("hy").GetDouble() : 0;
            var hx = curvature ? row.GetProperty("hx").GetDouble() : 0;
            var definition = new MeritOperandDefinition
            {
                Type = code,
                Enabled = true,
                Wavelength = wave,
                ZemaxIntegerParameters = [density, wave],
                ZemaxDataParameters = curvature ? [hx, hy, 0, 0] : [field, 0, 0, 0],
                Target = 0,
                Weight = 1
            };
            var result = MeritFunctionCatalog.Evaluate(optic, definition);
            var expected = row.GetProperty("value").GetDouble();
            values.Add(new(code, wave, density, field, hx, hy, expected, result.Error is null or "" ? result.Value : null,
                result.Error, result.Error is null or "" ? Math.Abs(result.Value - expected) : null,
                (result.Error is null or "") && expected != 0 ? Math.Abs(result.Value / expected - 1) : null));
        }
    }
    var summary = values.GroupBy(value => value.Code).Select(group => new
    {
        Code = group.Key,
        Total = group.Count(),
        Computable = group.Count(value => value.Actual is not null),
        MaxAbsoluteError = group.Max(value => value.AbsoluteError),
        MaxRelativeError = group.Max(value => value.RelativeError),
        ComparisonTarget = group.Key is "FCGS" or "FCGT" ? (double?)1e-5 : null,
        Status = group.Any(value => value.Actual is null) ? "Unavailable"
            : group.Key is "FCGS" or "FCGT" && group.Max(value => value.AbsoluteError) <= 1e-5 ? "Pass" : "Difference"
    }).ToArray();
    Console.WriteLine(item.Name + ": " + JsonSerializer.Serialize(summary));
    records.Add(new
    {
        item.Name,
        Source = item.Source,
        SourceSha256 = hash,
        NativeEnvironment = environment.Clone(),
        Capture = item.Capture,
        Summary = summary,
        Comparisons = values
    });
}
File.WriteAllText(args[2], JsonSerializer.Serialize(new
{
    CapturedUtc = DateTimeOffset.UtcNow,
    Scope = "Offline current Core vs explicit native MFE controls; no product runtime dependency",
    Cases = records
}, new JsonSerializerOptions { WriteIndented = true }));

internal sealed record Case(string Name, string Source, string SourceSha256, string Capture);
internal sealed record Comparison(string Code, int Wave, int Density, int Field, double Hx, double Hy, double Native, double? Actual,
    string? Error, double? AbsoluteError, double? RelativeError);
