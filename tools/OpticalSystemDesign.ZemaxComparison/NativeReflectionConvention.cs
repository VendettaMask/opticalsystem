using System.Text.Json;

namespace OpticalSystemDesign.ZemaxComparison;

/// <summary>Convert native signed-index direction/path reports into physical propagation vectors.</summary>
public static class NativeReflectionConvention
{
    public static int[] PropagationSigns(JsonElement model, IReadOnlyList<int>? ignored = null)
    {
        var surfaces = model.GetProperty("surfaces").EnumerateArray().ToArray();
        var signs = new int[surfaces.Length];
        var sign = 1;
        for (var i = 0; i < signs.Length; i++)
        {
            if (ignored?.Contains(i) != true
                && surfaces[i].GetProperty("Material").GetString()?.Equals("MIRROR", StringComparison.OrdinalIgnoreCase) == true)
                sign = -sign;
            signs[i] = sign;
        }
        return signs;
    }

    public static void ConvertRayReport(double[][] rows, int[] signs, JsonElement? model = null)
    {
        foreach (var row in rows)
        {
            var surface = checked((int)row[0]);
            if (surface < 1 || surface >= signs.Length) throw new InvalidDataException("Native report surface/model mismatch.");
            var before = signs[surface - 1];
            for (var coordinate = 4; coordinate <= 6; coordinate++) row[coordinate] *= signs[surface];
            // The native report orients its normal against the signed-index
            // incident vector; physical incident orientation follows its parity.
            for (var coordinate = 7; coordinate <= 9; coordinate++) row[coordinate] *= before;
            // A coordinate break leaves propagation unchanged. Its raw dummy
            // normal can keep the incoming frame's sign after the frame rotates.
            if (model is { } prescription
                && prescription.GetProperty("surfaces")[surface].GetProperty("Type").GetString() == "CoordinateBreak"
                && row[4] * row[7] + row[5] * row[8] + row[6] * row[9] > 0)
                for (var coordinate = 7; coordinate <= 9; coordinate++) row[coordinate] = -row[coordinate];
            row[11] *= before;
        }
    }
}
