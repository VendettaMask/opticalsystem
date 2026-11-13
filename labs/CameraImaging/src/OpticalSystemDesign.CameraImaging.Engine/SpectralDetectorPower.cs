using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public enum DetectorRowOrder { PositiveLocalYFirst, NegativeLocalYFirst }
public enum QuantumEfficiencyAreaReference { WholePixel, CollectionArea }

/// <summary>Power at one sampled wavelength, already integrated over its spectral band and each physical pixel.
/// Units are W/pixel, never W/nm, irradiance, display RGB, or a normalized detector plot.</summary>
public sealed record SpectralDetectorPowerBand(double WavelengthNanometers, IReadOnlyList<double> PixelBandPowerWatts);

/// <summary>Immutable physical detector plane for sequential/non-sequential producers.
/// Columns run from negative to positive local X. Row order must be declared explicitly.</summary>
public sealed class SpectralDetectorPower
{
    public const int MaximumBands = 1024;
    public const long MaximumPixelBands = 8_388_608;
    public int Width { get; }
    public int Height { get; }
    public double PixelWidthMicrometers { get; }
    public double PixelHeightMicrometers { get; }
    public DetectorRowOrder RowOrder { get; }
    public double CollectionAreaFraction { get; }
    public IReadOnlyList<SpectralDetectorPowerBand> Bands { get; }
    public double TotalPowerWatts { get; }

    public SpectralDetectorPower(int width, int height, double pixelWidthMicrometers, double pixelHeightMicrometers,
        DetectorRowOrder rowOrder, IEnumerable<SpectralDetectorPowerBand> bands, double collectionAreaFraction = 1)
    {
        CameraSensorPipeline.ValidateDimensions(width, height); ArgumentNullException.ThrowIfNull(bands);
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(rowOrder) || !double.IsFinite(pixelWidthMicrometers) || pixelWidthMicrometers <= 0
            || !double.IsFinite(pixelHeightMicrometers) || pixelHeightMicrometers <= 0
            || !double.IsFinite(collectionAreaFraction) || collectionAreaFraction is <= 0 or > 1)
            throw new ArgumentException("Detector pixel dimensions and row order must be explicit and valid.");
        var planes = bands.Take(MaximumBands + 1).ToArray();
        if (planes.Length > MaximumBands || (long)width * height * planes.Length > MaximumPixelBands)
            throw new ArgumentException("Spectral detector input exceeds the band/pixel budget.");
        Width = width; Height = height; PixelWidthMicrometers = pixelWidthMicrometers;
        PixelHeightMicrometers = pixelHeightMicrometers; RowOrder = rowOrder;
        CollectionAreaFraction = collectionAreaFraction;
        var frozen = new SpectralDetectorPowerBand[planes.Length];
        for (var b = 0; b < planes.Length; b++)
        {
            ComputationCancellation.ThrowIfCancellationRequested(); var plane = planes[b];
            if (plane is null || !double.IsFinite(plane.WavelengthNanometers) || plane.WavelengthNanometers is < 200 or > 20000
                || plane.PixelBandPowerWatts is null || plane.PixelBandPowerWatts.Count != width * height)
                throw new ArgumentException("Detector band wavelength or pixel dimensions are invalid.");
            var values = new double[width * height];
            for (var i = 0; i < values.Length; i++)
            {
                if (i % width == 0) ComputationCancellation.ThrowIfCancellationRequested();
                var value = plane.PixelBandPowerWatts[i];
                if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Detector input requires finite nonnegative absolute W/pixel.");
                values[i] = value;
            }
            frozen[b] = new(plane.WavelengthNanometers, Array.AsReadOnly(values));
        }
        Bands = Array.AsReadOnly(frozen);
        TotalPowerWatts = Bands.Sum(b => b.PixelBandPowerWatts.Sum());
        if (!double.IsFinite(TotalPowerWatts)) throw new ArgumentException("Detector power overflow.");
    }
}

/// <summary>Shared photon-energy constant. Existing camera arithmetic/order remains unchanged.</summary>
public static class PhotonRadiometry
{
    public const double PlanckTimesLightSpeed = 6.62607015e-34 * 299792458;
}

public static class SpectralDetectorSensor
{
    /// <summary>One power-to-charge integration, then the existing noise/ADC/ISP pipeline.
    /// The optical producer has already applied throughput and pixel area. No second optical propagation,
    /// spectrum weight, pixel-area factor, or scene irradiance calibration is applied here.</summary>
    public static CameraSensorResult Simulate(SpectralDetectorPower detector, SensorSpectralResponse response,
        CameraSensorSettings sensor, CameraIspSettings isp, QuantumEfficiencyAreaReference qeArea = QuantumEfficiencyAreaReference.WholePixel)
        => CameraSensorPipeline.SimulatePhotoElectrons(
            IntegratePhotoElectrons(detector, response, sensor, qeArea), detector.Width, detector.Height, sensor, isp);

    /// <summary>Output row zero is positive local Y (top); Bayer pattern refers to this output raster.</summary>
    public static IReadOnlyList<double> IntegratePhotoElectrons(SpectralDetectorPower detector,
        SensorSpectralResponse response, CameraSensorSettings sensor, QuantumEfficiencyAreaReference qeArea = QuantumEfficiencyAreaReference.WholePixel)
    {
        ArgumentNullException.ThrowIfNull(detector); ArgumentNullException.ThrowIfNull(response);
        CameraSensorPipeline.Validate(sensor, new());
        if (!Enum.IsDefined(qeArea)) throw new ArgumentOutOfRangeException(nameof(qeArea));
        // Unit-conversion roundoff only; never rescale a detector onto a different sensor.
        if (!SamePitch(detector.PixelWidthMicrometers, sensor.PixelPitchMicrometers)
            || !SamePitch(detector.PixelHeightMicrometers, sensor.PixelPitchMicrometers))
            throw new ArgumentException("Detector dimensions do not match the square physical camera pixels.");
        var photo = new double[detector.Width * detector.Height];
        foreach (var band in detector.Bands)
        {
            var efficiencies = Enumerable.Range(0, 3).Select(c => response.Evaluate(c, band.WavelengthNanometers)).ToArray();
            if (qeArea == QuantumEfficiencyAreaReference.WholePixel)
                for (var c = 0; c < 3; c++)
                {
                    var activeEfficiency = efficiencies[c] / detector.CollectionAreaFraction;
                    if (activeEfficiency > 1 + 8e-15)
                        throw new ArgumentException("总 QE 超过感光面积比例，需要额外微透镜/空间响应模型；当前裸像元模型请使用匹配的总 QE 或感光区 QE。");
                    efficiencies[c] = Math.Min(1, activeEfficiency);
                }
            var secondsPerPhotonEnergy = sensor.ExposureMilliseconds / 1000 * band.WavelengthNanometers * 1e-9 / PhotonRadiometry.PlanckTimesLightSpeed;
            for (var y = 0; y < detector.Height; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var inputY = detector.RowOrder == DetectorRowOrder.PositiveLocalYFirst ? y : detector.Height - 1 - y;
                for (var x = 0; x < detector.Width; x++)
                {
                    var i = y * detector.Width + x;
                    photo[i] += band.PixelBandPowerWatts[inputY * detector.Width + x] * secondsPerPhotonEnergy
                        * efficiencies[CameraSensorPipeline.ColorAt(sensor.Pattern, y, x)];
                    if (!double.IsFinite(photo[i]) || photo[i] > 1e10) throw new ArgumentOutOfRangeException(nameof(detector), "Photocharge exceeds the camera range.");
                }
            }
        }
        ComputationCancellation.ThrowIfCancellationRequested();
        return Array.AsReadOnly(photo);
    }

    private static bool SamePitch(double a, double b) => Math.Abs(a - b) <= 8e-15 * Math.Max(a, b);
}
