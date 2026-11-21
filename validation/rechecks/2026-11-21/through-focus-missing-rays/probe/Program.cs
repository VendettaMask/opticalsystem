using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core;

var bytes = File.ReadAllBytes(args[0]);
var optic = OpticalFormatCatalog.Import(ZemaxZmxReader.Decode(bytes), ".zmx");
var data = new ThroughFocusAnalysis(optic, new ThroughFocusSpotSettings(
    RayDensity: 6, DefocusStepMicrometers: 50, Reference: "chief")).GenerateData();
var panes = data.PlotPanes!.Select((pane, index) => new
{
    index,
    pane.Title,
    pane.PlotOptions,
    pane.Metrics,
    series = pane.Series.Select(series => new
    {
        series.Name,
        count = series.Points.Count,
        xMin = series.Points.Min(p => p.X), xMax = series.Points.Max(p => p.X),
        yMin = series.Points.Min(p => p.Y), yMax = series.Points.Max(p => p.Y),
        points = series.Points.Select(p => new { p.X, p.Y }).ToArray()
    }).ToArray()
}).ToArray();
File.WriteAllText(args[1], JsonSerializer.Serialize(new
{
    input = args[0], sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
    data.Values, panes
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var pane in panes)
    Console.WriteLine($"{pane.index}: {pane.Title.Replace('\n', '/')} | " +
        string.Join("; ", pane.series.Select(s => $"{s.Name}: {s.count}, X={s.xMin:G5}..{s.xMax:G5}, Y={s.yMin:G5}..{s.yMax:G5}")));

if (args.Length > 2 && args[2] == "verify")
{
    if (!optic.RayAimingEnabled || panes.Length != 15 || panes.Any(p => p.series.Any(s => s.count != 127)))
        throw new InvalidOperationException("The supplied reproduction must retain all 127 rays in each of its 45 field/plane/wavelength sets.");
    var referenceRms = new[] { 1.162, 1.624, 2.188 };
    var referenceGeo = new[] { 2.472, 3.213, 3.963 };
    for (var field = 0; field < 3; field++)
    {
        var metrics = panes[field * 5 + 2].Metrics!;
        if (Math.Abs(metrics[0].Value - referenceRms[field]) > .0005
            || Math.Abs(metrics[1].Value - referenceGeo[field]) > .0005)
            throw new InvalidOperationException($"Field {field + 1} does not match the screenshot at its displayed 0.001 µm precision.");
    }
    Console.WriteLine("VERIFIED: 45 complete ray sets; 6 nominal RMS/GEO values agree with the supplied screenshot's displayed precision.");
    return;
}

var fields = SpotAnalysisEngine.DefinedFields(optic);
var pupils = ApertureSampler.GenerateHexapolarRings(6);
for (var f = 0; f < fields.Count; f++)
    foreach (var wave in optic.Wavelengths)
    {
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            fields[f].Hx, fields[f].Hy, wave.Micrometers, pupils);
        var mask = bundle.Rays.Select(ray => optic.SequentialRayTracer.DiagnoseApertures(ray)).ToArray();
        Console.WriteLine($"MASK field={f + 1} wave={wave.Micrometers}: " + string.Join(", ",
            mask.GroupBy(d => d.FirstBlockedSurfaceIndex).Select(g => $"surface {g.Key?.ToString() ?? "none"}={g.Count()}")));
    }
foreach (var variant in new[] { "normal", "aimed", "diagnostic-without-apertures" })
{
    var copy = Optic.FromSnapshot(optic.ToSnapshot());
    if (variant == "diagnostic-without-apertures")
        foreach (var surface in copy.SurfaceGroup.Items) surface.PhysicalAperture = null;
    var result = SpotAnalysisEngine.Generate(copy, fields, copy.Wavelengths, 6, "hexapolar",
        reference: "chief", aimAtStop: variant == "aimed");
    Console.WriteLine($"VARIANT {variant}: " + string.Join("; ", result.Fields.Select(field =>
        $"counts={string.Join('/', field.Wavelengths.Select(w => w.Rays.Count))} rms={SpotAnalysisEngine.RmsRadius(field.WeightedRays.ToArray()) * 1000:G10}")));
}
