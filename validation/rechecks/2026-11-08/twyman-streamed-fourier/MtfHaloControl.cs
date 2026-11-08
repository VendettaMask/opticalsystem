using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true, WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter() }
};
var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(args[0]), options)!);
var wave = optic.Wavelengths[0];
const int pupilSize = 128, lagLimit = 31;
var privateFlags = BindingFlags.NonPublic | BindingFlags.Static;
var fft = (Action<Complex[]>)typeof(DiffractionEngine).GetMethod("Fft", privateFlags)!
    .CreateDelegate(typeof(Action<Complex[]>));
var prepare = typeof(DiffractionEngine).GetMethod("ComputeFftPsfCore", privateFlags)!;
object?[] parameters = [optic, (0d, 0d), wave, pupilSize, 2*pupilSize, false, true, 0d, null, null, true,
    false, null, wave, -1d, false, null, null, null, false, 2, null, false];
var metadata = (PsfResult)prepare.Invoke(null, parameters)!;
var pupil = (Complex[,])parameters[16]!;
var dense = DiffractionEngine.FourierPupilAutocorrelation(pupil, 2*pupilSize);
var cases = new List<object>();
foreach (var blockRows in new[] { 13, 24, 47 })
{
    var nfft = 1;
    while (nfft < 2 * (blockRows + lagLimit)) nfft *= 2;
    var power = new Complex[nfft];
    var whole = new Complex[nfft];
    var halo = new Complex[nfft];
    for (var blockStart = 0; blockStart < pupilSize; blockStart += blockRows)
    {
        var first = Math.Max(0, blockStart - lagLimit);
        var end = Math.Min(pupilSize, blockStart + blockRows);
        for (var column = 0; column < pupilSize; column++)
        {
            Array.Clear(whole);
            Array.Clear(halo);
            for (var row = first; row < end; row++)
            {
                var value = pupil[row, column];
                whole[row - first] = value;
                if (row < blockStart) halo[row - first] = value;
            }
            fft(whole);
            fft(halo);
            for (var i = 0; i < nfft; i++) power[i] += Power(whole[i]) - Power(halo[i]);
        }
    }
    fft(power);
    var error = Enumerable.Range(0, lagLimit + 1)
        .Max(lag => (power[lag] / power[0] - dense.Tangential[lag]).Magnitude);
    if (error > 1e-12) throw new InvalidOperationException($"Halo counting failed: {error:R}");
    Console.WriteLine($"B={blockRows}, L={lagLimit}, maximum complex OTF difference {error:R}");
    cases.Add(new { blockRows, lagLimit, nfft, maximumComplexOtfDifference = error });
}
var tangentialFNumber = metadata.TangentialFourierFNumber ?? metadata.TangentialWorkingFNumber;
var sagittalFNumber = metadata.SagittalFourierFNumber ?? metadata.SagittalWorkingFNumber;
const int proposedRows = 256, splineGuardLags = 24;
var planned = new[] { 5120, 6144 }.Select(n =>
{
    var lastRequestedLag = (int)Math.Ceiling(50 * (n - 1) * wave.Micrometers * .001 * Math.Max(tangentialFNumber, sagittalFNumber));
    var haloRows = lastRequestedLag + splineGuardLags;
    var fourierSize = 1;
    while (fourierSize < 2 * (proposedRows + haloRows)) fourierSize *= 2;
    return new { nominalSampling = n, lastRequestedLag, haloRows, proposedRows, fourierSize,
        complexSlabCells = (long)n * (proposedRows + haloRows) };
}).ToArray();
File.WriteAllText(args[1], JsonSerializer.Serialize(new
{
    pupilSize, metadata.PupilPhaseSampling, cases, tangentialFNumber, sagittalFNumber, planned,
    scope = "Exact same-grid arithmetic control. It does not certify this coarse physical pupil."
}, options));

static double Power(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;

