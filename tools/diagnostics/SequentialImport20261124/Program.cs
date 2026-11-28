using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Rays;

if (args[0] == "--rochon-native")
{
    await RochonNative.Check(args[1], args[2]);
    return;
}
var root = Path.GetFullPath(args[0]);
var directory = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "index.json")));
var entries = new List<object>();
foreach (var entry in manifest.RootElement.GetProperty("Files").EnumerateArray())
{
    var relative = entry.GetProperty("RelativePath").GetString()!;
    var path = Path.Combine(directory, relative);
    var source = File.ReadAllText(path);
    var sourceCount = Regex.Matches(source, @"(?m)^SURF \d+\s*$").Count;
    try
    {
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(path);
        var configurations = imported.Configurations.Select((optic, ci) =>
        {
            var surfaces = optic.SurfaceGroup.Items.Select((s, i) => new
            {
                Source = imported.SourceSurfaceNumbers[ci][i],
                s.Number, Geometry = s.Geometry.Kind, Interaction = s.InteractionModel.Kind,
                Exit = (s.InteractionModel as BirefringentInteractionModel)?.IsExit,
                s.Thickness, s.Radius, s.Conic, Material = s.MaterialAfter.Name,
                Coordinates = s.CoordinateSystem,
                CrystalAxis = (s.MaterialAfter as UniaxialMaterial)?.Axis,
                Extraordinary = (s.MaterialAfter as UniaxialMaterial)?.TraceExtraordinary
            }).ToArray();
            var rays = new List<object>();
            foreach (var py in new[] { -.5, 0, .5 })
            {
                try
                {
                    var input = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, py,
                        optic.Wavelengths.First(w => w.IsPrimary).Micrometers, false).Rays.Single();
                    using var trace = optic.SequentialRayTracer.Trace(new RealRayBundle([input]), TraceRequest.FinalOnly(false));
                    var found = trace.TryGetSample(0, optic.SurfaceGroup.Items.Count - 1, out var hit) && !hit.Vignetted;
                    rays.Add(new { Py = py, Position = found ? (object)hit.Position : null, Direction = found ? (object)hit.Direction : null, Error = (string?)null });
                }
                catch (Exception ex) { rays.Add(new { Py = py, Position = (object?)null, Direction = (object?)null, Error = ex.GetType().Name + ": " + ex.Message }); }
            }
            return new { Index = ci, Count = surfaces.Length, Surfaces = surfaces, Rays = rays };
        }).ToArray();
        entries.Add(new { Path = relative, Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), SourceCount = sourceCount, Configurations = (object)configurations, Error = (string?)null });
    }
    catch (Exception ex) { entries.Add(new { Path = relative, SourceCount = sourceCount, Configurations = (object?)null, Error = ex.ToString() }); }
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(entries, new JsonSerializerOptions
{
    WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
}));
Console.WriteLine($"Audited {entries.Count} menu sources: {args[1]}");
