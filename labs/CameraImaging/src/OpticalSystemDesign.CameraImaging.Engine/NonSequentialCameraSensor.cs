using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>Library adapter only. The caller must supply the matching immutable trace-session detector and wavelength snapshot.
/// No tracing, scene lookup, display normalization, or desktop publication is started by capture.</summary>
public static class NonSequentialCameraSensor
{
    public static SpectralDetectorPower Capture(NonSequentialDetectorFrame frame, NonSequentialObjectDefinition detector,
        IReadOnlyList<NonSequentialWavelength> wavelengths)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(detector); ArgumentNullException.ThrowIfNull(wavelengths);
        if (detector.Kind != NonSequentialObjectKind.DetectorRectangle || detector.Parameters is not DetectorRectangleParameters rectangle
            || frame.DetectorId != detector.Id || frame.PixelsX != rectangle.PixelsX || frame.PixelsY != rectangle.PixelsY
            || frame.PowerByWavelength is null || wavelengths.Count is < 1 or > NonSequentialDocument.MaximumWavelengthCount
            || frame.PowerByWavelength.Count > SpectralDetectorPower.MaximumBands)
            throw new ArgumentException("A matching rectangular detector and wavelength snapshot are required.");
        CameraSensorPipeline.ValidateDimensions(frame.PixelsX, frame.PixelsY);
        if (!double.IsFinite(rectangle.WidthMillimeters) || rectangle.WidthMillimeters <= 0
            || !double.IsFinite(rectangle.HeightMillimeters) || rectangle.HeightMillimeters <= 0
            || !double.IsFinite(frame.TotalPowerWatts) || frame.TotalPowerWatts < 0)
            throw new ArgumentException("Detector dimensions and total power are invalid.");
        var bands = new List<SpectralDetectorPowerBand>();
        foreach (var (number, values) in frame.PowerByWavelength)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (number < 1 || number > wavelengths.Count || wavelengths[number - 1] is not { } wavelength)
                throw new ArgumentException("Detector W number does not exist in the captured wavelength table.");
            // Power is already traced and band-integrated; the document's sampling Weight is not applied again.
            bands.Add(new(wavelength.Nanometers, values));
        }
        var captured = new SpectralDetectorPower(frame.PixelsX, frame.PixelsY,
            rectangle.WidthMillimeters / frame.PixelsX * 1000, rectangle.HeightMillimeters / frame.PixelsY * 1000,
            DetectorRowOrder.NegativeLocalYFirst, bands);
        var scale = Math.Max(captured.TotalPowerWatts, frame.TotalPowerWatts);
        if (Math.Abs(captured.TotalPowerWatts - frame.TotalPowerWatts) > 1e-12 * scale)
            throw new ArgumentException("Detector pixel powers and reported total power are inconsistent.");
        return captured;
    }
}
