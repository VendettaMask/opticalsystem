using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

// Only test-request/evidence adapters. Assertions are linked unchanged from the main test project.
internal static class CameraScene3DTests
{
    internal static CameraImagingRequest Request(ICameraImagingService service, int width = 8, int height = 8) =>
        new(Source: CameraSource.Scene3D, Width: width, Height: height, Aberration: CameraAberration.Geometric,
            Sensor: new(NoiseEnabled: false), SpectralModel: CameraSpectralModel.SceneBands,
            PixelSampling: new(2), Scene3D: service.CreateExampleScene3D());
}
internal static class CameraContinuousSpectrumTests
{
    internal static void Record(string name, object value)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
