using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record SensorCalibrationFrame(double ExposureMilliseconds, IReadOnlyList<ushort> RawDn);
public sealed record SensorCalibrationEstimate(double BlackLevelDn, double ReadNoiseElectrons,
    double DarkElectronsPerSecond, double DsnuElectrons, double PrnuPercent);

/// <summary>Scalar estimates for linear, unclipped RAW, known DN/e- gain and uniform flat illumination.
/// Two dark exposure groups and one flat group at the longer dark exposure; no QE/full-well inference.</summary>
public static class CameraSensorCalibration
{
    public const int MaximumFrameSamples = 4 * 1024 * 1024;
    public const string Method = "DarkFlatScalarV1: known gain; two dark exposure groups; temporal sample variance; "
        + "zero-exposure mean/variance intercepts; 1/12 DN^2 quantization subtraction; temporal-mean variance removed "
        + "from spatial variance; flat-minus-matched-dark PRNU normalized separately for four CFA phases. "
        + "Negative variance/current estimates are floored to zero. Assumes linear unclipped RAW, stable temperature, "
        + "independent temporal noise and uniform illumination. Scalar estimates only, no pixel correction or EMVA certification.";

    public static SensorCalibrationEstimate Estimate(int width, int height, int bitDepth, double gainDnPerElectron,
        IReadOnlyList<SensorCalibrationFrame> darkFrames, IReadOnlyList<SensorCalibrationFrame> flatFrames)
    {
        CameraSensorPipeline.ValidateDimensions(width, height);
        if (bitDepth is < 8 or > 16 || !double.IsFinite(gainDnPerElectron) || gainDnPerElectron is < 0.0001 or > 1000)
            throw new ArgumentException("Invalid calibration gain or ADC depth.");
        ArgumentNullException.ThrowIfNull(darkFrames); ArgumentNullException.ThrowIfNull(flatFrames);
        var pixels = width * height;
        if (darkFrames.Count is < 4 or > 128 || flatFrames.Count is < 2 or > 128
            || (long)(darkFrames.Count + flatFrames.Count) * pixels > MaximumFrameSamples)
            throw new ArgumentException("Calibration requires >=4 dark and >=2 flat frames, <=128 each and <=4194304 total RAW samples.");
        foreach (var frame in darkFrames.Concat(flatFrames))
        {
            if (frame is null || !double.IsFinite(frame.ExposureMilliseconds) || frame.ExposureMilliseconds is < 0 or > 10000
                || frame.RawDn is null || frame.RawDn.Count != pixels || frame.RawDn.Any(v => v == 0 || v >= (1 << bitDepth) - 1))
                throw new ArgumentException("Calibration frames must have matching dimensions, finite exposures and no clipped RAW samples.");
        }
        var groups = darkFrames.GroupBy(f => f.ExposureMilliseconds).OrderBy(g => g.Key).ToArray();
        if (groups.Length != 2 || groups.Any(g => g.Count() < 2) || groups[1].Key - groups[0].Key < 0.001
            || flatFrames.Any(f => f.ExposureMilliseconds != groups[1].Key))
            throw new ArgumentException("Use two distinct dark exposures (>=2 frames each); all flats must match the longer dark exposure.");
        var first = groups[0].ToArray(); var last = groups[1].ToArray();
        var shortStats = Statistics(first, pixels); var longStats = Statistics(last, pixels); var flatStats = Statistics(flatFrames, pixels);
        var t0 = groups[0].Key / 1000; var t1 = groups[1].Key / 1000; var dt = t1 - t0;
        var bias = new double[pixels]; var biasMeanVariance = new double[pixels]; var signal = new double[pixels];
        double dark = 0, readVariance = 0;
        for (var i = 0; i < pixels; i++)
        {
            if (i % width == 0) ComputationCancellation.ThrowIfCancellationRequested();
            var slope = (longStats.Mean[i] - shortStats.Mean[i]) / dt;
            dark += slope / gainDnPerElectron;
            bias[i] = shortStats.Mean[i] - slope * t0;
            biasMeanVariance[i] = Math.Pow(t1 / dt, 2) * shortStats.Variance[i] / first.Length
                + Math.Pow(t0 / dt, 2) * longStats.Variance[i] / last.Length;
            readVariance += (t1 * shortStats.Variance[i] - t0 * longStats.Variance[i]) / dt - 1d / 12;
            signal[i] = flatStats.Mean[i] - longStats.Mean[i];
        }
        double relativeVariance = 0;
        for (var phase = 0; phase < 4; phase++)
        {
            var indices = Enumerable.Range(0, pixels).Where(i => (i / width % 2) * 2 + i % width % 2 == phase).ToArray();
            var values = indices.Select(i => signal[i]).ToArray(); var mean = values.Average();
            if (mean <= 0 || values.Any(v => v <= 0)) throw new ArgumentException("Flat signal must exceed matched dark at every pixel.");
            var residualVariance = Variance(values) - indices.Average(i => flatStats.Variance[i] / flatFrames.Count + longStats.Variance[i] / last.Length);
            relativeVariance += Math.Max(0, residualVariance) / (mean * mean) * indices.Length / pixels;
        }
        var black = bias.Average();
        if (!double.IsFinite(black) || black < 0 || black >= (1 << bitDepth) - 1) throw new ArgumentException("Estimated black level exceeds the ADC range.");
        return new(black, Math.Sqrt(Math.Max(0, readVariance / pixels)) / gainDnPerElectron,
            Math.Max(0, dark / pixels), Math.Sqrt(Math.Max(0, Variance(bias) - biasMeanVariance.Average())) / gainDnPerElectron,
            Math.Sqrt(relativeVariance) * 100);
    }

    private static (double[] Mean, double[] Variance) Statistics(IReadOnlyList<SensorCalibrationFrame> frames, int pixels)
    {
        var mean = new double[pixels]; var variance = new double[pixels];
        for (var f = 0; f < frames.Count; f++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var i = 0; i < pixels; i++)
            {
                var delta = frames[f].RawDn[i] - mean[i]; mean[i] += delta / (f + 1);
                variance[i] += delta * (frames[f].RawDn[i] - mean[i]);
            }
        }
        for (var i = 0; i < pixels; i++) variance[i] /= frames.Count - 1;
        return (mean, variance);
    }

    private static double Variance(double[] values)
    {
        var mean = values.Average(); return values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
    }
}
