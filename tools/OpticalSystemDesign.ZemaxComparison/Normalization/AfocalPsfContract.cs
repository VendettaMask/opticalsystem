using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison.Zemax;

namespace OpticalSystemDesign.ZemaxComparison.Normalization;

// Only the frozen linear scalar, chief-reference PSF contract is established.
// This decodes physical units; it never reconciles different coordinate grids.
internal static class AfocalPsfContract
{
    internal static bool Applies(CanonicalAnalysisRequest? request) => request?.ImageSpaceAfocal == true
        && request.CanonicalAnalysisKey is "PSF" or "Huygens PSF";

    internal static void ValidateRequest(CanonicalAnalysisRequest request)
    {
        if (request.Field <= 0 || request.Wavelength <= 0 || request.Surface != -1
            || request.Polarization || request.Reference != "ChiefRay" || request.Normalization != "NativePhysical"
            || !double.IsFinite(request.ImageDeltaMicrometers) || request.ImageDeltaMicrometers < 0)
            throw new InvalidDataException("Afocal PSF requires a selected field/wavelength, final surface, scalar linear chief-reference intensity and nonnegative angular pitch.");
        _ = PhysicalNormalization.UnitScale(request.NativeAfocalAngleUnit!, "Milliradian");
    }

    internal static void ValidateCapture(string dataPath, CanonicalAnalysisRequest request)
    {
        ValidateRequest(request);
        var path = Path.Combine(Path.GetDirectoryName(dataPath)!, "captured-settings.json");
        if (!File.Exists(path)) throw new InvalidDataException("Afocal PSF captured settings are missing.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var properties = root.GetProperty("properties");
        var selectors = root.GetProperty("selectors");
        var fft = request.CanonicalAnalysisKey == "PSF";
        if (selectors.GetProperty("Field").GetInt32() != request.Field
            || selectors.GetProperty("Wavelength").GetInt32() != request.Wavelength
            || root.GetProperty("request").GetProperty("configuration").GetInt32() != request.Configuration
            || properties.GetProperty("Type").GetString() != "Linear"
            || properties.GetProperty("Normalize").GetBoolean()
            || properties.GetProperty("UsePolarization").GetBoolean()
            || properties.GetProperty("Rotation").GetString() != (fft ? "CW0" : "Rotate_0"))
            throw new InvalidDataException("Native afocal PSF selectors, intensity, normalization or orientation differ from the verified contract.");
        if (fft ? selectors.GetProperty("Surface").GetInt32() != 0
            : properties.GetProperty("UseCentroid").GetBoolean()
                || properties.GetProperty("Configuration").GetInt32() != NativeConfigurationSelector.Numbered(request.Configuration))
            throw new InvalidDataException("Native afocal PSF surface/reference/configuration differs from the request.");
        var prefix = fft ? "PsfS_" : "S_";
        if (properties.GetProperty(fft ? "SampleSize" : "PupilSampleSize").GetString()
                != $"{prefix}{request.PupilSampling}x{request.PupilSampling}"
            || properties.GetProperty(fft ? "OutputSize" : "ImageSampleSize").GetString()
                != $"{prefix}{request.ImageSampling}x{request.ImageSampling}")
            throw new InvalidDataException("Native afocal PSF pupil/image sampling differs from the request.");
        var expectedPitch = request.ImageDeltaMicrometers
            * PhysicalNormalization.UnitScale("Milliradian", request.NativeAfocalAngleUnit!);
        var actualPitch = properties.GetProperty("ImageDelta").GetDouble();
        if (!double.IsFinite(actualPitch) || Math.Abs(actualPitch - expectedPitch) > 1e-12 * Math.Max(1, Math.Abs(expectedPitch)))
            throw new InvalidDataException("Native afocal PSF angular pitch readback differs from the canonical request.");
    }
}
