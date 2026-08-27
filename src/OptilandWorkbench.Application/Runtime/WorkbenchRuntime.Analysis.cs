using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    public string BuildAnalysisReport()
    {
        return BuildAnalysisReport("Prescription Report");
    }

    public string BuildAnalysisReport(string analysisName)
    {
        return BuildAnalysisView(analysisName).ReportText;
    }

    public AnalysisView BuildAnalysisView(string analysisName)
    {
        return BuildAnalysisView(analysisName, null);
    }

    public AnalysisView BuildAnalysisView(string analysisName, IReadOnlyDictionary<string, string>? settings)
    {
        return BuildAnalysisView(analysisName, settings, CancellationToken.None);
    }

    public AnalysisView BuildAnalysisView(
        string analysisName,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken)
    {
        if (!WorkbenchAnalysisCatalog.TryGetDescriptor(analysisName, out var descriptor))
        {
            throw new UnknownAnalysisException(analysisName);
        }
        var canonicalName = descriptor.CanonicalKey;
        OpticCapabilityPreflight.EnsureSupported(
            CurrentOptic,
            OpticCapabilityOperation.Analysis,
            canonicalName);
        settings ??= new Dictionary<string, string>();
        var analysis = CreateAnalysis(canonicalName, settings);
        var data = analysis.GenerateData(cancellationToken);
        if (string.Equals(canonicalName, "Image Simulation", StringComparison.Ordinal)
            && settings.TryGetValue("OutputFile", out var outputFile)
            && !string.IsNullOrWhiteSpace(outputFile))
        {
            ImageFileLoader.SaveAnalysisRaster(data, outputFile);
        }

        var rows = data.Values
            .Select(item => new AnalysisRow(DisplayAnalysisKey(item.Key), FormatAnalysisValue(item.Value)))
            .ToArray();
        var plotSeries = data.PlotSeries;
        return new AnalysisView(
            DisplayAnalysisName(data.Name),
            rows,
            data.ReportText ?? FormatAnalysisData(data),
            plotSeries.FirstOrDefault(),
            plotSeries,
            data.PlotOptions ?? new AnalysisPlotOptions(),
            data.PlotPanes ?? Array.Empty<AnalysisPlotPane>(),
            data.PlotPaneColumns,
            data.Table);
    }

    public string CanonicalAnalysisKey(string analysisName)
    {
        return CanonicalAnalysisName(analysisName);
    }

    public Dictionary<string, string> MergeAnalysisSettings(
        string analysisName,
        IReadOnlyDictionary<string, string>? saved)
    {
        var merged = GetAnalysisParameters(analysisName)
            .ToDictionary(parameter => parameter.Key, parameter => parameter.DefaultValue);
        if (saved is not null)
        {
            foreach (var item in saved)
            {
                if (merged.ContainsKey(item.Key))
                {
                    merged[item.Key] = item.Value;
                }
            }
        }

        return merged;
    }

    private BaseAnalysis CreateAnalysis(
        string name,
        IReadOnlyDictionary<string, string> settings)
    {
        // Fallback values in this Application-layer factory are Workbench UI
        // presets. Some intentionally reproduce a committed comparison capture,
        // but they are not universal Zemax defaults or file-format rules.
        int Int(string key, int fallback)
        {
            return TryReadInt(settings, key, fallback);
        }

        double Double(string key, double fallback)
        {
            return TryReadDouble(settings, key, fallback);
        }

        bool Bool(string key, bool fallback)
        {
            return TryReadBool(settings, key, fallback);
        }

        string Text(string key, string fallback)
        {
            return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        int LeadingInt(string key, int fallback)
        {
            var value = Text(key, fallback.ToString(CultureInfo.InvariantCulture)).TrimStart();
            var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }

        IReadOnlyList<double> ImageSimulationWavelengths()
        {
            var selection = Text("WavelengthNumber", "RGB");
            if (!string.Equals(selection, "RGB", StringComparison.OrdinalIgnoreCase))
            {
                var index = Math.Clamp(LeadingInt("WavelengthNumber", 1) - 1, 0, Math.Max(0, CurrentOptic.Wavelengths.Count - 1));
                return CurrentOptic.Wavelengths.Count == 0
                    ? new[] { 0.55 }
                    : new[] { CurrentOptic.Wavelengths[index].Micrometers };
            }

            var values = CurrentOptic.Wavelengths
                .OrderByDescending(wavelength => wavelength.Micrometers)
                .Take(3)
                .Select(wavelength => wavelength.Micrometers)
                .ToArray();
            return values.Length == 0 ? new[] { 0.65, 0.55, 0.45 } : values;
        }

        (double X, double Y) ImageSimulationFieldCenter()
        {
            if (CurrentOptic.Fields.Count == 0)
            {
                return (0, 0);
            }

            var index = Math.Clamp(
                LeadingInt("FieldNumber", 1) - 1,
                0,
                CurrentOptic.Fields.Count - 1);
            var field = CurrentOptic.Fields[index];
            return FieldCoordinates.Normalize(CurrentOptic.Fields, field.X, field.Y);
        }

        int? OptionalGridSize()
        {
            var gridSize = Int("GridSize", 0);
            return gridSize <= 0 ? null : gridSize;
        }

        double? OptionalFrequency()
        {
            var frequency = Double("MaximumFrequency", 0);
            return frequency <= 0 ? null : frequency;
        }

        MtfComputationSettings MtfScanSettings(
            bool zemaxCompatible = true,
            bool useHuygensImageSampling = false)
        {
            var imageDeltaMicrometers = Double("ImageDeltaMicrometers", 0);
            var pupilSampling = Int("Sampling", Int("PupilSampling", 64));
            return new MtfComputationSettings(
                PupilSampling: pupilSampling,
                ImageSize: zemaxCompatible && !useHuygensImageSampling
                    ? pupilSampling * 2
                    : Int("ImageSampling", Int("ImageSize", 64)),
                PixelPitchMillimeters: imageDeltaMicrometers > 0
                    ? imageDeltaMicrometers / 1000.0
                    : useHuygensImageSampling
                        ? 0
                        : Double("PixelPitchMillimeters", 0.005),
                GeometricRayCount: Int("Sampling", Int("PupilSampling", 64)),
                Distribution: Text("Distribution", "uniform"),
                ScaleGeometricByDiffractionLimit: Bool("ScaleByDiffractionLimit", true),
                UsePolarization: Bool("UsePolarization", false),
                ZemaxCompatible: zemaxCompatible,
                UseZemaxHuygensSemantics: useHuygensImageSampling);
        }

        SpotDiagramSettings SpotSettings()
        {
            return new SpotDiagramSettings(
                RayDensity: Int("RayDensity", Int("NumRings", 6)),
                Pattern: Text("Pattern", Text("Distribution", "hexapolar")) switch
                {
                    "六边" => "hexapolar",
                    "矩形" => "uniform",
                    "随机" => "random",
                    "Sobol" => "sobol",
                    "环形" => "ring",
                    var value => value
                },
                WavelengthNumber: Int("WavelengthNumber", 0),
                FieldNumber: Int("FieldNumber", 0),
                SurfaceNumber: Int("SurfaceNumber", -1),
                ColorRaysBy: Text("ColorRaysBy", "波长"),
                Reference: Text("Reference", "主光线"),
                UsePolarization: Bool("UsePolarization", false),
                DirectionCosines: Bool("DirectionCosines", false),
                ShowAiryDisk: Bool("ShowAiryDisk", false),
                DisplayScale: Text("DisplayScale", "比例尺"),
                PlotScaleMicrometers: Double("PlotScaleMicrometers", 0),
                ScatterRays: Bool("ScatterRays", false),
                UseSymbols: Bool("UseSymbols", true),
                Magnification: Double("Magnification", 1),
                IgnoreLateralColor: Bool("IgnoreLateralColor", false));
        }

        var axis = Text("Axis", "Y").Equals("X", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        return name switch
        {
            "Single Ray Trace" => new SingleRayTraceAnalysis(
                CurrentOptic,
                Int("FieldNumber", 0),
                Double("Hx", 0),
                Double("Hy", 0),
                Int("WavelengthNumber", 1),
                Double("Px", 0),
                Double("Py", 0),
                Bool("GlobalCoordinates", false),
                Text("Type", "方向余弦"),
                Bool("UseRayAiming", true),
                Bool("ShowRaySegments", false)),
            "Non-Sequential Ray Trace" => new NonSequentialRayTraceAnalysis(
                CurrentOptic,
                CurrentNonSequentialDocument,
                Int("SourceNumber", 0),
                Bool("DirectRay", false),
                Double("X", 0),
                Double("Y", 0),
                Double("Z", 0),
                Double("L", 0),
                Double("M", 0),
                Double("N", 1),
                Int("WavelengthNumber", 1),
                Double("PowerWatts", 1),
                Bool("LayoutRays", false),
                Bool("SplitFresnelRays", true)),
            "Non-Sequential Detector Viewer" => new NonSequentialDetectorViewerAnalysis(
                CurrentOptic,
                CurrentNonSequentialDocument,
                Int("DetectorNumber", 1),
                Int("SourceNumber", 0),
                _databaseDetectorFrames),
            "First Order" => new FirstOrderAnalysis(CurrentOptic),
            "Seidel Coefficients" => new SeidelCoefficientsAnalysis(
                CurrentOptic,
                Int("WavelengthNumber", 0)),
            "Seidel Diagram" => new SeidelDiagramAnalysis(
                CurrentOptic,
                Int("WavelengthNumber", 0),
                Double("MaximumAberration", 0.1),
                Double("GridInterval", 0.01)),
            "Spot Diagram" => new SpotDiagramAnalysis(CurrentOptic, SpotSettings()),
            "Full Field Spot Diagram" => new SpotDiagramVariantAnalysis(
                CurrentOptic,
                SpotDiagramVariant.FullField,
                SpotSettings()),
            "Matrix Spot Diagram" => new SpotDiagramVariantAnalysis(
                CurrentOptic,
                SpotDiagramVariant.Matrix,
                SpotSettings()),
            "Configuration Matrix Spot Diagram" => new SpotDiagramVariantAnalysis(
                CurrentOptic,
                SpotDiagramVariant.ConfigurationMatrix,
                SpotSettings()),
            "Ray Fan" => new RayFanAnalysis(
                CurrentOptic,
                numPoints: 256,
                plotScaleMicrometers: Double("PlotScaleMicrometers", 0),
                numberOfRaysEachSide: Int("NumberOfRays", 20),
                useDashes: Bool("UseDashes", false),
                vignettedPupil: Bool("VignettedPupil", true),
                checkApertures: Bool("CheckApertures", true),
                wavelengthNumber: Int("WavelengthNumber", 0),
                fieldNumber: Int("FieldNumber", 0),
                tangentialAberration: Text("TangentialAberration", "Y Aberration"),
                sagittalAberration: Text("SagittalAberration", "X Aberration"),
                surfaceNumber: Int("SurfaceNumber", -1),
                zemaxCompatible: true),
            "Footprint Diagram" => new FootprintDiagramAnalysis(
                CurrentOptic,
                Int("RayDensity", 10),
                Int("SurfaceNumber", -1),
                Int("WavelengthNumber", 0),
                Int("FieldNumber", 0),
                Bool("DeleteVignetted", false),
                Bool("UseSymbols", true),
                Text("ColorRaysBy", "视场")),
            "Field Curvature and Distortion" => new FieldCurvatureAndDistortionAnalysis(
                CurrentOptic,
                Int("NumPoints", 128),
                Double("ParabasalDelta", 1e-5),
                Double("MaximumCurvature", 0),
                Text("DistortionType", "F-Tan(Theta)"),
                Int("WavelengthNumber", 0),
                Text("ScanDirection", "+y"),
                Text("DisplayMode", "百分比"),
                Int("ReferenceFieldNumber", 1),
                Bool("IgnoreVignettingFactors", true),
                Double("MaximumDistortion", 0)),
            "Grid Distortion" => new GridDistortionAnalysis(
                CurrentOptic,
                Int("NumPoints", 12),
                Int("WavelengthNumber", 1),
                Int("ReferenceFieldNumber", 1),
                Text("DisplayMode", "截面"),
                Double("Scale", 1),
                Double("HeightWidthAspect", 1),
                Bool("SymmetricMagnification", false),
                Double("FieldWidth", 0)),
            "Field Curvature" => new FieldCurvatureAnalysis(
                CurrentOptic,
                Int("NumPoints", 128),
                Double("ParabasalDelta", 1e-5),
                Double("MaximumCurvature", 0),
                Int("WavelengthNumber", 0),
                Text("ScanDirection", "+y"),
                Bool("IgnoreVignettingFactors", true)),
            "Color Focus Shift" => new ColorFocusShiftAnalysis(
                CurrentOptic,
                Double("MaximumShift", 0),
                Double("PupilZone", 0)),
            "Lateral Color" => new LateralColorAnalysis(
                CurrentOptic,
                Double("GraphScale", 0),
                Bool("AllWavelengths", false),
                Bool("UseRealRays", true),
                Bool("ShowAiryDisk", true)),
            "Axial Aberration" => new AxialAberrationAnalysis(
                CurrentOptic,
                Double("GraphScale", 0),
                Int("WavelengthNumber", 0),
                Bool("UseDashes", false)),
            "Full Field Aberration" => new FullFieldAberrationAnalysis(
                CurrentOptic,
                Text("FieldShape", "椭圆"),
                Double("XFieldWidth", FieldCoordinates.MaximumRadius(CurrentOptic.Fields)),
                Double("YFieldWidth", FieldCoordinates.MaximumRadius(CurrentOptic.Fields)),
                Int("MaximumTerm", 37),
                Text("Aberration", "离焦"),
                LeadingInt("FieldNumber", 1),
                Int("WavelengthNumber", 0),
                Int("XFieldSamples", 11),
                Int("YFieldSamples", 11),
                LeadingInt("PupilSampling", 32),
                Text("DisplayAs", "图标"),
                Text("DisplayMode", "绝对值")),
            "Encircled Energy" => new EncircledEnergyAnalysis(
                CurrentOptic,
                Int("NumRays", 10_000),
                Text("Distribution", "sobol"),
                Int("NumPoints", 256),
                Int("WavelengthNumber", 0),
                Text("Reference", "centroid"),
                Double("MaximumDistanceMicrometers", 0),
                Bool("MultiplyByDiffractionLimit", true)),
            "Diffraction Encircled Energy" => new DiffractionEncircledEnergyAnalysis(
                CurrentOptic,
                LeadingInt("PupilSampling", 64),
                LeadingInt("ImageSampling", 128),
                Int("NumPoints", 401),
                Int("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 0),
                Text("Type", "encircled"),
                Text("Reference", "centroid"),
                Double("MaximumDistanceMicrometers", 0)),
            "Geometric Line Edge Spread" => new GeometricLineEdgeSpreadAnalysis(
                CurrentOptic,
                LeadingInt("PupilSampling", 32),
                Int("NumPoints", 257),
                Int("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1),
                Text("Orientation", "X"),
                Text("Display", "line and edge"),
                Double("MaximumRadiusMicrometers", 0)),
            "Extended Source Encircled Energy" => new ExtendedSourceEncircledEnergyAnalysis(
                CurrentOptic,
                Double("FieldSize", 0),
                Int("SourceSampling", 5),
                Int("NumRays", 5000),
                Int("NumPoints", 256),
                Int("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1),
                Text("Type", "encircled"),
                Text("Reference", "centroid"),
                Double("MaximumDistanceMicrometers", 0),
                LoadExtendedSourceImage(Text("SourceFile", string.Empty)),
                string.IsNullOrWhiteSpace(Text("SourceFile", string.Empty))
                    ? "uniform square"
                    : Path.GetFileName(Text("SourceFile", string.Empty))),
            "Pupil Aberration" => new PupilAberrationAnalysis(
                CurrentOptic,
                settings.ContainsKey("NumberOfRays")
                    ? (Int("NumberOfRays", 20) * 2) + 1
                    : Int("NumPoints", 41)),
            "RMS vs Field" => new RmsVsFieldAnalysis(
                CurrentOptic,
                Int("NumFields", 64),
                Int("NumRings", 6),
                Text("Distribution", "hexapolar"),
                Text("Method", "GQ"),
                Text("Data", "wavefront"),
                Text("Reference", "chief"),
                Int("WavelengthNumber", 0),
                Bool("ShowDiffractionLimit", false),
                Bool("UsePolarization", false),
                Bool("RemoveVignetting", true),
                Int("FieldDensity", 15),
                Text("ScanDirection", "+y")),
            "RMS vs Wavelength" => new RmsVsWavelengthAnalysis(
                CurrentOptic,
                Int("WaveDensity", 21),
                Int("NumRings", 6),
                Text("Distribution", "hexapolar"),
                Int("FieldNumber", 0),
                Text("Reference", "centroid"),
                Text("Method", "GQ"),
                Text("Data", "spot"),
                Bool("ShowDiffractionLimit", false),
                Bool("UsePolarization", false),
                Bool("RemoveVignetting", true)),
            "RMS vs Focus" => new RmsVsFocusAnalysis(
                CurrentOptic,
                Int("FocusDensity", 16),
                Double("MinimumFocus", -0.01),
                Double("MaximumFocus", 0.01),
                Int("NumRings", 6),
                Text("Distribution", "hexapolar"),
                Int("WavelengthNumber", 0),
                Text("Reference", "chief"),
                Text("Method", "GQ"),
                Text("Data", "wavefront"),
                Bool("ShowDiffractionLimit", false),
                Bool("UsePolarization", false),
                Bool("RemoveVignetting", true)),
            "RMS Field Map" => new RmsFieldMapAnalysis(
                CurrentOptic,
                Int("XFieldSamples", 11),
                Int("YFieldSamples", 11),
                Double("XFieldWidth", 0),
                Double("YFieldWidth", 0),
                Int("NumRings", 6),
                Text("Distribution", "hexapolar"),
                Int("WavelengthNumber", 0),
                Text("Reference", "centroid"),
                Text("Method", "GQ"),
                Text("Data", "spot"),
                Bool("UsePolarization", false),
                Bool("RemoveVignetting", true)),
            "RMS Wavefront vs Field" => new RmsWavefrontVsFieldAnalysis(
                CurrentOptic,
                Int("NumFields", 32),
                Int("RayDensity", Int("NumRings", 6)),
                Int("FieldDensity", 15),
                Text("Method", "GQ"),
                Text("Reference", "chief"),
                Int("WavelengthNumber", 0),
                Text("ScanType", "+y"),
                Bool("RemoveVignettingFactors", true),
                zemaxCompatibleOutput: true),
            "Zernike vs Field" => new ZernikeVsFieldAnalysis(
                CurrentOptic,
                Int("FieldDensity", 20),
                Int("NumRings", 12),
                Int("ZernikeTerms", 8),
                LeadingInt("WavelengthNumber", 0)),
            "Through Focus" => new ThroughFocusAnalysis(
                CurrentOptic,
                new ThroughFocusSpotSettings(
                    RayDensity: Int("RayDensity", Int("NumRings", 6)),
                    Pattern: Text("Pattern", Text("Distribution", "hexapolar")) switch
                    {
                        "六边" => "hexapolar",
                        "矩形" => "uniform",
                        "随机" => "random",
                        "Sobol" => "sobol",
                        "环形" => "ring",
                        var value => value
                    },
                    DefocusStepMicrometers: Double(
                        "DefocusStepMicrometers",
                        Double("FocusStep", 0.05) * 1000),
                    FocusPlaneCount: 5,
                    WavelengthNumber: Int("WavelengthNumber", 0),
                    FieldNumber: Int("FieldNumber", 0),
                    SurfaceNumber: Int("SurfaceNumber", -1),
                    ColorRaysBy: Text("ColorRaysBy", "波长"),
                    Reference: Text("Reference", "主光线"),
                    UsePolarization: Bool("UsePolarization", false),
                    ShowAiryDisk: Bool("ShowAiryDisk", false),
                    DisplayScale: Text("DisplayScale", "比例尺"),
                    PlotScaleMicrometers: Double("PlotScaleMicrometers", 0),
                    ScatterRays: Bool("ScatterRays", false),
                    UseSymbols: Bool("UseSymbols", true))),
            "Through Focus MTF" => new MtfThroughFocusAnalysis(CurrentOptic, MtfComputationMethod.Fourier,
                Double("Frequency", Double("SpatialFrequency", 0)),
                Double("DeltaFocus", Double("FocusStep", 0.1)),
                Int("NumberOfSteps", Int("Steps", Int("FocusPlaneCount", 5))),
                MtfScanSettings(zemaxCompatible: true),
                Int("WavelengthNumber", 0),
                Int("FieldNumber", 0),
                Text("Type", "调制"),
                Bool("UseDashes", false)),
            "Fourier Through Focus MTF" => new MtfThroughFocusAnalysis(CurrentOptic, MtfComputationMethod.Fourier,
                Double("Frequency", Double("SpatialFrequency", 0)),
                Double("DeltaFocus", Double("FocusStep", 0.1)),
                Int("NumberOfSteps", Int("Steps", Int("FocusPlaneCount", 5))),
                MtfScanSettings(zemaxCompatible: true),
                Int("WavelengthNumber", 0), Int("FieldNumber", 0),
                Text("Type", "调制"), Bool("UseDashes", false)),
            "Huygens Through Focus MTF" => new MtfThroughFocusAnalysis(CurrentOptic, MtfComputationMethod.Huygens,
                Double("SpatialFrequency", 20), Double("DeltaFocus", Double("FocusStep", 0.1)),
                Int("Steps", Int("FocusPlaneCount", 5)), MtfScanSettings(useHuygensImageSampling: true),
                Int("WavelengthNumber", 0), Int("FieldNumber", 0)),
            "Geometric Through Focus MTF" => new MtfThroughFocusAnalysis(CurrentOptic, MtfComputationMethod.Geometric,
                Double("SpatialFrequency", 50), Double("DeltaFocus", Double("FocusStep", 0.1)),
                Int("Steps", Int("FocusPlaneCount", 5)), MtfScanSettings(),
                Int("WavelengthNumber", 0), Int("FieldNumber", 0)),
            "Fourier MTF vs Field" => new MtfVsFieldAnalysis(CurrentOptic, MtfComputationMethod.Fourier,
                Double("SpatialFrequency", 20), Int("FieldPointCount", 21), MtfScanSettings(),
                Int("WavelengthNumber", 0)),
            "Huygens MTF vs Field" => new MtfVsFieldAnalysis(CurrentOptic, MtfComputationMethod.Huygens,
                Double("Frequency1", 10), Int("FieldDensity", 10),
                MtfScanSettings(useHuygensImageSampling: true),
                Int("WavelengthNumber", 0),
                new[]
                {
                    Double("Frequency1", 10),
                    Double("Frequency2", 20),
                    Double("Frequency3", 30),
                    Double("Frequency4", 40),
                    Double("Frequency5", 50),
                    Double("Frequency6", 60)
                },
                Text("ScanType", "+y"),
                Bool("RemoveVignettingFactors", true),
                zemaxCompatibleOutput: true,
                useDashes: Bool("UseDashes", false)),
            "Geometric MTF vs Field" => new MtfVsFieldAnalysis(CurrentOptic, MtfComputationMethod.Geometric,
                Double("SpatialFrequency", 20), Int("FieldPointCount", 21), MtfScanSettings(),
                Int("WavelengthNumber", 0)),
            "Angle vs Image Height" => new IncidentAngleVsImageHeightAnalysis(
                CurrentOptic,
                Int("FieldDensity", 20),
                Int("WavelengthNumber", 0)),
            "Angle vs Image Height - Through Pupil" => new IncidentAngleVsHeightAnalysis(
                CurrentOptic,
                AngleScanMode.ThroughPupil,
                Int("SurfaceIndex", -1),
                axis,
                Int("NumPoints", 128)),
            "Angle vs Image Height - Through Field" => new IncidentAngleVsHeightAnalysis(
                CurrentOptic,
                AngleScanMode.ThroughField,
                Int("SurfaceIndex", -1),
                axis,
                Int("NumPoints", 128)),
            "Cardinal Points Data" => new CardinalPointsDataAnalysis(
                CurrentOptic,
                Int(
                    "ReferenceSurfaceNumber",
                    CurrentOptic.SurfaceGroup.Items.LastOrDefault()?.Number ?? 0)),
            "Vignetting Diagram" => new VignettingDiagramAnalysis(CurrentOptic),
            "Relative Illumination" => new RelativeIlluminationAnalysis(
                CurrentOptic,
                Int("RayDensity", 10),
                Int("FieldDensity", 21),
                Int("WavelengthNumber", 0),
                Text("ScanDirection", "+y"),
                Bool("RemoveVignettingFactors", true)),
            "Incoherent Irradiance" => new IncoherentIrradianceAnalysis(
                CurrentOptic,
                Int("NumRays", 5),
                Int("ResolutionX", 128),
                Int("ResolutionY", 128),
                Int("DetectorSurfaceIndex", -1),
                Text("Distribution", "random"),
                Bool("Normalized", true)),
            "Radiant Intensity" => new RadiantIntensityAnalysis(
                CurrentOptic,
                Int("AngularBinsX", 101),
                Int("AngularBinsY", 101),
                useAbsoluteUnits: Bool("UseAbsoluteUnits", true),
                referenceSurfaceIndex: Int("ReferenceSurfaceIndex", -1),
                numRays: Int("NumRays", 2048),
                distribution: Text("Distribution", "random")),
            "Y-Ybar" => new YYbarAnalysis(CurrentOptic),
            "PSF" => new PsfAnalysis(
                CurrentOptic,
                LeadingInt("Sampling", Int("NumRays", 64)),
                LeadingInt("Display", Int("GridSize", 128)),
                LeadingInt("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1),
                LeadingInt("SurfaceNumber", -1),
                Double("ImageDeltaMicrometers", 0),
                Double("Rotation", 0),
                Text("Type", "线性"),
                Text("DisplayAs", "伪彩色"),
                Bool("UsePolarization", false),
                Bool("Normalized", false)),
            "FFT PSF Cross Section" => new FftPsfCrossSectionAnalysis(
                CurrentOptic,
                LeadingInt("Sampling", Int("NumRays", 64)),
                settings.ContainsKey("Sampling")
                    ? LeadingInt("Sampling", 64) * 2
                    : OptionalGridSize(),
                Text("Row", "中心"),
                Double("GraphScaleMicrometers", 0),
                LeadingInt("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1),
                Text("Type", "X-线性"),
                Bool("UsePolarization", false),
                Bool("Normalized", false)),
            "FFT Line Edge Spread" => new FftLineEdgeSpreadAnalysis(
                CurrentOptic,
                LeadingInt("Sampling", Int("NumRays", 64)),
                settings.ContainsKey("Sampling")
                    ? LeadingInt("Sampling", 64) * 2
                    : OptionalGridSize(),
                Text("Spread", "线"),
                Double("GraphScaleMicrometers", 0),
                LeadingInt("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1),
                Text("Type", "X-线性"),
                Bool("UsePolarization", false),
                Bool("UseCoherentPsf", false)),
            "Huygens PSF" => new HuygensPsfAnalysis(
                CurrentOptic,
                LeadingInt("PupilSampling", Int("NumRays", 32)),
                LeadingInt("ImageSampling", Int("ImageSize", 32)),
                Double("ImageDeltaMicrometers", 0) / 1000.0,
                LeadingInt("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1),
                Double("Rotation", 0),
                Text("Type", "线性"),
                Text("DisplayAs", "伪彩色"),
                Bool("UsePolarization", false),
                Bool("Normalized", false),
                Bool("UseCentroid", false)),
            "Huygens PSF Cross Section" => new HuygensPsfCrossSectionAnalysis(
                CurrentOptic,
                Int("PupilSampling", Int("NumRays", 32)),
                Int("ImageSampling", Int("ImageSize", 32)),
                Double("ImageDeltaMicrometers", 0) / 1000.0,
                Int("WavelengthNumber", 0),
                Int("FieldNumber", 1),
                settings.ContainsKey("ProfileType")
                    ? Text("ProfileType", "X")
                    : settings.ContainsKey("NumRays") ? "Both" : "X",
                Bool("UsePolarization", false),
                Bool("UseCentroid", false)),
            "MTF" => new MtfAnalysis(
                CurrentOptic,
                Int("Sampling", Int("NumRays", 64)),
                settings.ContainsKey("Sampling") ? Int("Sampling", 64) * 2 : OptionalGridSize(),
                OptionalFrequency(),
                Int("WavelengthNumber", 0),
                Int("FieldNumber", 0),
                Int("SurfaceNumber", 0),
                Text("Type", "调制"),
                Bool("ShowDiffractionLimit", false),
                Bool("UsePolarization", false),
                Bool("UseDashes", false),
                zemaxCompatible: true),
            "Huygens MTF" => new HuygensMtfAnalysis(
                CurrentOptic,
                Int("PupilSampling", Int("NumRays", 32)),
                Int("ImageSampling", Int("ImageSize", 32)),
                Double("ImageDeltaMicrometers", 0) / 1000.0,
                maximumFrequency: OptionalFrequency(),
                wavelengthNumber: Int("WavelengthNumber", 0),
                fieldNumber: Int("FieldNumber", 0),
                zemaxCompatible: true),
            "Geometric MTF" => new GeometricMtfAnalysis(
                CurrentOptic,
                Int("NumRays", 32),
                Text("Distribution", "uniform"),
                Int("PlotPointCount", 128),
                OptionalFrequency(),
                Bool("ScaleByDiffractionLimit", true),
                Int("WavelengthNumber", 0),
                Int("FieldNumber", 0)),
            "Sampled MTF" => new SampledMtfAnalysis(
                CurrentOptic,
                Int("PupilSampling", 32),
                Int("ZernikeTerms", 37),
                Int("PlotPointCount", 128),
                OptionalFrequency()),
            "Contrast Loss Map" => new ContrastLossMapAnalysis(
                CurrentOptic,
                Int("Sampling", 13),
                Double("Frequency", 100),
                Bool("Normalize", false),
                LeadingInt("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1),
                Bool("ShowOPD", false)),
            "Optical Path Difference" => new OpticalPathDifferenceAnalysis(
                CurrentOptic,
                graphScaleWaves: Double("GraphScale", 0),
                numberOfRaysEachSide: Int("NumberOfRays", 20),
                useDashes: Bool("UseDashes", false),
                vignettedPupil: Bool("VignettedPupil", true),
                checkApertures: Bool("CheckApertures", true),
                wavelengthNumber: Int("WavelengthNumber", 0),
                fieldNumber: Int("FieldNumber", 0),
                surfaceNumber: Int("SurfaceNumber", -1)),
            "Wavefront Map" or "Wavefront" => new WavefrontAnalysis(
                CurrentOptic,
                pupilSampling: LeadingInt("Sampling", 64),
                mapSize: LeadingInt("Sampling", 64),
                wavelengthNumber: LeadingInt("WavelengthNumber", 0),
                fieldNumber: LeadingInt("FieldNumber", 1),
                removeTilt: Bool("RemoveTilt", false),
                rotationDegrees: Double("Rotation", 0),
                displayScale: Double("DisplayScale", 1),
                apodization: Text("Apodization", "无"),
                referenceChiefRay: Bool("ReferenceChiefRay", false),
                useExitPupilShape: Bool("UseExitPupilShape", true),
                surfaceNumber: LeadingInt("SurfaceNumber", -1),
                displayAs: Text("DisplayAs", "表面"),
                pupilSx: Double("PupilSx", 0),
                pupilSy: Double("PupilSy", 0),
                pupilSr: Double("PupilSr", 1),
                name: name),
            "Foucault Analysis" => new FoucaultAnalysis(
                CurrentOptic,
                sampling: LeadingInt("Sampling", 32),
                type: Text("Type", "线性"),
                displayAs: Text("DisplayAs", "灰度"),
                knifeEdge: Text("KnifeEdge", "水平线上"),
                dataSource: Text("DataSource", "计算的"),
                wavelengthNumber: LeadingInt("WavelengthNumber", 0),
                fieldNumber: LeadingInt("FieldNumber", 1),
                positionMicrometers: Double("YPositionMicrometers", 0),
                usePolarization: Bool("UsePolarization", false)),
            "Interferogram" => new WavefrontAnalysis(
                CurrentOptic,
                Int("NumRings", 15),
                Int("MapSize", 65),
                name: "Interferogram",
                defaultSquareViewport: true),
            "Centroid Sphere Wavefront" => new ReferenceSphereWavefrontAnalysis(
                CurrentOptic,
                ReferenceSphereStrategy.CentroidSphere,
                Int("NumRings", 8),
                Int("MapSize", 65),
                Double("RobustTrimStandardDeviations", 3),
                Int("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1)),
            "Best Fit Sphere Wavefront" => new ReferenceSphereWavefrontAnalysis(
                CurrentOptic,
                ReferenceSphereStrategy.BestFitSphere,
                Int("NumRings", 8),
                Int("MapSize", 65),
                Double("RobustTrimStandardDeviations", 3),
                Int("WavelengthNumber", 0),
                LeadingInt("FieldNumber", 1)),
            "Zernike Fringe" => new ZernikeAnalysis(
                CurrentOptic,
                LeadingInt("PupilSampling", 32),
                Int("ZernikeTerms", 37),
                mapSize: 65,
                wavelengthNumber: LeadingInt("WavelengthNumber", 0),
                fieldNumber: LeadingInt("FieldNumber", 1),
                name: "Zernike Fringe"),
            "Zernike Standard" => new ZernikeAnalysis(
                CurrentOptic,
                Int("NumRings", 15),
                Int("ZernikeTerms", 37),
                mapSize: 65,
                wavelengthNumber: LeadingInt("WavelengthNumber", 0),
                fieldNumber: LeadingInt("FieldNumber", 1),
                name: "Zernike Standard"),
            "Zernike Annular" => new ZernikeAnalysis(
                CurrentOptic,
                Int("NumRings", 15),
                Int("ZernikeTerms", 37),
                mapSize: 65,
                wavelengthNumber: LeadingInt("WavelengthNumber", 0),
                fieldNumber: LeadingInt("FieldNumber", 1),
                name: "Zernike Annular",
                obscurationRatio: Double("ObscurationRatio", 0.5)),
            "Zernike" => new ZernikeAnalysis(
                CurrentOptic,
                Int("NumRings", 15),
                Int("ZernikeTerms", 37),
                Int("MapSize", 65)),
            "Geometric Image Analysis" => new GeometricImageAnalysis(
                CurrentOptic,
                ParseImageSourcePattern(Text("SourceImage", "分辨率靶标")),
                Int("ImageSize", 64),
                Int("NumRays", 8),
                Double("FieldHeight", 0),
                Int("Oversampling", 1),
                Int("GuardBand", 4),
                Bool("RelativeIllumination", true),
                Text("AberrationMode", "Geometric")),
            "Geometric Bitmap Image Analysis" => new GeometricBitmapImageAnalysis(
                CurrentOptic,
                Int("ImageSize", 64),
                Int("RaysPerPixel", 8),
                Double("FieldHeight", 0),
                Int("Oversampling", 1),
                Int("GuardBand", 4),
                Bool("RelativeIllumination", true),
                Text("AberrationMode", "Geometric")),
            "Light Source Analysis" => new LightSourceAnalysis(
                CurrentOptic,
                Int("Resolution", 65),
                Int("NumRays", 2048)),
            "Partially Coherent Image Analysis" => new PartiallyCoherentImageAnalysis(
                CurrentOptic,
                Int("ImageSize", 64),
                LeadingInt("PupilSampling", 16),
                Double("Coherence", 0.5),
                Double("FieldHeight", 0),
                Int("Oversampling", 1),
                Int("GuardBand", 16),
                Bool("RelativeIllumination", true)),
            "Extended Diffraction Image Analysis" => new ExtendedDiffractionImageAnalysis(
                CurrentOptic,
                ParseImageSourcePattern(Text("SourceImage", "分辨率靶标")),
                Int("ImageSize", 64),
                LeadingInt("PupilSampling", 16),
                Int("FieldGrid", 5),
                Double("FieldHeight", 0),
                Int("Oversampling", 1),
                Int("GuardBand", 16),
                Bool("RelativeIllumination", true),
                Text("AberrationMode", "Diffraction")),
            "Image Simulation" => new ImageSimulationAnalysis(CurrentOptic, new ImageSimulationConfig
            {
                SourcePattern = Text("SourceImage", "彩色测试卡") switch
                {
                    "分辨率靶标" => ImageSimulationSourcePattern.ResolutionTarget,
                    "畸变网格" => ImageSimulationSourcePattern.DistortionGrid,
                    "西门子星" => ImageSimulationSourcePattern.SiemensStar,
                    _ => ImageSimulationSourcePattern.ColorChart
                },
                SourceFile = Text("SourceFile", string.Empty),
                SourceImage = string.IsNullOrWhiteSpace(Text("SourceFile", string.Empty))
                    ? null
                    : ImageFileLoader.LoadRgb(Text("SourceFile", string.Empty)),
                PsfSize = LeadingInt("PsfSize", 32),
                NumRays = LeadingInt("NumRays", 32),
                Components = Int("EigenPsfComponents", 3),
                DistortionGridSize = Int("DistortionGridSize", 9),
                DistortionPolynomialDegree = Int("DistortionPolynomialDegree", 5),
                PsfGridRows = LeadingInt("PsfGridRows", 3),
                PsfGridColumns = LeadingInt("PsfGridColumns", 3),
                Padding = LeadingInt("GuardBand", 0),
                SourceMode = string.IsNullOrWhiteSpace(Text("SourceFile", string.Empty))
                    ? "Built-in"
                    : "External bitmap",
                FieldHeight = Double("FieldHeight", 0),
                Oversampling = LeadingInt("Oversampling", 1),
                SourceFlip = Text("SourceFlip", "无"),
                SourceRotationDegrees = LeadingInt("SourceRotation", 0),
                ImageFlip = Text("ImageFlip", "无"),
                UseRelativeIllumination = Bool("RelativeIllumination", true),
                AberrationMode = Text("AberrationMode", "几何的") switch
                {
                    "无" => "None",
                    "衍射" => "Diffraction",
                    _ => "Geometric"
                },
                Reference = Text("Reference", "主光线") == "质心" ? "centroid" : "chief",
                DisplayAs = Text("DisplayAs", "仿真图"),
                FieldCenterX = ImageSimulationFieldCenter().X,
                FieldCenterY = ImageSimulationFieldCenter().Y,
                ImageWidth = Int("ImageWidth", 64),
                ImageHeight = Int("ImageHeight", 48),
                OutputWidth = Int("DetectorXPixels", 0),
                OutputHeight = Int("DetectorYPixels", 0),
                PixelSizeMillimeters = Double("PixelSize", 0),
                WavelengthsMicrometers = ImageSimulationWavelengths(),
                UsePolarization = Bool("UsePolarization", false),
                ApplyFixedApertures = Bool("ApplyFixedApertures", true),
                CompressFrame = Bool("CompressFrame", false),
                OutputFile = Text("OutputFile", string.Empty)
            }),
            "Jones Pupil" => new JonesPupilAnalysis(CurrentOptic, Int("GridSize", 65)),
            "Prescription Report" => new PrescriptionReportAnalysis(CurrentOptic),
            "System Data Report" => new SystemDataReportAnalysis(CurrentOptic),
            "Classified Data Report" => new ClassifiedDataReportAnalysis(CurrentOptic),
            _ => CurrentOptic.Analyses.Create(name)
        };
    }

    private static ImageSimulationSourcePattern ParseImageSourcePattern(string value)
    {
        return value switch
        {
            "彩色测试卡" => ImageSimulationSourcePattern.ColorChart,
            "畸变网格" => ImageSimulationSourcePattern.DistortionGrid,
            "西门子星" => ImageSimulationSourcePattern.SiemensStar,
            _ => ImageSimulationSourcePattern.ResolutionTarget
        };
    }

    private static ExtendedSourceImage? LoadExtendedSourceImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The extended-source IMA file does not exist.", fullPath);
        }

        if (!Path.GetExtension(fullPath).Equals(".ima", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Extended Source Encircled Energy requires a text .IMA file.");
        }

        return ExtendedSourceImage.ParseZemaxTextIma(File.ReadAllText(fullPath));
    }
}
