using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison;

public sealed record AnalysisComparisonEntry(string CanonicalAnalysisKey, string WorkbenchKey, string? ZemaxAnalysisType,
    SupportStatus Support, string Mode, string ZemaxSettingsMapper, string WorkbenchRequestMapper, ResultKind ResultKind,
    Axis? XAxis, Axis? YAxis, Tolerances? Tolerances, string Reason, bool RequiresScreenshot, bool RequiresTextExport, string? ScreenshotCode = null)
{
    public IReadOnlyDictionary<string, Tolerances> DefaultTolerances { get; init; } = new Dictionary<string, Tolerances>();
    public IReadOnlyList<string> DefaultMetrics { get; init; } = [];
    public Axis? ValueAxis { get; init; }
}

public static partial class AnalysisComparisonRegistry
{
    // Stable AnalysisIDM identities audited against the existing capture inventory. The Core catalog is authoritative
    // for public keys. Aliases and localized display names never participate in matching.
    private const string Mapping = """
Single Ray Trace|RayTrace|contract
Non-Sequential Ray Trace|NSCSingleRayTrace
Non-Sequential Detector Viewer|DetectorViewer
First Order|SystemData|first-order
Seidel Coefficients|SeidelCoefficients|contract
Seidel Diagram|SeidelDiagram|contract
Spot Diagram|StandardSpot|spot
Full Field Spot Diagram|FullFieldSpot|spot-layout
Matrix Spot Diagram|MatrixSpot|spot-layout
Configuration Matrix Spot Diagram|ConfigurationMatrixSpot|spot-layout
Ray Fan|RayFan|fan
Footprint Diagram|FootprintSettings|contract
Field Curvature and Distortion|FieldCurvatureAndDistortion|contract
Grid Distortion|GridDistortion|contract
Field Curvature|FieldCurvatureAndDistortion|contract
Color Focus Shift|FocalShiftDiagram|contract
Lateral Color|LateralColor|contract
Axial Aberration|LongitudinalAberration|contract
Full Field Aberration|FullFieldAberration|contract
Encircled Energy|GeometricEncircledEnergy|contract
Diffraction Encircled Energy|DiffractionEncircledEnergy|contract
Geometric Line Edge Spread|GeometricLineEdgeSpread|contract
Extended Source Encircled Energy|ExtendedSourceEncircledEnergy|contract
Pupil Aberration|PupilAberrationFan|fan
RMS vs Field|RMSField|contract
RMS vs Wavelength|RMSLambdaDiagram|contract
RMS vs Focus|RMSFocus|contract
RMS Field Map|RMSFieldMap|contract
RMS Wavefront vs Field|RMSField|contract
Through Focus|ThroughFocusSpot|spot-layout
Through Focus MTF|FftThroughFocusMtf|contract
Fourier Through Focus MTF|FftThroughFocusMtf|contract
Huygens Through Focus MTF|HuygensThroughFocusMtf|contract
Geometric Through Focus MTF|GeometricThroughFocusMtf|contract
Fourier MTF vs Field|FftMtfvsField|contract
Huygens MTF vs Field|HuygensMtfvsField|contract
Geometric MTF vs Field|GeometricMtfvsField|contract
Angle vs Image Height|IncidentAnglevsImageHeight|contract
Angle vs Image Height - Through Pupil|IncidentAnglevsImageHeight|contract
Angle vs Image Height - Through Field|IncidentAnglevsImageHeight|contract
Cardinal Points Data|CardinalPoints|contract
Vignetting Diagram|VignettingDiagramSettings|capability-audit
Relative Illumination|RelativeIllumination|contract
Incoherent Irradiance|
Radiant Intensity|
Y-Ybar|YYbarDiagram|contract
PSF|FftPsf|fft-psf
FFT PSF Cross Section|FftPsfCrossSection|contract
FFT Line Edge Spread|FftPsfLineEdgeSpread|contract
Huygens PSF|HuygensPsf|huygens-psf
Huygens PSF Cross Section|HuygensPsfCrossSection|contract
MTF|FftMtf|fft-mtf
Huygens MTF|HuygensMtf|huygens-mtf
Geometric MTF|GeometricMtf|contract
Sampled MTF|
Contrast Loss Map|ContrastLoss|contract
Optical Path Difference|OpticalPathFan|fan
Foucault Analysis|Foucault|capability-audit
Wavefront|WavefrontMap|wavefront
Centroid Sphere Wavefront|
Best Fit Sphere Wavefront|
Zernike|ZernikeFringeCoefficients|contract
Image Simulation|ImageSimulation|capability-audit
Geometric Image Analysis|GeometricImageAnalysis|capability-audit
Geometric Bitmap Image Analysis|GeometricBitmapImageAnalysis|capability-audit
Light Source Analysis|LightSourceAnalysis|capability-audit
Partially Coherent Image Analysis|PartiallyCoherentImageAnalysis|capability-audit
Extended Diffraction Image Analysis|ExtendedDiffractionImageAnalysis|capability-audit
Jones Pupil|PolarizationPupilMap|contract
Prescription Report|PrescriptionDataSettings|contract
System Data Report|SystemData|contract
Classified Data Report|
""";

    public static IReadOnlyList<AnalysisComparisonEntry> Entries { get; } = Build();
    public static AnalysisComparisonEntry Get(string key) => Entries.SingleOrDefault(e => e.CanonicalAnalysisKey == key)
        ?? throw new ArgumentException($"Unknown canonical analysis key: {key}");
    public static IEnumerable<AnalysisComparisonEntry> NativeOnly(IEnumerable<string> ids) => ids.Distinct(StringComparer.Ordinal)
        .Where(id => !Entries.Any(e => e.ZemaxAnalysisType == id))
        .Select(id => new AnalysisComparisonEntry("Zemax:" + id, "", id, SupportStatus.ZemaxOnly, "NativeModeDependent",
            "unimplemented", "none", ResultKind.TextReport, null, null, null,
            "Enumerated native AnalysisIDM; no corresponding public Workbench canonical analysis or explicit adapter. Not executed.", false, false));

    private static IReadOnlyList<AnalysisComparisonEntry> Build()
    {
        var rows = Mapping.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().Split('|'))
            .ToDictionary(p => p[0], StringComparer.Ordinal);
        var keys = new AnalysisCatalog(new Optic()).Names;
        var defaultConfiguration = Configuration.ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));
        if (!rows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(keys))
            throw new InvalidOperationException("Comparison registry must explicitly audit every Core canonical analysis");
        return keys.Select(key =>
        {
            if (!WorkbenchAnalysisCatalog.TryGetDescriptor(key, out var descriptor) || descriptor.CanonicalKey != key)
                throw new InvalidOperationException($"Missing canonical Application descriptor: {key}");
            var row = rows[key];
            var mapper = row.Length > 2 ? row[2] : "unimplemented";
            var id = string.IsNullOrWhiteSpace(row[1]) ? null : row[1];
            var reason = id is null ? "No equivalent Zemax analysis with the same physical definition."
                : mapper == "unimplemented" ? "ZOS-API analysis exists; explicit settings/result adapter is not implemented. No numerical claim."
                : mapper == "spot-layout" ? "Capture explicitly configured native spot layout and inspect every result channel; numerical availability must be established at runtime."
                : mapper == "spot" ? "Compare native RMS/GEO spot metrics only; point ordering and ray correspondence are not equated."
                : mapper == "first-order" ? "Compare EFL and F-number via native MFE operands; TotalTrack is not equated to optical total length."
                : "";
            var kind = mapper switch
            {
                "first-order" or "spot" => ResultKind.Scalar,
                "spot-layout" => ResultKind.Scatter,
                "fft-psf" or "huygens-psf" or "wavefront" => ResultKind.Grid2D,
                _ => ResultKind.Series1D
            };
            if (mapper == "unimplemented") kind = key switch
            {
                "Seidel Coefficients" or "Cardinal Points Data" or "Prescription Report" or "System Data Report" or "Classified Data Report" or "Single Ray Trace" => ResultKind.TextReport,
                "Full Field Spot Diagram" or "Matrix Spot Diagram" or "Configuration Matrix Spot Diagram" or "Through Focus" or "Footprint Diagram" => ResultKind.Scatter,
                "Image Simulation" or "Geometric Image Analysis" or "Geometric Bitmap Image Analysis" or "Light Source Analysis" or "Partially Coherent Image Analysis" or "Extended Diffraction Image Analysis" => ResultKind.Image,
                "Jones Pupil" => ResultKind.ComplexField,
                "RMS Field Map" or "Contrast Loss Map" or "Incoherent Irradiance" or "Foucault Analysis" or "Centroid Sphere Wavefront" or "Best Fit Sphere Wavefront" => ResultKind.Grid2D,
                _ => ResultKind.Series1D
            };
            var axis = mapper.Contains("mtf", StringComparison.Ordinal) ? new Axis("SpatialFrequency", "CyclesPerMillimeter")
                : new Axis("PupilCoordinate", "Dimensionless");
            var ordinate = key switch
            {
                "Ray Fan" => new Axis("ImageHeight", "Micrometer"),
                "Pupil Aberration" => new Axis("Distortion", "Percent"),
                "Optical Path Difference" or "Wavefront" => new Axis("WavefrontError", "Wave"),
                _ => new Axis("Modulation", "Dimensionless")
            };
            Axis? valueAxis = null;
            if (kind == ResultKind.Grid2D && mapper != "unimplemented")
            {
                axis = mapper == "wavefront" ? new("PupilCoordinate", "Dimensionless") : new("ImageHeight", "Micrometer");
                ordinate = axis;
                valueAxis = mapper == "wavefront" ? new("WavefrontError", "Wave") : new("Irradiance", "Dimensionless");
            }
            var entry = new AnalysisComparisonEntry(key, key, id,
                id is null ? SupportStatus.WorkbenchOnly : mapper == "unimplemented" ? SupportStatus.AdapterNotImplemented
                : mapper == "spot-layout" ? SupportStatus.UnsupportedByZosApi : mapper is "first-order" or "spot" ? SupportStatus.PartiallyComparable : SupportStatus.Comparable,
                key.StartsWith("Non-Sequential", StringComparison.Ordinal) ? "NonSequential" : "Sequential",
                mapper, "Application.WorkbenchRuntime.BuildAnalysisData/v1", kind, mapper == "unimplemented" || kind == ResultKind.Scalar ? null : axis, mapper == "unimplemented" || kind == ResultKind.Scalar ? null : ordinate,
                null, reason, false, true, id switch
                {
                    "SystemData" => "Sys",
                    "StandardSpot" => "Spt",
                    "RayFan" => "Ray",
                    "PupilAberrationFan" => "Pab",
                    "OpticalPathFan" => "Opd",
                    "FftMtf" => "Mtf",
                    "HuygensMtf" => "Hmf",
                    "FftPsf" => "Fps",
                    "HuygensPsf" => "Hps",
                    "WavefrontMap" => "Wfm",
                    _ => null
                })
            {
                DefaultTolerances = defaultConfiguration.Analyses.GetValueOrDefault(key)?.Quantities ?? [],
                ValueAxis = valueAxis,
                DefaultMetrics = mapper == "unimplemented" ? [] : ["Absolute", "Relative", "RMSE", "NRMSE(reference peak / absolute floor)", "P50", "P90", "P95", "Pearson", "PhysicalCoverage", "WorstCoordinate"]
            };
            return Describe(entry);
        }).ToArray();
    }

    public static Dictionary<string, string> MapWorkbench(AnalysisComparisonEntry entry, CanonicalAnalysisRequest r)
    {
        var s = new Dictionary<string, string>(r.WorkbenchSettings, StringComparer.Ordinal);
        void Set(string key, object value) => s[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
        Set("FieldNumber", r.Field); Set("WavelengthNumber", r.Wavelength); Set("SurfaceNumber", r.Surface);
        Set("UsePolarization", r.Polarization); Set("UseRayAiming", r.UseRayAiming);
        if (entry.ZemaxSettingsMapper == "fan")
        {
            Set("NumberOfRays", r.RayCount); Set("CheckApertures", true); Set("VignettedPupil", true);
        }
        if (entry.ZemaxSettingsMapper == "spot")
        {
            Set("RayDensity", r.RayCount); Set("Reference", "chief"); Set("Pattern", "hexapolar"); Set("UseSymbols", true);
            Set("DeltaFocus", 0); Set("DirectionCosines", false); Set("UsePolarization", false);
        }
        if (entry.ZemaxSettingsMapper.Contains("mtf", StringComparison.Ordinal)) Set("MaximumFrequency", r.MaximumFrequency);
        if (entry.ZemaxSettingsMapper is "fft-mtf" or "fft-psf" or "wavefront") Set("Sampling", r.PupilSampling);
        if (entry.ZemaxSettingsMapper is "huygens-mtf" or "huygens-psf")
        {
            Set("PupilSampling", r.PupilSampling); Set("ImageSampling", r.ImageSampling);
        }
        if (entry.ZemaxSettingsMapper is "huygens-psf" or "fft-psf" or "huygens-mtf")
        {
            Set("ImageDeltaMicrometers", r.ImageDeltaMicrometers); Set("Normalized", false);
            Set("Rotation", 0); Set("UseCentroid", false); Set("Display", r.ImageSampling);
        }
        if (entry.ZemaxSettingsMapper == "wavefront")
        {
            Set("RemoveTilt", false); Set("ReferenceChiefRay", false); Set("Rotation", 0);
            Set("UseExitPupilShape", true); Set("PupilSx", 0); Set("PupilSy", 0); Set("PupilSr", 1);
        }
        ExtendedAnalysisContracts.MapWorkbench(entry, r, s);
        return s;
    }
}
