using System.Reflection;
using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;

// Inverse identification of the native image plane, strictly offline.
// A fitted reference quantity is never supplied to runtime or acceptance.
internal static class Energy32PlaneAudit
{
    private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        using var coordinates = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives32-repair-20261021/coordinate-audit-v4/inputs.json"));
        var rows = new List<object>();
        foreach (var control in coordinates.RootElement.EnumerateArray())
        {
            var lens = control.GetProperty("lens").GetString()!;
            var expectedCount = control.GetProperty("expected").GetInt32();
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
            var wave = optic.Wavelengths[request.Wavelength - 1];
            var engine = typeof(DiffractionEngine);
            var reference = optic.Wavelengths.First(w => w.IsPrimary);
            var sphere = engine.GetMethod("CumulativeEnergyReferenceSphere", Static)!.Invoke(null, [optic, (0d, 0d), reference]);
            var grid = new PupilGridSpecification(32, true, true, 1);
            var step = expectedCount == 101 ? 1 / Math.Sqrt(32) : Math.BitIncrement(1 / Math.Sqrt(32));
            typeof(PupilGridSpecification).GetProperty("ExplicitCoordinateStep", Instance)!.SetValue(grid, step);
            var f = DiffractionEngine.WorkingFNumber(optic, (0, 0), wave, optic.RayAimingEnabled);
            var pitch = wave.Micrometers * .001 / Math.Sqrt(32) * f * 1000;
            object?[] args = [optic, (0d, 0d), wave, 32, 64, false, true, 0d, null, null, true,
                true, optic.RayAimingEnabled, reference, pitch, true, null, sphere, grid];
            var psf = (PsfResult)engine.GetMethod("ComputeFftPsfCore", Static)!.Invoke(null, args)!;
            var pupil = (Complex[,])args[16]!;
            var actualCount = pupil.Cast<Complex>().Count(c => c.Magnitude > 0);
            if (actualCount != expectedCount) throw new InvalidOperationException("Unexpected pupil support.");
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var native = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json"), entry, request);
            var expected = native.Series[0].Y;
            var analysis = new DiffractionEncircledEnergyAnalysis(optic, 32, 64, 401, request.Wavelength, request.Field, maximumDistanceMicrometers: 5);
            var initial = pitch;
            for (var iteration = 0; iteration < 5; iteration++)
            {
                var actual = EnergyGridControls.Curve(analysis, psf, pitch).Select(p => p.Y).ToArray();
                var h = pitch * 1e-6;
                var plus = EnergyGridControls.Curve(analysis, psf, pitch + h).Select(p => p.Y).ToArray();
                var minus = EnergyGridControls.Curve(analysis, psf, pitch - h).Select(p => p.Y).ToArray();
                var derivatives = plus.Zip(minus, (a, b) => (a - b) / (2 * h)).ToArray();
                var delta = derivatives.Zip(actual.Zip(expected, (a, b) => b - a), (d, e) => d * e).Sum() / derivatives.Sum(d => d * d);
                if (!double.IsFinite(delta) || Math.Abs(delta / initial) > 1e-8) throw new InvalidOperationException("Plane fit is not a rounding-scale difference.");
                pitch += delta;
            }
            var final = EnergyGridControls.Curve(analysis, psf, pitch).Select(p => p.Y).ToArray();
            var maximum = final.Zip(expected, (a, b) => Math.Abs(a - b)).Max();
            var cone = ((double Rms, double Tangential, double Sagittal)?)engine.GetMethod("CumulativeEnergyCone", Static)!.Invoke(null, [optic, (0d, 0d), wave]);
            var nativeDirections = control.GetProperty("native").EnumerateArray().Skip(1).ToArray();
            var nativeNa = Math.Sqrt(nativeDirections.Select(d => Math.Pow(d.GetProperty("x").GetDouble(), 2) + Math.Pow(d.GetProperty("y").GetDouble(), 2)).Average());
            rows.Add(new { lens, expectedCount, initialPitch = initial, identifiedPitch = pitch, maximum,
                fFromEnergy = pitch * Math.Sqrt(32) / wave.Micrometers, coreWorkingF = f,
                coreCrossF = cone is { } c ? .5 / c.Rms : double.NaN,
                nativeWorkingF = control.GetProperty("nativeF").GetDouble(), nativeTransverseF = .5 / nativeNa });
            Console.WriteLine($"{lens}: fitted F={pitch * Math.Sqrt(32) / wave.Micrometers:R}; Core F={f:R}; cross F={(cone is { } co ? .5 / co.Rms : double.NaN):R}; native WFNO={control.GetProperty("nativeF").GetDouble():R}; native transverse={.5 / nativeNa:R}; residual={maximum:G4}");
        }
        JsonFiles.Write(Path.Combine(output, "identified-planes.json"), rows);
    }
}
