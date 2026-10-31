using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

if (args.Length != 3) throw new ArgumentException("prepare|probe <repository> <fresh study root>");
var repository = Path.GetFullPath(args[1]);
var root = Path.GetFullPath(args[2]);
var prior = Path.Combine(repository, "artifacts/validation/afocal-mtf-field-20261031");
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
if (args[0] == "prepare")
{
    if (Directory.Exists(root)) throw new IOException("Fresh study root required");
    Directory.CreateDirectory(Path.Combine(root, "native"));
    var jobs = new List<object>();
    void Job(string id, Action<Dictionary<string, JsonElement>> change)
    {
        var request = JsonFiles.Read<Dictionary<string, JsonElement>>(Path.Combine(prior, "native/m3-fourier.request.json"));
        change(request);
        JsonFiles.Write(Path.Combine(root, "native", id + ".request.json"), request);
        jobs.Add(new { id, request = id + ".request.json", inputSha256 = request["inputSha256"].GetString() });
    }
    JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, JsonFiles.Options);
    void Scan(Dictionary<string, JsonElement> r, int wave, int sampling, double[]? frequencies = null)
    {
        var native = r["zemaxSettings"].Deserialize<Dictionary<string, JsonElement>>(JsonFiles.Options)!;
        native["Wavelength"] = Element(wave); native["SampleSize"] = Element($"S_{sampling}x{sampling}");
        if (frequencies is not null) for (var i = 0; i < frequencies.Length; i++) native["Freq_" + (i + 1)] = Element(frequencies[i]);
        r["zemaxSettings"] = Element(native); r["wavelength"] = Element(wave); r["pupilSampling"] = Element(sampling);
    }
    for (var wave = 1; wave <= 3; wave++) { var selected = wave; Job("scan32-w" + wave, r => Scan(r, selected, 32)); }
    foreach (var sampling in new[] { 64, 128 }) Job("scan" + sampling + "-all", r => Scan(r, 0, sampling));
    Job("scan32-reverse", r => Scan(r, 0, 32, [3.2, 1.6, .8, .4, .2, .1]));
    Job("scan32-equal", r => Scan(r, 0, 32, [3.2, 3.2, 3.2, 3.2, 3.2, 3.2]));
    foreach (var sampling in new[] { 32, 64 }) Job("regular" + sampling + "-all-field3", r =>
    {
        r["adapter"] = Element("fft-mtf"); r["analysisType"] = Element("FftMtf");
        r["field"] = Element(3); r["wavelength"] = Element(0); r["pupilSampling"] = Element(sampling);
        r["maximumFrequency"] = Element(3.2);
    });
    JsonFiles.Write(Path.Combine(root, "native/jobs.json"), jobs);
    JsonFiles.Write(Path.Combine(root, "identity.json"), new { originalNativeSha256 = Hash(Path.Combine(prior, "native/m3-fourier/data.json")),
        originalCoreSha256 = "fb6d2b94cb461da4eef88ba6ead5e57bc7da4dc27419e6bedc005de70e11af22",
        policySha256 = Hash(Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json")), backgroundOnly = true });
    Console.WriteLine("Prepared " + jobs.Count + " independent native controls"); return;
}
if (args[0] == "rules") { RuleProbe.Run(prior, root); return; }
if (args[0] == "gauss") { GaussProbe.Run(prior, root); return; }
if (args[0] == "prepare-points") { PointProbe.Prepare(prior, root); return; }
if (args[0] == "compare-points") { PointProbe.Compare(prior, root); return; }
if (args[0] == "prepare-flat") { FlatProbe.Prepare(prior, root); return; }
if (args[0] == "endpoints") { FocusedStudy.Endpoints(prior, root); return; }
if (args[0] == "curves") { FocusedStudy.Curves(repository, prior, root); return; }
if (args[0] == "assess") { ConvergedAssessment.Run(repository, prior, root); return; }
if (args[0] == "prepare-remaining") { RemainingFrequencies.Prepare(repository, prior, root); return; }
if (args[0] == "compute-remaining32") { RemainingFrequencies.Compute(root, 32); return; }
if (args[0] == "compute-remaining64") { RemainingFrequencies.Compute(root, 64); return; }
if (args[0] == "assess-remaining") { RemainingFrequencies.Assess(repository, root); return; }
if (args[0] == "normalize-native-nodes") { NativeOperandReference.Run(repository, root); return; }
if (args[0] == "assess-direct") { DirectReferenceAssessment.Run(repository, root); return; }
if (args[0] is not ("probe" or "clm" or "pupils" or "paraxial" or "clm-paraxial" or "clm-inset")) throw new ArgumentException("Unknown mode");
using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(prior, "cases.json")));
var row = cases.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == "m3-fourier");
var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.GetProperty("snapshot").GetString()!));
if (args[0] == "pupils")
{
    var methodInfo = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!
        .GetMethod("AfocalDiffractionPupilDiameterMillimeters", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    var data = new { paraxialExitDiameter = optic.Paraxial.EstimateExitPupilDiameter(),
        entranceDiameter = optic.Paraxial.EstimateEntrancePupilDiameter(),
        diffractionDiameter = methodInfo.Invoke(null, new object[] { optic }),
        referenceOpd = optic.ReferenceOpd.ToString() };
    JsonFiles.Write(Path.Combine(root, "core-pupils.json"), data);
    Console.WriteLine(JsonSerializer.Serialize(data)); return;
}
var diffractionDiameter = (double)typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!
    .GetMethod("AfocalDiffractionPupilDiameterMillimeters", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
    .Invoke(null, new object[] { optic })!;
var pupilRatio = diffractionDiameter / Math.Abs(optic.Paraxial.EstimateExitPupilDiameter());
if (args[0] is "clm" or "clm-paraxial" or "clm-inset")
{
    var data = new ContrastLossMapAnalysis(optic, sampling: args[0] == "clm-inset" ? 15 : 13,
        frequency: 3.2 * (args[0] == "clm-paraxial" ? pupilRatio : 1),
        wavelengthNumber: 2, fieldNumber: 3, showOpd: true).GenerateData();
    JsonFiles.Write(Path.Combine(root, args[0] == "clm" ? "core-contrast.json" : args[0] == "clm-inset" ? "core-contrast-inset.json" : "core-contrast-paraxial.json"), data);
    return;
}
var method = typeof(DiffractionEngine).GetMethod("ComputeFastFftMtfAtFrequency", BindingFlags.Static | BindingFlags.NonPublic)!;
var output = new List<object>();
foreach (var n in new[] { 8, 16, 24, 32, 48, 64, 128 })
foreach (var wavelength in optic.Wavelengths)
{
    var primary = optic.Wavelengths.First(w => w.IsPrimary);
    var field = (0d, 1d);
    var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength, n,
        cellCentered: true, aimAtStop: optic.RayAimingEnabled, referenceWavelength: primary);
    foreach (var frequency in n == 32 ? new[] { .1, .2, .4, .8, 1.6, 3.2 } : new[] { 3.2 })
    {
        var otf = ((Complex T, Complex S))method.Invoke(null,
            new object?[] { optic, field, wavelength, n, frequency * (args[0] == "paraxial" ? pupilRatio : 1), 0d, false, wavefront, null, primary })!;
        output.Add(new { n, wavelength.Micrometers, wavelength.Weight, frequency,
            t = new { otf.T.Real, otf.T.Imaginary, otf.T.Magnitude }, s = new { otf.S.Real, otf.S.Imaginary, otf.S.Magnitude } });
    }
    Console.WriteLine($"Pupil={n}; wavelength={wavelength.Micrometers:R}");
}
JsonFiles.Write(Path.Combine(root, (args[0] == "paraxial" ? "core-paraxial-probe-" : "core-probe-") + Hash(typeof(Optic).Assembly.Location)[..12] + ".json"), output);
