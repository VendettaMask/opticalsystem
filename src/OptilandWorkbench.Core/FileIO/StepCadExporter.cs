using OptilandWorkbench.Core.Capabilities;

namespace OptilandWorkbench.Core.FileIO;

public sealed record StepCadExportOptions(
    int SurfaceSamples = 33,
    int AngularSamples = 64,
    string? ProductName = null,
    DateTimeOffset? CreatedUtc = null,
    double MaximumChordErrorMillimeters = 0.005,
    int MaximumTrianglesPerPart = 500_000);

public static class StepCadExporter
{
    public static string Serialize(
        Optic optic,
        StepCadExportOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Build(optic, options, cancellationToken).Content;

    public static StepCadDocument Build(
        Optic optic,
        StepCadExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(optic);
        options ??= new StepCadExportOptions();
        OpticCapabilityPreflight.EnsureSupported(
            optic,
            OpticCapabilityOperation.Export,
            "STEP");
        return StepCadAssemblyWriter.Build(optic, options, cancellationToken);
    }
}
