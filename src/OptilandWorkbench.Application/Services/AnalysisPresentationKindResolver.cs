using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.Application.Services;

internal static class AnalysisPresentationKindResolver
{
    public static AnalysisPresentationKind Resolve(string canonicalAnalysisKey)
    {
        return canonicalAnalysisKey switch
        {
            "Cardinal Points Data" => AnalysisPresentationKind.CardinalPoints,
            "Seidel Coefficients" => AnalysisPresentationKind.SeidelCoefficients,
            "Zernike Fringe" => AnalysisPresentationKind.ZernikeFringe,
            "Zernike Standard" => AnalysisPresentationKind.ZernikeStandard,
            "Zernike Annular" => AnalysisPresentationKind.ZernikeAnnular,
            "Seidel Diagram" => AnalysisPresentationKind.SeidelDiagram,
            "Full Field Aberration" => AnalysisPresentationKind.FullFieldAberration,
            "Wavefront Map" => AnalysisPresentationKind.WavefrontMap,
            "PSF" => AnalysisPresentationKind.FftPsf,
            "Huygens PSF" => AnalysisPresentationKind.HuygensPsf,
            "Foucault Analysis" => AnalysisPresentationKind.Foucault,
            "Spot Diagram" => AnalysisPresentationKind.SpotDiagram,
            "Through Focus" => AnalysisPresentationKind.ThroughFocusSpot,
            "Matrix Spot Diagram" => AnalysisPresentationKind.MatrixSpot,
            "Configuration Matrix Spot Diagram" => AnalysisPresentationKind.ConfigurationMatrixSpot,
            "Full Field Spot Diagram" => AnalysisPresentationKind.FullFieldSpot,
            "Ray Fan" => AnalysisPresentationKind.RayFan,
            "Optical Path Difference" => AnalysisPresentationKind.OpticalPathDifference,
            "Footprint Diagram" => AnalysisPresentationKind.FootprintDiagram,
            "Axial Aberration" => AnalysisPresentationKind.AxialAberration,
            "Lateral Color" => AnalysisPresentationKind.LateralColor,
            "Color Focus Shift" => AnalysisPresentationKind.ColorFocusShift,
            "Field Curvature and Distortion" => AnalysisPresentationKind.FieldCurvatureAndDistortion,
            "Field Curvature" => AnalysisPresentationKind.FieldCurvature,
            "Distortion" => AnalysisPresentationKind.Distortion,
            _ => AnalysisPresentationKind.Standard
        };
    }
}
