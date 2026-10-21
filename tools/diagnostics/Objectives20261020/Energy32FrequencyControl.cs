using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class Energy32FrequencyControl
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        using var controls = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives32-repair-20261021/coordinate-audit-v4/inputs.json"));
        var rows = new List<object>();
        foreach (var control in controls.RootElement.EnumerateArray())
        {
            var lens = control.GetProperty("lens").GetString()!;
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
            var wave = optic.Wavelengths[request.Wavelength - 1];
            var reference = optic.Wavelengths.First(w => w.IsPrimary);
            var engine = typeof(DiffractionEngine);
            object?[] probes = [optic, (0d, 0d), wave, optic.RayAimingEnabled, 1d, null];
            engine.GetMethod("TryWorkingPupilDirections", Static)!.Invoke(null, probes);
            var directions = (Vector3D[])probes[5]!;
            var chief = directions[0];
            var na = directions.Skip(1).Select(d => Math.Sin(Math.Acos(chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z))).ToArray();
            var rms = (double)engine.GetMethod("CumulativeEnergyRootMeanSquare", Static)!.Invoke(null, [na])!;
            var f = .5 / rms;
            var lambda = wave.Nanometers * 1e-6;
            var pitch = lambda / Math.Sqrt(32) * f;
            var sx = 1 / (32 * pitch * (1 / (lambda * (.5 / ((na[2] + na[3]) / 2)))));
            var sy = 1 / (32 * pitch * (1 / (lambda * (.5 / ((na[0] + na[1]) / 2)))));
            var grid = new PupilGridSpecification(32, true, true, sx * 15);
            typeof(PupilGridSpecification).GetProperty("ExplicitCoordinateStep", Instance)!.SetValue(grid, sx);
            typeof(PupilGridSpecification).GetProperty("ExplicitCoordinateStepY", Instance)!.SetValue(grid, sy);
            typeof(PupilGridSpecification).GetProperty("UseCachedFftStopMap", Instance)!.SetValue(grid, true);
            var sphere = engine.GetMethod("CumulativeEnergyReferenceSphere", Static)!.Invoke(null, [optic, (0d, 0d), reference]);
            var native = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json"), AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey), request);
            var analysis = new DiffractionEncircledEnergyAnalysis(optic, 32, 64, 401, request.Wavelength, request.Field, maximumDistanceMicrometers: 5);
            foreach (var ideal in new[] { true, false })
            {
                object?[] args = [optic, (0d, 0d), wave, 32, 64, false, true, 0d, null, null, true,
                    ideal, optic.RayAimingEnabled, reference, pitch * 1000, true, null, sphere, grid];
                var psf = (PsfResult)engine.GetMethod("ComputeFftPsfCore", Static)!.Invoke(null, args)!;
                var pupil = (Complex[,])args[16]!;
                var curve = EnergyGridControls.Curve(analysis, psf, pitch * 1000);
                var expected = native.Series[ideal ? 0 : 1].Y;
                var max = curve.Zip(expected, (a, b) => Math.Abs(a.Y - b)).Max();
                var nrmse = Math.Sqrt(curve.Zip(expected, (a, b) => Math.Pow(a.Y - b, 2)).Average()) / (expected.Max() - expected.Min());
                rows.Add(new { lens, ideal, pupilCount = pupil.Cast<Complex>().Count(c => c.Magnitude > 0), max, nrmse, sx, sy, f, pitchMicrometers = pitch * 1000 });
                Console.WriteLine($"{lens} ideal={ideal}: pupil={pupil.Cast<Complex>().Count(c => c.Magnitude > 0)}, max={max:G5}, NRMSE={nrmse:G5}");
            }
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
    }
}
