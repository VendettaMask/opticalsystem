using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class Energy32WidthHoldout
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        using var recipe = JsonDocument.Parse(File.ReadAllText(root + "/holdout-request.json"));
        using var cones = JsonDocument.Parse(File.ReadAllText(root + "/native-holdout/capture/cones.json"));
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        const BindingFlags stat = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
        var engine = typeof(DiffractionEngine);
        var rows = new List<object>();
        foreach (var job in recipe.RootElement.GetProperty("jobs").EnumerateArray().Where(j => j.TryGetProperty("analysisRequest", out _)))
        {
            var id = job.GetProperty("id").GetString()!;
            var lens = job.GetProperty("lens").GetString()!;
            var nativeRequest = job.GetProperty("analysisRequest").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var source = File.ReadAllText(Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens));
            var apertureScale = job.GetProperty("apertureScale").GetDouble();
            source = System.Text.RegularExpressions.Regex.Replace(source, @"(?m)^ENPD ([^\r\n ]+)", m => "ENPD "
                + (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * apertureScale).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            // Re-import the changed prescription so its existing curvature
            // solves are evaluated at the perturbed aperture as native does.
            var optic = OpticalSystemDesign.Core.FileIO.OpticalFormatCatalog.Import(source, ".zmx");
            var wave = optic.Wavelengths[nativeRequest.Wavelength - 1];
            var reference = optic.Wavelengths.First(w => w.IsPrimary);
            var f = cones.RootElement.EnumerateArray().Single(c => c.GetProperty("id").GetString() == id + "-cone")
                .GetProperty("waves")[0].GetProperty("primaryWorkingFNumber").GetDouble();
            var lambda = wave.Micrometers * .001;
            var pitch = Math.Sqrt(32) * lambda * f / 32;
            var step = 1 / (32 * pitch / lambda / f);
            object?[] probes = [optic, (0d, 0d), wave, optic.RayAimingEnabled, 1d, null];
            engine.GetMethod("TryWorkingPupilDirections", stat)!.Invoke(null, probes);
            var directions = (OpticalSystemDesign.Core.Backend.Vector3D[])probes[5]!;
            var chief = directions[0];
            var crossF = .5 / Math.Sqrt(directions.Skip(1).Select(d => Math.Pow(optic.Backend.Current.Cross(chief, d).Length, 2)).Average());
            var cosF = .5 / Math.Sqrt(directions.Skip(1).Select(d => 1 - Math.Pow(chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z, 2)).Average());
            var sphere = engine.GetMethod("CumulativeEnergyReferenceSphere", stat)!.Invoke(null, [optic, (0d, 0d), reference]);
            var native = ResultNormalizer.Zemax(Path.Combine(root, "native-holdout/capture", id, "data.json"), AnalysisComparisonRegistry.Get(nativeRequest.CanonicalAnalysisKey), nativeRequest);
            var analysis = new DiffractionEncircledEnergyAnalysis(optic, 32, 64, 401, nativeRequest.Wavelength, nativeRequest.Field, maximumDistanceMicrometers: 5);
            foreach (var method in new[] { "frozen-width-hypothesis", "measured-101-control", "measured-97-control", "core-cross-inverse-spacing", "core-cos-conjugate-step" })
            {
                var computedF = method == "core-cross-inverse-spacing" ? crossF : method == "core-cos-conjugate-step" ? cosF : f;
                var computedPitch = Math.Sqrt(32) * lambda * computedF / 32;
                var interval = method == "measured-101-control" ? 1 / Math.Sqrt(32) : method == "measured-97-control" ? Math.BitIncrement(1 / Math.Sqrt(32))
                    : method == "core-cross-inverse-spacing" ? 1 / (computedPitch / (lambda * computedF / 32))
                    : method == "core-cos-conjugate-step" ? 1 / (32 * computedPitch / lambda / computedF) : step;
                var grid = new PupilGridSpecification(32, true, true, interval * 15);
                typeof(PupilGridSpecification).GetProperty("ExplicitCoordinateStep", inst)!.SetValue(grid, interval);
                typeof(PupilGridSpecification).GetProperty("UseCachedFftStopMap", inst)!.SetValue(grid, true);
                object?[] args = [optic, (0d, 0d), wave, 32, 64, false, true, 0d, null, null, true, true, optic.RayAimingEnabled, reference, computedPitch * 1000, true, null, sphere, grid];
                var psf = (PsfResult)engine.GetMethod("ComputeFftPsfCore", stat)!.Invoke(null, args)!;
                var count = ((Complex[,])args[16]!).Cast<Complex>().Count(c => c.Magnitude > 0);
                var curve = EnergyGridControls.Curve(analysis, psf, computedPitch * 1000);
                var max = curve.Zip(native.Series[0].Y, (a, b) => Math.Abs(a.Y - b)).Max();
                rows.Add(new { id, lens, method, f, computedF, interval, count, max });
                Console.WriteLine($"{id}/{method}: count={count}, max={max:R}");
            }
        }
        JsonFiles.Write(Path.Combine(output, "rows.json"), rows);
    }
}
