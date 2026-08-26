namespace OptilandWorkbench.Core.Analysis;

public sealed class AnalysisCatalog
{
    private readonly Optic _optic;

    public AnalysisCatalog(Optic optic)
    {
        _optic = optic;
    }

    // This core catalog intentionally uses each analysis' general-purpose
    // defaults. Product UI presets and captured-file parity settings belong in
    // the Application layer and tests respectively; neither is a universal
    // Zemax requirement.

    public IReadOnlyList<string> Names { get; } = new[]
    {
        "Single Ray Trace",
        "Non-Sequential Ray Trace",
        "Non-Sequential Detector Viewer",
        "First Order",
        "Seidel Coefficients",
        "Seidel Diagram",
        "Spot Diagram",
        "Full Field Spot Diagram",
        "Matrix Spot Diagram",
        "Configuration Matrix Spot Diagram",
        "Ray Fan",
        "Footprint Diagram",
        "Field Curvature and Distortion",
        "Grid Distortion",
        "Field Curvature",
        "Color Focus Shift",
        "Lateral Color",
        "Axial Aberration",
        "Full Field Aberration",
        "Encircled Energy",
        "Diffraction Encircled Energy",
        "Geometric Line Edge Spread",
        "Extended Source Encircled Energy",
        "Pupil Aberration",
        "RMS vs Field",
        "RMS vs Wavelength",
        "RMS vs Focus",
        "RMS Field Map",
        "RMS Wavefront vs Field",
        "Through Focus",
        "Through Focus MTF",
        "Fourier Through Focus MTF",
        "Huygens Through Focus MTF",
        "Geometric Through Focus MTF",
        "Fourier MTF vs Field",
        "Huygens MTF vs Field",
        "Geometric MTF vs Field",
        "Angle vs Image Height",
        "Angle vs Image Height - Through Pupil",
        "Angle vs Image Height - Through Field",
        "Cardinal Points Data",
        "Vignetting Diagram",
        "Relative Illumination",
        "Incoherent Irradiance",
        "Radiant Intensity",
        "Y-Ybar",
        "PSF",
        "FFT PSF Cross Section",
        "FFT Line Edge Spread",
        "Huygens PSF",
        "Huygens PSF Cross Section",
        "MTF",
        "Huygens MTF",
        "Geometric MTF",
        "Sampled MTF",
        "Contrast Loss Map",
        "Optical Path Difference",
        "Foucault Analysis",
        "Wavefront",
        "Centroid Sphere Wavefront",
        "Best Fit Sphere Wavefront",
        "Zernike",
        "Image Simulation",
        "Geometric Image Analysis",
        "Geometric Bitmap Image Analysis",
        "Light Source Analysis",
        "Partially Coherent Image Analysis",
        "Extended Diffraction Image Analysis",
        "Jones Pupil",
        "Prescription Report",
        "System Data Report",
        "Classified Data Report"
    };

    public BaseAnalysis Create(string name)
    {
        return name switch
        {
            "Single Ray Trace" => new SingleRayTraceAnalysis(_optic),
            "Non-Sequential Ray Trace" => new NonSequentialRayTraceAnalysis(_optic),
            "Non-Sequential Detector Viewer" => new NonSequentialDetectorViewerAnalysis(_optic),
            "First Order" => new FirstOrderAnalysis(_optic),
            "Seidel Coefficients" => new SeidelCoefficientsAnalysis(_optic),
            "Seidel Diagram" => new SeidelDiagramAnalysis(_optic),
            "Spot Diagram" => new SpotDiagramAnalysis(_optic),
            "Full Field Spot Diagram" => new SpotDiagramVariantAnalysis(_optic, SpotDiagramVariant.FullField),
            "Matrix Spot Diagram" => new SpotDiagramVariantAnalysis(_optic, SpotDiagramVariant.Matrix),
            "Configuration Matrix Spot Diagram" => new SpotDiagramVariantAnalysis(
                _optic,
                SpotDiagramVariant.ConfigurationMatrix),
            "Ray Fan" => new RayFanAnalysis(_optic),
            "Footprint Diagram" => new FootprintDiagramAnalysis(_optic),
            "Field Curvature and Distortion" => new FieldCurvatureAndDistortionAnalysis(_optic),
            "Grid Distortion" => new GridDistortionAnalysis(_optic),
            "Field Curvature" => new FieldCurvatureAnalysis(_optic),
            "Color Focus Shift" => new ColorFocusShiftAnalysis(_optic),
            "Lateral Color" => new LateralColorAnalysis(_optic),
            "Axial Aberration" => new AxialAberrationAnalysis(_optic),
            "Full Field Aberration" => new FullFieldAberrationAnalysis(_optic),
            "Encircled Energy" => new EncircledEnergyAnalysis(_optic),
            "Diffraction Encircled Energy" => new DiffractionEncircledEnergyAnalysis(_optic),
            "Geometric Line Edge Spread" => new GeometricLineEdgeSpreadAnalysis(_optic),
            "Extended Source Encircled Energy" => new ExtendedSourceEncircledEnergyAnalysis(_optic),
            "Pupil Aberration" => new PupilAberrationAnalysis(_optic),
            "RMS vs Field" => new RmsVsFieldAnalysis(_optic),
            "RMS vs Wavelength" => new RmsVsWavelengthAnalysis(_optic),
            "RMS vs Focus" => new RmsVsFocusAnalysis(_optic),
            "RMS Field Map" => new RmsFieldMapAnalysis(_optic),
            "RMS Wavefront vs Field" => new RmsWavefrontVsFieldAnalysis(_optic),
            "Through Focus" => new ThroughFocusAnalysis(_optic),
            "Through Focus MTF" => new ThroughFocusMtfAnalysis(_optic),
            "Fourier Through Focus MTF" => new MtfThroughFocusAnalysis(_optic, MtfComputationMethod.Fourier),
            "Huygens Through Focus MTF" => new MtfThroughFocusAnalysis(_optic, MtfComputationMethod.Huygens),
            "Geometric Through Focus MTF" => new MtfThroughFocusAnalysis(_optic, MtfComputationMethod.Geometric),
            "Fourier MTF vs Field" => new MtfVsFieldAnalysis(_optic, MtfComputationMethod.Fourier),
            "Huygens MTF vs Field" => new MtfVsFieldAnalysis(_optic, MtfComputationMethod.Huygens),
            "Geometric MTF vs Field" => new MtfVsFieldAnalysis(_optic, MtfComputationMethod.Geometric),
            "Angle vs Image Height" => new IncidentAngleVsImageHeightAnalysis(_optic),
            "Angle vs Image Height - Through Pupil" => new IncidentAngleVsHeightAnalysis(_optic, AngleScanMode.ThroughPupil),
            "Angle vs Image Height - Through Field" => new IncidentAngleVsHeightAnalysis(_optic, AngleScanMode.ThroughField),
            "Cardinal Points Data" => new CardinalPointsDataAnalysis(_optic),
            "Vignetting Diagram" => new VignettingDiagramAnalysis(_optic),
            "Relative Illumination" => new RelativeIlluminationAnalysis(_optic),
            "Incoherent Irradiance" => new IncoherentIrradianceAnalysis(_optic),
            "Radiant Intensity" => new RadiantIntensityAnalysis(_optic, numRays: 2048),
            "Y-Ybar" => new YYbarAnalysis(_optic),
            "PSF" => new PsfAnalysis(_optic),
            "FFT PSF Cross Section" => new FftPsfCrossSectionAnalysis(_optic),
            "FFT Line Edge Spread" => new FftLineEdgeSpreadAnalysis(_optic),
            "Huygens PSF" => new HuygensPsfAnalysis(_optic),
            "Huygens PSF Cross Section" => new HuygensPsfCrossSectionAnalysis(_optic),
            "MTF" => new MtfAnalysis(_optic),
            "Huygens MTF" => new HuygensMtfAnalysis(_optic),
            "Geometric MTF" => new GeometricMtfAnalysis(_optic, numRays: 32, numPoints: 128),
            "Sampled MTF" => new SampledMtfAnalysis(_optic, pupilSampling: 32, numPoints: 128),
            "Contrast Loss Map" => new ContrastLossMapAnalysis(_optic),
            "Optical Path Difference" => new OpticalPathDifferenceAnalysis(_optic),
            "Foucault Analysis" => new FoucaultAnalysis(_optic),
            "Wavefront" => new WavefrontAnalysis(_optic),
            "Centroid Sphere Wavefront" => new ReferenceSphereWavefrontAnalysis(
                _optic,
                ReferenceSphereStrategy.CentroidSphere,
                numRings: 8),
            "Best Fit Sphere Wavefront" => new ReferenceSphereWavefrontAnalysis(
                _optic,
                ReferenceSphereStrategy.BestFitSphere,
                numRings: 8),
            "Zernike" => new ZernikeAnalysis(_optic),
            "Image Simulation" => new ImageSimulationAnalysis(_optic),
            "Geometric Image Analysis" => new GeometricImageAnalysis(_optic),
            "Geometric Bitmap Image Analysis" => new GeometricBitmapImageAnalysis(_optic),
            "Light Source Analysis" => new LightSourceAnalysis(_optic),
            "Partially Coherent Image Analysis" => new PartiallyCoherentImageAnalysis(_optic),
            "Extended Diffraction Image Analysis" => new ExtendedDiffractionImageAnalysis(_optic),
            "Jones Pupil" => new JonesPupilAnalysis(_optic),
            "Prescription Report" => new PrescriptionReportAnalysis(_optic),
            "System Data Report" => new SystemDataReportAnalysis(_optic),
            "Classified Data Report" => new ClassifiedDataReportAnalysis(_optic),
            _ => throw new UnknownAnalysisException(name)
        };
    }
}

public sealed class UnknownAnalysisException : ArgumentException
{
    public UnknownAnalysisException(string analysisName)
        : base($"Unknown analysis '{analysisName}'.", nameof(analysisName))
    {
        AnalysisName = analysisName;
    }

    public string AnalysisName { get; }
}
