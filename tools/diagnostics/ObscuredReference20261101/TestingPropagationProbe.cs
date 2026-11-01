using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class TestingPropagationProbe
{
    internal static readonly string[] Lenses = ["Optical testing/Double-pass null test.zmx", "Optical testing/Mach Zender Interferometer.zmx", "Optical testing/Twyman Green Interferometer.zmx"];
    internal static async Task Run(string repository, string output, string coreHash)
    {
        var records = new List<object>();
        foreach (var lens in Lenses)
        {
            var imported = await WorkbenchExecutor.Import(Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens));
            for (var configuration = 0; configuration < imported.Configurations.Count; configuration++)
            {
                var optic = imported.Configurations[configuration];
                JsonFiles.Write(Path.Combine(output, $"{Path.GetFileNameWithoutExtension(lens)}-c{configuration + 1}.optic.json"), optic.ToSnapshot());
                var clear = Optic.FromSnapshot(optic.ToSnapshot()); foreach (var s in clear.SurfaceGroup.Items) s.PhysicalAperture = null;
                foreach (var aiming in new[] { false, true })
                {
                    var rays = new List<object>();
                    foreach (var pupil in new[] { (0d, 0d), (.1, 0d), (0d, .1), (.5, 0d), (0d, .5), (.95, 0d), (0d, .95) })
                    {
                        var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, pupil.Item1, pupil.Item2, optic.Wavelengths[0].Micrometers, aiming).Rays.Single();
                        var trace = optic.SequentialRayTracer.Trace(new([source]));
                        var failure = optic.SequentialRayTracer.DiagnoseFirstFailure(source);
                        var unclipped = clear.SequentialRayTracer.DiagnoseFirstFailure(source);
                        rays.Add(new { pupil, source, failure, apertureFreeDiagnostic = unclipped, samples = trace.RayHistories.Single() });
                    }
                    records.Add(new { lens, configuration = configuration + 1, aiming, coreSha256 = coreHash,
                        entrancePupil = optic.Paraxial.EstimateEntrancePupilDiameter(), entranceLocation = optic.Paraxial.EstimateEntrancePupilLocation(),
                        surfaces = optic.SurfaceGroup.Items.Select((s, i) => new { i, s.Label, s.Thickness, s.IsStop, s.IsReflective, s.CoordinateSystem,
                            geometry = s.Geometry.Kind, before = s.MaterialBefore.Name, after = s.MaterialAfter.Name }), rays });
                    Console.WriteLine($"{lens} c{configuration + 1} aiming={aiming}: chief={optic.SequentialRayTracer.DiagnoseFirstFailure(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, optic.Wavelengths[0].Micrometers, aiming).Rays.Single())}");
                }
            }
        }
        JsonFiles.Write(Path.Combine(output, "probe.json"), records);
    }
}
