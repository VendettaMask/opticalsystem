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
    double DiameterTolerance,
    double CenterThicknessTolerance,
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
    GlassMaterialDto? MaterialData = null);
