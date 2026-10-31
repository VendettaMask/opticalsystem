using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;

var original = Optic.CreateCookeTriplet(); original.InvalidateRayTraceCache();
foreach (var s in original.SurfaceGroup.Items) s.MaterialAfter = new ConstantIndexMaterial("Frozen " + s.Number, s.MaterialAfter.RefractiveIndex(587.6));
original.ImageSpaceAfocal = true; original.Wavelengths.Clear(); original.Wavelengths.Add(new Wavelength { Nanometers = 587.6, IsPrimary = true });
var rows = new List<object>(); double[]? reference = null;
foreach (var scale in new[] { 1, 1.5, 2 })
{
    var optic = Optic.FromSnapshot(original.ToSnapshot()); optic.InvalidateRayTraceCache();
    if (scale != 1) foreach (var s in optic.SurfaceGroup.Items)
        s.MaterialAfter = new ConstantIndexMaterial("Scaled " + s.Number, s.MaterialAfter.RefractiveIndex(587.6) * scale);
    optic.Wavelengths[0].Nanometers *= scale; var wave = optic.Wavelengths[0];
    var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 16);
    var psf = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 16, 16, .1);
    var values = psf.Values.Cast<double>().ToArray(); reference ??= values;
    var stop = optic.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop);
    object?[] probeArgs = [null];
    var probeMethod = optic.SequentialRayTracer.RayGenerator.GetType().GetMethod("TryGenerateAfocalScaleProbes", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var probesAvailable = (bool)probeMethod.Invoke(optic.SequentialRayTracer.RayGenerator, probeArgs)!;
    var probes = probesAvailable ? (RealRayBundle)probeArgs[0]! : null;
    rows.Add(new { scale, wf.ImageRefractiveIndex, wf.AfocalPupilDiameterMillimeters,
        entrance = optic.Paraxial.EstimateEntrancePupilDiameter(), exit = optic.Paraxial.EstimateExitPupilDiameter(),
        pupilZ = optic.Paraxial.EstimateExitPupilLocation(),
        stopTarget = optic.Paraxial.TraceNormalizedPupil(0, [1d], wave.Micrometers).Heights[stop][0],
        probeLaunches = probes?.Rays.Select(r => new { r.Origin, r.Direction }).ToArray(),
        maximumDifference = values.Zip(reference, (a, b) => Math.Abs(a - b)).Max() });
}
var json = new JsonSerializerOptions { WriteIndented = true };
var result = new { coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))), rows };
File.WriteAllText(args[0], JsonSerializer.Serialize(result, json)); Console.WriteLine(JsonSerializer.Serialize(result, json));
