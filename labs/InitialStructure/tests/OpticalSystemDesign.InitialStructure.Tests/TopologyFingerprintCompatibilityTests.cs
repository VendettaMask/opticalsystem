using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.Validation;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologyFingerprintCompatibilityTests
{
    [Fact]
    public void OriginalAssessmentRetainsIdentityAcrossSchemaAndUnusedCatalogRetirement()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) directory = directory.Parent;
        var path = Path.Combine(directory!.FullName,
            "validation/rechecks/2026-10-27/autonomous-recovery-progress/recovery-True-resumed.autonomous.json");
        using var document = JsonDocument.Parse(EvidenceFiles.ReadAllText(path));
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var checkpoint = document.RootElement.GetProperty("checkpoint").Deserialize<AutonomousSearchCheckpoint>(options)!;
        var candidate = checkpoint.Candidates[0];
        var compiled = TopologyCompiler.Compile(candidate.Topology, checkpoint.Plan.Constraints);
        Assert.NotEqual(compiled.OpticFingerprint, candidate.Assessment.OpticFingerprint);
        Assert.True(compiled.MatchesOpticFingerprint(candidate.Assessment.OpticFingerprint));
        Assert.True(compiled.MatchesOpticFingerprint(compiled.OpticFingerprint));
        Assert.False(compiled.MatchesOpticFingerprint(new string('0', 64)));
        var optic = compiled.CreateOptic();
        optic.SurfaceGroup.Items[compiled.Elements[0].FrontIndex].Radius += .1;
        var changed = TopologyCompiler.Update(compiled, optic.ToSnapshot());
        Assert.False(changed.MatchesOpticFingerprint(candidate.Assessment.OpticFingerprint));
        Assert.False(changed.MatchesOpticFingerprint(compiled.OpticFingerprint));
    }
}
