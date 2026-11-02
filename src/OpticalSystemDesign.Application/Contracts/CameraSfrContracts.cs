namespace OpticalSystemDesign.Application.Contracts;

public sealed record CameraSfrRequest(double WavelengthNanometers = 550, int FieldNumber = 1,
    double PixelPitchMicrometers = 3, int Width = 96, int Height = 96,
    double EdgeAngleDegrees = 5, double DarkLevel = .2, double BrightLevel = .8, double EdgeOffsetPixels = .2,
    int PupilSampling = 256, int ImageSamplesPerPixel = 8, int PsfRadiusPixels = 32,
    int RoiX = 0, int RoiY = 0, int RoiWidth = 96, int RoiHeight = 96, int EdgeHalfWindowPixels = 24,
    bool CheckSampling = true, double DifferenceThreshold = .01, bool UseLensWavelengths = false)
{
    public static CameraSfrRequest FromLensSpectrum() => new(UseLensWavelengths: true, PsfRadiusPixels: 24);
}
public sealed record CameraSfrSpectralBand(int WavelengthNumber, double WavelengthNanometers,
    double Weight, double NormalizedWeight, bool IsPrimary);
public sealed record CameraSfrPoint(double CyclesPerPixel, double CyclesPerMillimeter,
    double Sfr, double OpticalMtf, double PixelIntegratedMtf, double? FullPupilMtf = null, double? FullPupilPixelIntegratedMtf = null);
public sealed record CameraSfrProfilePoint(double DistancePixels, double Esf, double Lsf);
public sealed record CameraSfrFrame(CameraSfrRequest Sampling, IReadOnlyList<double> Source,
    IReadOnlyList<double> LinearPixels, IReadOnlyList<CameraSfrPoint> Curve, IReadOnlyList<CameraSfrProfilePoint> Profile,
    double EdgeSlope, double EdgeIntercept, double EdgeFitRmsPixels, double Contrast,
    double? Mtf50CyclesPerPixel, double? Mtf10CyclesPerPixel, double PsfSpacingMicrometers,
    int PsfGridSize, double RelativePsfReadoutIntegral, double MaximumSfrReferenceDifference)
{
    public double? MaximumSfrFullPupilDifference { get; init; }
    public double? MaximumWindowFullPupilDifference { get; init; }
    public IReadOnlyList<CameraSfrSpectralBand>? Spectrum { get; init; }
    public double? ReferenceWavelengthNanometers { get; init; }
}
public sealed record CameraSfrSamplingComparison(string Axis, CameraSfrRequest Requested, CameraSfrFrame? Refined,
    double? MaximumImageDifference, double? MaximumSfrDifference, double? MaximumMtfDifference,
    double? RelativeReadoutIntegralDifference, bool WithinCriteria, string Message)
{
    public double? MaximumFullPupilMtfDifference { get; init; }
}
public sealed record CameraSfrResult(string Format, int SchemaVersion, string Algorithm,
    string DocumentName, long SourceRevision, string LensSnapshotJson, string LensSha256,
    CameraSfrRequest Request, CameraSfrFrame Baseline, IReadOnlyList<CameraSfrSamplingComparison> Sampling,
    bool? SamplingConverged)
{
    public const string FormatIdentifier = "OpticalSystemDesign.MonochromeSlantedEdge";
    public const string CurrentAlgorithm = "MonochromeLocalFourierEdge/3";
    public const string WindowAlgorithm = "MonochromeLocalFourierEdge/2";
    public const string PreviousAlgorithm = "MonochromeLocalFourierEdge/1";
    public const string PolychromeAlgorithm = "PolychromeLocalFourierEdge/1";
    public static bool CanRecompute(string? algorithm) => algorithm is CurrentAlgorithm or PolychromeAlgorithm;
    public OpticalDocumentSnapshot? SourceDocument { get; init; }
}

