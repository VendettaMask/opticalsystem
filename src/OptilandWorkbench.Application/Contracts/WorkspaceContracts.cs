namespace OptilandWorkbench.Application.Contracts;

public enum NonSequentialObjectKind
{
    SourceRay,
    SourcePoint,
    SourceRectangle,
    SourceGaussian,
    PlaneRectangle,
    Sphere,
    Cylinder,
    Box,
    StandardLens,
    Mesh,
    DetectorRectangle,
    SourceEllipse,
    SourceTwoAngle,
    SourceRadial,
    SourceVolumeRectangle,
    SourceVolumeEllipse,
    SourceVolumeCylinder
}

public enum NonSequentialSourceApertureShape
{
    Rectangle,
    Ellipse
}

public enum NonSequentialSurfaceSourceAngularDistribution
{
    LegacyUniformCone,
    VirtualPoint,
    Cosine,
    Gaussian
}

public enum NonSequentialVolumeSourceAngularDistribution
{
    LegacyForwardCone,
    UniformSphere
}

public enum NonSequentialSurfaceBehavior
{
    Refractive,
    Reflective,
    Absorbing
}

public sealed record NonSequentialVector3(double X, double Y, double Z);

public sealed record NonSequentialTraceSettings(
    int LayoutRaysPerSource,
    int AnalysisRaysPerSource,
    int MaximumTotalSourceRays,
    int MaximumSegmentsPerRay,
    int MaximumActiveBranches,
    double MinimumRelativeIntensity,
    int RandomSeed,
    bool SplitFresnelRays);

public abstract record NonSequentialObjectParameters;

public abstract record SourceParameters(
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount) : NonSequentialObjectParameters;

public sealed record SourceRayParameters(
    double PowerWatts,
    int WavelengthNumber,
    NonSequentialVector3 Origin,
    NonSequentialVector3 Direction) : SourceParameters(PowerWatts, WavelengthNumber, 1, 1);

public sealed record SourcePointParameters(
    double PowerWatts,
    int WavelengthNumber,
    double ConeHalfAngleDegrees,
    int LayoutRayCount,
    int AnalysisRayCount) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceRectangleParameters(
    double WidthMillimeters,
    double HeightMillimeters,
    double AngularHalfAngleDegrees,
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount,
    NonSequentialSurfaceSourceAngularDistribution AngularDistribution = NonSequentialSurfaceSourceAngularDistribution.LegacyUniformCone,
    double SourceDistanceMillimeters = 0,
    double CosineExponent = 1,
    double GaussianX = 1,
    double GaussianY = 1,
    double SourceX = 0,
    double SourceY = 0,
    double MinimumXHalfWidthMillimeters = 0,
    double MinimumYHalfWidthMillimeters = 0) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceGaussianParameters(
    double WaistXMillimeters,
    double WaistYMillimeters,
    double DivergenceHalfAngleDegrees,
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceEllipseParameters(
    double WidthMillimeters,
    double HeightMillimeters,
    double AngularHalfAngleDegrees,
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount,
    NonSequentialSurfaceSourceAngularDistribution AngularDistribution = NonSequentialSurfaceSourceAngularDistribution.LegacyUniformCone,
    double SourceDistanceMillimeters = 0,
    double CosineExponent = 1,
    double GaussianX = 1,
    double GaussianY = 1,
    double SourceX = 0,
    double SourceY = 0,
    double MinimumXHalfWidthMillimeters = 0,
    double MinimumYHalfWidthMillimeters = 0) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceTwoAngleParameters(
    double WidthMillimeters,
    double HeightMillimeters,
    NonSequentialSourceApertureShape Shape,
    double AngularHalfAngleXDegrees,
    double AngularHalfAngleYDegrees,
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceRadialSample(double AngleDegrees, double RelativeIntensity);

public sealed record SourceRadialParameters(
    IReadOnlyList<SourceRadialSample> Samples,
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount) : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceVolumeRectangleParameters(
    double WidthMillimeters,
    double HeightMillimeters,
    double DepthMillimeters,
    double AngularHalfAngleDegrees,
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount,
    NonSequentialVolumeSourceAngularDistribution AngularDistribution = NonSequentialVolumeSourceAngularDistribution.LegacyForwardCone)
    : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceVolumeEllipseParameters(
    double SemiAxisXMillimeters,
    double SemiAxisYMillimeters,
    double SemiAxisZMillimeters,
    double AngularHalfAngleDegrees,
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount,
    NonSequentialVolumeSourceAngularDistribution AngularDistribution = NonSequentialVolumeSourceAngularDistribution.LegacyForwardCone)
    : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record SourceVolumeCylinderParameters(
    double RadiusXMillimeters,
    double RadiusYMillimeters,
    double LengthMillimeters,
    double AngularHalfAngleDegrees,
    double PowerWatts,
    int WavelengthNumber,
    int LayoutRayCount,
    int AnalysisRayCount,
    NonSequentialVolumeSourceAngularDistribution AngularDistribution = NonSequentialVolumeSourceAngularDistribution.LegacyForwardCone)
    : SourceParameters(PowerWatts, WavelengthNumber, LayoutRayCount, AnalysisRayCount);

public sealed record PlaneRectangleParameters(
    double WidthMillimeters,
    double HeightMillimeters,
    NonSequentialSurfaceBehavior Behavior,
    string MaterialBefore,
    string MaterialAfter) : NonSequentialObjectParameters;

public sealed record SphereParameters(
    double RadiusMillimeters,
    string Material,
    NonSequentialSurfaceBehavior Behavior) : NonSequentialObjectParameters;

public sealed record CylinderParameters(
    double RadiusMillimeters,
    double LengthMillimeters,
    string Material,
    NonSequentialSurfaceBehavior Behavior) : NonSequentialObjectParameters;

public sealed record BoxParameters(
    double WidthMillimeters,
    double HeightMillimeters,
    double LengthMillimeters,
    string Material,
    NonSequentialSurfaceBehavior Behavior) : NonSequentialObjectParameters;

public sealed record StandardLensParameters(
    double FrontRadiusMillimeters,
    double BackRadiusMillimeters,
    double FrontConic,
    double BackConic,
    double CenterThicknessMillimeters,
    double SemiDiameterMillimeters,
    string Material) : NonSequentialObjectParameters;

public sealed record MeshObjectParameters(
    Guid MeshAssetId,
    NonSequentialSurfaceBehavior Behavior,
    string Material,
    bool TwoSided,
    string OriginalFileName,
    string Sha256,
    int VertexCount,
    int TriangleCount,
    bool IsClosed,
    IReadOnlyList<string> Warnings) : NonSequentialObjectParameters;

public sealed record DetectorRectangleParameters(
    double WidthMillimeters,
    double HeightMillimeters,
    int PixelsX,
    int PixelsY,
    bool FrontOnly,
    bool Absorb) : NonSequentialObjectParameters;

public enum OpticalWorkbenchMode
{
    Sequential,
    NonSequential
}

public sealed record WorkbenchModeChangedEventArgs(
    OpticalWorkbenchMode PreviousMode,
    OpticalWorkbenchMode CurrentMode);

public sealed record NonSequentialObjectRowDto(
    Guid Id,
    int ObjectNumber,
    bool Enabled,
    bool Visible,
    NonSequentialObjectKind Kind,
    string Name,
    string Role,
    Guid? ReferenceObjectId,
    Guid? ContainingObjectId,
    double X,
    double Y,
    double Z,
    double TiltXDegrees,
    double TiltYDegrees,
    double TiltZDegrees,
    string Material,
    NonSequentialObjectParameters Parameters,
    string ParameterSummary);

public sealed record NonSequentialWavelengthDto(
    int Index,
    string Label,
    double Nanometers,
    double Weight,
    bool IsPrimary);

public sealed record NonSequentialDocumentDto(
    string Name,
    string AmbientMaterial,
    IReadOnlyList<NonSequentialWavelengthDto> Wavelengths,
    IReadOnlyList<NonSequentialObjectRowDto> Objects,
    NonSequentialTraceSettings TraceSettings);

public sealed record NonSequentialObjectUpdateDto(
    Guid Id,
    bool Enabled,
    bool Visible,
    NonSequentialObjectKind Kind,
    string Name,
    Guid? ReferenceObjectId,
    Guid? ContainingObjectId,
    double X,
    double Y,
    double Z,
    double TiltXDegrees,
    double TiltYDegrees,
    double TiltZDegrees,
    NonSequentialObjectParameters Parameters);

public sealed record NonSequentialConversionResultDto(
    int ObjectCount,
    IReadOnlyList<string> Warnings);

public enum NonSequentialMeshUnit
{
    Millimeter,
    Centimeter,
    Meter,
    Inch
}

public sealed record NonSequentialMeshImportOptionsDto(
    NonSequentialMeshUnit Unit = NonSequentialMeshUnit.Millimeter,
    NonSequentialSurfaceBehavior Behavior = NonSequentialSurfaceBehavior.Absorbing,
    string Material = "Air",
    bool TwoSided = true,
    int? InsertionIndex = null);

public sealed record NonSequentialMeshImportResultDto(
    Guid ObjectId,
    Guid AssetId,
    string Name,
    int VertexCount,
    int TriangleCount,
    bool IsClosed,
    bool IsManifold,
    double SignedVolumeCubicMillimeters,
    IReadOnlyList<string> Warnings);

public enum NonSequentialTraceOutputMode
{
    LayoutSample,
    InMemory,
    RayDatabase,
    SummaryOnly
}

public enum NonSequentialTraceCommand
{
    ClearAndTrace,
    TraceOnly,
    ClearOnly
}

public enum NonSequentialSplittingMode
{
    None,
    FullFresnel,
    SimpleStochastic
}

public enum NonSequentialTraceSessionState
{
    Empty,
    Running,
    Completed,
    Warning,
    Failed,
    Canceled
}

public sealed record NonSequentialTraceRunRequestDto(
    NonSequentialTraceOutputMode OutputMode = NonSequentialTraceOutputMode.InMemory,
    Guid? SourceObjectId = null,
    bool AnalysisRays = true,
    bool? SplitFresnelRays = null,
    int MaximumRetainedBranches = 2_000,
    string? PathFilterExpression = null,
    string? RayDatabasePath = null,
    NonSequentialTraceCommand Command = NonSequentialTraceCommand.ClearAndTrace,
    NonSequentialSplittingMode? SplittingMode = null,
    int? RandomSeed = null,
    int? MaximumSegmentsPerRay = null,
    int? MaximumActiveBranches = null,
    double? MinimumRelativeIntensity = null,
    int? RayCountOverride = null,
    IReadOnlyList<Guid>? SourceObjectIds = null);

public sealed record NonSequentialTraceRunResultDto(
    int TotalBranchCount,
    int MatchedBranchCount,
    int RetainedBranchCount,
    long SegmentCount,
    double SourcePowerWatts,
    double DetectorPowerWatts,
    double AbsorbedPowerWatts,
    double EscapedPowerWatts,
    double TruncatedPowerWatts,
    string? RayDatabasePath,
    long RayDatabaseBytes,
    Guid? SessionId = null,
    NonSequentialTraceSessionState SessionState = NonSequentialTraceSessionState.Completed,
    TimeSpan? Elapsed = null,
    int TracePassCount = 1,
    bool IsStale = false,
    IReadOnlyList<string>? Warnings = null);

public sealed record NonSequentialTraceSessionDto(
    Guid Id,
    NonSequentialTraceSessionState State,
    string SceneHash,
    long SourceRevision,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    int TracePassCount,
    int RandomSeed,
    NonSequentialSplittingMode SplittingMode,
    IReadOnlyList<Guid> SourceObjectIds,
    long BranchCount,
    long SegmentCount,
    double SourcePowerWatts,
    double DetectorPowerWatts,
    double AbsorbedPowerWatts,
    double EscapedPowerWatts,
    double TruncatedPowerWatts,
    double GeometryErrorPowerWatts,
    int GeometryErrorCount,
    TimeSpan Elapsed,
    string RayDatabasePath,
    bool IsTemporaryDatabase,
    bool IsStale,
    string? FilterExpression,
    IReadOnlyList<string> Warnings,
    string? TraceConfigurationFingerprint = null);

public enum NonSequentialDetectorSpace
{
    Position,
    Angle
}

public enum NonSequentialDetectorDataType
{
    PixelPower,
    IncoherentIrradiance,
    HitCount,
    RadiantIntensity
}

public sealed record NonSequentialDetectorViewRequestDto(
    Guid DetectorId,
    NonSequentialDetectorSpace Space = NonSequentialDetectorSpace.Position,
    NonSequentialDetectorDataType DataType = NonSequentialDetectorDataType.IncoherentIrradiance,
    int WavelengthNumber = 0,
    string? PathFilterExpression = null,
    string? RayDatabasePath = null);

public sealed record NonSequentialDetectorStatisticsDto(
    double TotalPowerWatts,
    long TotalHits,
    double PeakValue,
    double CentroidX,
    double CentroidY,
    double RmsX,
    double RmsY,
    double Uniformity);

public sealed record NonSequentialDetectorViewDto(
    Guid DetectorId,
    string DetectorName,
    int PixelsX,
    int PixelsY,
    double XMinimum,
    double XMaximum,
    double YMinimum,
    double YMaximum,
    string XUnit,
    string YUnit,
    string ValueUnit,
    IReadOnlyList<double> Values,
    IReadOnlyList<double> XProfile,
    IReadOnlyList<double> YProfile,
    NonSequentialDetectorStatisticsDto Statistics,
    bool IsStale,
    string ResultSource);

public sealed record NonSequentialRaySegmentDto(
    long BranchId,
    Guid? ObjectId,
    int ObjectNumber,
    string ObjectName,
    int FaceNumber,
    string Interaction,
    double X,
    double Y,
    double Z,
    double L,
    double M,
    double N,
    double PowerWatts,
    double WavelengthNanometers,
    double GeometricPathLength,
    double OpticalPathLength);

public sealed record NonSequentialRayBranchDto(
    long Id,
    long? ParentId,
    int Level,
    Guid? SourceObjectId,
    string TerminationReason,
    double FinalPowerWatts,
    double WavelengthNanometers,
    IReadOnlyList<NonSequentialRaySegmentDto> Segments);

public sealed record NonSequentialRayDatabasePageDto(
    string Path,
    long TotalBranchCount,
    int PageIndex,
    int PageSize,
    bool IsStale,
    IReadOnlyList<NonSequentialRayBranchDto> Branches);

public sealed record NonSequentialPathSummaryDto(
    string Path,
    string FilterExpression,
    int RayCount,
    double TotalPowerWatts,
    double PowerFraction,
    double MinimumOpticalPathLength,
    double AverageOpticalPathLength,
    double MaximumOpticalPathLength,
    string TerminationReason);

public sealed record NonSequentialRayDatabaseDto(
    string Path,
    string SceneHash,
    long SourceRevision,
    DateTimeOffset CreatedUtc,
    long BranchCount,
    bool IsStale,
    string? StoredFilterExpression,
    IReadOnlyList<NonSequentialPathSummaryDto> Paths);

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
    Tolerancing,
    NonSequential
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
    int WavelengthCount,
    double EntrancePupilDiameter = 0,
    bool IsDirty = false);

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
    string SourcePath,
    double NumericalAperture,
    string NumericalApertureBasis,
    double WorkingDistance,
    string WorkingDistanceBasis,
    int LensElementCount,
    double MaximumClearAperture,
    string LensType,
    string Application,
    string DesignOrganization,
    DateTimeOffset? ImportedAt,
    string ImporterVersion);

public sealed record LensLibraryCatalogDocument(
    int Version,
    DateTimeOffset BuiltAt,
    IReadOnlyList<LensLibraryEntryDto> Entries);

public sealed record CommercialLensEntryDto(
    string Id,
    string Manufacturer,
    string PartNumber,
    string Name,
    string ProductStatus,
    string ProductUrl,
    string DataSheetUrl,
    string LensType,
    string ShapeCode,
    string SurfaceType,
    int ElementCount,
    double EffectiveFocalLength,
    double CatalogDiameter,
    double ClearAperture,
    double BackFocalLength,
    double NumericalAperture,
    double MinimumWavelengthNanometers,
    double MaximumWavelengthNanometers,
    double MinimumWorkingDistance,
    double MaximumWorkingDistance,
    string ModelStatus,
    string? NativePath,
    string SourceNote,
    DateTimeOffset VerifiedAt,
    double EntrancePupilDiameter);

public sealed record CommercialLensCatalogDocument(
    int Version,
    DateTimeOffset BuiltAt,
    IReadOnlyList<CommercialLensEntryDto> Entries);

public sealed record StockLensMatchRequestDto(
    double TargetEffectiveFocalLength,
    double TargetEntrancePupilDiameter,
    IReadOnlyList<string> Manufacturers,
    int MaximumResults,
    double EffectiveFocalLengthTolerancePercent,
    double EntrancePupilDiameterTolerancePercent,
    bool MatchShape,
    string TargetShapeCode,
    bool MatchPowerDirection);

public sealed record StockLensMatchResultDto(
    CommercialLensEntryDto Entry,
    double EffectiveFocalLengthDeviationPercent,
    double EntrancePupilDiameterDeviationPercent,
    double NormalizedScore,
    bool DirectionMatches,
    bool ShapeMatches);

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
    bool SemiDiameterFixed = false,
    bool GeometryComputable = true);

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
    Transmission,
    Power,
    Count
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
    PartsPerMillionPerKelvin,
    Watt
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
    bool MarginalAndChiefOnly = false,
    bool IncludeStaleNonSequentialRays = false);

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

public enum SceneSurfaceRenderRole
{
    OpticalSurface,
    NonSequentialObject,
    Source,
    Detector
}

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
    bool IsStandaloneStop = false,
    SceneSurfaceRenderRole RenderRole = SceneSurfaceRenderRole.OpticalSurface,
    double? DisplayWavelengthNanometers = null);

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

public sealed record NonSequentialLayoutResultDto(
    bool HasResult,
    string CurrentSceneHash,
    string? ResultSceneHash,
    long? ResultRevision,
    bool IsStale,
    bool RaysLoaded,
    string? DatabasePath);

public sealed record SceneDto(
    long SourceRevision,
    SceneDimension Dimension,
    Scene2Dto? TwoDimensional,
    Scene3Dto? ThreeDimensional,
    OpticalDocumentSnapshot Summary,
    NonSequentialLayoutResultDto? NonSequentialLayoutResult = null);

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
    int Iterations,
    string AlgorithmVersion = "",
    string StopReason = "",
    double? GradientNorm = null,
    long FunctionEvaluations = 0,
    int? RandomSeed = null,
    IReadOnlyList<string>? Warnings = null);

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
    bool PolychromaticReference = false,
    bool CompatibilityOnly = false);

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
    IReadOnlyList<OptimizationVariableResultDto> Variables,
    string AlgorithmVersion = "",
    string StopReason = "",
    double? GradientNorm = null,
    long FunctionEvaluations = 0,
    int? RandomSeed = null,
    IReadOnlyList<string>? Warnings = null);

public enum ToleranceOperandKind
{
    Radius,
    Thickness,
    Conic,
    DecenterX,
    DecenterY,
    TiltX,
    TiltY,
    ElementDecenterX,
    ElementDecenterY,
    ElementTiltX,
    ElementTiltY,
    AsphereCoefficient,
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

public enum ToleranceAnalysisMode
{
    Sensitivity,
    InverseLimit,
    InverseIncrement,
    SkipSensitivity
}

public enum ToleranceInverseEndpointStatus
{
    UnchangedWithinTarget,
    Tightened,
    ZeroRange,
    UnsupportedPerturbation
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
    string Comment = "",
    int EndSurfaceNumber = -1,
    int ParameterIndex = 0);

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
    double ConicTolerance = 0,
    bool UseElementGroups = false,
    bool IncludeAsphereCoefficients = false,
    double AsphereCoefficientTolerance = 0);

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
    int MaxDegreeOfParallelism = -1,
    ToleranceAnalysisMode Mode = ToleranceAnalysisMode.Sensitivity,
    double InverseValue = 0);

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

public sealed record TolerancingSensitivityStatisticsDto(
    string Nominal,
    string RssEstimatedChange,
    string EstimatedCriterion);

public sealed record TolerancingInverseEndpointDto(
    string OriginalTolerance,
    string AdjustedTolerance,
    string Criterion,
    ToleranceInverseEndpointStatus Status,
    int Iterations);

public sealed record TolerancingInverseRowDto(
    string Perturbation,
    TolerancingInverseEndpointDto Minimum,
    TolerancingInverseEndpointDto Maximum);

public sealed record TolerancingResultDto(
    string Summary,
    IReadOnlyList<TolerancingSensitivityRowDto> SensitivityRows,
    IReadOnlyList<TolerancingTrialRowDto> TrialRows,
    string Details,
    TolerancingStatisticsDto? Statistics = null,
    TolerancingSensitivityStatisticsDto? SensitivityStatistics = null,
    long SourceRevision = 0,
    IReadOnlyList<TolerancingInverseRowDto>? InverseRows = null,
    IReadOnlyList<ToleranceOperandDto>? AdjustedOperands = null,
    string InverseTarget = "");

public sealed record MultiConfigurationRowDto(
    int Index,
    string Name,
    bool Active,
    int SurfaceCount,
    string TotalTrack,
    string EffectiveFocalLength);
