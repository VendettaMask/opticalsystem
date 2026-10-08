using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Serialization;

internal static class TranslationDiagnostic
{
    public static Task Run(string root, string output, JsonSerializerOptions json)
    {
        // A rigid axial shift is only a coordinate-origin change. Do not derive
        // an alternative diffraction result: compare the existing formal engines.
        var cookePath = Path.Combine(root, "validation/zemax/2026-r1/standard-sample-opd-2026-10-06/cooke-40-degree-field/snapshot.json");
        var msPath = Path.Combine(root, "validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/source.ZMX");
        var hashes = new Dictionary<string, string> { [cookePath] = Hash(cookePath), [msPath] = Hash(msPath) };
        var cooke = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(cookePath), json)!);
        var ms = OpticalFormatCatalog.Import(File.ReadAllText(msPath), ".zmx");
        var rows = new List<object>();
        foreach (var (name, source, field, waveNumber) in new[]
        {
            ("Cooke axis", cooke, (Hx: 0d, Hy: 0d), 2),
            ("Cooke edge", cooke, (Hx: 0d, Hy: 1d), 1),
            ("MS-L7 axis", ms, (Hx: 0d, Hy: 0d), 1)
        })
        {
            var baseOptic = Optic.FromSnapshot(source.ToSnapshot());
            var wave = baseOptic.Wavelengths[waveNumber - 1];
            var baselineWavefront = WavefrontEngine.GenerateChiefRayUniform(baseOptic, field, wave, 32, aimAtStop: source.RayAimingEnabled);
            var baselineFrame = DiffractionEngine.CreateHuygensImageFrame(baseOptic, field, wave, source.RayAimingEnabled);
            var baselinePsf = DiffractionEngine.ComputeHuygensPsf(baseOptic, field, wave, 32, 32, .00025, aimAtStop: source.RayAimingEnabled);
            var baselineFft = DiffractionEngine.ComputeFftPsf(baseOptic, field, wave, 32, 64, cellCenteredPupil: true, zemaxFftSampling: true);
            var shifts = new List<object>();
            foreach (var shift in new[] { 1d, 10d, 100d })
            {
                var optic = Optic.FromSnapshot(source.ToSnapshot());
                optic.InvalidateRayTraceCache();
                foreach (var surface in optic.SurfaceGroup.Items)
                    surface.CoordinateSystem = surface.CoordinateSystem with { Origin = surface.CoordinateSystem.Origin + new Vector3D(0, 0, shift) };
                var shiftedWavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, 32, aimAtStop: source.RayAimingEnabled);
                var shiftedFrame = DiffractionEngine.CreateHuygensImageFrame(optic, field, wave, source.RayAimingEnabled);
                var shiftedPsf = DiffractionEngine.ComputeHuygensPsf(optic, field, wave, 32, 32, .00025, aimAtStop: source.RayAimingEnabled);
                var shiftedFft = DiffractionEngine.ComputeFftPsf(optic, field, wave, 32, 64, cellCenteredPupil: true, zemaxFftSampling: true);
                var validPairs = baselineWavefront.Samples.Zip(shiftedWavefront.Samples)
                    .Where(p => p.First.Intensity > 0 && p.Second.Intensity > 0).ToArray();
                shifts.Add(new
                {
                    axialTranslationMillimeters = shift,
                    candidateCount = baselineWavefront.Samples.Count,
                    commonValidCount = validPairs.Length,
                    maskMismatchCount = baselineWavefront.Samples.Zip(shiftedWavefront.Samples).Count(p => (p.First.Intensity > 0) != (p.Second.Intensity > 0)),
                    wavefrontOpdDifferenceWaves = validPairs.Max(p => Math.Abs(p.First.OpdWaves - p.Second.OpdWaves)),
                    intensityDifference = validPairs.Max(p => Math.Abs(p.First.Intensity - p.Second.Intensity)),
                    imageDirectionZDifference = validPairs.Max(p => Math.Abs(p.First.ImageDirectionZ - p.Second.ImageDirectionZ)),
                    pupilTranslationResidualMillimeters = validPairs.Max(p => new Vector3D(
                        p.Second.PupilX - p.First.PupilX, p.Second.PupilY - p.First.PupilY,
                        p.Second.PupilZ - p.First.PupilZ - shift).Length),
                    frameTranslationResidualMillimeters = (shiftedFrame.Center - baselineFrame.Center - new Vector3D(0, 0, shift)).Length,
                    referenceRadiusDifferenceMillimeters = Math.Abs(shiftedWavefront.Radius - baselineWavefront.Radius),
                    huygensMaximumIntensityDifference = Difference(baselinePsf, shiftedPsf),
                    baselineHuygensPeak = baselinePsf.PeakStrehlRatio, shiftedHuygensPeak = shiftedPsf.PeakStrehlRatio,
                    fftMaximumIntensityDifference = Difference(baselineFft, shiftedFft),
                    fftPitchDifferenceMicrometers = Math.Abs(baselineFft.SampleSpacingMicrometers - shiftedFft.SampleSpacingMicrometers)
                });
                Console.WriteLine($"Rigid Z origin control: {name}, shift {shift} mm inspected.");
            }
            rows.Add(new { name, originalSystemAiming = source.RayAimingEnabled, waveNumber,
                referenceSphere = baselineWavefront.ReferenceSphere,
                pupilBounds = new { minZ = baselineWavefront.Samples.Where(s => s.Intensity > 0).Min(s => s.PupilZ),
                    maxZ = baselineWavefront.Samples.Where(s => s.Intensity > 0).Max(s => s.PupilZ) }, shifts });
        }
        foreach (var pair in hashes)
            if (Hash(pair.Key) != pair.Value) throw new InvalidDataException("Input changed.");
        using var stream = File.Create(Path.Combine(output, "translation-summary.json"));
        JsonSerializer.Serialize(stream, new
        {
            scope = "Rigid global Z-origin controls on unchanged Cooke and MS-L7 models through formal Core. No native capture or alternate optical formula.",
            createdUtc = DateTimeOffset.UtcNow, phase = "post-repair verification", inputsUnchanged = true, cases = rows,
            inputHashes = hashes, coreAssemblySha256 = Hash(typeof(Optic).Assembly.Location),
            probeSourceSha256 = Hash(Path.Combine(root, "tools/diagnostics/CalculationPaths20261008/TranslationDiagnostic.cs"))
        }, json);
        return Task.CompletedTask;
    }

    private static double Difference(PsfResult a, PsfResult b) => a.Values.Cast<double>().Zip(b.Values.Cast<double>(),
        (x, y) => Math.Abs(x - y) / 100).Max();
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
