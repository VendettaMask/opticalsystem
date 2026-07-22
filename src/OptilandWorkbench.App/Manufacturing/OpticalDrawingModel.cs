using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Manufacturing;

public enum OpticalDrawingPageSize
{
    A4,
    A3
}

public sealed record OpticalDrawingSheet(
    OpticalElementDefinition Element,
    OpticalDrawingPageSize PageSize,
    string DrawingNumber,
    string PartName,
    string Designer,
    string Reviewer,
    string Revision,
    double DiameterUpperDeviation,
    double DiameterLowerDeviation,
    double CenterThicknessUpperDeviation,
    double CenterThicknessLowerDeviation,
    double FrontSurfaceFormNanometers,
    double BackSurfaceFormNanometers,
    double CenteringToleranceArcMinutes,
    double SurfaceTextureNanometers,
    string SurfaceImperfection,
    string Coating,
    string EdgeTreatment,
    string StressBirefringence,
    string BubblesAndInclusions,
    string HomogeneityAndStriae,
    GlassMaterialDto? MaterialData = null,
    byte[]? CompanyLogoPng = null,
    double RefractiveIndexTolerance = 0.0005,
    double AbbeNumberTolerance = 0.5);
