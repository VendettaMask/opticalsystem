namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record LinearIrradianceBand(double WavelengthNanometers, IReadOnlyList<double> WattsPerSquareMeter);

/// <summary>Linear irradiance averaged over the collecting area. Detector power has already integrated that area.
/// Column centers increase in local X; row order is inherited explicitly from the power plane.
/// No QE, exposure, gamma or per-band normalization.</summary>
public sealed class LinearDetectorImage
{
    public SpectralDetectorPower Power { get; }
    public IReadOnlyList<LinearIrradianceBand> Bands { get; }
    public double CollectionAreaSquareMeters { get; }
    public LinearDetectorImage(SpectralDetectorPower power)
    {
        ArgumentNullException.ThrowIfNull(power);
        Power = power;
        CollectionAreaSquareMeters = power.PixelWidthMicrometers * 1e-6 * power.PixelHeightMicrometers * 1e-6 * power.CollectionAreaFraction;
        if (!double.IsFinite(CollectionAreaSquareMeters) || CollectionAreaSquareMeters <= 0)
            throw new ArgumentException("Collecting area cannot be represented.");
        Bands = Array.AsReadOnly(power.Bands.Select(b =>
        {
            var values = b.PixelBandPowerWatts.Select(v => v / CollectionAreaSquareMeters).ToArray();
            if (values.Any(v => !double.IsFinite(v))) throw new ArgumentException("Irradiance overflow.");
            return new LinearIrradianceBand(b.WavelengthNanometers, Array.AsReadOnly(values));
        }).ToArray());
    }

    public (double XMillimeters, double YMillimeters) PixelCenter(int x, int y)
    {
        if ((uint)x >= Power.Width || (uint)y >= Power.Height) throw new ArgumentOutOfRangeException(nameof(x));
        return ((x + .5 - Power.Width / 2d) * Power.PixelWidthMicrometers * .001,
            (Power.RowOrder == DetectorRowOrder.PositiveLocalYFirst ? 1 : -1)
            * (Power.Height / 2d - y - .5) * Power.PixelHeightMicrometers * .001);
    }
}
