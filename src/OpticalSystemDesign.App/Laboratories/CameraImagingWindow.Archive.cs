using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow : IProjectArchiveParticipant
{
    private CameraImagingRequest CaptureRequest()
    {
        var sensor = ReadSensor();
        var isp = new CameraIspParameters(_isp.IsChecked == true, V("wb-r"), V("wb-g"), V("wb-b"), V("gamma"), V("denoise"), V("sharpen"));
        var (width, height) = SelectedSize;
        var request = new CameraImagingRequest((CameraSource)_source.SelectedIndex, _sourceFile, width, height,
            (CameraOptics)_optics.SelectedIndex, (CameraAberration)_aberration.SelectedIndex, sensor, isp,
            _spectralResponse, _sensorIdentity, (CameraSpectralModel)_spectralModel.SelectedIndex, _illuminant, _reflectance,
            new(V("spectrum-min"), V("spectrum-max"), V("spectrum-step")), IsScene3D ? CameraSceneMode.Uniform : (CameraSceneMode)_sceneMode.SelectedIndex, IsScene3D ? null : _materialScene,
            ReadPixelSampling(), IsScene3D ? ReadScene3D() : null, IsScene3D ? new((int)V("scene3d-rings"), (int)V("scene3d-arms"), _scene3DFresnel.IsChecked == true,
                _aberration.SelectedIndex == (int)CameraAberration.Diffraction
                    ? new((int)V("scene3d-pupil-grid"), (int)V("scene3d-subdivide"), (int)V("scene3d-psf-radius"), (int)V("scene3d-guard")) : null) : null);
        if (IsNsc)
        {
            var input = _nscInput ?? throw new InvalidOperationException("请先读取非序列探测器结果。");
            request = request with
            {
                Width = input.Detector.PixelsX,
                Height = input.Detector.PixelsY,
                NscDetector = input,
                SceneMode = CameraSceneMode.Uniform,
                MaterialScene = null,
                PixelSampling = null
            };
        }
        return request;
    }

    public async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken cancellationToken)
    {
        var request = CaptureRequest();
        var files = new List<ProjectArchiveFileDto>();
        if (request.Source == CameraSource.ExternalImage)
        {
            var relative = "inputs/camera/source" + Path.GetExtension(request.SourceFile).ToLowerInvariant();
            files.Add(new(relative, "camera-image", request.SourceFile, File.Exists(request.SourceFile) ? new FileInfo(request.SourceFile).Length : 0));
            request = request with { SourceFile = relative };
        }
        files.Add(await ProjectArchiveArtifacts.WriteAsync(directory, "labs/camera/input.json", "camera-input", request, token: cancellationToken));
        if (_result is { Inputs: not null } result)
        {
            var path = Path.Combine(directory, "labs/camera/result.camera-experiment.zip");
            await _application.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, result, _comparison, _samplingReport), path, cancellationToken);
            files.Add(ProjectArchiveArtifacts.File("labs/camera/result.camera-experiment.zip", "camera-result", path, true));
        }
        return files;
    }
    internal void OpenArchiveInput(string path, string directory)
    {
        var request = ProjectArchiveArtifacts.Read<CameraImagingRequest>(path);
        if (request.Source == CameraSource.ExternalImage)
        {
            if (Path.IsPathRooted(request.SourceFile)) throw new InvalidDataException("Camera archive image must use a project-relative path.");
            var image = Path.GetFullPath(Path.Combine(directory, request.SourceFile.Replace('/', Path.DirectorySeparatorChar)));
            if (!image.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Camera archive image is outside the project.");
            request = request with { SourceFile = image };
        }
        request = request with { Sensor = request.Sensor ?? new(), Isp = request.Isp ?? new() };
        ApplyExperimentRequest(request); Invalidate("已载入归档输入。");
    }
}
