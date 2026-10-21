using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;

// Diagnostic variations call the existing Core tracer, Fourier transform and
// cumulative integrator. They never supply product computations or references.
internal static class EnergyGridControls
{
    private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type Engine = typeof(DiffractionEngine);
    private static readonly Assembly Core = Engine.Assembly;

    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh diagnostic output directory.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        var rows = new List<object>();
        foreach (var item in items.RootElement.EnumerateArray())
        {
            var run = item.GetProperty("run").Deserialize<AnalysisRun>(JsonFiles.Options)!;
            if (run.Key != "Diffraction Encircled Energy") continue;
            var lens = item.GetProperty("lens").GetString()!;
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
            var request = run.Request!;
            var wave = optic.Wavelengths[request.Wavelength - 1];
            var reference = optic.Wavelengths.First(w => w.IsPrimary);
            var definedField = optic.Fields[request.Field - 1];
            var field = FieldCoordinates.Normalize(optic.Fields, definedField.X, definedField.Y);
            var n = request.PupilSampling;
            var scaleF = DiffractionEngine.WorkingFNumber(optic, field, reference, optic.RayAimingEnabled);
            var f = DiffractionEngine.WorkingFNumber(optic, field, wave, optic.RayAimingEnabled);
            var axes = DiffractionEngine.WorkingFNumbers(optic, field, wave, optic.RayAimingEnabled, true);
            var pitchMm = wave.Micrometers * .001 / Math.Sqrt(n) * scaleF;
            var originalPitch = pitchMm * 1000;
            var cone = ((double Rms, double Tangential, double Sagittal)?)Engine.GetMethod("CumulativeEnergyCone", Static)!.Invoke(null, [optic, field, wave]);
            object?[] directionArgs = [optic, field, wave, optic.RayAimingEnabled, 1d, null];
            Engine.GetMethod("TryWorkingPupilDirections", Static)!.Invoke(null, directionArgs);
            var directions = (OpticalSystemDesign.Core.Backend.Vector3D[])directionArgs[5]!;
            var chief = directions[0];
            var cosSquaredNas = directions.Skip(1).Select(d =>
            {
                var cosine = chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z;
                return Math.Max(0, 1 - cosine * cosine);
            }).ToArray();
            var cosineF = .5 / Math.Sqrt(cosSquaredNas.Average());
            var sphere = Engine.GetMethod("CumulativeEnergyReferenceSphere", Static)!.Invoke(null, [optic, field, reference]);
            var entry = AnalysisComparisonRegistry.Get(run.Key);
            NumericResult native;
            try { native = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json"), entry, request); }
            catch (InvalidDataException exception) { rows.Add(new { lens, incomparable = exception.Message }); continue; }
            var analysis = new DiffractionEncircledEnergyAnalysis(optic, n, 2 * n, 401, request.Wavelength, request.Field,
                maximumDistanceMicrometers: 5);
            foreach (var variant in new[] { "baseline", "divided-mm", "nanometers-mm", "pitch-first-mm", "cutoff-mm",
                "cone-divided-mm", "cone-pitch-first-mm", "cone-cutoff-mm", "cosine-mm", "cosine-cutoff" })
            {
                var pitch = variant.StartsWith("cone") && cone is { } c
                    ? wave.Micrometers * .001 / Math.Sqrt(n) * (.5 / c.Rms) * 1000 : originalPitch;
                if (variant.StartsWith("cosine")) pitch = wave.Micrometers * .001 / Math.Sqrt(n) * cosineF * 1000;
                double Step(double axis)
                {
                    if (variant != "unsnapped" && Math.Abs(axis - f) <= 1e-14 * f) axis = f;
                    return variant switch
                    {
                        "micrometers" => 1 / (n * pitch / wave.Micrometers / axis),
                        "direct" => axis / scaleF / Math.Sqrt(n),
                        "fft-extent" => (wave.Micrometers * axis * (n - 2) / (2 * n)) / pitch / (n / 2d - 1),
                        "inward" => Math.BitDecrement(axis / scaleF / Math.Sqrt(n)),
                        "outward" => Math.BitIncrement(axis / scaleF / Math.Sqrt(n)),
                        _ => 1 / (n * pitchMm / (wave.Micrometers * .001) / axis)
                    };
                }
                var sx = Step(axes.Sagittal);
                var sy = Step(axes.Tangential);
                if (variant is "divided-mm" or "nanometers-mm" or "pitch-first-mm" or "cutoff-mm")
                {
                    var wavelengthMm = variant == "nanometers-mm" ? wave.Nanometers * 1e-6 : wave.Micrometers / 1000;
                    var physicalPitch = variant == "pitch-first-mm" ? wavelengthMm * scaleF / Math.Sqrt(n)
                        : variant == "cutoff-mm" ? Math.Sqrt(1d / n) / (1 / (wavelengthMm * scaleF))
                        : wavelengthMm / Math.Sqrt(n) * scaleF;
                    pitch = physicalPitch * 1000;
                    double ConvertedStep(double axis)
                    {
                        if (Math.Abs(axis - f) <= 1e-14 * f) axis = f;
                        return 1 / (n * physicalPitch / wavelengthMm / axis);
                    }
                    sx = ConvertedStep(axes.Sagittal);
                    sy = ConvertedStep(axes.Tangential);
                }
                if (variant.StartsWith("cone") && cone is { } c2)
                {
                    sx = variant == "cone-mm" ? 1 / (n * (pitch / 1000) / (wave.Micrometers * .001) / (.5 / c2.Sagittal))
                        : 1 / (n * pitch / wave.Micrometers / (.5 / c2.Sagittal));
                    sy = variant == "cone-mm" ? 1 / (n * (pitch / 1000) / (wave.Micrometers * .001) / (.5 / c2.Tangential))
                        : 1 / (n * pitch / wave.Micrometers / (.5 / c2.Tangential));
                    var waveMm = wave.Micrometers / 1000;
                    var physicalPitch = variant == "cone-pitch-first-mm" ? waveMm * (.5 / c2.Rms) / Math.Sqrt(n)
                        : variant == "cone-cutoff-mm" ? Math.Sqrt(1d / n) / (2 * c2.Rms / waveMm)
                        : waveMm / Math.Sqrt(n) * (.5 / c2.Rms);
                    pitch = physicalPitch * 1000;
                    sx = 1 / (n * physicalPitch / waveMm / (.5 / c2.Sagittal));
                    sy = 1 / (n * physicalPitch / waveMm / (.5 / c2.Tangential));
                }
                if (variant.StartsWith("cosine"))
                {
                    double StepCos(double axisF) => variant == "cosine-mm"
                        ? 1 / (n * (wave.Micrometers * .001 / Math.Sqrt(n) * cosineF) / (wave.Micrometers * .001) / axisF)
                        : 1 / (n * Math.Sqrt(1d / n) / (1 / (wave.Micrometers * .001 * cosineF)) * (1 / (wave.Micrometers * .001 * axisF)));
                    sx = StepCos(.5 / ((Math.Sqrt(cosSquaredNas[2]) + Math.Sqrt(cosSquaredNas[3])) / 2));
                    sy = StepCos(.5 / ((Math.Sqrt(cosSquaredNas[0]) + Math.Sqrt(cosSquaredNas[1])) / 2));
                }
                var grid = new PupilGridSpecification(n, true, true, sx * (n / 2d - 1));
                typeof(PupilGridSpecification).GetProperty("ExplicitCoordinateStep", Instance)!.SetValue(grid, sx);
                typeof(PupilGridSpecification).GetProperty("ExplicitCoordinateStepY", Instance)!.SetValue(grid, sy);
                typeof(PupilGridSpecification).GetProperty("UseCachedFftStopMap", Instance)!.SetValue(grid, true);
                foreach (var ideal in new[] { true, false })
                {
                    object?[] arguments = [optic, field, wave, n, 2 * n, false, true, 0d, null, null, true,
                        ideal, optic.RayAimingEnabled, reference, pitch, true, null, sphere, grid];
                    var psf = (PsfResult)Engine.GetMethod("ComputeFftPsfCore", Static)!.Invoke(null, arguments)!;
                    var pupil = (System.Numerics.Complex[,])arguments[16]!;
                    var curve = Curve(analysis, psf, pitch);
                    var expected = native.Series[ideal ? 0 : 1].Y;
                    var error = curve.Zip(expected, (a, b) => Math.Pow(a.Y - b, 2)).Average();
                    var max = curve.Zip(expected, (a, b) => Math.Abs(a.Y - b)).Max();
                    var nrmse = Math.Sqrt(error) / (expected.Max() - expected.Min());
                    rows.Add(new
                    {
                        lens,
                        variant,
                        ideal,
                        sx,
                        sy,
                        f,
                        scaleF,
                        cosineF,
                        directions,
                        cone = cone is { } co ? new { co.Rms, co.Tangential, co.Sagittal } : null,
                        axes = new { axes.Tangential, axes.Sagittal },
                        wavelength = wave.Micrometers,
                        nanometers = wave.Nanometers,
                        pitch,
                        pupilCount = pupil.Cast<System.Numerics.Complex>().Count(v => v.Magnitude > 0),
                        max,
                        nrmse
                    });
                    Console.WriteLine($"{Path.GetFileName(lens)} {variant} ideal={ideal}: count={pupil.Cast<System.Numerics.Complex>().Count(v => v.Magnitude > 0)}, max={max:G5}, nrmse={nrmse:G5}, step={sx:R}");
                }
            }
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
    }

    internal static IReadOnlyList<AnalysisPoint> Curve(DiffractionEncircledEnergyAnalysis analysis, PsfResult psf, double pitch,
        bool nativeShiftedGrid = false)
    {
        var size = psf.GridSize;
        var samples = Array.CreateInstance(Core.GetType("OpticalSystemDesign.Core.Analysis.EnergySample")!, size * size);
        var k = 0;
        for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
                samples.SetValue(Activator.CreateInstance(samples.GetType().GetElementType()!,
                    (column - size / 2d + 1) * pitch, (row - size / 2d + 1) * pitch,
                    psf.Values[nativeShiftedGrid ? row : (row + 1) % size, nativeShiftedGrid ? column : (column + 1) % size]), k++);
        var center = Core.GetType("OpticalSystemDesign.Core.Analysis.EnergyCurveSupport")!.GetMethod("Centroid")!.Invoke(null, [samples]);
        var energy = Activator.CreateInstance(Core.GetType("OpticalSystemDesign.Core.Analysis.FftCumulativeEnergyGrid")!, samples, center, "encircled");
        return (IReadOnlyList<AnalysisPoint>)typeof(DiffractionEncircledEnergyAnalysis).GetMethod("BuildCurve", Instance)!.Invoke(analysis, [energy, 5d])!;
    }
}
