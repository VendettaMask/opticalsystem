using System.Numerics;
using System.Reflection;
using System.Globalization;
using System.Security.Cryptography;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class FftReferenceStudy
{
    internal static Task Run(string repository, string study)
    {
        var snapshot = Path.Combine(repository, "artifacts/validation/model-capabilities-stage5-20261101/models/014-c1.optic.json");
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot)); var wave = optic.Wavelengths[1];
        const int sampling = 512; const int transform = 1024;
        var sphere = WavefrontEngine.CreateChiefRayReferenceSphere(optic, (0, 0), wave, false);
        var axisRadius = Math.Abs(optic.Paraxial.EstimateExitPupilLocation(wave.Micrometers));
        var prepare = typeof(DiffractionEngine).GetMethod("ComputeFftPsfCore", BindingFlags.Static | BindingFlags.NonPublic)!;
        var correlate = typeof(DiffractionEngine).GetMethod("FourierPupilAutocorrelation", BindingFlags.Static | BindingFlags.NonPublic)!;
        var makeMtf = typeof(DiffractionEngine).GetMethod("CreateFftMtfResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var resample = typeof(MtfAnalysis).GetMethod("ResampleSpectrum", BindingFlags.Static | BindingFlags.NonPublic)!;
        var sourceIdentity = (PupilSourceIdentity?)typeof(PupilSourceIdentity)
            .GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { optic });
        var choices = File.Exists(Path.Combine(study, "variant-choices.json")) ? JsonFiles.Read<string[]>(Path.Combine(study, "variant-choices.json")) : null;
        foreach (var variant in new[] { (Id: "chief-plane", Sphere: sphere), (Id: "axial-distance", Sphere: sphere with { Radius = axisRadius }),
            (Id: "sine-pupil", Sphere: sphere), (Id: "inverse-sine-pupil", Sphere: sphere),
            (Id: "native-wavefront-residual", Sphere: sphere) }.Where(v => choices is null || choices.Contains(v.Id)))
        {
            var folder = Path.Combine(study, variant.Id); if (Directory.Exists(folder)) throw new IOException("Fresh reference diagnostic required");
            Directory.CreateDirectory(folder);
            WavefrontResult? prepared = null;
            object? phaseDiagnostic = null;
            if (variant.Id == "native-wavefront-residual")
            {
                // Causal experiment only: inject the directly measured phase
                // discrepancy, never a fitted runtime correction or acceptance.
                var csv = Path.Combine(repository, "artifacts/validation/incomparable-current-accepted-assessment-20261101/comparison/core-494-64/grid-values.csv");
                var difference = new Dictionary<(int X, int Y), double>();
                foreach (var line in File.ReadLines(csv).Skip(1))
                {
                    var p = line.Split(',').Take(4).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    difference[((int)Math.Round(p[0] * 31 + 32), (int)Math.Round(p[1] * 31 + 32))] = p[3] - p[2];
                }
                var pupilAxes = DiffractionEngine.WorkingFNumbers(optic, (0, 0), wave, false);
                var minimum = Math.Min(pupilAxes.Tangential, pupilAxes.Sagittal);
                var traced = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, sampling,
                    cellCentered: true, aimAtStop: false, zemaxCentered: true, referenceWavelength: wave);
                var corrected = 0;
                prepared = traced with { Samples = traced.Samples.Select(p =>
                {
                    var x = p.NormalizedPupilX * minimum / pupilAxes.Sagittal * 31 + 32;
                    var y = p.NormalizedPupilY * minimum / pupilAxes.Tangential * 31 + 32;
                    var ix = (int)Math.Floor(x); var iy = (int)Math.Floor(y);
                    if (!difference.TryGetValue((ix, iy), out var a) || !difference.TryGetValue((ix + 1, iy), out var b)
                        || !difference.TryGetValue((ix, iy + 1), out var c) || !difference.TryGetValue((ix + 1, iy + 1), out var d)) return p;
                    corrected++;
                    return p with { OpdWaves = p.OpdWaves + (1 - (y - iy)) * (a + (x - ix) * (b - a))
                        + (y - iy) * (c + (x - ix) * (d - c)) };
                }).ToArray() };
                phaseDiagnostic = new { diagnosticOnly = true, runtimeUse = false, csv, corrected,
                    total = traced.Samples.Count, noExtrapolation = true,
                    csvSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(csv))) };
            }
            if (variant.Id.Contains("sine-pupil", StringComparison.Ordinal))
            {
                var grid = new PupilGridSpecification(sampling, true, true, 1);
                var nodes = Enumerable.Range(0, sampling).SelectMany(y => Enumerable.Range(0, sampling)
                    .Select(x => (X: grid.Coordinate(x), Y: grid.Coordinate(y))))
                    .Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray();
                var na = optic.Aperture.Value;
                var positions = nodes.Select(p =>
                {
                    var r2 = p.X * p.X + p.Y * p.Y;
                    var factor = variant.Id == "sine-pupil" ? Math.Sqrt((1 - na * na) / (1 - na * na * r2))
                        : 1 / Math.Sqrt(1 - na * na + na * na * r2);
                    return (p.X * factor, p.Y * factor);
                }).ToArray();
                var traced = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave, positions,
                    aimAtStop: false, referenceSphere: variant.Sphere, referenceWavelength: wave);
                prepared = traced with { Samples = traced.Samples.Select((p, i) => p with
                    { NormalizedPupilX = nodes[i].X, NormalizedPupilY = nodes[i].Y }).ToArray(),
                    PupilGrid = grid, SourceIdentity = sourceIdentity };
            }
            var parameters = new Dictionary<string, object?>
            {
                ["optic"] = optic, ["field"] = (0d, 0d), ["wavelength"] = wave, ["pupilSampling"] = sampling, ["gridSize"] = transform,
                ["usePolarization"] = false, ["cellCenteredPupil"] = true, ["defocusMillimeters"] = 0d,
                ["preparedWavefront"] = prepared, ["preparedPolarization"] = null, ["zemaxFftSampling"] = true,
                ["ignoreOpd"] = false, ["aimAtStop"] = false, ["referenceWavelength"] = wave, ["imageDelta"] = -1d,
                ["ignoreScalarTransmission"] = false, ["complexPupil"] = null, ["referenceSphere"] = variant.Sphere,
                ["explicitPupilGrid"] = null, ["interpolateScalarPupilAmplitude"] = false, ["croppedOutputSize"] = 2
            };
            var arguments = prepare.GetParameters().Select(p => parameters[p.Name!]).ToArray();
            var metadata = (PsfResult)prepare.Invoke(null, arguments)!;
            metadata = metadata with { FrequencySampleCount = sampling - 1 };
            var pupil = (Complex[,])arguments[Array.FindIndex(prepare.GetParameters(), p => p.Name == "complexPupil")]!;
            var axes = ((Complex[], Complex[]))correlate.Invoke(null, new object[] { pupil, transform })!;
            var raw = (MtfResult)makeMtf.Invoke(null, new object[] { metadata, optic, wave, axes.Item1, axes.Item2 })!;
            var curve = (MtfResult)resample.Invoke(null, new object[] { new[] { (wave, raw) }, 50d, FftMtfDataType.Modulation, 300 })!;
            var series = new[] { Series(curve.Tangential, "Field 1, T"), Series(curve.Sagittal, "Field 1, S") };
            JsonFiles.Write(Path.Combine(folder, "data.json"), new AnalysisData("MTF", new Dictionary<string, object>
                { ["DiagnosticOnly"] = true, ["ReferenceSphere"] = variant.Sphere, ["PupilSampling"] = sampling,
                    ["PhaseDiagnostic"] = phaseDiagnostic ?? new { runtimeUse = false } }, series[0], series));
            JsonFiles.Write(Path.Combine(folder, "provenance.json"), new { diagnosticOnly = true,
                executorId = "Diagnostic.SharedCore.FftReference/v1", codeFingerprint = "diagnostic:" + Convert.ToHexStringLower(
                    SHA256.HashData(File.ReadAllBytes(typeof(DiffractionEngine).Assembly.Location))), snapshot });
            Console.WriteLine(folder);
            AnalysisSeries Series(IReadOnlyList<double> values, string name) => new("Frequency", "MTF",
                curve.Frequency.Select((f, i) => new AnalysisPoint(f, values[i])).ToArray(), Name: name,
                XQuantity: AnalysisAxisQuantity.SpatialFrequency, XUnit: AnalysisAxisUnit.CyclesPerMillimeter,
                YQuantity: AnalysisAxisQuantity.Modulation, YUnit: AnalysisAxisUnit.Dimensionless);
        }
        JsonFiles.Write(Path.Combine(study, "references.json"), new { diagnosticOnly = true, source = snapshot, sphere, axisRadius,
            purpose = "Diagnostic geometric sphere and sine/height pupil-coordinate conventions; no fitted optical correction" });
        return Task.CompletedTask;
    }
}
