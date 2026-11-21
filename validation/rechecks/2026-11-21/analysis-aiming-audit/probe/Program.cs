using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.FileIO;

var input = File.ReadAllBytes(args[0]);
var source = OpticalFormatCatalog.Import(ZemaxZmxReader.Decode(input), ".zmx");
var rows = new List<object>();
foreach (var aiming in new[] { false, true })
{
    var optic = Optic.FromSnapshot(source.ToSnapshot());
    optic.RayAimingEnabled = aiming;
    var geometric = new GeometricMtfAnalysis(optic, 6, "hexapolar", 5, 100, false).GenerateData();
    var radiant = new RadiantIntensityAnalysis(optic, 9, 9, -89, 89, -89, 89,
        numRays: 6, distribution: "hexapolar", normalize: false).GenerateData();
    // An explicit large detector window isolates pupil acceptance from detector cropping.
    var detectorOptic = Optic.FromSnapshot(optic.ToSnapshot());
    detectorOptic.SurfaceGroup.Items[^1].PhysicalAperture = new RectangularAperture(50, 50);
    var irradiance = new IncoherentIrradianceAnalysis(detectorOptic, 6, 16, 16,
        distribution: "hexapolar", normalize: false).GenerateData();
    var grid = ImageSimulationEngine.GenerateDistortionGrid(optic, optic.Wavelengths[0],
        8, 8, numGridPoints: 7, degree: 3, halfFieldX: .25, halfFieldY: .25);
    var row = new
    {
        aiming,
        radiantRays = radiant.Values["ValidRayCount"],
        irradianceRays = irradiance.Values["ValidRayCount"],
        mtf = geometric.PlotSeries.Select(s => s.Points.Select(p => new { p.X, p.Y }).ToArray()).ToArray(),
        distortion = grid.Cast<(double X, double Y)>().Select(p => new { p.X, p.Y }).ToArray()
    };
    rows.Add(row);
    Console.WriteLine($"Aiming={aiming}: radiant={row.radiantRays}, irradiance={row.irradianceRays}, MTF first={geometric.PlotSeries[0].Points[1].Y:R}");
}
File.WriteAllText(args[1], JsonSerializer.Serialize(new
{
    input = args[0], sha256 = Convert.ToHexStringLower(SHA256.HashData(input)), rows
}, new JsonSerializerOptions { WriteIndented = true }));
