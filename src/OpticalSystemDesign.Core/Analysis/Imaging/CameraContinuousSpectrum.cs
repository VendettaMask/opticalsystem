using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public enum ScalarSpectrumKind { Illuminant, Reflectance }
public sealed record ScalarSpectrumSample(double WavelengthNanometers, double Value);

/// <summary>Piecewise linear absolute W/m²/nm illuminant, or dimensionless material reflectance.</summary>
public sealed class ScalarSpectrum
{
    public ScalarSpectrumKind Kind { get; }
    public IReadOnlyList<ScalarSpectrumSample> Samples { get; }
    public ScalarSpectrum(ScalarSpectrumKind kind, IEnumerable<ScalarSpectrumSample> samples)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(samples); Kind = kind;
        var frozen = samples.Take(SensorSpectralResponse.MaximumSamples + 1).ToArray();
        if (frozen.Length is < 2 or > SensorSpectralResponse.MaximumSamples) throw new ArgumentException("Spectrum requires 2–4096 samples.");
        var previous = double.NegativeInfinity;
        foreach (var sample in frozen)
        {
            if (sample is null || !double.IsFinite(sample.WavelengthNanometers)
                || sample.WavelengthNanometers is < 200 or > 20000 || sample.WavelengthNanometers <= previous
                || !double.IsFinite(sample.Value) || sample.Value < 0 || sample.Value > (kind == ScalarSpectrumKind.Reflectance ? 1 : 1000))
                throw new ArgumentException("Spectrum must have increasing 200–20000 nm wavelengths and finite nonnegative values; reflectance ≤1, irradiance ≤1000 W/m²/nm.");
            previous = sample.WavelengthNanometers;
        }
        Samples = Array.AsReadOnly(frozen);
    }
    public double Evaluate(double wavelength)
    {
        if (!double.IsFinite(wavelength) || wavelength < Samples[0].WavelengthNanometers || wavelength > Samples[^1].WavelengthNanometers)
            throw new ArgumentOutOfRangeException(nameof(wavelength), "Spectrum does not cover the requested wavelength; extrapolation is disabled.");
        var low = 0; var high = Samples.Count - 1;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (Samples[middle].WavelengthNanometers <= wavelength) low = middle; else high = middle;
        }
        var fraction = (wavelength - Samples[low].WavelengthNanometers) / (Samples[high].WavelengthNanometers - Samples[low].WavelengthNanometers);
        return Samples[low].Value * (1 - fraction) + Samples[high].Value * fraction;
    }
}

public sealed record ContinuousSpectrumSettings(double MinimumNanometers = 400, double MaximumNanometers = 700,
    double MaximumIntervalNanometers = 100);
public sealed record SpectralIntegrationNode(double WavelengthNanometers, double WeightNanometers,
    double IrradianceWattsPerSquareMeterPerNanometer, double Reflectance, double RedQe, double GreenQe, double BlueQe);
public sealed class SpectralIntegrationPlan
{
    public ContinuousSpectrumSettings Settings { get; }
    public int Intervals { get; }
    public IReadOnlyList<SpectralIntegrationNode> Nodes { get; }
    public double IlluminantIrradiance { get; }
    public double ReflectedIrradiance { get; }
    internal SpectralIntegrationPlan(ContinuousSpectrumSettings settings, int intervals,
        IReadOnlyList<SpectralIntegrationNode> nodes, double illuminantIrradiance, double reflectedIrradiance)
    { Settings = settings; Intervals = intervals; Nodes = nodes; IlluminantIrradiance = illuminantIrradiance; ReflectedIrradiance = reflectedIrradiance; }
}
public sealed record ContinuousImageResult(RgbImage SourceMask, RgbImage OpticalPreview, double[] PhotoElectrons);

/// <summary>One material spectrum modulated by a linear grayscale spatial mask. All optics are supplied by the shared image engine.</summary>
public static partial class CameraContinuousSpectrum
{
    public const int MaximumNodes = 36864;
    public const long MaximumPixelNodes = 100_000_000;
    private const double Hc = 6.62607015e-34 * 299792458;

    public static SpectralIntegrationPlan BuildPlan(ScalarSpectrum illuminant, ScalarSpectrum reflectance,
        SensorSpectralResponse qe, ContinuousSpectrumSettings settings)
        => BuildPlan(illuminant, reflectance, qe, settings, Array.Empty<double>());

    private static SpectralIntegrationPlan BuildPlan(ScalarSpectrum illuminant, ScalarSpectrum reflectance,
        SensorSpectralResponse qe, ContinuousSpectrumSettings settings, IEnumerable<double> additionalKnots)
    {
        ArgumentNullException.ThrowIfNull(illuminant); ArgumentNullException.ThrowIfNull(reflectance);
        ArgumentNullException.ThrowIfNull(qe); ArgumentNullException.ThrowIfNull(settings);
        if (illuminant.Kind != ScalarSpectrumKind.Illuminant || reflectance.Kind != ScalarSpectrumKind.Reflectance)
            throw new ArgumentException("Illuminant and reflectance spectra have different units and cannot be interchanged.");
        if (!double.IsFinite(settings.MinimumNanometers) || !double.IsFinite(settings.MaximumNanometers)
            || settings.MinimumNanometers < 200 || settings.MaximumNanometers > 20000 || settings.MinimumNanometers >= settings.MaximumNanometers
            || !double.IsFinite(settings.MaximumIntervalNanometers) || settings.MaximumIntervalNanometers is < 1 or > 1000)
            throw new ArgumentException("Invalid integration range or maximum interval (1–1000 nm).");
        foreach (var endpoint in new[] { settings.MinimumNanometers, settings.MaximumNanometers })
        { _ = illuminant.Evaluate(endpoint); _ = reflectance.Evaluate(endpoint); _ = qe.Evaluate(0, endpoint); }
        var knots = illuminant.Samples.Select(s => s.WavelengthNanometers)
            .Concat(reflectance.Samples.Select(s => s.WavelengthNanometers)).Concat(qe.Samples.Select(s => s.WavelengthNanometers))
            .Concat(additionalKnots)
            .Where(w => w > settings.MinimumNanometers && w < settings.MaximumNanometers)
            .Append(settings.MinimumNanometers).Append(settings.MaximumNanometers).Distinct().Order().ToArray();
        var nodes = new List<SpectralIntegrationNode>(); var intervals = 0;
        double lightPower = 0, reflectedPower = 0;
        for (var k = 1; k < knots.Length; k++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var panels = (int)Math.Ceiling((knots[k] - knots[k - 1]) / settings.MaximumIntervalNanometers);
            if (intervals + panels > MaximumNodes / 3) throw new ArgumentException("Continuous integration exceeds 36864 nodes; reduce input knots or the wavelength range, or increase the interval.");
            var width = (knots[k] - knots[k - 1]) / panels;
            for (var panel = 0; panel < panels; panel++)
            {
                var center = knots[k - 1] + (panel + .5) * width; var half = width / 2;
                // Three-point Gauss-Legendre: exact for degree ≤5 on each piecewise-linear segment.
                foreach (var (offset, weight) in new[] { (-Math.Sqrt(3d / 5), 5d / 9), (0d, 8d / 9), (Math.Sqrt(3d / 5), 5d / 9) })
                {
                    var wavelength = center + half * offset; var w = half * weight;
                    var light = illuminant.Evaluate(wavelength); var rho = reflectance.Evaluate(wavelength);
                    nodes.Add(new(wavelength, w, light, rho, qe.Evaluate(0, wavelength), qe.Evaluate(1, wavelength), qe.Evaluate(2, wavelength)));
                    lightPower += light * w; reflectedPower += light * rho * w;
                }
            }
            intervals += panels;
        }
        return new(settings, intervals, nodes.AsReadOnly(), lightPower, reflectedPower);
    }

    public static ContinuousImageResult Integrate(RgbImage linearSource, CameraSensorSettings sensor,
        SpectralIntegrationPlan plan, Func<RgbImage, double, RgbImage>? propagate = null)
    {
        ArgumentNullException.ThrowIfNull(linearSource); ArgumentNullException.ThrowIfNull(plan);
        CameraSensorPipeline.Validate(sensor, new()); CameraSensorPipeline.ValidateDimensions(linearSource.Width, linearSource.Height);
        if (linearSource.Channels != 3 || plan.Nodes.Count is < 3 or > MaximumNodes
            || propagate is not null && (long)linearSource.Width * linearSource.Height * plan.Nodes.Count > MaximumPixelNodes)
            throw new ArgumentException("Invalid continuous source or pixel × node work exceeds 100 million.");
        var width = linearSource.Width; var height = linearSource.Height;
        var mask = new double[1, height, width]; var displayMask = new double[3, height, width];
        var preview = new double[3, height, width]; var photo = new double[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            for (var c = 0; c < 3; c++)
                if (!double.IsFinite(linearSource[c, y, x]) || linearSource[c, y, x] is < 0 or > 1)
                    throw new ArgumentException("Spatial input must be finite linear RGB in [0,1].");
            mask[0, y, x] = .2126 * linearSource[0, y, x] + .7152 * linearSource[1, y, x] + .0722 * linearSource[2, y, x];
            for (var c = 0; c < 3; c++) displayMask[c, y, x] = mask[0, y, x];
        }
        var input = new RgbImage(mask);
        var areaTime = Math.Pow(sensor.PixelPitchMicrometers * 1e-6, 2) * sensor.ExposureMilliseconds / 1000;
        if (propagate is null)
        {
            // Identity optical transfer is wavelength independent: sum spectra once, preserving
            // every input knot, rather than repeating the full image for every quadrature node.
            var coefficients = new double[3];
            foreach (var node in plan.Nodes)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var weight = node.IrradianceWattsPerSquareMeterPerNanometer * node.Reflectance * node.WeightNanometers
                    * node.WavelengthNanometers * 1e-9 / Hc * areaTime;
                coefficients[0] += weight * node.RedQe; coefficients[1] += weight * node.GreenQe; coefficients[2] += weight * node.BlueQe;
            }
            var transfer = plan.IlluminantIrradiance > 0 ? plan.ReflectedIrradiance / plan.IlluminantIrradiance : 0;
            for (var y = 0; y < height; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    photo[i] = mask[0, y, x] * coefficients[CameraSensorPipeline.ColorAt(sensor.Pattern, y, x)];
                    if (!double.IsFinite(photo[i]) || photo[i] > 1e10) throw new ArgumentOutOfRangeException(nameof(plan), "Integrated photocharge exceeds 1e10 electrons.");
                    for (var c = 0; c < 3; c++) preview[c, y, x] = mask[0, y, x] * transfer;
                }
            }
            return new(new(displayMask), new(preview), photo);
        }
        foreach (var node in plan.Nodes)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var optical = propagate(input, node.WavelengthNanometers);
            if (optical.Channels != 1 || optical.Width != width || optical.Height != height)
                throw new ArgumentException("Monochromatic optical result must match the spatial mask.");
            var energyWeight = node.IrradianceWattsPerSquareMeterPerNanometer * node.Reflectance * node.WeightNanometers;
            var photonWeight = energyWeight * node.WavelengthNanometers * 1e-9 / Hc * areaTime;
            var efficiencies = new[] { node.RedQe, node.GreenQe, node.BlueQe };
            for (var y = 0; y < height; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var value = optical[0, y, x];
                    if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Optical transfer must be finite and nonnegative.");
                    var i = y * width + x;
                    photo[i] += value * photonWeight * efficiencies[CameraSensorPipeline.ColorAt(sensor.Pattern, y, x)];
                    if (!double.IsFinite(photo[i]) || photo[i] > 1e10) throw new ArgumentOutOfRangeException(nameof(plan), "Integrated photocharge exceeds 1e10 electrons.");
                    var energy = plan.IlluminantIrradiance > 0 ? value * energyWeight / plan.IlluminantIrradiance : 0;
                    for (var c = 0; c < 3; c++) preview[c, y, x] += energy;
                }
            }
        }
        return new(new(displayMask), new(preview), photo);
    }
}
