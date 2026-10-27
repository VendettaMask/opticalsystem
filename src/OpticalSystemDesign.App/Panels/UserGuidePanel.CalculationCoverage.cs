using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class UserGuidePanel
{
    // Documentation mapping only. Never selects a numerical implementation.
    internal static string? CalculationChapterForAnalysis(string key) => (key switch
    {
        "Single Ray Trace" => "ray-tracing",
        "Non-Sequential Ray Trace" or "Non-Sequential Detector Viewer" => "non-sequential",
        "First Order" or "Cardinal Points Data" or "Y-Ybar" or "System Data Report" => "paraxial",
        "Seidel Coefficients" or "Seidel Diagram" or "Grid Distortion" or "Field Curvature and Distortion" or
            "Field Curvature" or "Color Focus Shift" or "Lateral Color" or "Axial Aberration" or "Full Field Aberration" => "seidel-field",
        "Spot Diagram" or "Full Field Spot Diagram" or "Matrix Spot Diagram" or "Configuration Matrix Spot Diagram" or "Ray Fan" => "spot-aberrations",
        "Footprint Diagram" or "Vignetting Diagram" or "Pupil Aberration" => "pupil-sampling",
        "Encircled Energy" or "Diffraction Encircled Energy" or "MTF" or "Huygens MTF" or "Geometric MTF" or "Sampled MTF" or "Contrast Loss Map" => "mtf-energy",
        "Geometric Line Edge Spread" or "FFT Line Edge Spread" or "Extended Source Encircled Energy" => "line-edge",
        "RMS vs Field" or "RMS vs Wavelength" or "RMS vs Focus" or "RMS Field Map" or "RMS Wavefront vs Field" or "Through Focus" or
            "Fourier Through Focus MTF" or "Huygens Through Focus MTF" or "Geometric Through Focus MTF" or
            "Fourier MTF vs Field" or "Huygens MTF vs Field" or "Geometric MTF vs Field" or
            "Angle vs Image Height" or "Angle vs Image Height - Through Pupil" or "Angle vs Image Height - Through Field" or
            "Centroid Sphere Wavefront" or "Best Fit Sphere Wavefront" => "scans-configurations",
        "Relative Illumination" or "Incoherent Irradiance" or "Radiant Intensity" => "illumination",
        "PSF" or "FFT PSF Cross Section" or "Huygens PSF" or "Huygens PSF Cross Section" or
            "Optical Path Difference" or "Wavefront Map" or "Wavefront" => "wavefront-psf",
        "Interferogram" => "interferogram",
        "Foucault Analysis" => "foucault",
        "Zernike Fringe" or "Zernike Standard" or "Zernike Annular" or "Zernike vs Field" => "zernike",
        "Image Simulation" or "Geometric Image Analysis" or "Geometric Bitmap Image Analysis" or "Light Source Analysis" or
            "Partially Coherent Image Analysis" or "Extended Diffraction Image Analysis" => "extended-imaging",
        "Jones Pupil" => "polarization-coatings",
        "Prescription Report" or "Classified Data Report" => "pipeline",
        _ => null
    }) is { } topic ? "calculation-" + topic : null;

    private static string? CalculationChapterForCommand(AnalysisRibbonCommand command) => command.Kind switch
    {
        AnalysisRibbonCommandKind.ImaBimViewer or AnalysisRibbonCommandKind.BitmapViewer => null,
        AnalysisRibbonCommandKind.Analysis => CalculationChapterForAnalysis(WorkbenchAnalysisCatalog.CanonicalKey(command.Name)),
        _ => "calculation-non-sequential"
    };

    private void NavigateToChapter(string id)
    {
        _kindFilter.SelectedIndex = 0;
        _search.Text = "";
        // TextBox.TextChanged can be deferred; rebuild now before resolving a
        // chapter that was excluded by the previous query/category.
        RebuildContents();
        if (_chapterItems.TryGetValue(id, out var item)) _contents.SelectedItem = item;
    }
}
