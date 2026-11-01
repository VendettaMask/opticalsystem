using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

if (args.Length != 2) throw new ArgumentException("Repository and fresh output directory required");
var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Fresh output required");
Directory.CreateDirectory(output);
var source = Path.Combine(root, "artifacts/validation/afocal-huygens-20261030-native/disk.ZMX");
var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
if (!optic.ImageSpaceAfocal) throw new InvalidDataException("Afocal source required");
var rows = new List<object>();
foreach (var n in new[] { 32, 64, 128 })
{
    using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, $"artifacts/validation/afocal-huygens-20261030-native-02/disk-n{n}/data.json")));
    var grid = native.RootElement.GetProperty("dataGrids")[0]; var values = grid.GetProperty("values");
    if (grid.GetProperty("nx").GetInt32() != 32 || grid.GetProperty("ny").GetInt32() != 32
        || grid.GetProperty("dx").GetDouble() != .02 || grid.GetProperty("dy").GetDouble() != .02
        || grid.GetProperty("minX").GetDouble() != -.32 || grid.GetProperty("minY").GetDouble() != -.32)
        throw new InvalidDataException("Native diagnostic grid differs from the declared independent window");
    var psf = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], n, 32, .02);
    var gaussian = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], n, 32, .02,
        AfocalHuygensIntegration.GaussianPupil);
    double nativeError = 0, coreError = 0, nativeDifference = 0;
    double gaussianError = 0;
    for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
    {
        var radius = .02 * Math.Sqrt((x - 16) * (x - 16) + (y - 16) * (y - 16));
        var v = Math.PI * 2 * radius / .55;
        var expected = v == 0 ? 1 : Math.Pow(2 * J1(v) / v, 2);
        var reference = values[y][x].GetDouble(); var actual = psf.Values[y, x] / 100;
        nativeError = Math.Max(nativeError, Math.Abs(reference - expected));
        coreError = Math.Max(coreError, Math.Abs(actual - expected));
        nativeDifference = Math.Max(nativeDifference, Math.Abs(actual - reference));
        gaussianError = Math.Max(gaussianError, Math.Abs(gaussian.Values[y, x] / 100 - expected));
    }
    rows.Add(new { n, nativeAiryMaximumAbsolute = nativeError, coreAiryMaximumAbsolute = coreError,
        nativeMaximumAbsoluteDifference = nativeDifference, gaussianAiryMaximumAbsolute = gaussianError,
        gaussian.PupilQuadratureNodeCount, gaussianModeCountedAsNativePass = false });
    Console.WriteLine(JsonSerializer.Serialize(rows[^1]));
}
var json = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(rows, json));
var wideWindow = new List<object>();
foreach (var n in new[] { 8, 16, 32, 64 })
{
    var result = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], n, 32, .08,
        AfocalHuygensIntegration.GaussianPupil);
    var maximum = 0d;
    for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
    {
        var v = Math.PI * 2 * .08 * Math.Sqrt((x - 16) * (x - 16) + (y - 16) * (y - 16)) / .55;
        var expected = v == 0 ? 1 : Math.Pow(2 * J1(v) / v, 2);
        maximum = Math.Max(maximum, Math.Abs(result.Values[y, x] / 100 - expected));
    }
    wideWindow.Add(new { n, pitchMilliradians = .08, maximumAiryAbsoluteError = maximum,
        strictContinuumGate = 1e-10, meetsStrictContinuumGate = maximum <= 1e-10,
        result.PupilQuadratureNodeCount, diagnosticOnly = true });
}
File.WriteAllText(Path.Combine(output, "wide-window.json"), JsonSerializer.Serialize(wideWindow, json));
File.WriteAllText(Path.Combine(output, "identity.json"), JsonSerializer.Serialize(new { coreSha256 = Hash(typeof(Optic).Assembly.Location), sourceSha256 = Hash(source), foregroundDesktopUsed = false, nativeControlsCountedAsFormalPass = false }, json));
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static double J1(double z)
{
    var term = z / 2; var sum = term;
    for (var k = 1; k < 100; k++) { term *= -z * z / (4 * k * (k + 1)); sum += term; if (Math.Abs(term) < 1e-17) break; }
    return sum;
}
