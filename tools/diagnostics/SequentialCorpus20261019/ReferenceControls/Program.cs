using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Optimization;

if (args.Length != 3) throw new ArgumentException("<original-yolo-copy> <native-control-json> <fresh-output-json>");
if (File.Exists(args[2])) throw new IOException("Use a fresh control output.");
var optic = OpticalFormatCatalog.Import(File.ReadAllText(args[0]), ".zmx");
var sourceRows = File.ReadLines(args[0]).Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
    .Where(tokens => tokens.Length > 2 && tokens[0] is "REAX" or "REAY").ToArray();
var rows = optic.MeritFunctionOperands.Where(row => row.Type is "REAX" or "REAY").ToArray();
if (sourceRows.Length != 4 || rows.Length != 4) throw new InvalidDataException("Expected the four original Yolo merit rows.");
using var native = JsonDocument.Parse(File.ReadAllText(args[1]));
var results = new List<object>();
foreach (var item in native.RootElement.GetProperty("rows").EnumerateArray())
{
    var code = item.GetProperty("code").GetString()!;
    var source = item.GetProperty("surface").GetInt32();
    var index = Array.FindIndex(sourceRows, tokens => tokens[0] == code && int.Parse(tokens[1], CultureInfo.InvariantCulture) == source);
    if (index < 0) throw new InvalidDataException("Native row not in original merit table.");
    var row = rows[index].Clone();
    row.Px = item.GetProperty("px").GetDouble();
    row.Py = item.GetProperty("py").GetDouble();
    row.ZemaxDataParameters[2] = row.Px;
    row.ZemaxDataParameters[3] = row.Py;
    var actual = MeritFunctionCatalog.Evaluate(optic, row);
    var referenceEligible = item.TryGetProperty("error", out var error) && error.GetInt32() == 0
        && item.GetProperty("vignette").GetInt32() == 0 && item.GetProperty("referenceConsistent").GetBoolean();
    double? expected = item.GetProperty("value").ValueKind == JsonValueKind.Number ? item.GetProperty("value").GetDouble() : null;
    double? delta = expected.HasValue && double.IsFinite(actual.Value) ? Math.Abs(actual.Value - expected.Value) : null;
    var tolerance = expected.HasValue ? 1e-6 + 1e-6 * Math.Abs(expected.Value) : (double?)null;
    results.Add(new { code, sourceSurface = source, coreSurface = row.Surface, row.Px, row.Py, expected,
        actual = double.IsFinite(actual.Value) ? actual.Value : (double?)null, delta, tolerance,
        referenceEligible,
        conclusion = !referenceEligible ? "Incomparable" : delta.HasValue ? delta <= tolerance ? "Pass" : "Difference" : "Error", actual.Error });
}
File.WriteAllText(args[2], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { controls = results.Count, output = args[2] }));
