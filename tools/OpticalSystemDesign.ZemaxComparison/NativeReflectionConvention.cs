using System.Text.Json;

namespace OpticalSystemDesign.ZemaxComparison;

/// <summary>Convert native signed-index direction/path reports into physical propagation vectors.</summary>
public static class NativeReflectionConvention
{
    public static int[] PropagationSigns(JsonElement model)
    {
        var surfaces = model.GetProperty("surfaces").EnumerateArray().ToArray();
        var signs = new int[surfaces.Length];
        var sign = 1;
        for (var i = 0; i < signs.Length; i++)
        {
            if (surfaces[i].GetProperty("Material").GetString()?.Equals("MIRROR", StringComparison.OrdinalIgnoreCase) == true)
                sign = -sign;
            signs[i] = sign;
        }
        return signs;
    }

    public static void ConvertRayReport(double[][] rows, int[] signs)
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
            row[11] *= before;
        }
    }
}
