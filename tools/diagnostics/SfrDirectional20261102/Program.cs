using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;

var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
var results = new List<object>();
foreach (var path in new[] { "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx",
    "artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX" })
{
    var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, path)), ".zmx");
    for (var field = 1; field <= optic.Fields.Count; field++)
    {
        try
        {
            var settings = new SlantedEdgeSettings(FieldNumber: field, PupilSampling: 256, ImageSamplesPerPixel: 8, PsfRadiusPixels: 32);
            var frame = MonochromeSlantedEdge.Render(optic, settings);
            var point = optic.Fields[field - 1]; var normalized = FieldCoordinates.Normalize(optic.Fields, point.X, point.Y);
            var slope = frame.Measurement.EdgeSlope; var nx = 1 / Math.Sqrt(1 + slope * slope); var ny = -slope * nx;
            var frequencies = frame.Measurement.Curve.Select(p => p.CyclesPerMillimeter).ToArray();
            var full = DiffractionEngine.ComputeLocalPupilMtfAlongDirection(optic, (normalized.X, normalized.Y), new() { Micrometers = .55 }, settings.PupilSampling, frequencies, nx, ny);
            var fullFine = DiffractionEngine.ComputeLocalPupilMtfAlongDirection(optic, (normalized.X, normalized.Y), new() { Micrometers = .55 }, settings.PupilSampling * 2, frequencies, nx, ny);
            double Sinc(double x) => Math.Abs(x) < 1e-12 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
            var error = full.Select((m, i) => Math.Abs(m * Sinc(frame.Measurement.Curve[i].CyclesPerPixel * nx) * Sinc(frame.Measurement.Curve[i].CyclesPerPixel * ny) - frame.Measurement.Curve[i].Response)).Max();
            results.Add(new { path, field, Algorithm = SlantedEdgeExperiment.Algorithm, settings, frame.RelativePsfReadoutIntegral, frame.MaximumSfrReferenceDifference,
                FullPupilSfrError = error, FullPupilWindowError = full.Zip(frame.OpticalMtf, (a, b) => Math.Abs(a - b.Response)).Max(),
                FullPupilSamplingError = full.Zip(fullFine, (a, b) => Math.Abs(a - b)).Max() });
        }
        catch (Exception e) { results.Add(new { path, field, Error = e.Message }); }
        Console.WriteLine(JsonSerializer.Serialize(results[^1]));
    }
}
await File.WriteAllTextAsync(Path.Combine(output, "directional-probe.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
