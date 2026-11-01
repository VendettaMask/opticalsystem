using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison;

internal static class SourceProbe
{
    internal static void Run(string source, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        JsonFiles.Write(Path.Combine(output, "import.json"), new { source, sourceSha256 = JsonFiles.Hash(File.ReadAllBytes(source)),
            imageSpaceAfocal = optic.ImageSpaceAfocal, aperture = optic.SurfaceGroup.Items[1].PhysicalAperture?.Kind });
        foreach (var n in new[] { 32, 64 })
        {
            try
            {
                var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens PSF", new Dictionary<string, string>
                {
                    ["PupilSampling"] = n.ToString(), ["ImageSampling"] = "32", ["ImageDeltaMilliradians"] = ".02",
                    ["WavelengthNumber"] = "1", ["FieldNumber"] = "1", ["UseCentroid"] = "False"
                });
                JsonFiles.Write(Path.Combine(output, "n" + n + ".json"), data);
                Console.WriteLine($"{n}: {data.Outcome}");
            }
            catch (Exception e)
            {
                JsonFiles.Write(Path.Combine(output, "n" + n + "-failure.json"), new { error = e.ToString() });
                Console.WriteLine($"{n}: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
