using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

/// <summary>Portable research evidence; neither an autonomous checkpoint nor permission to export an unvalidated null state.</summary>
public sealed record SpcBranchStudy(AutonomousSearchPlan Plan, ParaxialSeedSearchOptions Budget, SpcBranchOptions Policy,
    SpcBranchSearchResult Result, bool DirectGrowthControl = false);

public static class SpcBranchStudyFiles
{
    private const string Identity = "initial-structure-spc-study-v1";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static Task SaveAsync(SpcBranchStudy study, string path, CancellationToken token = default)
    {
        Validate(study);
        return BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "SPC branch study",
            (stream, cancellation) => JsonSerializer.SerializeAsync(stream, new Envelope(Identity, study, ContentFingerprint.Compute(study)), Json, cancellation), token);
    }
    public static async Task<SpcBranchStudy> LoadAsync(string path, CancellationToken token = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes, "SPC branch study", token);
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(bytes, Json) ?? throw new InvalidDataException("Empty SPC study.");
            if (envelope.Algorithm != Identity || envelope.Study is null || envelope.Sha256 != ContentFingerprint.Compute(envelope.Study))
                throw new InvalidDataException("SPC study identity/checksum mismatch.");
            Validate(envelope.Study); return envelope.Study;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or TopologyValidationException or NullReferenceException)
        { throw new InvalidDataException("Invalid SPC branch study.", error); }
    }
    private static void Validate(SpcBranchStudy study)
    {
        ArgumentNullException.ThrowIfNull(study);
        var result = study.Result;
        if (result.ProtocolFingerprint != SpcBranchSearch.GetProtocolFingerprint(study.Plan, study.Budget, study.Policy, study.DirectGrowthControl)
            || result.Algorithm != (study.DirectGrowthControl ? "initial-structure-direct-growth-control-v1" : "initial-structure-spc-branches-v1")
            || result.Parents.Length > study.Policy.MaximumParents || result.Branches.Length > 2 * study.Policy.MaximumParents
            || result.Branches.Select(b => b.Id).Distinct().Count() != result.Branches.Length
            || !result.QualifiedBranchIds.SequenceEqual(result.Branches.Where(b => b.Outcome == "QualifiedOriginalSpecification"
                && b.Assessment is { Completed: true, MeetsTargets: true } && b.Endpoint is { HasTransitionElements: false }).Select(b => b.Id)))
            throw new InvalidDataException("SPC study protocol or branch identities do not match.");
        foreach (var branch in result.Branches)
        {
            if (branch.Sign is not (-1 or 1) || !result.Parents.Any(p => p.Id == branch.ParentId)
                || branch.Stages.Length > study.Policy.ThicknessMultipliers.Length + study.Policy.MaximumMaterialAlternatives)
                throw new InvalidDataException("Invalid SPC branch lineage or stage count.");
            Check(branch.Endpoint, branch.Assessment);
            foreach (var stage in branch.Stages) Check(stage.Topology, stage.Assessment);
        }
        void Check(DesignTopology? topology, TopologyAssessment? assessment)
        {
            if (topology is null || assessment is null) return;
            var compiled = TopologyCompiler.Compile(topology, study.Plan.Constraints);
            if (topology.HasTransitionElements || assessment.StructuralConstraints != study.Plan.Constraints
                && ContentFingerprint.Compute(assessment.StructuralConstraints) != ContentFingerprint.Compute(study.Plan.Constraints)
                || ContentFingerprint.Compute(assessment.Targets) != ContentFingerprint.Compute(study.Plan.Targets)
                || assessment.ProtocolFingerprint != TopologyValidator.GetProtocolFingerprint(compiled, study.Plan.Targets))
                throw new InvalidDataException("SPC physical evidence changed original acceptance inputs.");
        }
    }
    private sealed record Envelope(string Algorithm, SpcBranchStudy Study, string Sha256);
}
