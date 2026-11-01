using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison;

internal static class FlatProbe
{
    internal static void Prepare(string prior, string root)
    {
        var native = Path.Combine(root, "flat-native"); Directory.CreateDirectory(native);
        var optic = new Optic("Afocal uniform pupil integration control") { ImageSpaceAfocal = true };
        optic.Aperture.Value = 10; optic.Solves.KeepImageAtBackFocus = false;
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() { Thickness = double.PositiveInfinity, SemiDiameter = 50 },
            new() { Thickness = 10, IsStop = true, SemiDiameter = 5 },
            new() { Thickness = 10, SemiDiameter = 50 },
            new() { SemiDiameter = 50 }
        ]);
        var source = Path.Combine(native, "flat.ZMX");
        File.WriteAllText(source, OpticalFormatCatalog.Export(optic, ".zmx"));
        JsonFiles.Write(Path.Combine(native, "flat.optic.json"), optic.ToSnapshot());
        var jobs = new List<object>();
        foreach (var sampling in new[] { 32, 64, 128 })
        {
            var request = JsonFiles.Read<Dictionary<string, JsonElement>>(Path.Combine(prior, "native/m3-fourier.request.json"));
            JsonElement E(object v) => JsonSerializer.SerializeToElement(v, JsonFiles.Options);
            var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
            request["input"] = E(source); request["inputSha256"] = E(sha);
            request["configuration"] = E(1); request["field"] = E(1); request["wavelength"] = E(1);
            request["analysisType"] = E("FftThroughFocusMtf"); request.Remove("polygonDependencies");
            request["zemaxSettings"] = E(new Dictionary<string, object>
            {
                ["SampleSize"] = $"S_{sampling}x{sampling}", ["Field"] = 1, ["Wavelength"] = 1,
                ["Frequency"] = 3.2, ["UsePolarization"] = false, ["UseDashes"] = false,
                ["Type"] = "Real", ["DeltaFocus"] = .25, ["NumberOfSteps"] = 21
            });
            var id = "flat" + sampling; JsonFiles.Write(Path.Combine(native, id + ".request.json"), request);
            jobs.Add(new { id, request = id + ".request.json", inputSha256 = sha });
        }
        JsonFiles.Write(Path.Combine(native, "jobs.json"), jobs);
        Console.WriteLine("Prepared three uniform-pupil defocus controls");
    }
}
