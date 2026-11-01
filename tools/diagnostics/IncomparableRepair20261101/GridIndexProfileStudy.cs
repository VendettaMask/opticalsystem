using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.ZemaxComparison;

internal static class GridIndexProfileStudy
{
    internal static async Task Run(string study)
    {
        var output = Path.Combine(study, "index-profile-assessment.json");
        if (File.Exists(output)) throw new IOException("Fresh index-profile assessment required");
        var records = new List<object>();
        foreach (var model in JsonFiles.Read<JsonElement[]>(Path.Combine(study, "models.json")))
        {
            var native = model.GetProperty("native").GetString()!;
            var captured = JsonFiles.Read<JsonElement>(Path.Combine(native, "captured-settings.json"));
            var request = captured.GetProperty("request");
            var input = request.GetProperty("input").GetString()!;
            var optic = await new ZemaxZmxImporter().ImportFileAsync(input);
            var profile = (GridGradientIndexProfile)((GradientIndexMaterial)optic.SurfaceGroup.Items[1].MaterialAfter).Profile;
            var wavelength = optic.Wavelengths[captured.GetProperty("selectors").GetProperty("Wavelength").GetInt32() - 1].Nanometers;
            var dataPath = Path.Combine(native, "data.json");
            var groups = JsonFiles.Read<JsonElement>(dataPath).GetProperty("dataSeries");
            if (groups.GetArrayLength() != 1) throw new InvalidDataException("One direct index curve required");
            var series = groups[0]; var xs = series.GetProperty("x"); var ys = series.GetProperty("y");
            var axis = model.GetProperty("axis").GetString()!;
            var points = new List<object>(); var globalSquare = 0d; var querySquare = 0d; var globalMax = 0d; var queryMax = 0d;
            for (var i = 0; i < xs.GetArrayLength(); i++)
            {
                var q = xs[i].GetDouble(); var expected = ys[i][0].GetDouble();
                var point = new Vector3D(axis == "X" ? q : model.GetProperty("x").GetDouble(),
                    axis == "Y" ? q : model.GetProperty("y").GetDouble(), axis == "Z" ? q : model.GetProperty("z").GetDouble());
                var global = profile.Evaluate(point, wavelength).Index;
                var query = profile.InterfaceRefractiveIndex(point, wavelength);
                globalSquare += Math.Pow(global - expected, 2); querySquare += Math.Pow(query - expected, 2);
                globalMax = Math.Max(globalMax, Math.Abs(global - expected)); queryMax = Math.Max(queryMax, Math.Abs(query - expected));
                points.Add(new { coordinate = q, nativeIndex = expected, continuousGlobalIndex = global, localQueryIndex = query });
            }
            records.Add(new { model, globalMax, globalRms = Math.Sqrt(globalSquare / points.Count), queryMax,
                queryRms = Math.Sqrt(querySquare / points.Count), points,
                nativeDataSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dataPath))) });
        }
        JsonFiles.Write(output, new { diagnosticOnly = true, acceptanceSubstitution = false, records,
            coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))) });
    }
}
