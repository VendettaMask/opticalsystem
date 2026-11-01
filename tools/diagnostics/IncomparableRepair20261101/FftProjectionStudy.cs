using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class FftProjectionStudy
{
    internal static Task Run(string repository, string study)
    {
        var snapshot = Path.Combine(repository, "artifacts/validation/model-capabilities-stage5-20261101/models/014-c1.optic.json");
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot)); var wave = optic.Wavelengths[1];
        var axes = DiffractionEngine.WorkingFNumbers(optic, (0, 0), wave, false);
        var directional = DiffractionEngine.WorkingFNumbers(optic, (0, 0), wave, false, zemaxDirectionalAverage: true);
        var mean = DiffractionEngine.WorkingFNumber(optic, (0, 0), wave, false);
        const int sampling = 1024; const int transform = 2048; const int display = 128; const double pitch = .25;
        var nativeFile = Path.Combine(repository, "artifacts/validation/incomparable-grid-native-current-runner-20261101/job-grid-495-1024-step.001/grid-495-1024-step.001/data.json");
        var native = JsonFiles.Read<JsonElement>(nativeFile).GetProperty("dataGrids")[0].GetProperty("values");
        var method = typeof(DiffractionEngine).GetMethod("ComputeFftPsfCore", BindingFlags.Static | BindingFlags.NonPublic)!;
        var results = new List<object>();
        var choices = File.Exists(Path.Combine(study, "variant-choices.json")) ? JsonFiles.Read<string[]>(Path.Combine(study, "variant-choices.json")) : null;
        var minimum = Math.Min(directional.Sagittal, directional.Tangential);
        foreach (var variant in new[] { (Id: "isotropic-control", X: mean, Y: mean), (Id: "directional-conjugate", X: axes.Sagittal, Y: axes.Tangential),
            (Id: "isotropic-minimum-average", X: minimum, Y: minimum) }.Where(v => choices is null || choices.Contains(v.Id)))
        {
            var grid = new PupilGridSpecification(sampling, true, true, wave.Micrometers * variant.X * (sampling - 2) / (transform * pitch));
            typeof(PupilGridSpecification).GetProperty("StretchY", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(grid, wave.Micrometers * variant.Y * (sampling - 2) / (transform * pitch));
            var parameters = new Dictionary<string, object?>
            {
                ["optic"] = optic, ["field"] = (0d, 0d), ["wavelength"] = wave, ["pupilSampling"] = sampling,
                ["gridSize"] = transform, ["usePolarization"] = false, ["cellCenteredPupil"] = true,
                ["defocusMillimeters"] = 0d, ["preparedWavefront"] = null, ["preparedPolarization"] = null,
                ["zemaxFftSampling"] = true, ["ignoreOpd"] = false, ["aimAtStop"] = false,
                ["referenceWavelength"] = wave, ["imageDelta"] = pitch, ["ignoreScalarTransmission"] = true,
                ["complexPupil"] = null, ["referenceSphere"] = null, ["explicitPupilGrid"] = grid,
                ["interpolateScalarPupilAmplitude"] = true, ["croppedOutputSize"] = display
            };
            var arguments = method.GetParameters().Select(p => parameters[p.Name!]).ToArray();
            var result = (PsfResult)method.Invoke(null, arguments)!;
            var max = 0d; var square = 0d; var peak = 0d; var pointPass = true;
            var values = new double[display][];
            for (var row = 0; row < display; row++)
            {
                values[row] = new double[display];
                for (var column = 0; column < display; column++)
                {
                    var expected = native[row][column].GetDouble(); var actual = result.Values[row, column] / 100 / result.UnaberratedPeakRatio;
                    values[row][column] = actual; var error = actual - expected;
                    square += error * error; max = Math.Max(max, Math.Abs(error)); peak = Math.Max(peak, Math.Abs(expected));
                    pointPass &= Math.Abs(error) <= 1e-5 + .001 * Math.Abs(expected);
                }
            }
            var nrmse = Math.Sqrt(square / (display * display)) / peak;
            results.Add(new { variant.Id, grid, maxAbsolute = max, nrmse, pointPass, coverage = 1,
                originalGatePass = pointPass || nrmse <= .01,
                note = "Intensity is evaluated at the physical frequencies from the declared directional conjugate pupil; metadata pitch from the unchanged scalar production routine is not used" });
            JsonFiles.Write(Path.Combine(study, variant.Id + "-values.json"), values);
            JsonFiles.Write(Path.Combine(study, "diagnostic.json"), new { diagnosticOnly = true, source = snapshot,
                axes = new { axes.Tangential, axes.Sagittal }, directional = new { directional.Tangential, directional.Sagittal }, mean,
                expectedAutomaticXHalfWidth8192 = wave.Micrometers * axes.Sagittal * (8192 - 2) / (2 * Math.Sqrt(8192d / 32)),
                expectedAutomaticMinimumMeanHalfWidth8192 = wave.Micrometers * minimum * (8192 - 2) / (2 * Math.Sqrt(8192d / 32)), results });
            Console.WriteLine(JsonSerializer.Serialize(results[^1]));
        }
        return Task.CompletedTask;
    }
}
