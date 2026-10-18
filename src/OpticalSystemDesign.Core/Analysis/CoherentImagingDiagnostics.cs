namespace OpticalSystemDesign.Core.Analysis;

public sealed record CoherentKernelWindow(double WavelengthMicrometers, int Size, double RequestedEnergyFraction,
    double SelectionCapturedPowerFraction, double CapturedPowerFraction, int EffectiveCoherenceSampling = 0,
    double MaximumIntensityPixelPitchMillimeters = 0);

public sealed record CoherentImagingDiagnostics(double SpatialCoherence, int FourierGridSize,
    double DetectorPixelPitchMillimeters, double MinimumKernelCapturedFraction, string SourcePhaseModel)
{
    public SpatialCoherenceFunction Function { get; init; }
    public double AlphaMillimeters { get; init; }
    public double FileSizeMillimeters { get; init; }
    public bool NormalizedImagePeak { get; init; }
    public SpatialCoherenceMethod Method { get; init; }
    public double Fraction { get; init; } = .98;
    public int CoherenceSampling { get; init; } = 64;
    public double PsfEnergyFraction { get; init; }
    public IReadOnlyList<CoherentKernelWindow> KernelWindows { get; init; } = Array.Empty<CoherentKernelWindow>();
    public const string Pipeline = "Complex pupil transfer + sampled spatial mutual-intensity propagation";

    internal void AddValues(IDictionary<string, object> values)
    {
        if (Function == SpatialCoherenceFunction.Uniform) values["Coherence"] = SpatialCoherence;
        values["CoherenceFunction"] = Function.ToString();
        values["CoherenceModel"] = Function switch
        {
            SpatialCoherenceFunction.Gaussian => "W_ij = sqrt(I_i I_j) exp(i(phi_i-phi_j)) exp(-|r_i-r_j|^2/alpha^2)",
            SpatialCoherenceFunction.Sinc => "W_ij = sqrt(I_i I_j) exp(i(phi_i-phi_j)) sinc(dx/alpha) sinc(dy/alpha); signed normalized sinc",
            _ => "W_ij = (1-gamma) I_i delta_ij + gamma sqrt(I_i I_j) exp(i(phi_i-phi_j))"
        };
        if (Method != SpatialCoherenceMethod.ExactSourcePairs)
        {
            values["RequestedCoherenceModel"] = values["CoherenceModel"];
            values["CoherenceModel"] = Method == SpatialCoherenceMethod.FourierModes
                ? "W_ij uses the periodic covariance of the retained nonnegative Fourier modes, normalized to Gamma(0)=1"
                : "Ordered finite-window compatibility quadrature of the requested W_ij; reported intensity is the complex sum magnitude";
        }
        if (Function != SpatialCoherenceFunction.Uniform) values["CoherenceAlphaMillimeters"] = AlphaMillimeters;
        values["FileSizeMillimeters"] = FileSizeMillimeters;
        values["CoherentFieldModel"] = "Scalar isoplanatic, one reference pupil per wavelength; no geometric distortion or lateral-color warp";
        values["ObjectSamplingModel"] = Function == SpatialCoherenceFunction.Uniform
            ? "Band-limited sampled object in image-scaled coordinates; wavelengths mutually incoherent"
            : "Finite sampled irradiance with physical image-space coherence length; wavelengths mutually incoherent";
        values["IntensityNormalization"] = NormalizedImagePeak
            ? "Unit maximum pupil amplitude; image peak normalized to one per wavelength before display resampling"
            : "Unit maximum pupil amplitude; no kernel or output renormalization";
        values["IntensityUnits"] = NormalizedImagePeak
            ? "Relative intensity normalized to unit peak per wavelength"
            : "Relative linear intensity, not absolute watts; values above one are retained";
        values["PupilSamplingModel"] = Function == SpatialCoherenceFunction.Uniform
            ? "Cell-centered pupil interpolated onto the image frequency grid"
            : "Direct Fourier-conjugate pupil at the requested detector pitch; shared scalar FFT phase interpolation";
        values["ScalarTransmissionModel"] = Function == SpatialCoherenceFunction.Uniform
            ? "Traced OPD phase and power amplitude (including bulk absorption); no vector interface or thin-film phase model"
            : "Traced aperture and OPD; launch apodization only, scalar transmission excluded as in native polarization-off PCI; no vector interface or thin-film phase model";
        values["CoherenceMethod"] = Method.ToString();
        values["CoherenceFraction"] = Fraction;
        values["CoherenceSampling"] = CoherenceSampling;
        values["CoherenceSamplingSelection"] = Method != SpatialCoherenceMethod.FourierModes
            ? "Not used by the selected coherence method"
            : CoherenceSampling == 0
                ? "Automatic: smallest power of two covering each wavelength's actual complex kernel support; see ComplexKernelWindows"
                : "Explicit fixed coherence Fourier grid";
        values["PsfEnergyFraction"] = PsfEnergyFraction;
        values["ComplexKernelWindows"] = KernelWindows.ToArray();
        values["PsfWindowSelection"] = PsfEnergyFraction > 0
            ? "Smallest square enclosing the requested fraction of full sampled PSF intensity; extracted even support [1-r,r]; selection and retained energy reported separately; no amplitude renormalization"
            : "Explicit finite support; no PSF energy selection";
        values["CoherenceTruncation"] = Function == SpatialCoherenceFunction.Uniform
            ? "Constant discrete mutual coherence"
            : Method switch
            {
                SpatialCoherenceMethod.FiniteCorrelationWindow => "Finite ordered displacement window [-ceil(2.5 alpha/pitch),ceil(2.5 alpha/pitch)); complex magnitude compatibility quadrature; not the exact PSD covariance",
                SpatialCoherenceMethod.FourierModes => "Finite periodic Gamma grid; square window enclosing Fraction of spectral magnitude, positive modes at least 0.001 of spectral peak; unit Gamma(0)",
                _ => "Exact finite source-pair sum; no Fraction correlation truncation; finite complex kernel support retained"
            };
        values["SourcePhaseModel"] = SourcePhaseModel;
        values["FourierGridSize"] = FourierGridSize;
        values["DetectorPixelPitchMillimeters"] = DetectorPixelPitchMillimeters;
        values["MinimumKernelCapturedFraction"] = MinimumKernelCapturedFraction;
        values["FieldGrid"] = "1 x 1 (isoplanatic)";
    }
}
