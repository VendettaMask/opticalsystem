using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using NonSequentialObjectKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;
using PlaneRectangleParameters = OpticalSystemDesign.Core.NonSequential.PlaneRectangleParameters;
using BoxParameters = OpticalSystemDesign.Core.NonSequential.BoxParameters;
using SphereParameters = OpticalSystemDesign.Core.NonSequential.SphereParameters;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    private static readonly double[] SceneBands = [650, 550, 450];
    private static readonly JsonSerializerOptions Scene3DJson = new()
    {
        WriteIndented = true, MaxDepth = 24, PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public Camera3DScene CreateExampleScene3D() => new(Camera3DScene.FormatIdentifier, 1,
        "近景色块与遮挡", "Analytical demonstration; specified narrow-band powers and reflectances, not measured data",
        [new("棋盘靶", Camera3DShape.Rectangle, 0, 0, -10000, 1000, 800, 10, [.8, .8, .8],
            DarkReflectance: [.04, .04, .04], CheckerSize: 7),
         new("近景红块", Camera3DShape.Rectangle, -20, 10, -8500, 20, 20, 10, [.8, .08, .04]),
         new("蓝球", Camera3DShape.Sphere, 20, -10, -9000, 12, 12, 12, [.04, .1, .75]),
         new("遮挡体", Camera3DShape.Box, 10, 20, -7000, 6, 20, 10, [.02, .02, .02])],
        [new("各向同性点光源", 400, 600, -6000, [300, 400, 300])]);

    public Task<Camera3DScene> ImportScene3DAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = BoundedFile.ReadAllBytes(path, 1024 * 1024, "3D camera scene");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 }); CheckDuplicateProperties(json.RootElement);
        var scene = JsonSerializer.Deserialize<Camera3DScene>(bytes, Scene3DJson) ?? throw new InvalidDataException("三维场景为空。");
        var frozen = FreezeScene3D(scene)!; cancellationToken.ThrowIfCancellationRequested();
        return frozen with { Origin = Origin(path, bytes) };
    }, cancellationToken);

    public Task SaveScene3DAsync(Camera3DScene scene, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        cancellationToken.ThrowIfCancellationRequested(); var frozen = FreezeScene3D(scene)!;
        return BoundedFile.WriteAllBytesAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(frozen, Scene3DJson),
            1024 * 1024, "3D camera scene", cancellationToken);
    }

    private static Camera3DScene? FreezeScene3D(Camera3DScene? scene)
    {
        if (scene is null) return null;
        if (scene.Format != Camera3DScene.FormatIdentifier || scene.SchemaVersion != 1
            || scene.Objects is null || scene.Objects.Count is < 1 or > 64 || scene.Lights is null || scene.Lights.Count > 8)
            throw new InvalidDataException("三维场景格式、版本或物体/光源数量无效。");
        Label(scene.Name, 512, "Scene name", false); Label(scene.Source, 4096, "Scene source", false); ValidateOrigin(scene.Origin);
        static IReadOnlyList<double> Values(IReadOnlyList<double> values, double max)
        {
            if (values is null || values.Count != 3 || values.Any(v => !double.IsFinite(v) || v < 0 || v > max))
                throw new InvalidDataException("三维场景必须明确提供 650/550/450 nm 三个有限、非负波段值。");
            return Array.AsReadOnly(values.ToArray());
        }
        var objects = scene.Objects.Select(o =>
        {
            if (o is null || !Enum.IsDefined(o.Shape)) throw new InvalidDataException("无效三维物体。");
            Label(o.Name, 512, "Object name", false);
            var coordinates = new[] { o.X, o.Y, o.Z, o.RotationX, o.RotationY, o.RotationZ };
            if (coordinates.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e6)
                || new[] { o.SizeX, o.SizeY, o.SizeZ }.Any(v => !double.IsFinite(v) || v is <= 0 or > 1e5)
                || !double.IsFinite(o.CheckerSize) || o.CheckerSize is < 0 or > 1e5)
                throw new InvalidDataException("三维坐标、尺寸或棋盘周期无效（单位 mm）。");
            // Conservative bounding sphere: v1 light transport does not trace lighting through the lens housing/glass.
            var bound = o.Shape == Camera3DShape.Sphere ? o.SizeX / 2
                : Math.Sqrt(o.SizeX * o.SizeX + o.SizeY * o.SizeY + (o.Shape == Camera3DShape.Box ? o.SizeZ * o.SizeZ : 0)) / 2;
            if (o.Z + bound >= -1) throw new InvalidDataException("首版所有场景物体须完整位于第一镜面物方 1 mm 之外。");
            return o with { Reflectance = Values(o.Reflectance, 1), DarkReflectance = o.DarkReflectance is null ? null : Values(o.DarkReflectance, 1),
                EmittedRadiance = o.EmittedRadiance is null ? null : Values(o.EmittedRadiance, 1e6) };
        }).ToArray();
        var lights = scene.Lights.Select(l =>
        {
            if (l is null || new[] { l.X, l.Y, l.Z }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e6) || l.Z >= -1)
                throw new InvalidDataException("点光源须有限且位于第一镜面物方 1 mm 之外。");
            Label(l.Name, 512, "Light name", false); return l with { BandPowerWatts = Values(l.BandPowerWatts, 1e6) };
        }).ToArray();
        return scene with { Objects = Array.AsReadOnly(objects), Lights = Array.AsReadOnly(lights) };
    }

    private static DirectCameraScene CompileScene(Camera3DScene scene, double lensZ)
    {
        var objects = scene.Objects.Select((o, i) => NonSequentialObjectDefinition.Create(o.Shape switch
        {
            Camera3DShape.Rectangle => NonSequentialObjectKind.PlaneRectangle,
            Camera3DShape.Box => NonSequentialObjectKind.Box, _ => NonSequentialObjectKind.Sphere
        }) with
        {
            Id = new Guid(i + 1, 0, 0, new byte[8]),
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(o.X, o.Y, o.Z + lensZ), o.RotationX, o.RotationY, o.RotationZ),
            Parameters = o.Shape switch
            {
                Camera3DShape.Rectangle => new PlaneRectangleParameters(o.SizeX, o.SizeY),
                Camera3DShape.Box => new BoxParameters(o.SizeX, o.SizeY, o.SizeZ),
                _ => new SphereParameters(o.SizeX / 2)
            }
        }).ToArray();
        var document = new NonSequentialDocument(scene.Name, SceneBands.Select(w => new NonSequentialWavelength($"{w} nm", w, 1, w == 550)).ToArray(), objects);
        return new(document, scene.Objects.Select((o, i) => new DirectSceneMaterial(objects[i].Id, o.Reflectance,
            o.EmittedRadiance, o.DarkReflectance, o.CheckerSize, o.TwoSided)),
            scene.Lights.Select(l => new DirectPointLight(new(l.X, l.Y, l.Z + lensZ), l.BandPowerWatts)));
    }

    private static void ValidateSceneMode(CameraImagingRequest request, string version)
    {
        if (version is not (CameraExperiment.CurrentEngineVersion or CameraExperiment.DirectSceneEngineVersion) || request.Optics != CameraOptics.CurrentLens
            || !Enum.IsDefined(request.Aberration) || request.SpectralModel != CameraSpectralModel.SceneBands
            || request.SceneMode != CameraSceneMode.Uniform || request.Scene3D is null)
            throw new InvalidDataException("三维模式需要 V7/V8、当前镜头、场景波段与完整三维场景。");
        var render = request.Render3D ?? new();
        if (render.PupilRings is < 1 or > 8 || render.PupilArms is < 4 or > 32) throw new InvalidDataException("瞳面采样超出范围。");
        if (request.Aberration == CameraAberration.Diffraction)
        {
            if (version != ExperimentEngineVersion) throw new InvalidDataException("V7 仅支持几何三维；衍射需 V8。");
            DiffractionCameraRenderer.Validate(ToCore(render.Diffraction ?? new()));
        }
        else if (render.Diffraction is not null) throw new InvalidDataException("几何模式不允许夹带衍射采样设置。");
    }

    private static SceneDiffractionSettings ToCore(CameraSceneDiffractionSettings settings) =>
        new(settings.PupilSamplesAcross, settings.SceneSubdivisions, settings.PsfRadiusPixels, settings.SourceGuardPixels);

    private async Task<CameraImagingResult> SimulateScene3DAsync(CameraImagingRequest request, CameraExperimentInputs? inputs,
        string? savedName, CancellationToken cancellationToken)
    {
        ValidateSceneMode(request, inputs?.EngineVersion ?? ExperimentEngineVersion);
        request = request with { Scene3D = FreezeScene3D(request.Scene3D), Render3D = request.Render3D ?? new() };
        if (request.Aberration == CameraAberration.Diffraction)
            request = request with { Render3D = request.Render3D with { Diffraction = request.Render3D.Diffraction ?? new() } };
        var response = SceneResponse(request);
        response.ValidateCameraCoverage();
        Optic optic; long revision; string name; CancellationTokenSource linked;
        lock (Gate)
        {
            if (inputs is null && modes.CurrentMode != OpticalWorkbenchMode.Sequential) throw new InvalidOperationException("三维成像需要顺序镜头。");
            optic = inputs is null ? Runtime.CurrentOptic.Clone() : RestoreLens(inputs.LensSnapshotJson ?? throw new InvalidDataException("实验没有镜头快照。"));
            revision = Workspace.Revision; name = savedName ?? Runtime.CurrentOptic.Name;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }
        var snapshot = JsonSerializer.Serialize(optic.ToSnapshot(), LensJsonOptions);
        using (linked) return await Task.Run(() =>
        {
            using var scope = ComputationCancellation.Push(linked.Token);
            var sampling = ToCore(request.PixelSampling); var sensor = ToCore(request.Sensor!);
            var scene = CompileScene(request.Scene3D!, optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z);
            var detectorSettings = new DirectCameraRenderSettings(request.Width, request.Height, sensor.PixelPitchMicrometers,
                sampling, request.Render3D!.PupilRings, request.Render3D.PupilArms, request.Render3D.BareInterfaceFresnel);
            var diffraction = request.Aberration == CameraAberration.Diffraction
                ? DiffractionCameraRenderer.Render(optic, scene, detectorSettings, ToCore(request.Render3D.Diffraction!)) : null;
            var render = diffraction?.Image ?? DirectCameraRenderer.Render(optic, scene, detectorSettings);
            var output = SpectralDetectorSensor.Simulate(render.DetectorPower, response, sensor, ToCore(request.Isp!),
                request.PixelSampling?.QeIncludesFillFactor == false ? QuantumEfficiencyAreaReference.CollectionArea : QuantumEfficiencyAreaReference.WholePixel);
            var optical = new double[3, request.Height, request.Width];
            var area = Math.Pow(sensor.PixelPitchMicrometers * 1e-6, 2) * render.DetectorPower.CollectionAreaFraction;
            for (var b = 0; b < 3; b++) for (var y = 0; y < request.Height; y++) for (var x = 0; x < request.Width; x++)
                optical[b, y, x] = render.DetectorPower.Bands[b].PixelBandPowerWatts[y * request.Width + x] / area / .02;
            var diagnostics = new Camera3DDiagnostics(render.DetectorPower.Bands.Select(b => new CameraDetectorBand(b.WavelengthNanometers,
                b.PixelBandPowerWatts)).ToArray(), render.DetectorPower.TotalPowerWatts, render.LaunchedRays, render.PassedLensRays,
                render.SceneHitRays, render.SurfaceInteractions, render.ShadowQueries, render.ShadowedLightSamples,
                render.PupilPlaneZMillimeters, render.PupilPlaneRadiusMillimeters,
                diffraction is null ? null : new(diffraction.Diffraction.SurfaceSamples, diffraction.Diffraction.ScalarCellEvaluations,
                    diffraction.Diffraction.OccludedPupilCells, diffraction.Diffraction.SourceAreaSquareMillimeters,
                    diffraction.Diffraction.CollectedPowerWatts, diffraction.Diffraction.SamplesPerObject,
                    diffraction.Diffraction.MaximumGeometricSpreadPixels, diffraction.Diffraction.RequiredSourceGuardPixels));
            var captured = new CameraExperimentInputs(inputs?.EngineVersion ?? ExperimentEngineVersion, snapshot, LensDocumentName: inputs?.LensDocumentName ?? name,
                LensSourceRevision: inputs?.LensSourceRevision ?? revision);
            linked.Token.ThrowIfCancellationRequested();
            return new CameraImagingResult(name, revision, request, Display(render.VisibleReflectance, true), Display(new RgbImage(optical), true),
                Gray(output.PhotoElectrons, request.Width, request.Height, sensor.FullWellElectrons),
                Gray(output.RawDn.Select(v => (double)v).ToArray(), request.Width, request.Height, (1 << sensor.BitDepth) - 1),
                Display(output.ReferenceDeveloped), Display(output.Developed), output.RawDn, output.ReferenceRawDn, output.PhotoElectrons,
                output.MeanPhotoElectrons, output.RawNoiseRmseDn, output.SaturatedPixels,
                diffraction is null ? "Shared Core reverse geometric lens + NSC scene intersections" : "Shared Core finite-cell scalar Huygens + incoherent 3D surface quadrature", 0,
                "3D direct Lambertian + isotropic point lights, opaque rectangles/boxes/spheres; 650/550/450 nm band-integrated W; mm geometry; "
                + "actual lens prescription and active-pixel Gauss quadrature; bare-interface unpolarized Fresnel or explicit SimpleCoating transmission; "
                + "source preview is visible reflectance; optical preview uses fixed 0.02 W/m² per band; absolute W/pixel in diagnostics/export. "
                + (diffraction is null ? "Geometric transport. " : "Scalar small-angle surface-patch diffraction: coherent pupil per point/band, incoherent source/band sums; finite source guard and PSF window, no crop renormalization; convergence is user-controlled. ")
                + "No interreflection, scattering, vector polarization, microlenses, motion or vendor ISP; no commercial numerical certification.",
                Inputs: captured, Scene3D: diagnostics);
        }, linked.Token).ConfigureAwait(false);
    }

    private static SensorSpectralResponse SceneResponse(CameraImagingRequest request) => ToCore(request.SpectralResponse)
        ?? new SensorSpectralResponse(SceneBands.Order().Select(w => new SensorResponseSample(w,
            w == 650 ? request.Sensor!.RedQe : 0, w == 550 ? request.Sensor!.GreenQe : 0, w == 450 ? request.Sensor!.BlueQe : 0)).ToArray());
}
