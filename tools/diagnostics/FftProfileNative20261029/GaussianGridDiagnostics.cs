using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;

internal static class GaussianGridDiagnostics
{
    internal static int Run(string repository, string capture, string output, bool pairedUniform = false)
    {
        if (File.Exists(output)) throw new IOException("Diagnostic output exists");
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        var summaries = new List<object>();
        foreach (var configuration in new[] { 1, 2 })
        {
            JsonElement Frozen(string kind)
            {
                if (pairedUniform)
                    return JsonFiles.Read<JsonElement>(Path.Combine(capture, $"binocular-c{configuration}-uniform-{kind}/data.json")).GetProperty("dataGrids")[0];
                using var stream = zip.GetEntry($"artifacts/validation/afocal-fft-energy-20261027/native-binocular/binocular-c{configuration}-{kind}/data.json")!.Open();
                using var document = JsonDocument.Parse(stream);
                return document.RootElement.GetProperty("dataGrids")[0].Clone();
            }
            JsonElement Gaussian(string kind) => JsonFiles.Read<JsonElement>(Path.Combine(capture, $"binocular-c{configuration}-gaussian-{kind}/data.json")).GetProperty("dataGrids")[0];
            var uniform = Reconstruct(Frozen("Real"), Frozen("Imaginary"));
            var gaussian = Reconstruct(Gaussian("Real"), Gaussian("Imaginary"));
            var referenceX = -Math.Log(gaussian[32, 33].Magnitude / uniform[32, 33].Magnitude);
            var referenceY = -Math.Log(gaussian[33, 32].Magnitude / uniform[33, 32].Magnitude);
            var points = new List<object>();
            var axis = new List<object>();
            for (var y = -15; y <= 15; y++)
                for (var x = -15; x <= 15; x++)
                {
                    if (uniform[y + 32, x + 32].Magnitude < .5) continue;
                    var ratio = gaussian[y + 32, x + 32].Magnitude / uniform[y + 32, x + 32].Magnitude;
                    var radiusSquared = -Math.Log(ratio);
                    var circular = (x * x + y * y) / 225d;
                    points.Add(new { x, y, ratio, radiusSquared, circular, difference = radiusSquared - circular });
                    if ((x == 0 || y == 0) && (Math.Abs(x + y) % 2 == 1))
                    {
                        var reference = x == 0 ? referenceY : referenceX;
                        double? stretch = Math.Abs(x + y) == 1 ? null : Math.Sqrt((radiusSquared - reference) / (circular - 1 / 225d));
                        axis.Add(new { x, y, radiusSquared, normalizationReference = reference, inferredCoordinateStretch = stretch });
                    }
                }
            summaries.Add(new { configuration, axis, points });
            Console.WriteLine(JsonSerializer.Serialize(new { configuration, axis }, JsonFiles.Options));
        }
        JsonFiles.Write(output, summaries);
        return 0;
    }
    private static Complex[,] Reconstruct(JsonElement real, JsonElement imaginary)
    {
        var values = new Complex[64, 64];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                values[(y + 33) % 64, (x + 33) % 64] = Complex.Conjugate(new Complex(real.GetProperty("values")[y][x].GetDouble(), imaginary.GetProperty("values")[y][x].GetDouble()));
        typeof(DiffractionEngine).GetMethod("FourierTransform2D", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [values, true]);
        var center = values[0, 0];
        var result = new Complex[64, 64];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++) result[y, x] = values[(y + 32) % 64, (x + 32) % 64] / center;
        return result;
    }
}
