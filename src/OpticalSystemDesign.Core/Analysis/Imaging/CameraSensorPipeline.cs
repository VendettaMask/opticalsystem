using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public enum BayerPattern { RGGB, BGGR, GRBG, GBRG }

/// <summary>Shared sensor settings. ReferenceIrradiance and scalar QE belong to the legacy narrow-band path:
/// irradiance is integrated per band at linear value one, not scene lux. Continuous input supplies integrated photocharge.</summary>
public sealed record CameraSensorSettings(
    double ExposureMilliseconds = 10, double PixelPitchMicrometers = 3,
    double ReferenceIrradiance = 0.02, double RedQe = 0.5, double GreenQe = 0.6, double BlueQe = 0.45,
    double FullWellElectrons = 10000, double ReadNoiseElectrons = 2,
    double DarkElectronsPerSecond = 0.1, double PrnuPercent = 1, double DsnuElectrons = 0.5,
    double GainDnPerElectron = 0.35, int BlackLevel = 64, int BitDepth = 12,
    bool NoiseEnabled = true, int Seed = 42, BayerPattern Pattern = BayerPattern.RGGB);

public sealed record CameraIspSettings(bool Enabled = true, double RedGain = 1,
    double GreenGain = 1, double BlueGain = 1, double Gamma = 2.2,
    double Denoise = 0, double Sharpen = 0);

public sealed record CameraSensorResult(int Width, int Height, double[] PhotoElectrons,
    double[] MeasuredElectrons, ushort[] RawDn, ushort[] ReferenceRawDn,
    RgbImage LinearRgb, RgbImage Developed, RgbImage ReferenceDeveloped,
    int SaturatedPixels, double RawNoiseRmseDn, double MeanPhotoElectrons);

/// <summary>Shared managed sensor and basic ISP. No optical tracing formula is duplicated here.</summary>
public static class CameraSensorPipeline
{
    public const int MaximumPixels = 262144;
    public static readonly IReadOnlyList<double> BandWavelengthsNanometers = Array.AsReadOnly(new[] { 650d, 550d, 450d });
    private const double PlanckTimesLightSpeed = 6.62607015e-34 * 299792458;

    public static void Validate(CameraSensorSettings s, CameraIspSettings isp)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(isp);
        Range(s.ExposureMilliseconds, 0.001, 10000, nameof(s.ExposureMilliseconds));
        Range(s.PixelPitchMicrometers, 0.1, 100, nameof(s.PixelPitchMicrometers));
        Range(s.ReferenceIrradiance, 0, 1000, nameof(s.ReferenceIrradiance));
        foreach (var qe in new[] { s.RedQe, s.GreenQe, s.BlueQe }) Range(qe, 0, 1, "QE");
        Range(s.FullWellElectrons, 1, 1e7, nameof(s.FullWellElectrons));
        Range(s.ReadNoiseElectrons, 0, 10000, nameof(s.ReadNoiseElectrons));
        Range(s.DarkElectronsPerSecond, 0, 1e6, nameof(s.DarkElectronsPerSecond));
        Range(s.PrnuPercent, 0, 30, nameof(s.PrnuPercent));
        Range(s.DsnuElectrons, 0, 10000, nameof(s.DsnuElectrons));
        Range(s.GainDnPerElectron, 0.0001, 1000, nameof(s.GainDnPerElectron));
        if (s.BitDepth is < 8 or > 16 || s.BlackLevel < 0 || s.BlackLevel >= (1 << s.BitDepth) - 1
            || !Enum.IsDefined(s.Pattern)) throw new ArgumentException("Invalid ADC or Bayer settings.");
        foreach (var gain in new[] { isp.RedGain, isp.GreenGain, isp.BlueGain }) Range(gain, 0.01, 16, "White balance");
        Range(isp.Gamma, 0.1, 5, nameof(isp.Gamma));
        Range(isp.Denoise, 0, 1, nameof(isp.Denoise)); Range(isp.Sharpen, 0, 2, nameof(isp.Sharpen));
    }

    public static CameraSensorResult Simulate(RgbImage opticalLinear, CameraSensorSettings sensor, CameraIspSettings isp,
        SensorSpectralResponse? spectralResponse = null)
    {
        Validate(sensor, isp); ArgumentNullException.ThrowIfNull(opticalLinear);
        ValidateDimensions(opticalLinear.Width, opticalLinear.Height);
        if (opticalLinear.Channels != 3) throw new ArgumentException("Three linear narrow-band channels are required.");
        spectralResponse?.ValidateCameraCoverage();
        var width = opticalLinear.Width; var height = opticalLinear.Height; var count = width * height;
        var photo = new double[count];
        var areaTime = Math.Pow(sensor.PixelPitchMicrometers * 1e-6, 2) * sensor.ExposureMilliseconds / 1000;
        var efficiencies = new[] { sensor.RedQe, sensor.GreenQe, sensor.BlueQe };
        var spectralWeights = new double[3, 3];
        if (spectralResponse is not null)
            for (var c = 0; c < 3; c++) for (var band = 0; band < 3; band++)
                spectralWeights[c, band] = sensor.ReferenceIrradiance * areaTime * BandWavelengthsNanometers[band]
                    * 1e-9 / PlanckTimesLightSpeed * spectralResponse.Evaluate(c, BandWavelengthsNanometers[band]);
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                // Reject corrupt off-CFA channels too, before they can enter an ISP/reference image later.
                for (var c = 0; c < 3; c++)
                    if (!double.IsFinite(opticalLinear[c, y, x]) || opticalLinear[c, y, x] < 0)
                        throw new ArgumentException("Optical image values must be finite nonnegative linear intensities.");
                var i = y * width + x; var channel = ColorAt(sensor.Pattern, y, x);
                var electrons = opticalLinear[channel, y, x] * sensor.ReferenceIrradiance * areaTime
                    * BandWavelengthsNanometers[channel] * 1e-9 / PlanckTimesLightSpeed * efficiencies[channel];
                if (spectralResponse is not null)
                {
                    electrons = 0;
                    // Each CFA type sees all three input bands through its imported total response.
                    for (var band = 0; band < 3; band++) electrons += opticalLinear[band, y, x] * spectralWeights[channel, band];
                }
                if (!double.IsFinite(electrons) || electrons > 1e10)
                    throw new ArgumentOutOfRangeException(nameof(opticalLinear), "Photon count exceeds the laboratory range.");
                photo[i] = electrons;
            }
        }
        return SimulatePhotoElectrons(photo, width, height, sensor, isp);
    }

    /// <summary>Shared noise, full well, ADC and ISP for pre-integrated expected photocharge.</summary>
    public static CameraSensorResult SimulatePhotoElectrons(IReadOnlyList<double> expectedPhotoElectrons,
        int width, int height, CameraSensorSettings sensor, CameraIspSettings isp)
    {
        Validate(sensor, isp); ValidateDimensions(width, height); ArgumentNullException.ThrowIfNull(expectedPhotoElectrons);
        var count = width * height;
        if (expectedPhotoElectrons.Count != count) throw new ArgumentException("Photocharge dimensions do not match.");
        var photo = expectedPhotoElectrons.ToArray();
        foreach (var value in photo)
            if (!double.IsFinite(value) || value < 0 || value > 1e10)
                throw new ArgumentOutOfRangeException(nameof(expectedPhotoElectrons), "Photocharge must be finite and in [0,1e10] electrons.");
        var measured = new double[count]; var raw = new ushort[count]; var referenceRaw = new ushort[count];
        var temporal = new Random(sensor.Seed);
        var fixedPattern = new Random(unchecked(sensor.Seed ^ 0x6A09E667));
        var dark = sensor.DarkElectronsPerSecond * sensor.ExposureMilliseconds / 1000;
        var maximumDn = (1 << sensor.BitDepth) - 1;
        double noiseSquares = 0, sum = 0; var saturated = 0;
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x; var electrons = photo[i]; sum += electrons;
                var response = Math.Max(0, 1 + Gaussian(fixedPattern) * sensor.PrnuPercent / 100);
                var fixedOffset = Gaussian(fixedPattern) * sensor.DsnuElectrons;
                var charge = sensor.NoiseEnabled ? Poisson(temporal, electrons * response + dark) : electrons + dark;
                // Full-well limits collected charge. Read noise follows charge collection and may produce DN below black.
                measured[i] = Math.Min(charge, sensor.FullWellElectrons)
                    + (sensor.NoiseEnabled ? Gaussian(temporal) * sensor.ReadNoiseElectrons + fixedOffset : 0);
                if (charge >= sensor.FullWellElectrons || measured[i] * sensor.GainDnPerElectron + sensor.BlackLevel >= maximumDn)
                    saturated++;
                raw[i] = Quantize(measured[i]);
                referenceRaw[i] = Quantize(Math.Clamp(electrons + dark, 0, sensor.FullWellElectrons));
                noiseSquares += Math.Pow(raw[i] - referenceRaw[i], 2);
            }
        }
        var linear = Demosaic(raw, width, height, sensor);
        return new(width, height, photo, measured, raw, referenceRaw, linear,
            Develop(linear, isp), Develop(Demosaic(referenceRaw, width, height, sensor), isp),
            saturated, Math.Sqrt(noiseSquares / count), sum / count);

        ushort Quantize(double value) => (ushort)Math.Clamp(
            Math.Round(sensor.BlackLevel + value * sensor.GainDnPerElectron, MidpointRounding.AwayFromZero), 0, maximumDn);
    }

    public static int ColorAt(BayerPattern pattern, int row, int column) => pattern switch
    {
        BayerPattern.RGGB => (row % 2, column % 2) switch { (0, 0) => 0, (1, 1) => 2, _ => 1 },
        BayerPattern.BGGR => (row % 2, column % 2) switch { (0, 0) => 2, (1, 1) => 0, _ => 1 },
        BayerPattern.GRBG => (row % 2, column % 2) switch { (0, 1) => 0, (1, 0) => 2, _ => 1 },
        BayerPattern.GBRG => (row % 2, column % 2) switch { (0, 1) => 2, (1, 0) => 0, _ => 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(pattern))
    };

    public static RgbImage Demosaic(ushort[] raw, int width, int height, CameraSensorSettings sensor)
    {
        ValidateDimensions(width, height); Validate(sensor, new()); ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length != width * height) throw new ArgumentException("RAW size mismatch.");
        var image = new double[3, height, width]; var span = (1 << sensor.BitDepth) - 1 - sensor.BlackLevel;
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++) for (var c = 0; c < 3; c++)
            {
                double total = 0; var samples = 0;
                if (ColorAt(sensor.Pattern, y, x) == c) { total = raw[y * width + x]; samples = 1; }
                else for (var yy = Math.Max(0, y - 1); yy <= Math.Min(height - 1, y + 1); yy++)
                    for (var xx = Math.Max(0, x - 1); xx <= Math.Min(width - 1, x + 1); xx++)
                        if (ColorAt(sensor.Pattern, yy, xx) == c) { total += raw[yy * width + xx]; samples++; }
                image[c, y, x] = Math.Max(0, total / samples - sensor.BlackLevel) / span;
            }
        }
        return new(image);
    }

    public static RgbImage Develop(RgbImage linear, CameraIspSettings isp)
    {
        Validate(new(), isp); ValidateDimensions(linear.Width, linear.Height);
        var values = (double[,,])linear.Values.Clone();
        var gains = new[] { isp.RedGain, isp.GreenGain, isp.BlueGain };
        if (isp.Enabled)
        {
            if (isp.Denoise > 0) BlendBlur(values, isp.Denoise, sharpen: false);
            for (var c = 0; c < 3; c++) for (var y = 0; y < linear.Height; y++) for (var x = 0; x < linear.Width; x++)
                values[c, y, x] *= gains[c];
            if (isp.Sharpen > 0) BlendBlur(values, isp.Sharpen, sharpen: true);
        }
        for (var c = 0; c < 3; c++) for (var y = 0; y < linear.Height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < linear.Width; x++)
                values[c, y, x] = Math.Pow(Math.Clamp(values[c, y, x], 0, 1), isp.Enabled ? 1 / isp.Gamma : 1);
        }
        return new(values);
    }

    public static double SrgbToLinear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    public static void ValidateDimensions(int width, int height)
    {
        if (width < 4 || height < 4 || width > 1024 || height > 1024 || (long)width * height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "Camera laboratory images require 4–1024 pixels per side and at most 262144 pixels.");
    }

    private static void BlendBlur(double[,,] image, double strength, bool sharpen)
    {
        var original = (double[,,])image.Clone(); var height = image.GetLength(1); var width = image.GetLength(2);
        for (var c = 0; c < 3; c++) for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                double sum = 0; var n = 0;
                for (var yy = Math.Max(0, y - 1); yy <= Math.Min(height - 1, y + 1); yy++)
                    for (var xx = Math.Max(0, x - 1); xx <= Math.Min(width - 1, x + 1); xx++) { sum += original[c, yy, xx]; n++; }
                var delta = sum / n - original[c, y, x];
                image[c, y, x] = original[c, y, x] + (sharpen ? -strength : strength) * delta;
            }
        }
    }

    private static void Range(double value, double min, double max, string name)
    { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentOutOfRangeException(name); }

    private static double Gaussian(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    // Poisson: product inversion for small means, transformed rejection (PTRS) for large means.
    // No Gaussian substitution for photon statistics.
    private static double Poisson(Random random, double mean)
    {
        if (mean <= 0) return 0;
        if (mean < 30)
        {
            var threshold = Math.Exp(-mean); double product = 1; var count = 0;
            do { product *= random.NextDouble(); count++; } while (product > threshold);
            return count - 1;
        }
        var root = Math.Sqrt(mean); var b = 0.931 + 2.53 * root;
        var a = -0.059 + 0.02483 * b; var alpha = 1.1239 + 1.1328 / (b - 3.4);
        var fast = 0.9277 - 3.6224 / (b - 2);
        while (true)
        {
            var u = random.NextDouble() - 0.5; var v = random.NextDouble(); var distance = 0.5 - Math.Abs(u);
            if (distance <= 0) continue;
            var k = Math.Floor((2 * a / distance + b) * u + mean + 0.43);
            if (k < 0 || (distance < 0.013 && v > distance)) continue;
            if (distance >= 0.07 && v <= fast) return k;
            if (Math.Log(v * alpha / (a / (distance * distance) + b)) <= -mean + k * Math.Log(mean) - LogFactorial(k)) return k;
        }
    }

    private static double LogFactorial(double k)
    {
        if (k < 32) { double sum = 0; for (var i = 2; i <= k; i++) sum += Math.Log(i); return sum; }
        var inverse = 1 / k;
        return (k + 0.5) * Math.Log(k) - k + 0.9189385332046727
            + inverse / 12 - Math.Pow(inverse, 3) / 360 + Math.Pow(inverse, 5) / 1260;
    }
}
