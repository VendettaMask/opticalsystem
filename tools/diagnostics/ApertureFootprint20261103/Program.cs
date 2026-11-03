using System.Security.Cryptography;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;

var repo = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Fresh output required");
Directory.CreateDirectory(output);
if (args.Length > 2 && args[2] == "convergence") { await Convergence.Run(repo, output); return; }
if (args.Length > 2 && args[2] is "native" or "compare")
{
    await NativeControls.Run(repo, output, args[2]); return;
}
var records = new List<object>();
foreach (var name in new[] { "Telescopes/Unobscured Gregorian.zmx", "Telescopes/Yolo.zmx", "Telescopes/Schmidt-Cassegrain spider obscuration.zmx", "Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx" })
{
    var source = Path.Combine(repo, "src/OpticalSystemDesign.App/Assets/SequentialExamples", name);
    var optic = (await WorkbenchExecutor.Import(source)).Configurations[0];
    var wave = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
    JsonFiles.Write(Path.Combine(output, Path.GetFileName(name) + ".optic.json"), optic.ToSnapshot());
    var rays = new List<object>();
    foreach (var field in new[] { (0d, 0d), (0d, 1d) })
    foreach (var p in new[] { (0d, 0d), (1d, 0d), (-1d, 0d), (0d, 1d), (0d, -1d), (.5, .5), (-.5, -.5), (.5, -.5), (-.5, .5) })
    {
        var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Item1, field.Item2, p.Item1, p.Item2, wave.Micrometers, optic.RayAimingEnabled).Rays.Single();
        var trace = optic.SequentialRayTracer.TraceFinalSamples(new([ray])).Single();
        var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(ray);
        rays.Add(new { field, p, ray, trace, diagnostic });
    }
    var surfaces = optic.SurfaceGroup.Items.Select((s, i) => new { i, s.IsStop, s.SemiDiameter, automatic = !s.SemiDiameterFixed, s.SemiDiameterMaximum, frame = s.CoordinateSystem, geometry = s.Geometry.GetType().Name, aperture = s.PhysicalAperture, type = s.PhysicalAperture?.GetType().Name }).ToArray();
    records.Add(new { name, wave.Nanometers, optic.RayAimingEnabled, surfaces, rays });
    Console.WriteLine(name);
    foreach (var s in surfaces) Console.WriteLine($"{s.i} stop={s.IsStop} semi={s.SemiDiameter:G9} automatic={s.automatic} aperture={s.type}: {s.aperture}");
    Console.WriteLine("physical=" + rays.Count);
}
JsonFiles.Write(Path.Combine(output, "probe.json"), new { coreHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(WavefrontEngine).Assembly.Location))), records });
