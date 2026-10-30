using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static class LensLibraryService
{
    public static LensLibraryEntry Index(string id, LensSourceProvenance provenance, CompiledTopology source,
        AutonomousSearchWork? sourceAcquisitionWork = null)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var descriptor = CenteredPrescriptionScaling.Describe(source.CreateOptic(), source.Elements[^1].BackIndex);
        var entry = new LensLibraryEntry
        {
            Id = id,
            Provenance = provenance,
            Topology = source.Topology,
            Constraints = source.Constraints,
            SourceOpticFingerprint = source.OpticFingerprint,
            Descriptor = descriptor,
            IndexingWork = new() { FunctionEvaluations = 1, Elapsed = timer.Elapsed },
            SourceAcquisitionWork = sourceAcquisitionWork
        };
        Validate(new() { Entries = [entry] });
        return entry;
    }

    /// <summary>Bounded structural/metadata checks only: opening a library never dispatches optical work.</summary>
    public static void Validate(LensLibraryDocument library)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (library.Algorithm != "InitialStructureLensLibraryV1" || library.Version != 1 || !Text(library.Name, 256)
            || library.Entries.IsDefault || library.Entries.Length > 128)
            throw new ArgumentException("Unsupported or unbounded local lens library.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in library.Entries)
        {
            if (entry is null || !Text(entry.Id, 128) || !ids.Add(entry.Id) || entry.Provenance is not { } p
                || !Enum.IsDefined(p.Kind) || !Text(p.Title, 256) || !Text(p.Origin, 2048) || !Text(p.UsageAuthority, 2048)
                || !Hash(p.SourceDocumentSha256) || !Hash(entry.SourceOpticFingerprint) || entry.Topology is null || entry.Constraints is null || entry.IndexingWork is null
                || entry.Topology.HasTransitionElements || entry.Constraints.AllowTransitionElements || entry.Constraints.TransitionAllowance is not null
                || entry.Descriptor is not { } descriptor || !double.IsFinite(descriptor.EffectiveFocalLengthMillimeters) || descriptor.EffectiveFocalLengthMillimeters <= 0
                || !double.IsFinite(descriptor.FNumber) || descriptor.FNumber <= 0 || !double.IsFinite(descriptor.MaximumHalfFieldDegrees)
                || descriptor.MaximumHalfFieldDegrees < 0 || !double.IsFinite(descriptor.BackWorkingSpaceMillimeters)
                || !double.IsFinite(descriptor.BackFocalLengthMillimeters))
                throw new ArgumentException("Each lens needs a unique identity, declared lawful-use provenance, frozen source hash and finite descriptor.");
            TopologyValidation.Validate(entry.Topology, entry.Constraints);
            foreach (var work in new[] { entry.SourceAcquisitionWork, entry.IndexingWork }.OfType<AutonomousSearchWork>())
                if (work.FunctionEvaluations < 0 || work.GradientEvaluations < 0
                    || work.ValidationEvaluations < 0 || work.MatrixEvaluations < 0 || work.DifferenceEvaluations < 0
                    || work.JacobianBuilds < 0 || work.TracedRays < 0 || work.GeometryProbes < 0 || work.Elapsed < TimeSpan.Zero)
                    throw new ArgumentException("Known library acquisition work cannot be negative.");
        }
        foreach (var source in library.Entries.GroupBy(entry => entry.Provenance.SourceDocumentSha256, StringComparer.OrdinalIgnoreCase))
            if (source.Select(entry => entry.SourceAcquisitionWork).OfType<AutonomousSearchWork>().Select(ContentFingerprint.Compute).Distinct().Count() > 1)
                throw new ArgumentException("Entries from the same source document must agree on its acquisition cost.");
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Hash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
}
