using System.Text.Json;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static class NativeReferenceDiagnostics
{
    public static void Validate(JsonElement report)
    {
        if (!report.TryGetProperty("messages", out var messages)) return;
        if (messages.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Native diagnostic metadata must be an array");
        // ZOS can publish an invalid or undersampled result with ErrorCode=Success.
        // A precision reference must therefore have no unresolved diagnostics.
        // Preserve the message payload; do not infer severity from localized text.
        if (messages.GetArrayLength() != 0)
            throw new InvalidDataException("Native report emitted diagnostics; numerical reference requires review: " + messages.GetRawText());
    }
}
