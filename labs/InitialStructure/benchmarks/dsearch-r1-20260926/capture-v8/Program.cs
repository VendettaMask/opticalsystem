using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

// Run only against retained v8 binaries. Reproduce the eight independently
// specified audit starts, checking every fingerprint before publishing inputs.
if (FlatStartAlgorithm.Version != "8") throw new InvalidOperationException("Requires v8 binaries.");
using var audit = JsonDocument.Parse(File.ReadAllText(args[0]));
var options = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var rows = new List<object>();
foreach (var row in audit.RootElement.GetProperty("Results").EnumerateArray()
    .Where(row => row.GetProperty("Mode").GetString() == "joint-ray-vector-continuous"))
{
    var spec = row.GetProperty("Target").Deserialize<InitialStructureSpecification>()!;
    var family = new FlatStartFamily
    {
        GlassNames = ["N-BK7", "N-F2", "N-BK7", "N-F2"],
        CenterThicknesses = [3, 3, 3, 3],
        AirGaps = [2, 2, 2],
        StopSurfaceIndex = 5,
        BinaryStart = new(row.GetProperty("Form").GetString()!, 10 * spec.EffectiveFocalLengthMillimeters)
    };
    var warmup = new FlatStartDesignService().Solve(spec, 4, family: family, screenOnly: true);
    var initial = warmup.Candidate!.Optic;
    if (ContentFingerprint.Compute(initial) != row.GetProperty("InitialOpticSha256").GetString())
        throw new InvalidOperationException("Historical starting snapshot changed.");
    rows.Add(new { Specification = spec, Family = family, Initial = initial, Reference = row.Clone() });
    Console.WriteLine($"Captured {spec.EffectiveFocalLengthMillimeters} {family.BinaryStart.Signs}");
}
File.WriteAllText(args[1], JsonSerializer.Serialize(rows, options) + "\n");
