using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;

// Offline inspection of the three explicitly accepted 128-grid requests.
// Grid reconstruction is diagnostic and must reproduce formal Core FFT values
// bit for bit before any ray inputs are accepted. No reference feeds runtime.
internal static class Energy128RayControls
{
    private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Captures = "validation/zemax/2026-r1/objectives-residuals-2026-10-21";

    public static void Prepare(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output);
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Captures, "cases.json")));
        var jobs = new List<object>();
        foreach (var item in cases.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("id").GetString()!.EndsWith("energy128", StringComparison.Ordinal)))
        {
            var id = item.GetProperty("id").GetString()!;
            var lens = item.GetProperty("lens").GetString()!;
            var source = Path.GetFullPath(Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens));
            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
            if (sourceHash != item.GetProperty("sourceSha256").GetString()) throw new InvalidDataException("Source changed.");
            var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(Captures, id, "request.json"));
            if (request.PupilSampling != 128 || request.ImageSampling != 256 || request.Wavelength < 1 || request.Field != 1)
                throw new InvalidDataException("Expected explicit axial selected-wave 128-grid request.");
            var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
            var wave = optic.Wavelengths[request.Wavelength - 1];
            if (!wave.IsPrimary) throw new InvalidDataException("Expected selected primary wavelength.");
            var reference = optic.Wavelengths.First(w => w.IsPrimary);
            var field = (Hx: 0d, Hy: 0d);
            var engine = typeof(DiffractionEngine);
            var formal = ((ValueTuple<Wavelength, PsfResult>[])engine.GetMethod("ComputeFftCumulativeEnergySpectrum", Static)!
                .Invoke(null, [optic, field, new[] { wave }, 128, false])!)[0].Item2;
            var cone = ((double Rms, double Tangential, double Sagittal)?)engine.GetMethod("CumulativeEnergyCone", Static)!
                .Invoke(null, [optic, field, wave]) ?? throw new InvalidDataException("Full cone required.");
            PupilGridSpecification grid;
            if (cone.Tangential == cone.Sagittal && cone.Rms == Math.BitIncrement(cone.Tangential))
            {
                var step = 1d / Math.Sqrt(128);
                grid = new(128, true, true, step * 63);
                Set("ExplicitCoordinateStep", step);
            }
            else
            {
                var fftFNumber = DiffractionEngine.WorkingFNumber(optic, field, wave, optic.RayAimingEnabled);
                var defaultPitch = wave.Micrometers * fftFNumber * 126 / (256 * 2d);
                var extentPerFNumber = 2 * defaultPitch / formal.SampleSpacingMicrometers / fftFNumber;
                grid = new(128, true, true, extentPerFNumber * (.5 / cone.Sagittal));
                Set("StretchY", extentPerFNumber * (.5 / cone.Tangential));
            }
            Set("UseCachedFftStopMap", true);
            var sphere = engine.GetMethod("CumulativeEnergyReferenceSphere", Static)!.Invoke(null, [optic, field, reference]);
            object?[] parameters = [optic, field, wave, 128, 256, false, true, 0d, null, null, true,
                false, optic.RayAimingEnabled, reference, formal.SampleSpacingMicrometers, true, null, sphere, grid];
            var replay = (PsfResult)engine.GetMethod("ComputeFftPsfCore", Static)!.Invoke(null, parameters)!;
            if (!formal.Values.Cast<double>().Select(BitConverter.DoubleToInt64Bits)
                .SequenceEqual(replay.Values.Cast<double>().Select(BitConverter.DoubleToInt64Bits)))
                throw new InvalidDataException("Reconstructed pupil does not reproduce the formal energy FFT exactly.");
            var yCoordinate = typeof(PupilGridSpecification).GetMethod("YCoordinate", Instance)!;
            var samples = Enumerable.Range(0, 128).SelectMany(row => Enumerable.Range(0, 128).Select(column =>
                new PupilSample(grid.Coordinate(column), (double)yCoordinate.Invoke(grid, [row])!, 1)))
                .Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray();
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0,
                wave.Micrometers, samples, aimAtStop: optic.RayAimingEnabled);
            var histories = optic.SequentialRayTracer.Trace(bundle).RayHistories;
            var core = histories.Select((history, index) => new
            {
                pupilIndex = index,
                samples = history.Select(s => new
                {
                    s.SurfaceNumber,
                    s.Vignetted,
                    localPosition = optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalPoint(s.Position),
                    localDirection = optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalDirection(s.Direction)
                }).ToArray()
            }).ToArray();
            var directory = Path.Combine(output, id);
            Directory.CreateDirectory(directory);
            var copy = Path.Combine(directory, "source.zmx");
            File.Copy(source, copy);
            JsonFiles.Write(Path.Combine(directory, "core.json"), core);
            JsonFiles.Write(Path.Combine(directory, "identity.json"), new
            {
                id,
                lens,
                source,
                sourceHash,
                request.Wavelength,
                wavelengthMicrometers = wave.Micrometers,
                pupilSampling = 128,
                imageSampling = 256,
                pupilCount = samples.Length,
                formalEnergyFftExactlyReproduced = true,
                formal.SampleSpacingMicrometers,
                optic.RayAimingEnabled,
                coreAssemblySha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location))
            });
            jobs.Add(new
            {
                id,
                lens,
                input = copy,
                rayAuditRequest = new
                {
                    wavelength = request.Wavelength,
                    removeVignettingFactors = false,
                    surfaceRecordLimit = 1000000,
                    rayAuditInputs = samples.Select((p, i) => new { fieldIndex = 0, pupilIndex = i, hx = 0d, hy = 0d, px = p.X, py = p.Y, weight = p.Weight }).ToArray()
                }
            });
            Console.WriteLine($"{id}: {samples.Length} formal 128-grid pupil inputs; exact FFT replay");
            void Set(string name, object value) => typeof(PupilGridSpecification).GetProperty(name, Instance)!.SetValue(grid, value);
        }
        if (jobs.Count != 3) throw new InvalidDataException("Exactly three energy cases required.");
        JsonFiles.Write(Path.Combine(output, "native-request.json"), new { zosApiPath = @"D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio", jobs });
    }

    public static void Compare(string output)
    {
        var configuration = Path.Combine("tools/OpticalSystemDesign.ZemaxComparison", "comparison-settings.json");
        using var policy = JsonDocument.Parse(File.ReadAllText(configuration));
        var quantities = policy.RootElement.GetProperty("analyses").GetProperty("Single Ray Trace").GetProperty("quantities");
        var position = quantities.GetProperty("Coordinate");
        var direction = quantities.GetProperty("DirectionCosine");
        var rows = new List<object>();
        using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native-request.json")));
        foreach (var job in request.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var id = job.GetProperty("id").GetString()!;
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native/capture", id, "ray-audit.json")));
            using var core = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, id, "core.json")));
            using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, id, "identity.json")));
            var source = identity.RootElement.GetProperty("source").GetString()!;
            if (JsonFiles.Hash(File.ReadAllBytes(source)) != identity.RootElement.GetProperty("sourceHash").GetString())
                throw new InvalidDataException("Source changed.");
            var records = 0; var coordinateFailures = 0; var directionFailures = 0; var statusFailures = 0;
            var maxCoordinate = 0d; var maxDirection = 0d;
            foreach (var surface in native.RootElement.GetProperty("surfaces").EnumerateArray())
            {
                var number = surface.GetProperty("surface").GetInt32();
                var nativeRays = surface.GetProperty("rays");
                if (nativeRays.GetArrayLength() != core.RootElement.GetArrayLength()) throw new InvalidDataException("Ray count mismatch.");
                for (var i = 0; i < nativeRays.GetArrayLength(); i++)
                {
                    records++;
                    var ray = nativeRays[i];
                    var found = core.RootElement[i].GetProperty("samples").EnumerateArray()
                        .Where(s => s.GetProperty("surfaceNumber").GetInt32() == number).ToArray();
                    if (ray.GetProperty("error").GetInt32() != 0 || found.Length != 1) { statusFailures++; continue; }
                    var sample = found[0];
                    if (sample.GetProperty("vignetted").GetBoolean() != (ray.GetProperty("vignette").GetInt32() != 0)) statusFailures++;
                    foreach (var (local, nativeNames, tolerance, isPosition) in new[]
                    {
                        ("localPosition", new[] { "x", "y", "z" }, position, true),
                        ("localDirection", new[] { "l", "m", "n" }, direction, false)
                    })
                    {
                        for (var axis = 0; axis < 3; axis++)
                        {
                            var actual = sample.GetProperty(local).GetProperty(new[] { "x", "y", "z" }[axis]).GetDouble();
                            var expected = ray.GetProperty(nativeNames[axis]).GetDouble();
                            var error = Math.Abs(actual - expected);
                            var limit = tolerance.GetProperty("absolute").GetDouble() + tolerance.GetProperty("relative").GetDouble() * Math.Abs(expected);
                            if (!double.IsFinite(error) || error > limit)
                            { if (isPosition) coordinateFailures++; else directionFailures++; }
                            if (isPosition) maxCoordinate = Math.Max(maxCoordinate, error); else maxDirection = Math.Max(maxDirection, error);
                        }
                    }
                }
            }
            var pass = coordinateFailures + directionFailures + statusFailures == 0;
            rows.Add(new
            {
                id,
                conclusion = pass ? "Pass" : "Difference",
                pupilCount = core.RootElement.GetArrayLength(),
                records,
                maxCoordinateMillimeters = maxCoordinate,
                maxDirectionCosine = maxDirection,
                coordinateFailures,
                directionFailures,
                statusFailures
            });
            Console.WriteLine($"{id}: {(pass ? "Pass" : "Difference")}; records={records}, position={maxCoordinate:R} mm, direction={maxDirection:R}");
        }
        JsonFiles.Write(Path.Combine(output, "ray-comparison.json"), new
        {
            scope = "Actual formal Core 128 cumulative-energy pupil nodes; axial selected primary wavelength; all physical surfaces; coordinate/direction component limits and exact acceptance status",
            policySha256 = JsonFiles.Hash(File.ReadAllBytes(configuration)),
            original32EnergyCasesRemainUnresolved = true,
            rows
        });
    }
}
