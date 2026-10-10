using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Analysis;

/// <summary>Immutable provenance of traced inputs, independent of caller edits to their phase.</summary>
public sealed class PupilSourceIdentity
{
    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private PupilSourceIdentity(string fingerprint) => Fingerprint = fingerprint;
    public string Fingerprint { get; }

    internal static PupilSourceIdentity? Capture(Optic optic)
    {
        // Unknown models/backends must not acquire a false identity from a lossy
        // name-only serializer. Automatic tracing remains available for them.
        if (optic.Backend.Current is not ManagedCpuBackend || !Known(optic.Apodization)
            || optic.SurfaceGroup.Items.Any(surface => !Known(surface.Geometry)
                || !Known(surface.MaterialBefore) || !Known(surface.MaterialAfter)
                || !Known(surface.CoatingModel) || !Known(surface.InteractionModel)
                || !Known(surface.PhysicalAperture) || !Known(surface.ScatteringModel)))
            return null;

        try
        {
            var snapshot = optic.ToSnapshot();
            // Labels and merit-function editing do not change already resolved
            // optical geometry. All physical component parameters are retained.
            snapshot = snapshot with
            {
                Name = "", MeritOperands = [],
                Fields = snapshot.Fields.Select(field => field with { Label = "" }).ToList(),
                Wavelengths = snapshot.Wavelengths.Select(wave => wave with { Label = "" }).ToList(),
                Surfaces = snapshot.Surfaces.Select(surface => surface with { Label = "" }).ToList()
            };
            var element = JsonSerializer.SerializeToElement(snapshot, Options);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
            return new("optic-pupil/v1:" + Convert.ToHexString(SHA256.HashData(stream.ToArray())));
        }
        catch (NotSupportedException)
        {
            // An unpersistable custom geometry is still traceable; it cannot be
            // safely reused as a prepared input without a lossless identity.
            return null;
        }
    }

    private static bool Known(object? component)
    {
        if (component is null) return true;
        if (component.GetType().Assembly != typeof(Optic).Assembly) return false;
        return component switch
        {
            // Ordinary material snapshots omit their pluggable propagation
            // model. GRIN owns a sealed spatial model and persists its full
            // profile/integration configuration; all other models must be the
            // stateless homogeneous implementation to permit prepared reuse.
            IMaterial material => material is GradientIndexMaterial
                || material.PropagationModel is HomogeneousPropagationModel,
            BooleanAperture aperture => Known(aperture.Left) && Known(aperture.Right),
            CoherentMultilayerCoating coating => coating.Layers.All(layer => Known(layer.Material)),
            PhaseInteractionModel phase => Known(phase.Profile),
            _ => true
        };
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }
}
