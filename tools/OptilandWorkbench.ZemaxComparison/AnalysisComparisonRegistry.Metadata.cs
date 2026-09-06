namespace OptilandWorkbench.ZemaxComparison;

public static partial class AnalysisComparisonRegistry
{
    private static AnalysisComparisonEntry Describe(AnalysisComparisonEntry e)
    {
        if (e.ZemaxSettingsMapper == "spot-layout") return e with
        { XAxis = null, YAxis = null, DefaultMetrics = [], RequiresTextExport = false };
        if (e.ZemaxSettingsMapper == "capability-audit")
        {
            var mismatch = e.CanonicalAnalysisKey switch
            {
                "Vignetting Diagram" => "Workbench plots Field Editor X/Y vignette factors; native Vignetting Diagram measures transmitted ray fraction. These are different quantities.",
                "Foucault Analysis" => "Workbench uses a wavefront-gradient shadow approximation with an artificial rim; a native knife-edge diffraction intensity is not the same physical observable.",
                "Partially Coherent Image Analysis" => "Workbench blends source and simulated intensities; its blend coefficient is not native source coherence. A mutual-coherence propagation model is required before equivalence can be tested.",
                _ => null
            };
            return e with
            {
                XAxis = null,
                YAxis = null,
                ValueAxis = null,
                DefaultMetrics = [],
                ResultKind = e.CanonicalAnalysisKey == "Vignetting Diagram" ? ResultKind.Series1D : ResultKind.Image,
                RequiresTextExport = false,
                Support = mismatch is null ? SupportStatus.AdapterNotImplemented : SupportStatus.PhysicalDefinitionMismatch,
                Reason = (mismatch ?? "Native image output capability is inspected, but a common source, spectral, detector and radiometric contract is not yet implemented.")
                    + " Reset settings and native raw results are inspection evidence only; no numerical equivalence claim."
            };
        }
        if (e.ZemaxSettingsMapper != "contract") return e;
        var key = e.CanonicalAnalysisKey;
        var field = new Axis("NormalizedField", "Dimensionless");
        var pupil = new Axis("PupilCoordinate", "Dimensionless");
        var surface = new Axis("SurfaceNumber", "Dimensionless");
        var mm = new Axis("Coordinate", "Millimeter");
        var wave = new Axis("WavefrontError", "Wave");
        var modulation = new Axis("Modulation", "Dimensionless");
        AnalysisComparisonEntry Axes(Axis? x, Axis? y, ResultKind kind = ResultKind.Series1D, Axis? value = null)
            => e with { XAxis = x, YAxis = y, ResultKind = kind, ValueAxis = value };
        var description = key switch
        {
            "Single Ray Trace" => Axes(surface, null) with { Reason = "Eleven per-surface coordinate, direction, normal and optical-path columns; each column publishes its own physical unit." },
            "Seidel Coefficients" => Axes(surface, null) with { Reason = "Four native coefficient tables, with separate millimeter and wave quantities; no single shared ordinate unit." },
            "Seidel Diagram" => Axes(surface, new("Coefficient", "Millimeter")) with { Support = SupportStatus.PartiallyComparable, Reason = "Seven Seidel coefficients from the native auxiliary coefficient report; diagram geometry and rendering are not equated." },
            "Cardinal Points Data" => Axes(new("Coordinate", "Dimensionless"), mm),
            "Prescription Report" => Axes(surface, null) with { Support = SupportStatus.PartiallyComparable, Reason = "Surface curvature, gaps, semi-diameters, conics, finite states and common metadata; excludes material prose and native report-only sections." },
            "System Data Report" => Axes(null, null, ResultKind.Scalar) with { Support = SupportStatus.PartiallyComparable, Reason = "Seven common metadata/first-order scalars only; other System Data sections are retained without an equivalence claim." },
            "Footprint Diagram" => Axes(null, null, ResultKind.Scalar) with { Support = SupportStatus.PartiallyComparable, Reason = "Five native footprint extent/radius scalars; individual footprint ray points are not exposed or matched." },
            "Y-Ybar" => Axes(surface, new("RayHeight", "Millimeter")),
            "Angle vs Image Height" => Axes(new("ImageHeight", "Millimeter"), new("IncidentAngle", "Degree")),
            "Angle vs Image Height - Through Pupil" or "Angle vs Image Height - Through Field" => Axes(new("SampleIndex", "Dimensionless"), null) with { Support = SupportStatus.PartiallyComparable, Reason = "Same ordered inputs compared with native IBatchRayTrace image-local Y and asin(M), including validity. These are not the built-in three-curve IHT analysis." },
            "Grid Distortion" => Axes(new("FieldGridColumn", "Dimensionless"), new("FieldGridRow", "Dimensionless"), ResultKind.Grid2D, new("ImageHeight", "Millimeter")),
            "Full Field Aberration" or "RMS Field Map" => Axes(null, null, ResultKind.Grid2D, wave) with { Reason = "Physical field axes are resolved from the captured model; normalized output records exact field quantity and unit." },
            "Field Curvature" => Axes(field, new("Defocus", "Millimeter")),
            "Field Curvature and Distortion" => Axes(field, null) with { Reason = "Tangential/sagittal curvature in mm and distortion in percent, under one explicit wavelength and field scan." },
            "Color Focus Shift" => Axes(new("Wavelength", "Micrometer"), new("Defocus", "Millimeter")),
            "Lateral Color" => Axes(field, new("ImageHeight", "Micrometer")),
            "Axial Aberration" => Axes(pupil, new("Defocus", "Millimeter")),
            "RMS vs Field" => Axes(field, new("Radius", "Micrometer")),
            "RMS vs Wavelength" => Axes(new("Wavelength", "Micrometer"), new("Radius", "Micrometer")),
            "RMS vs Focus" => Axes(new("Defocus", "Millimeter"), wave),
            "RMS Wavefront vs Field" => Axes(field, wave),
            "Encircled Energy" or "Diffraction Encircled Energy" or "Extended Source Encircled Energy" => Axes(new("Radius", "Micrometer"), new("EnergyFraction", "Dimensionless")),
            "Geometric Line Edge Spread" => Axes(new("ImageHeight", "Micrometer"), new("Irradiance", "Dimensionless")) with { Support = SupportStatus.PartiallyComparable, Reason = "Selected X-line orientation only: native Y-displacement LSF and ERF columns; the orthogonal pair is outside this request." },
            "Relative Illumination" => Axes(field, new("Irradiance", "Dimensionless")),
            "Zernike" => Axes(new("CoefficientIndex", "Dimensionless"), new("WaveCoefficient", "Wave")),
            "Jones Pupil" => Axes(pupil, pupil, ResultKind.Grid2D, new("Coefficient", "Dimensionless")) with { Support = SupportStatus.PartiallyComparable, Reason = "Y-input electric-field magnitudes projected to local image-plane X/Y against native Ex/Ey. The other input state, full complex Jones matrix and phase are not equated." },
            "Contrast Loss Map" => Axes(pupil, pupil, ResultKind.Grid2D) with { Support = SupportStatus.PartiallyComparable, Reason = "Two loss grids plus the native exported original pupil phase, compared through sine/cosine. The documented GUI mean-shifted-ray phase indicator is a separate observable and is excluded. Explicit Frequency=0 means 5% of cutoff." },
            _ when ExtendedAnalysisContracts.IsPsfProfile(key) => Axes(new("ImageHeight", "Micrometer"), new("Irradiance", "Dimensionless")),
            _ when ExtendedAnalysisContracts.IsMtfContract(key) => Axes(key.Contains("vs Field", StringComparison.Ordinal) ? field : key.Contains("Through Focus", StringComparison.Ordinal) ? new("Defocus", "Millimeter") : new("SpatialFrequency", "CyclesPerMillimeter"), modulation),
            _ => throw new InvalidOperationException("Missing extended registry metadata: " + key)
        };
        return description;
    }
}
