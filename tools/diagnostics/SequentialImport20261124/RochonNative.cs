using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;

internal static class RochonNative
{
    internal static async Task Check(string repository, string output)
    {
        repository = Path.GetFullPath(repository);
        var source = Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Birefringent prisms/Rochon prism.zmx");
        var authority = Path.Combine(repository, "artifacts/zemax-sequential-corpus/20261021/full-v1");
        var frozenSource = Path.Combine(authority, "sources/Birefringent prisms/Rochon prism.zmx");
        static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        if (Hash(source) != Hash(frozenSource)) throw new InvalidDataException("Source bytes differ from the native authority.");
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(source);
        var policyPath = Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
        using var policy = JsonDocument.Parse(File.ReadAllText(policyPath));
        var quantities = policy.RootElement.GetProperty("analyses").GetProperty("Single Ray Trace").GetProperty("quantities");
        var coordinateLimit = quantities.GetProperty("Coordinate").GetProperty("absolute").GetDouble();
        var directionLimit = quantities.GetProperty("DirectionCosine").GetProperty("absolute").GetDouble();
        var cases = new List<object>();
        var allPassed = true;
        foreach (var directory in Directory.GetDirectories(Path.Combine(authority, "analyses"), "*rochon*single-ray-trace"))
        {
            var requestPath = Path.Combine(directory, "native/request.json");
            var reportPath = Path.Combine(directory, "native/data.txt");
            using var request = JsonDocument.Parse(File.ReadAllText(requestPath));
            var settings = request.RootElement.GetProperty("workbenchSettings");
            if (!settings.GetProperty("GlobalCoordinates").GetString()!.Equals("false", StringComparison.OrdinalIgnoreCase)
                || request.RootElement.GetProperty("polarization").GetBoolean()) throw new InvalidDataException("Unexpected native coordinate/polarization convention.");
            double Setting(string key) => double.Parse(settings.GetProperty(key).GetString()!, CultureInfo.InvariantCulture);
            var configuration = request.RootElement.GetProperty("configuration").GetInt32();
            var optic = imported.Configurations[configuration - 1];
            var waveIndex = (int)Setting("WavelengthNumber") - 1;
            var rays = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(Setting("Hx"), Setting("Hy"), Setting("Px"), Setting("Py"),
                optic.Wavelengths[waveIndex].Micrometers, false);
            using var trace = optic.SequentialRayTracer.Trace(rays, TraceRequest.FullHistory());
            var rows = new List<object>();
            var maxPosition = 0d; var maxDirection = 0d; var maxLength = 0d;
            var report = File.ReadAllText(reportPath).Split("近轴光线追迹数据:", 2)[0];
            foreach (var line in report.Split('\n').Where(line => Regex.IsMatch(line, @"^\s*\d+\s")))
            {
                var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 12) throw new InvalidDataException("Unexpected native ray report row.");
                var number = int.Parse(tokens[0], CultureInfo.InvariantCulture);
                var surface = Array.IndexOf(imported.SourceSurfaceNumbers[configuration - 1].ToArray(), number);
                if (!trace.TryGetSample(0, surface, out var sample) || sample.Vignetted) throw new InvalidDataException("Current ray is unavailable.");
                var frame = optic.SurfaceGroup.Items[surface].CoordinateSystem;
                var p = frame.ToLocalPoint(sample.Position); var d = frame.ToLocalDirection(sample.Direction);
                var actual = new[] { p.X, p.Y, p.Z, d.X, d.Y, d.Z };
                var expected = tokens.Skip(1).Take(6).Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
                var differences = actual.Zip(expected, (a, e) => Math.Abs(a - e)).ToArray();
                maxPosition = Math.Max(maxPosition, differences.Take(3).Max());
                maxDirection = Math.Max(maxDirection, differences.Skip(3).Max());
                var expectedLength = double.Parse(tokens[11], CultureInfo.InvariantCulture);
                var lengthDelta = number == 1 ? 0 : Math.Abs(sample.SegmentLength - expectedLength);
                maxLength = Math.Max(maxLength, lengthDelta);
                rows.Add(new { Surface = number, Actual = actual, Native = expected, PositionDelta = differences.Take(3).Max(),
                    DirectionDelta = differences.Skip(3).Max(), ActualSegmentLength = sample.SegmentLength, NativeSegmentLength = expectedLength, LengthDelta = lengthDelta });
            }
            if (rows.Count != 9) throw new InvalidDataException("The full nine-surface native ray report is required.");
            var passed = maxPosition <= coordinateLimit && maxDirection <= directionLimit && maxLength <= coordinateLimit;
            allPassed &= passed;
            cases.Add(new { Id = Path.GetFileName(directory), Configuration = configuration, WaveIndex = waveIndex + 1,
                WorkbenchSettings = settings.Clone(), NativeRequestSha256 = Hash(requestPath), NativeReportSha256 = Hash(reportPath),
                NativeEnvironmentSha256 = Hash(Path.Combine(directory, "native/environment.json")),
                MaximumPositionDelta = maxPosition, MaximumDirectionDelta = maxDirection, MaximumSegmentLengthDelta = maxLength, Passed = passed, Rows = rows });
        }
        if (cases.Count != 8) throw new InvalidDataException("All eight Rochon single-ray captures are required.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Scope = "Existing 2026 R1 captures; local positions, energy directions and geometric segment lengths only. No recapture or complete analysis certification.",
            Authority = "artifacts/zemax-sequential-corpus/20261021/full-v1", SourceSha256 = Hash(source), PolicySha256 = Hash(policyPath), PositionAbsoluteLimit = coordinateLimit, DirectionAbsoluteLimit = directionLimit,
            CoreAssemblySha256 = Hash(typeof(ZemaxZmxImporter).Assembly.Location),
            Cases = cases, Passed = allPassed }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Rochon native ray comparison: {cases.Count} captures, passed={allPassed}");
        if (!allPassed) throw new InvalidDataException("Native ray comparison failed; see retained differences.");
    }
}
