namespace OptilandWorkbench.Application.Contracts;

public enum WorkspaceChangeCategory
{
    Document,
    Prescription,
    Surface,
    Field,
    Wavelength,
    SystemSettings,
    Configuration,
    Optimization,
    Tolerancing
}

public sealed record WorkspaceChangedEventArgs(
    long Revision,
    WorkspaceChangeCategory Category,
    string Message,
    bool FileSwitched = false);

public sealed record OpticalDocumentSnapshot(
    string Name,
    string? Path,
    long Revision,
    string Status,
    bool CanUndo,
    bool CanRedo,
    double EffectiveFocalLength,
    double FNumber,
    double ApertureValue,
    double TotalTrack,
    int SurfaceCount,
    int FieldCount,
    int WavelengthCount);

public sealed record MaterialCatalogDto(
    string Manufacturer,
    int GlassCount);

public sealed record MaterialCatalogImportResultDto(
    string CatalogName,
    int GlassCount,
    string SavedPath);

public sealed record GlassMaterialDto(
    string Name,
    string Manufacturer,
    string Formula,
    double RefractiveIndexD,
    double AbbeNumber,
    double MinimumWavelengthMicrometers,
    double MaximumWavelengthMicrometers,
    IReadOnlyList<double> DispersionCoefficients,
    int RefractiveIndexSampleCount,
    int ExtinctionSampleCount,
    int? ZemaxFormulaNumber,
    string Status,
    string Comment,
    bool ExcludeSubstitution,
    int MeltFrequency,
    double? ThermalExpansionLow,
    double? ThermalExpansionHigh,
    double? Density,
    double? RelativePartialDispersionDeviation,
    IReadOnlyList<double> ThermalCoefficients,
    IReadOnlyList<double> MechanicalData,
    IReadOnlyList<double> OtherData,
    int InternalTransmissionCount,
    int StressDataCount);

public enum MaterialAnalysisKind
{
    DispersionDiagram,
    GlassMap,
    AthermalGlassMap,
    InternalTransmission,
    DispersionVsWavelength
}

public sealed record MaterialAnalysisRequestDto(
    MaterialAnalysisKind Kind,
    string? Manufacturer = null,
    string? GlassName = null,
    double ThicknessMillimeters = 10,
    int SampleCount = 161);

public sealed record LensLibraryEntryDto(
    string Id,
    string Name,
    string Category,
    string SourceName,
    string SourceUrl,
    string License,
    string SourceFormat,
    string ImportStatus,
    string? ImportMessage,
    double EffectiveFocalLength,
    double FNumber,
    string ApertureKind,
    double ApertureValue,
    double TotalTrack,
    int SurfaceCount,
    string FieldDefinition,
    double MaximumField,
    int FieldCount,
    int WavelengthCount,
    double MinimumWavelengthNanometers,
    double MaximumWavelengthNanometers,
    string NativePath,
    string SourcePath);

public sealed record LensLibraryCatalogDocument(
    int Version,
    DateTimeOffset BuiltAt,
    IReadOnlyList<LensLibraryEntryDto> Entries);

public sealed record SurfaceRowDto(
    int Number,
    string Label,
    double Radius,
    double Thickness,
    string Material,
    string Coating,
    double SemiDiameter,
    double Conic,
    bool IsStop,
    string GeometryKind,
    string CoatingKind,
    string InteractionKind,
    string ApertureKind,
    int GratingOrder,
    double GratingPeriodMicrometers,
    double GrooveOrientationAngleDegrees,
    double ThinLensFocalLength,
    bool RadiusVariable,
    bool ThicknessVariable,
    bool SemiDiameterFixed = false);

public sealed record SurfaceComponentUpdateDto(
    string GeometryKind,
    string ApertureKind,
    int GratingOrder,
    double GratingPeriodMicrometers,
    double GrooveOrientationAngleDegrees,
    double ThinLensFocalLength);

public sealed record FieldRowDto(
    int Index,
    string Label,
    double X,
    double Y,
    double VignetteFactorX,
    double VignetteFactorY,
    double Weight);

public sealed record WavelengthRowDto(
    int Index,
    string Label,
    double Nanometers,
    double Weight,
    bool IsPrimary);

public sealed record SystemSettingsDto(
    string Backend,
    string ApertureKind,
    double ApertureValue,
    string FieldDefinition,
    bool ObjectSpaceTelecentric,
    string ApodizationKind,
    double FirstApodizationParameter,
    double SecondApodizationParameter);

public sealed record EnvironmentSettingsDto(
    bool MatchRefractiveIndexData,
    double TemperatureCelsius,
    double PressureAtmospheres);

public sealed record PrescriptionOptionsDto(
    IReadOnlyList<string> Backends,
    IReadOnlyList<string> ApertureKinds,
    IReadOnlyList<string> FieldDefinitions,
    IReadOnlyList<string> ApodizationKinds,
    IReadOnlyList<string> GeometryKinds,
    IReadOnlyList<string> Materials,
    IReadOnlyList<string> CoatingKinds,
    IReadOnlyList<string> InteractionKinds,
    IReadOnlyList<string> PhysicalApertureKinds);

public enum AnalysisParameterKind
{
    Integer,
    Double,
    Choice,
    Boolean,
    File
}

public sealed record AnalysisParameterDescriptor(
    string Key,
    string DisplayName,
    AnalysisParameterKind Kind,
    string DefaultValue,
    double Minimum,
    double Maximum,
    double Increment,
    IReadOnlyList<string>? Choices = null);

public enum AnalysisSeriesKind
{
    Line,
    Scatter,
    Bar,
    Heatmap,
    Raster,
    ColoredLine
}

public enum AnalysisLineStyle
{
    Solid,
    Dashed,
    Dotted
}

public enum AnalysisMarkerStyle
{
    Circle,
    Square,
    Triangle,
    Cross
}

public enum AnalysisColorMap
{
    Viridis,
    Inferno,
    Jet
}

public enum AnalysisAxisQuantity
{
    Unspecified,
    Coordinate,
    FieldAngle,
    FieldHeight,
    ImageHeight,
    ObjectHeight,
    PupilCoordinate,
    Wavelength,
    WavefrontError,
    Defocus,
    Radius,
    SpatialFrequency,
    Modulation,
    EnergyFraction,
    Irradiance,
    Distortion,
    RayHeight,
    IncidentAngle,
    ZernikeTerm,
    Coefficient,
    SurfaceNumber,
    RefractiveIndex,
    AbbeNumber,
    Dispersion,
    Intensity,
    Pixel,
    ChromaticPower,
    ThermalOpticalPower,
    Transmission
}

public enum AnalysisAxisUnit
{
    Unspecified,
    Dimensionless,
    Millimeter,
    Micrometer,
    Nanometer,
    Degree,
    Wave,
    Percent,
    CyclesPerMillimeter,
    InverseMicrometer,
    Pixel,
    Radian,
    Decibel,
    WattsPerSteradian,
    WattsPerSquareMillimeter,
    PartsPerMillionPerKelvin
}

public sealed record AnalysisPointDto(
    double X,
    double Y,
    string Label = "",
    double? Value = null,
    double? Red = null,
    double? Green = null,
    double? Blue = null);

public sealed record AnalysisSeriesDto(
    string XAxisLabel,
    string YAxisLabel,
    IReadOnlyList<AnalysisPointDto> Points,
    AnalysisSeriesKind Kind = AnalysisSeriesKind.Line,
    string Name = "",
    AnalysisLineStyle LineStyle = AnalysisLineStyle.Solid,
    int ColorIndex = 0,
    bool ShowMarkers = false,
    double LineWidth = 1.5,
    AnalysisMarkerStyle MarkerStyle = AnalysisMarkerStyle.Circle,
    double MarkerSize = 3.2,
    double Opacity = 1,
    string ValueLabel = "",
    AnalysisColorMap ColorMap = AnalysisColorMap.Viridis,
    double? ValueMinimum = null,
    double? ValueMaximum = null,
    string LegendKey = "",
    string LegendLabel = "",
    AnalysisAxisQuantity XQuantity = AnalysisAxisQuantity.Unspecified,
    AnalysisAxisUnit XUnit = AnalysisAxisUnit.Unspecified,
    AnalysisAxisQuantity YQuantity = AnalysisAxisQuantity.Unspecified,
    AnalysisAxisUnit YUnit = AnalysisAxisUnit.Unspecified,
    AnalysisAxisQuantity ValueQuantity = AnalysisAxisQuantity.Unspecified,
    AnalysisAxisUnit ValueUnit = AnalysisAxisUnit.Unspecified);

public sealed record AnalysisPlotOptionsDto(
    string Title = "",
    bool SymmetricX = false,
    bool EqualAspect = false,
    bool ShowVerticalZeroLine = false,
    bool ShowHorizontalZeroLine = false,
    AnalysisLineStyle VerticalZeroLineStyle = AnalysisLineStyle.Solid,
    double VerticalZeroLineWidth = 0.5,
    double? XMinimum = null,
    double? XMaximum = null,
    double? YMinimum = null,
    double? YMaximum = null,
    bool ShowLegend = false,
    bool HideTopAndRightAxes = false,
    bool DottedGrid = false,
    double GridOpacity = 1,
    bool HideAxes = false,
    bool ReverseX = false,
    bool ShowPointLabels = false,
    bool HideTickLabels = false,
    bool LegendBelow = false,
    bool DefaultSquareViewport = false);

public sealed record AnalysisPlotPaneDto(
    string Title,
    IReadOnlyList<AnalysisSeriesDto> Series,
    AnalysisPlotOptionsDto PlotOptions,
    IReadOnlyList<AnalysisPlotMetricDto>? Metrics = null,
    string Footer = "");

public sealed record AnalysisPlotMetricDto(
    string Label,
    double Value,
    string Unit = "");

public sealed record AnalysisRowDto(string Metric, string Value);

public sealed record AnalysisTableDto(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string>? RowGroups = null);

public enum AnalysisPresentationKind
{
    Standard,
    CardinalPoints,
    SeidelCoefficients,
    ZernikeFringe,
    ZernikeStandard,
    ZernikeAnnular,
    SeidelDiagram,
    FullFieldAberration,
    WavefrontMap,
    FftPsf,
    HuygensPsf,
    Foucault,
    SpotDiagram,
    ThroughFocusSpot,
    MatrixSpot,
    ConfigurationMatrixSpot,
    FullFieldSpot,
    RayFan,
    PupilAberration,
    OpticalPathDifference,
    FootprintDiagram,
    AxialAberration,
    LateralColor,
    ColorFocusShift,
    FieldCurvatureAndDistortion,
    FieldCurvature
}

public sealed record AnalysisViewDto(
    string Name,
    IReadOnlyList<AnalysisRowDto> Rows,
    string ReportText,
    IReadOnlyList<AnalysisSeriesDto> Series,
    AnalysisPlotOptionsDto PlotOptions,
    IReadOnlyList<AnalysisPlotPaneDto> PlotPanes,
    int PlotPaneColumns,
    AnalysisTableDto? Table = null,
    AnalysisPresentationKind PresentationKind = AnalysisPresentationKind.Standard);

public sealed record AnalysisRequestDto(
    Guid InstanceId,
    int Generation,
    string AnalysisKey,
    IReadOnlyDictionary<string, string> Settings);

public sealed record AnalysisResultDto(
    Guid InstanceId,
    int Generation,
    long SourceRevision,
    AnalysisViewDto View,
    AnalysisExecutionProvenanceDto Provenance)
{
    public string CanonicalAnalysisKey => Provenance.CanonicalAnalysisKey;

    public string RequestFingerprint => Provenance.RequestFingerprint;

    public string ExecutorId => Provenance.ExecutorId;
}

public sealed record AnalysisExecutionProvenanceDto(
    string CanonicalAnalysisKey,
    string RequestFingerprint,
    string ExecutorId);

public enum SceneDimension
{
    TwoDimensional,
    ThreeDimensional
}

public sealed record VisualizationSelectorOptionDto(int Index, string Label);

public sealed record VisualizationOptionsDto(
    IReadOnlyList<int> SurfaceNumbers,
    IReadOnlyList<VisualizationSelectorOptionDto> Fields,
    IReadOnlyList<VisualizationSelectorOptionDto> Wavelengths);

public sealed record VisualizationRequestDto(
    SceneDimension Dimension,
    int? FirstSurface = null,
    int? LastSurface = null,
    int? FieldIndex = null,
    int? WavelengthIndex = null,
    bool IncludeAllWavelengths = false,
    int RayCount = 3,
    double LowerPupil = -0.85,
    double UpperPupil = 0.85,
    bool DeleteVignetted = true,
    bool MarginalAndChiefOnly = false);

public sealed record ScenePoint2Dto(double Z, double Y);

public sealed record ScenePoint3Dto(double X, double Y, double Z);

public sealed record SceneRayDirection2Dto(double Z, double Y);

public sealed record SceneRayDirection3Dto(double X, double Y, double Z);

public enum SceneRayInteractionType
{
    None,
    Refractive,
    Reflective,
    Diffractive,
    ThinLens,
    Phase
}

public enum SceneRaySegmentType
{
    Unspecified,
    Incident,
    Transmitted,
    Reflected,
    TotalInternalReflection
}

public sealed record SceneRaySegment2Dto(
    ScenePoint2Dto Start,
    ScenePoint2Dto End,
    SceneRayDirection2Dto Direction,
    SceneRaySegmentType SegmentType,
    SceneRayInteractionType InteractionType,
    int? SourceSurfaceNumber,
    int? TargetSurfaceNumber);

public sealed record SceneRaySegment3Dto(
    ScenePoint3Dto Start,
    ScenePoint3Dto End,
    SceneRayDirection3Dto Direction,
    SceneRaySegmentType SegmentType,
    SceneRayInteractionType InteractionType,
    int? SourceSurfaceNumber,
    int? TargetSurfaceNumber);

public sealed record SceneSurfaceFace3Dto(IReadOnlyList<ScenePoint3Dto> Points);

public sealed record SceneSurface2Dto(
    int SurfaceNumber,
    string Label,
    bool IsStop,
    bool IsReferencePlane,
    IReadOnlyList<ScenePoint2Dto> Points,
    bool IsStandaloneStop = false);

public sealed record SceneLensEdge2Dto(
    int FrontSurfaceNumber,
    int BackSurfaceNumber,
    ScenePoint2Dto Start,
    ScenePoint2Dto End);

public sealed record SceneLensElement2Dto(
    int FrontSurfaceNumber,
    int BackSurfaceNumber,
    string Material,
    IReadOnlyList<ScenePoint2Dto> Boundary);

public sealed record SceneRay2Dto(
    int RayNumber,
    int FieldIndex,
    int PupilIndex,
    int WavelengthIndex,
    double WavelengthNanometers,
    bool Vignetted,
    double FinalIntensity,
    IReadOnlyList<ScenePoint2Dto> Points,
    IReadOnlyList<SceneRaySegment2Dto> Segments);

public sealed record Scene2Dto(
    IReadOnlyList<SceneSurface2Dto> Surfaces,
    IReadOnlyList<SceneLensElement2Dto> LensElements,
    IReadOnlyList<SceneLensEdge2Dto> LensEdges,
    IReadOnlyList<SceneRay2Dto> Rays,
    double ZMin,
    double ZMax,
    double YExtent);

public sealed record SceneSurface3Dto(
    int SurfaceNumber,
    string Label,
    bool IsStop,
    bool IsReferencePlane,
    string Material,
    IReadOnlyList<ScenePoint3Dto> Rim,
    IReadOnlyList<ScenePoint3Dto> MeridianY,
    IReadOnlyList<ScenePoint3Dto> MeridianX,
    IReadOnlyList<SceneSurfaceFace3Dto> Faces,
    bool IsStandaloneStop = false);

public sealed record SceneLensElement3Dto(
    int FrontSurfaceNumber,
    int BackSurfaceNumber,
    string Material,
    double RefractiveIndex,
    IReadOnlyList<ScenePoint3Dto> FrontRim,
    IReadOnlyList<ScenePoint3Dto> BackRim,
    IReadOnlyList<SceneSurfaceFace3Dto> FrontFaces,
    IReadOnlyList<SceneSurfaceFace3Dto> BackFaces,
    IReadOnlyList<ScenePoint3Dto> MeridianBoundary);

public sealed record SceneRay3Dto(
    int RayNumber,
    int FieldIndex,
    int PupilIndex,
    int WavelengthIndex,
    double WavelengthNanometers,
    bool Vignetted,
    double FinalIntensity,
    IReadOnlyList<ScenePoint3Dto> Points,
    IReadOnlyList<SceneRaySegment3Dto> Segments);

public sealed record Scene3Dto(
    IReadOnlyList<SceneSurface3Dto> Surfaces,
    IReadOnlyList<SceneLensElement3Dto> LensElements,
    IReadOnlyList<SceneRay3Dto> Rays,
    double XExtent,
    double YExtent,
    double ZMin,
    double ZMax);

public sealed record SceneDto(
    long SourceRevision,
    SceneDimension Dimension,
    Scene2Dto? TwoDimensional,
    Scene3Dto? ThreeDimensional,
    OpticalDocumentSnapshot Summary);

public enum CadExportFormat
{
    Step
}

public sealed record CadExportOptionsDto(
    CadExportFormat Format = CadExportFormat.Step,
    int SurfaceSamples = 33,
    int AngularSamples = 64,
    double MaximumChordErrorMillimeters = 0.005,
    int MaximumTrianglesPerPart = 500_000);

public sealed record CadExportResultDto(
    string Path,
    CadExportFormat Format,
    long ByteCount,
    int PartCount = 0,
    int VertexCount = 0,
    int TriangleCount = 0,
    IReadOnlyList<string>? Warnings = null);

public sealed record OptimizationResultDto(
    string Optimizer,
    string Message,
    double InitialRadius,
    double FinalRadius,
    double Merit,
    int Iterations);

public enum OptimizationVariableUpdateMode
{
    ClearAll,
    SetAllRadii,
    SetAllThicknesses
}

public sealed record OptimizationVariableUpdateResultDto(
    OptimizationVariableUpdateMode Mode,
    int RadiusVariableCount,
    int ThicknessVariableCount);

public sealed record QuickFocusResultDto(
    int SurfaceNumber,
    double InitialThickness,
    double AppliedShift,
    double FinalThickness,
    double RmsSpotRadius);

public enum MeritFunctionPreset
{
    RmsSpot,
    RmsWavefront
}

public enum OptimizationImageQuality
{
    RmsWavefront,
    Contrast,
    RmsSpot,
    Angular
}

public enum OptimizationPupilSampling
{
    GaussianQuadrature,
    RectangularArray
}

public enum OptimizationSpotReference
{
    Centroid,
    ChiefRay,
    Unreferenced
}

public sealed record OptimizationWizardSettingsDto(
    OptimizationImageQuality ImageQuality,
    OptimizationPupilSampling PupilSampling,
    int PupilRings,
    int PupilArms,
    double PupilObscuration,
    int StartRow,
    double WeightScale,
    bool UseAllWavelengths,
    bool IncludeCommonOperands,
    bool ReplaceExisting,
    OptimizationSpotReference Reference = OptimizationSpotReference.Centroid,
    double SpatialFrequency = 30,
    double XWeight = 1,
    double YWeight = 1,
    bool IgnoreLateralColor = false);

public sealed record MeritOperandTypeDto(
    string Code,
    string DisplayName,
    string Description);

public sealed record MeritOperandRowDto(
    int Index,
    bool Enabled,
    string Type,
    int Surface,
    int Field,
    int Wavelength,
    double Hx,
    double Hy,
    double Px,
    double Py,
    double Target,
    double Weight,
    double Value,
    double Contribution,
    string Comment,
    string Error = "",
    int PupilRings = 3,
    int PupilArms = 6,
    double PupilObscuration = 0,
    string PupilSampling = "hexapolar",
    double SpatialFrequency = 30,
    bool IgnoreLateralColor = false,
    bool PolychromaticReference = false);

public enum OptimizationVariableKind
{
    Radius,
    Thickness
}

public sealed record OptimizationVariableResultDto(
    int SurfaceNumber,
    OptimizationVariableKind Kind,
    string Name,
    double InitialValue,
    double FinalValue);

public sealed record OptimizationRunResultDto(
    string Optimizer,
    string Message,
    double InitialMerit,
    double FinalMerit,
    int Iterations,
    IReadOnlyList<OptimizationVariableResultDto> Variables);

public enum ToleranceOperandKind
{
    Radius,
    Thickness,
    Conic,
    DecenterX,
    DecenterY,
    TiltX,
    TiltY,
    RefractiveIndex,
    AbbeNumber,
    Compensator
}

public enum ToleranceDistribution
{
    Normal,
    Uniform
}

public enum ToleranceCriterion
{
    RmsSpotRadius,
    RmsWavefront
}

public enum RadiusToleranceMode
{
    Fixed,
    Percent
}

public sealed record ToleranceOperandDto(
    int Index,
    bool Enabled,
    ToleranceOperandKind Kind,
    int SurfaceNumber,
    double Minimum,
    double Maximum,
    ToleranceDistribution Distribution = ToleranceDistribution.Normal,
    string Comment = "");

public sealed record ToleranceWizardSettingsDto(
    int StartSurface,
    int EndSurface,
    bool IncludeRadius,
    RadiusToleranceMode RadiusMode,
    double RadiusTolerance,
    bool IncludeThickness,
    double ThicknessTolerance,
    bool IncludeDecenter,
    double DecenterTolerance,
    bool IncludeTilt,
    double TiltToleranceDegrees,
    bool IncludeRefractiveIndex,
    double RefractiveIndexTolerance,
    bool IncludeAbbeNumber,
    double AbbeNumberTolerance,
    bool IncludeImageCompensator,
    double CompensatorMinimum,
    double CompensatorMaximum,
    ToleranceDistribution Distribution = ToleranceDistribution.Normal,
    bool ReplaceExisting = true,
    bool IncludeConic = false,
    double ConicTolerance = 0);

public sealed record ToleranceValidationResultDto(
    bool IsValid,
    IReadOnlyList<string> Messages);

public sealed record TolerancingRequestDto(
    int SurfaceNumber,
    double RadiusSigma,
    double ThicknessSigma,
    int Trials,
    int Seed,
    int CompensationIterations,
    IReadOnlyList<ToleranceOperandDto>? Operands = null,
    ToleranceCriterion Criterion = ToleranceCriterion.RmsSpotRadius,
    double YieldLimit = 0,
    int MaxDegreeOfParallelism = -1);

public sealed record TolerancingSensitivityRowDto(
    string Perturbation,
    string DeltaMerit,
    string NegativeMerit = "",
    string PositiveMerit = "",
    string WorstMerit = "");

public sealed record TolerancingTrialRowDto(
    int Trial,
    string Merit,
    string CompensatedMerit,
    string Degradation = "");

public sealed record TolerancingStatisticsDto(
    string Nominal,
    string Mean,
    string StandardDeviation,
    string Minimum,
    string Maximum,
    string Percentile50,
    string Percentile90,
    string Percentile95,
    string Yield);

public sealed record TolerancingResultDto(
    string Summary,
    IReadOnlyList<TolerancingSensitivityRowDto> SensitivityRows,
    IReadOnlyList<TolerancingTrialRowDto> TrialRows,
    string Details,
    TolerancingStatisticsDto? Statistics = null);

public sealed record MultiConfigurationRowDto(
    int Index,
    string Name,
    bool Active,
    int SurfaceCount,
    string TotalTrack,
    string EffectiveFocalLength);
