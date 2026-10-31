using System.Security.Cryptography;
using System.Text.Json;

namespace OpticalSystemDesign.ZemaxComparison.Diagnostics;

public sealed record FftFieldCapture(int Configuration, int Sampling, double[] Frequencies, Series1DResult[] Curves, string DataSha256);
public sealed record FftFieldCaptureInspection(string Id, CaptureStatus Status, string Reason, string? DataSha256 = null);

// Completed execution and final buffers are authoritative. An intermediate null result or old poll error is not a final verdict.
public static class FftFieldCaptureAudit
{
    public static string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    public static FftFieldCapture Read(string folder, int config, int sampling, double[] frequencies, string sourceSha256)
    {
        var capture = JsonFiles.Read<JsonElement>(Path.Combine(folder, "capture.json"));
        if (capture.GetProperty("analysisType").GetString() != "FftMtfvsField"
            || !capture.GetProperty("textSaved").GetBoolean() || capture.GetProperty("screenshotStatus").GetString() != "NotRequested")
            throw new InvalidDataException("A complete background FFT MTF field capture is required");
        var settings = JsonFiles.Read<JsonElement>(Path.Combine(folder, "captured-settings.json"));
        var properties = settings.GetProperty("properties");
        var request = settings.GetProperty("request");
        if (request.GetProperty("configuration").GetInt32() != config || request.GetProperty("captureScreenshots").GetBoolean()
            || request.GetProperty("inputSha256").GetString() != sourceSha256
            || properties.GetProperty("SampleSize").GetString() != $"S_{sampling}x{sampling}"
            || properties.GetProperty("FieldDensity").GetInt32() != 10 || properties.GetProperty("ScanType").GetString() != "Plus_Y"
            || properties.GetProperty("UsePolarization").GetBoolean() || !properties.GetProperty("RemoveVignetting").GetBoolean()
            || settings.GetProperty("selectors").GetProperty("Wavelength").GetInt32() != 0)
            throw new InvalidDataException("FFT MTF field source/settings readback differs");
        for (var i = 0; i < 6; i++)
            if (properties.GetProperty("Freq_" + (i + 1)).GetDouble() != (i < frequencies.Length ? frequencies[i] : 0))
                throw new InvalidDataException("Native frequency slot readback differs");
        var model = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model.json"));
        if (!model.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean()
            || model.GetProperty("units").GetProperty("AfocalModeUnits").GetString() != "Milliradians")
            throw new InvalidDataException("Native afocal mode/unit differs");
        var dependencies = JsonFiles.Read<JsonElement>(Path.Combine(folder, "polygon-dependencies.json"));
        if (!dependencies.GetProperty("allMatched").GetBoolean()) throw new InvalidDataException("Native polygon dependency mismatch");
        var path = Path.Combine(folder, "data.json");
        var raw = JsonFiles.Read<JsonElement>(path);
        if (raw.GetProperty("messages").GetArrayLength() != 0) throw new InvalidDataException("Native analysis reported messages");
        var series = raw.GetProperty("dataSeries");
        if (series.GetArrayLength() != frequencies.Length) throw new InvalidDataException("Incomplete native frequency buffers");
        var curves = new List<Series1DResult>();
        foreach (var s in series.EnumerateArray())
        {
            var x = s.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var pairs = s.GetProperty("y").EnumerateArray().ToArray();
            if (x.Length != 300 || pairs.Length != x.Length || x[0] != 0 || x[^1] != 1
                || x.Any(v => !double.IsFinite(v)) || x.Zip(x.Skip(1)).Any(p => p.First >= p.Second)
                || pairs.Any(p => p.GetArrayLength() != 2)) throw new InvalidDataException("Incomplete native whole-field lattice");
            for (var d = 0; d < 2; d++)
            {
                var y = pairs.Select(p => p[d].GetDouble()).ToArray();
                if (y.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Nonfinite native curve");
                curves.Add(new("curve", "Native", x, y, new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless")));
            }
        }
        return new(config, sampling, frequencies, curves.ToArray(), Hash(path));
    }

    public static FftFieldCaptureInspection Inspect(string jobDirectory, string id)
    {
        var executionPath = Path.Combine(jobDirectory, "execution.json");
        if (!File.Exists(executionPath)) return new(id, CaptureStatus.Skipped, "Execution has not completed");
        var all = JsonFiles.Read<JsonElement>(executionPath).EnumerateArray().Where(e => e.GetProperty("id").GetString() == id).ToArray();
        if (all.Length == 0) return new(id, CaptureStatus.Skipped, "Job has no final execution record");
        if (all.Length != 1) return new(id, CaptureStatus.Failed, "Ambiguous execution identity");
        var execution = all[0]; var result = execution.GetProperty("result");
        if (result.GetProperty("Cancelled").GetBoolean()) return new(id, CaptureStatus.Cancelled, "Worker cancelled");
        if (result.GetProperty("TimedOut").GetBoolean()) return new(id, CaptureStatus.TimedOut, "Worker timed out");
        if (result.GetProperty("ExitCode").GetInt32() != 0 || !execution.GetProperty("sourceUnchanged").GetBoolean())
            return new(id, CaptureStatus.Failed, "Worker failed or changed source");
        try
        {
            var folder = Path.Combine(jobDirectory, id);
            var request = JsonFiles.Read<JsonElement>(Path.Combine(folder, "captured-settings.json")).GetProperty("request");
            var properties = JsonFiles.Read<JsonElement>(Path.Combine(folder, "captured-settings.json")).GetProperty("properties");
            var samplingText = properties.GetProperty("SampleSize").GetString()!;
            var sampling = int.Parse(samplingText.AsSpan(2, samplingText.IndexOf('x') - 2));
            var frequencies = Enumerable.Range(1, 6).Select(i => properties.GetProperty("Freq_" + i).GetDouble()).Where(f => f > 0).ToArray();
            var captured = Read(folder, request.GetProperty("configuration").GetInt32(), sampling, frequencies,
                request.GetProperty("inputSha256").GetString()!);
            return new(id, CaptureStatus.Captured, "Final successful execution and complete direct native buffers", captured.DataSha256);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        { return new(id, CaptureStatus.Failed, error.Message); }
    }
}
