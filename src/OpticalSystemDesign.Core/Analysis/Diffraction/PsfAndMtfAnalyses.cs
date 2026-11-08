using System.Numerics;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed class PsfAnalysis : BaseAnalysis
{
    private readonly int _requestedRays;
    private readonly int? _gridSize;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly int _surfaceNumber;
    private readonly double _imageDeltaMicrometers;
    private readonly double _rotationDegrees;
    private readonly string _type;
    private readonly string _displayAs;
    private readonly bool _usePolarization;
    private readonly bool _normalize;
    private readonly bool _zemaxCompatible;
    private readonly bool _ignoreOpd;
    private readonly int? _displaySize;

    public PsfAnalysis(
        Optic optic,
        int numRays = 128,
        int? gridSize = null,
        int wavelengthNumber = -1,
        int fieldNumber = 0,
        int surfaceNumber = -1,
        double imageDeltaMicrometers = 0,
        double rotationDegrees = 0,
        string type = "线性",
        string displayAs = "伪彩色",
        bool usePolarization = false,
        bool normalize = false,
        bool zemaxCompatible = false,
        bool ignoreOpd = false,
        int? displaySize = null) : base(optic)
    {
        _requestedRays = Math.Max(2, numRays);
        _gridSize = gridSize;
        _wavelengthNumber = Math.Max(-1, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
        _surfaceNumber = surfaceNumber;
        if (!double.IsFinite(imageDeltaMicrometers) || (!zemaxCompatible && imageDeltaMicrometers < 0))
            throw new ArgumentOutOfRangeException(nameof(imageDeltaMicrometers));
        _imageDeltaMicrometers = imageDeltaMicrometers;
        _rotationDegrees = rotationDegrees;
        _type = type;
        _displayAs = displayAs;
        _usePolarization = usePolarization;
        _normalize = normalize;
        _zemaxCompatible = zemaxCompatible;
        _ignoreOpd = ignoreOpd;
        if (displaySize is < 2) throw new ArgumentOutOfRangeException(nameof(displaySize));
        _displaySize = displaySize;
    }

    public override string Name => "PSF";

    internal bool SupportsDensePhysicalPupil => !Optic.ImageSpaceAfocal && _zemaxCompatible && !_ignoreOpd
        && !_usePolarization && !_normalize && _imageDeltaMicrometers > 0 && _displaySize.HasValue
        && !_type.Contains("对数", StringComparison.Ordinal) && !_type.Contains("log", StringComparison.OrdinalIgnoreCase);

    internal bool SupportsStreamedPhysicalPupil => SupportsDensePhysicalPupil && _displaySize <= 128
        && !Optic.RayAimingEnabled && (_wavelengthNumber != 0 || Optic.Wavelengths.Count == 1);

    internal bool SupportsPhysicalPsfWindow => SupportsDensePhysicalPupil && _displaySize <= 128;

    public override AnalysisData GenerateData()
    {
        try { return GenerateData(denseDisplaySize: null); }
        catch (InvalidOperationException error) when (error.Data[DiffractionEngine.InsufficientFftPupilNodesKey] is true
            && SupportsPhysicalPsfWindow && _requestedRays is >= 4 and <= DiffractionEngine.MaximumPhysicalPsfWindowSampling
            && BitOperations.IsPow2(_requestedRays) && _displaySize is { } displaySize)
        {
            // A narrow image pitch can leave almost no samples in the stretched
            // pupil. Keep the selected density and evaluate the original image
            // frequencies directly from the full, unstretched pupil.
            var transformSize = Math.Max(_requestedRays, _gridSize ?? 2 * _requestedRays);
            return GenerateData(Math.Min(displaySize, transformSize), physicalPsfWindow: true, interpolateNativePupil: false);
        }
    }

    // The explicit validation modes keep their established baseline, fallback
    // sequence and convergence certificates separate from a fixed finite sum.
    internal AnalysisData GenerateSamplingValidationBaseline() => GenerateData(denseDisplaySize: null);

    internal AnalysisData GenerateDensePhysicalPupil(int? displaySize) => GenerateData(displaySize ?? _displaySize
        ?? throw new InvalidOperationException("Dense pupil refinement requires a fixed output size."));

    internal AnalysisData GenerateStreamedPhysicalPupil(int displaySize, long maximumTraceNodes, bool physicalWindow = false) =>
        GenerateData(displaySize, streamedMaximumTraceNodes: maximumTraceNodes, physicalPsfWindow: physicalWindow);

    internal AnalysisData GeneratePhysicalPsfWindow(int displaySize) =>
        GenerateData(displaySize, physicalPsfWindow: true);

    // Profile and energy contracts consume the historical intermediate grid.
    // Their own calibration is separate from a direct focal-plane image request.
    internal AnalysisData GenerateIntermediateGrid() => GenerateData(denseDisplaySize: null, requireFocalImage: false);

    private AnalysisData GenerateData(int? denseDisplaySize, bool requireFocalImage = true,
        long? streamedMaximumTraceNodes = null, bool physicalPsfWindow = false, bool interpolateNativePupil = true)
    {
        var analysisOptic = ResolveAnalysisOptic();
        if (requireFocalImage && FocalImageDomain.IsCollimatedContactPupil(analysisOptic))
            return FocalImageDomain.NotApplicable(Name, "Focal FFT PSF has no finite diffraction image for a collimated beam at the pupil plane.");
        if (requireFocalImage && FocalImageDomain.IsCollimatedPlaneTrain(analysisOptic))
            return FocalImageDomain.NotApplicable(Name, "Focal FFT PSF requires converging or diverging image rays with a finite diffraction scale.",
                FocalImageDomain.CollimatedPlaneTrain);
        var allWavelengths = analysisOptic.Wavelengths.ToArray();
        if (allWavelengths.Length == 0)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var wavelengths = _wavelengthNumber switch
        {
            < 0 => new[]
            {
                allWavelengths.FirstOrDefault(item => item.IsPrimary) ?? allWavelengths[0]
            },
            0 => allWavelengths,
            _ => new[]
            {
                allWavelengths[Math.Clamp(_wavelengthNumber - 1, 0, allWavelengths.Length - 1)]
            }
        };
        var allFields = SpotAnalysisEngine.DefinedFields(analysisOptic);
        var field = allFields.Count == 0
            ? (Hx: 0.0, Hy: 0.0)
            : _fieldNumber <= 0
                ? allFields[^1]
                : allFields[Math.Clamp(_fieldNumber - 1, 0, allFields.Count - 1)];
        if (requireFocalImage && !_ignoreOpd && !_usePolarization && wavelengths.Length == 1
            && FocalImageDomain.CheckFftImageDomain(Name, analysisOptic, field, wavelengths[0]) is { } outsideDomain)
            return outsideDomain;
        var pupilSampling = _requestedRays;
        var densePhysicalPupil = denseDisplaySize.HasValue;
        var transformSize = physicalPsfWindow ? pupilSampling : streamedMaximumTraceNodes.HasValue ? 2 * pupilSampling
            : densePhysicalPupil ? DiffractionEngine.DensePupilPaddingFactor * pupilSampling
            : Math.Max(pupilSampling, _gridSize ?? (_requestedRays * 2));
        var displaySize = denseDisplaySize ?? Math.Min(transformSize, _displaySize ?? transformSize);
        var croppedReadout = densePhysicalPupil || transformSize > AnalysisResourceLimits.MaximumFftGridSize && _displaySize.HasValue;
        if (croppedReadout && _normalize)
            throw new AnalysisDataUnavailableException(Name, "full-image peak normalization is unavailable for a cropped large Fourier readout");
        // Native scalar FFT display excludes surface/material attenuation.
        // The shared engine retains launch apodization, clipping and polarized
        // transmission; general-purpose callers retain their normal defaults.
        var streamed = streamedMaximumTraceNodes is { } maximumTraceNodes
            ? DiffractionEngine.ComputeStreamedFocalFftPsf(analysisOptic, field, wavelengths.Single(), pupilSampling,
                displaySize, _imageDeltaMicrometers, maximumTraceNodes, physicalWindow: physicalPsfWindow)
            : ((PsfResult Psf, StreamedFourierPupilDiagnostics Diagnostics)?)null;
        var results = streamed is { } window ? new[] { (Wavelength: wavelengths[0], Result: window.Psf) }
            : physicalPsfWindow ? DiffractionEngine.ComputePhysicalPsfWindowSpectrum(analysisOptic, field, wavelengths,
                pupilSampling, displaySize, _imageDeltaMicrometers, interpolateNativePupil) : densePhysicalPupil
            ? DiffractionEngine.ComputeDenseFftPsfSpectrum(analysisOptic, field, wavelengths,
                pupilSampling, transformSize, displaySize, _imageDeltaMicrometers)
            : DiffractionEngine.ComputeFftPsfSpectrum(analysisOptic, field, wavelengths,
            pupilSampling, transformSize, usePolarization: _usePolarization,
            cellCenteredPupil: _zemaxCompatible, zemaxFftSampling: _zemaxCompatible,
            ignoreOpd: _ignoreOpd, aimAtStop: analysisOptic.RayAimingEnabled,
            referenceWavelength: wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0],
            imageDelta: _zemaxCompatible ? _imageDeltaMicrometers : 0,
            ignoreScalarTransmission: _zemaxCompatible,
            interpolateScalarPupilAmplitude: _zemaxCompatible && !_usePolarization,
            croppedOutputSize: croppedReadout ? displaySize : null);
        foreach (var item in results) item.Result.PupilPhaseSampling?.EnforceForCurrentRequest(Name);
        var primary = results.FirstOrDefault(item => item.Wavelength.IsPrimary);
        if (primary.Wavelength is null)
        {
            primary = results[0];
        }

        var afocalImageSpace = analysisOptic.ImageSpaceAfocal;
        var sampleSpacing = _imageDeltaMicrometers > 0
            ? _imageDeltaMicrometers
            : _zemaxCompatible
                ? results.Min(item => item.Result.SampleSpacingMicrometers)
                : primary.Result.SampleSpacingMicrometers;
        var imageUnitLabel = afocalImageSpace
            ? ImageSpaceAnalysisSupport.MilliradianLabel
            : "µm";
        var imageAxisUnit = afocalImageSpace
            ? AnalysisAxisUnit.Milliradian
            : AnalysisAxisUnit.Micrometer;
        var gridSize = croppedReadout ? displaySize : transformSize;
        var displayOffset = (gridSize - displaySize) / 2;
        var values = new double[gridSize, gridSize];
        var useConfiguredWeights = results.Any(item => item.Wavelength.Weight > 0);
        var totalWeight = results.Sum(item =>
            useConfiguredWeights ? item.Wavelength.Weight : 1);
        // Native scalar FFT intensity is relative to the unaberrated PSF with
        // the same apodization and accepted pupil. Use its incoherent spectral
        // peak, without fitting the actual image or tracing an extra ideal pupil.
        // Keep general-purpose and experimental polarized power contracts intact.
        var idealPeak = _zemaxCompatible && !_usePolarization
            ? results.Sum(item => (useConfiguredWeights ? item.Wavelength.Weight : 1)
                * item.Result.UnaberratedPeakRatio) / totalWeight
            : 1;
        if (!double.IsFinite(idealPeak) || idealPeak <= 0)
            throw new InvalidOperationException("FFT PSF has no finite unaberrated peak reference.");

        for (var row = 0; row < gridSize; row++)
        {
            var y = Coordinate(row, gridSize, sampleSpacing);
            for (var column = 0; column < gridSize; column++)
            {
                var x = Coordinate(column, gridSize, sampleSpacing);
                var sum = 0.0;
                foreach (var item in results)
                {
                    var weight = useConfiguredWeights ? item.Wavelength.Weight : 1;
                    sum += weight * BilinearSample(item.Result, x, y) / 100.0;
                }

                values[row, column] = sum / totalWeight / idealPeak;
            }
        }

        var peak = values.Cast<double>().DefaultIfEmpty(0).Max();
        if (_normalize && peak > 0)
        {
            for (var row = 0; row < gridSize; row++)
            {
                for (var column = 0; column < gridSize; column++)
                {
                    values[row, column] /= peak;
                }
            }
        }

        var logarithmic = _type.Contains("对数", StringComparison.Ordinal)
            || _type.Contains("log", StringComparison.OrdinalIgnoreCase);
        var xExtent = displaySize * sampleSpacing;
        var yExtent = displaySize * sampleSpacing;
        var points = new List<AnalysisPoint>(displaySize * displaySize);
        var displayedWindowPeak = 0d;
        for (var row = displayOffset; row < displayOffset + displaySize; row++)
        {
            var y = Coordinate(row, gridSize, sampleSpacing);
            for (var column = displayOffset; column < displayOffset + displaySize; column++)
            {
                var value = values[row, column];
                displayedWindowPeak = Math.Max(displayedWindowPeak, value);
                points.Add(new AnalysisPoint(
                    Coordinate(column, gridSize, sampleSpacing),
                    y,
                    Value: logarithmic ? 10 * Math.Log10(Math.Max(1e-12, value)) : value));
            }
        }

        var series = new AnalysisSeries(
            $"X ({imageUnitLabel})",
            $"Y ({imageUnitLabel})",
            points,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: logarithmic ? "Relative Intensity (dB)" : "Relative Intensity",
            XQuantity: afocalImageSpace ? AnalysisAxisQuantity.IncidentAngle : AnalysisAxisQuantity.ImageHeight,
            XUnit: imageAxisUnit,
            YQuantity: afocalImageSpace ? AnalysisAxisQuantity.IncidentAngle : AnalysisAxisQuantity.ImageHeight,
            YUnit: imageAxisUnit,
            ValueQuantity: AnalysisAxisQuantity.Irradiance,
            ValueUnit: logarithmic ? AnalysisAxisUnit.Decibel : AnalysisAxisUnit.Dimensionless);
        var centerIndex = _zemaxCompatible ? (gridSize - 1) / 2 : gridSize / 2;
        var centerValue = values[centerIndex, centerIndex];
        var title = _usePolarization
            ? wavelengths.Length > 1
                ? "复色光 Polarization-weighted scalar FFT PSF（Experimental）"
                : "Polarization-weighted scalar FFT PSF（Experimental）"
            : wavelengths.Length > 1 ? "复色光FFT PSF" : "FFT PSF";
        var resultValues = new Dictionary<string, object>
        {
            ["Method"] = "FFT",
            ["PupilSampling"] = pupilSampling,
            ["GridSize"] = transformSize,
            ["CroppedFourierReadout"] = croppedReadout,
            ["DisplaySize"] = displaySize,
            ["RequestedDisplaySize"] = _displaySize ?? gridSize,
            ["RequestedImageDelta"] = _imageDeltaMicrometers,
            ["PupilGridStretch"] = results.Select(item => item.Result.PupilGridStretch).ToArray(),
            ["PupilGridStretchX"] = results.Select(item => item.Result.PupilGridStretchX).ToArray(),
            ["PupilGridStretchY"] = results.Select(item => item.Result.PupilGridStretchY).ToArray(),
            ["UseRayAiming"] = results.Select(item => item.Result.UseRayAiming).ToArray(),
            ["StopAimingFallbackUsed"] = results.Any(item => item.Result.StopAimingFallbackUsed),
            ["ImageDeltaMicrometers"] = afocalImageSpace ? 0 : sampleSpacing,
            ["ImageDeltaMilliradians"] = afocalImageSpace ? sampleSpacing : 0,
            ["ImageSpaceAfocal"] = afocalImageSpace,
            ["ImageCoordinateUnit"] = imageUnitLabel,
            ["WorkingFNumber"] = results.Average(item => item.Result.WorkingFNumber),
            ["StrehlRatio"] = centerValue,
            ["PeakStrehlRatio"] = croppedReadout ? (object)"Not evaluated outside the display window"
                : values.Cast<double>().DefaultIfEmpty(0).Max(),
            ["DisplayedWindowPeakRatio"] = displayedWindowPeak,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthRange"] = $"{wavelengths.Min(item => item.Micrometers):0.0000}–{wavelengths.Max(item => item.Micrometers):0.0000} µm",
            ["FieldNumber"] = _fieldNumber,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["SurfaceNumber"] = _surfaceNumber,
            ["RotationDegrees"] = _rotationDegrees,
            ["Type"] = _type,
            ["DisplayAs"] = _displayAs,
            ["UsePolarization"] = _usePolarization,
            ["PolarizationModel"] = _usePolarization
                ? "Polarization-weighted scalar approximation"
                : "Scalar",
            ["PolarizationLimit"] = _usePolarization
                ? "Jones component powers are collapsed before scalar diffraction; complex cross-polarization interference and longitudinal high-NA fields are not modeled."
                : string.Empty,
            ["Normalized"] = _normalize,
            ["UnaberratedPeakRatio"] = idealPeak
        };
        if (streamed is { } resolvedWindow) resultValues["StreamedFourierPupil"] = resolvedWindow.Diagnostics;
        if (physicalPsfWindow) resultValues["PhysicalFourierWindow"] = true;
        return new AnalysisData(Name, resultValues, series, new[] { series }, new AnalysisPlotOptions(
            Title: title,
            EqualAspect: true,
            XMinimum: -xExtent / 2,
            XMaximum: xExtent / 2,
            YMinimum: -yExtent / 2,
            YMaximum: yExtent / 2));
    }

    private Optic ResolveAnalysisOptic()
    {
        return AnalysisSurfacePreparation.Prepare(Optic, _surfaceNumber, zeroMeansImage: true);
    }

    private double Coordinate(int index, int size, double spacing)
    {
        return (index - (_zemaxCompatible ? (size - 1) / 2 : size / 2)) * spacing;
    }

    internal static double BilinearSample(PsfResult source, double x, double y)
    {
        var center = source.CroppedImageCenterIndex ?? source.GridSize / 2;
        var column = (x / source.SampleSpacingMicrometers) + center;
        var row = (y / source.SampleSpacingMicrometers) + center;
        var maximumIndex = source.CroppedImageCenterIndex.HasValue ? source.GridSize - 1 : source.GridSize;
        // A separately computed physical pitch can round the Nyquist endpoint
        // just outside the grid. Retain that endpoint without wrapping exterior data.
        var roundoff = 1e-12 * source.GridSize;
        if (column < -roundoff || row < -roundoff
            || column > maximumIndex + roundoff || row > maximumIndex + roundoff)
        {
            return 0;
        }
        column = Math.Clamp(column, 0, maximumIndex);
        row = Math.Clamp(row, 0, maximumIndex);

        // The positive Nyquist boundary is the same Fourier sample as the
        // negative boundary. Preserve it when presenting Zemax's even grid.
        var floorColumn = (int)Math.Floor(column);
        var floorRow = (int)Math.Floor(row);
        var tx = column - floorColumn;
        var ty = row - floorRow;
        var left = floorColumn % source.GridSize;
        var top = floorRow % source.GridSize;
        var right = source.CroppedImageCenterIndex.HasValue ? Math.Min(left + 1, source.GridSize - 1) : (left + 1) % source.GridSize;
        var bottom = source.CroppedImageCenterIndex.HasValue ? Math.Min(top + 1, source.GridSize - 1) : (top + 1) % source.GridSize;
        var topValue = source.Values[top, left] * (1 - tx)
            + source.Values[top, right] * tx;
        var bottomValue = source.Values[bottom, left] * (1 - tx)
            + source.Values[bottom, right] * tx;
        return topValue * (1 - ty) + bottomValue * ty;
    }
}

public sealed class MtfAnalysis : BaseAnalysis
{
    private readonly int _requestedRays;
    private readonly int? _gridSize;
    private readonly double? _maximumFrequency;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly int _surfaceNumber;
    private readonly FftMtfDataType _dataType;
    private readonly bool _showDiffractionLimit;
    private readonly bool _usePolarization;
    private readonly bool _useDashes;
    private readonly bool _zemaxCompatible;

    public MtfAnalysis(
        Optic optic,
        int numRays = 128,
        int? gridSize = null,
        double? maximumFrequency = null,
        int wavelengthNumber = -1,
        int fieldNumber = 0,
        int surfaceNumber = 0,
        string type = "Modulation",
        bool showDiffractionLimit = false,
        bool usePolarization = false,
        bool useDashes = false,
        bool zemaxCompatible = false) : base(optic)
    {
        _requestedRays = Math.Max(2, numRays);
        _gridSize = gridSize;
        _maximumFrequency = maximumFrequency is > 0 && double.IsFinite(maximumFrequency.Value)
            ? maximumFrequency
            : null;
        _wavelengthNumber = wavelengthNumber;
        _fieldNumber = Math.Max(0, fieldNumber);
        _surfaceNumber = Math.Max(0, surfaceNumber);
        _dataType = MtfDataTypeSupport.Parse(type);
        _showDiffractionLimit = showDiffractionLimit;
        _usePolarization = usePolarization;
        _useDashes = useDashes;
        _zemaxCompatible = zemaxCompatible;
    }

    public override string Name => "MTF";

    internal bool SupportsStreamedPhysicalPupil => _zemaxCompatible && _maximumFrequency.HasValue
        && _dataType == FftMtfDataType.Modulation && !_usePolarization
        && !Optic.ImageSpaceAfocal && !Optic.RayAimingEnabled
        && (_wavelengthNumber != 0 || Optic.Wavelengths.Count == 1)
        && (_fieldNumber > 0 || SpotAnalysisEngine.DefinedFields(Optic).Count == 1);

    public override AnalysisData GenerateData() => GenerateData(null);

    internal AnalysisData GenerateStreamedPhysicalPupil(long maximumTraceNodes) => GenerateData(maximumTraceNodes);

    private AnalysisData GenerateData(long? streamedMaximumTraceNodes)
    {
        var analysisOptic = ResolveAnalysisOptic();
        if (FocalImageDomain.IsCollimatedContactPupil(analysisOptic))
            return FocalImageDomain.NotApplicable(Name, "Focal FFT MTF has no image-space frequency scale for a collimated beam at the pupil plane.");
        if (FocalImageDomain.IsCollimatedPlaneTrain(analysisOptic))
            return FocalImageDomain.NotApplicable(Name, "Focal FFT MTF has no finite image-plane frequency scale for parallel image rays.",
                FocalImageDomain.CollimatedPlaneTrain);
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(analysisOptic, _wavelengthNumber);
        if (wavelengths.Count == 0)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var pupilSampling = _gridSize.HasValue
            ? _requestedRays
            : (int)Math.Floor(32 * Math.Pow(2, (Math.Log2(_requestedRays) - 5) / 2));
        var gridSize = _gridSize ?? (_requestedRays * 2);
        var allFields = SpotAnalysisEngine.DefinedFields(analysisOptic);
        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1)) };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        if (!_usePolarization && fields.Length == 1 && wavelengths.Count == 1
            && FocalImageDomain.CheckFftImageDomain(Name, analysisOptic, fields[0], wavelengths[0]) is { } outsideDomain)
            return outsideDomain;
        var series = new List<AnalysisSeries>();
        StreamedFourierPupilDiagnostics? streamedDiagnostics = null;
        var cutoff = 0.0;
        IReadOnlyList<AnalysisPoint>? diffractionLimit = null;
        var yAxisLabel = MtfDataTypeSupport.Label(_dataType, "Modulation");
        var frequencyLabel = ImageSpaceAnalysisSupport.SpatialFrequencyLabel(analysisOptic);
        var frequencyUnit = ImageSpaceAnalysisSupport.SpatialFrequencyUnit(analysisOptic);
        var frequencyUnitLabel = ImageSpaceAnalysisSupport.SpatialFrequencyUnitLabel(analysisOptic);
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var wavelengthResults = wavelengths.Select(wavelength =>
            {
                if (streamedMaximumTraceNodes.HasValue)
                {
                    var streamed = DiffractionEngine.ComputeStreamedFocalFftMtf(analysisOptic, field, wavelength,
                        pupilSampling, _maximumFrequency!.Value, streamedMaximumTraceNodes.Value);
                    streamed.Phase.EnforceForCurrentRequest(Name);
                    streamedDiagnostics = streamed.Diagnostics;
                    return (wavelength, streamed.Mtf);
                }
                if (gridSize > AnalysisResourceLimits.MaximumFftGridSize)
                    return (wavelength, DiffractionEngine.ComputeFftMtfFromPupil(
                        analysisOptic, field, wavelength, pupilSampling, gridSize, _usePolarization,
                        cellCenteredPupil: _zemaxCompatible, zemaxFftSampling: _zemaxCompatible,
                        referenceWavelength: wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0],
                        requireResolvedPupilPhase: FftPupilSampling.IsRequired));
                var psf = DiffractionEngine.ComputeFftPsf(
                    analysisOptic,
                    field,
                    wavelength,
                    pupilSampling,
                    gridSize,
                    _usePolarization,
                    cellCenteredPupil: _zemaxCompatible,
                    zemaxFftSampling: _zemaxCompatible,
                    imageDelta: _zemaxCompatible ? -1 : 0,
                    referenceWavelength: wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0]);
                psf.PupilPhaseSampling?.EnforceForCurrentRequest(Name);
                if (_zemaxCompatible) psf = psf with { FrequencySampleCount = pupilSampling - 1 };
                return (wavelength, DiffractionEngine.ComputeFftMtf(psf, analysisOptic, wavelength));
            }).ToArray();
            var fullMtf = MtfMethodEvaluator.CombinePolychromatic(wavelengthResults);
            cutoff = cutoff <= 0
                ? fullMtf.CutoffFrequency
                : Math.Min(cutoff, fullMtf.CutoffFrequency);
            var fieldPlottedMaximum = _maximumFrequency ?? fullMtf.CutoffFrequency;
            var mtf = _zemaxCompatible
                ? ResampleSpectrum(wavelengthResults, fieldPlottedMaximum, _dataType, 300)
                : DiffractionEngine.LimitFrequency(fullMtf, _maximumFrequency);
            if (_showDiffractionLimit && diffractionLimit is null)
            {
                diffractionLimit = mtf.Frequency.Select(frequency => new AnalysisPoint(
                    frequency,
                    DiffractionLimit(wavelengthResults, frequency))).ToArray();
            }

            series.Add(new AnalysisSeries(
                frequencyLabel,
                yAxisLabel,
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Tangential[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Tangential"),
                ColorIndex: _useDashes ? 0 : fieldIndices[fieldIndex],
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: frequencyUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: _dataType == FftMtfDataType.Phase ? AnalysisAxisUnit.Radian : AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                frequencyLabel,
                yAxisLabel,
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Sagittal[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: _useDashes ? 0 : fieldIndices[fieldIndex],
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: frequencyUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: _dataType == FftMtfDataType.Phase ? AnalysisAxisUnit.Radian : AnalysisAxisUnit.Dimensionless));
        }

        if (diffractionLimit is not null)
        {
            series.Add(new AnalysisSeries(
                frequencyLabel,
                "Modulation",
                diffractionLimit,
                Name: "Diffraction Limit",
                ColorIndex: fields.Length,
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: frequencyUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        var plottedMaximum = _zemaxCompatible
            ? _maximumFrequency ?? cutoff
            : _maximumFrequency.HasValue
                ? Math.Min(_maximumFrequency.Value, cutoff)
                : cutoff;
        var values = new Dictionary<string, object>
        {
            ["Method"] = "FFT",
            ["PupilSampling"] = pupilSampling,
            ["GridSize"] = gridSize,
            ["MaximumFrequency"] = plottedMaximum,
            ["CutoffFrequency"] = cutoff,
            ["FrequencyUnit"] = frequencyUnitLabel,
            ["ImageSpaceAfocal"] = analysisOptic.ImageSpaceAfocal,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthsMicrometers"] = wavelengths.Select(item => item.Micrometers).ToArray(),
            ["FieldNumber"] = _fieldNumber,
            ["FieldCount"] = fields.Length,
            ["SurfaceNumber"] = _surfaceNumber,
            ["Type"] = MtfDataTypeSupport.Name(_dataType),
            ["ShowDiffractionLimit"] = _showDiffractionLimit,
            ["UsePolarization"] = _usePolarization,
            ["UseDashes"] = _useDashes,
            ["ZemaxCompatible"] = _zemaxCompatible,
            ["PlotPointCount"] = series.FirstOrDefault()?.Points.Count ?? 0
        };
        if (streamedDiagnostics is not null) values["StreamedFourierPupil"] = streamedDiagnostics;
        var (yMinimum, yMaximum) = _dataType switch
        {
            FftMtfDataType.Real or FftMtfDataType.Imaginary => (-1.0, 1.0),
            FftMtfDataType.Phase => (-Math.PI, Math.PI),
            _ => (0.0, _zemaxCompatible ? 1.05 : 1.0)
        };
        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: 0,
            XMaximum: plottedMaximum,
            YMinimum: yMinimum,
            YMaximum: yMaximum,
            ShowLegend: true,
            GridOpacity: 0.25));
    }

    private Optic ResolveAnalysisOptic()
    {
        return AnalysisSurfacePreparation.Prepare(Optic, _surfaceNumber, zeroMeansImage: true);
    }

    private static MtfResult ResampleSpectrum(
        IReadOnlyList<(Wavelength Wavelength, MtfResult Result)> results,
        double maximumFrequency, FftMtfDataType type, int pointCount)
    {
        // Preserve the existing square-wave harmonic contract. For ordinary FFT
        // responses, interpolate each complex OTF once on its own physical axis,
        // then combine on the requested display grid. Combining on one coarse
        // wavelength grid first loses curvature before the final spline.
        if (type == FftMtfDataType.SquareWave)
            return Resample(MtfMethodEvaluator.CombinePolychromatic(results), maximumFrequency, type, pointCount);
        var totalWeight = results.Sum(item => item.Wavelength.Weight);
        var equalWeights = totalWeight <= 1e-30;
        var active = results.Where(item => equalWeights || item.Wavelength.Weight > 0).ToArray();
        var components = active.Select(item =>
        {
            if (item.Result.TangentialOtf is null || item.Result.SagittalOtf is null)
                throw new ArgumentException("FFT MTF spectrum requires complex OTF data.", nameof(results));
            return (Weight: equalWeights ? 1 : item.Wavelength.Weight,
                Real: Resample(item.Result, maximumFrequency, FftMtfDataType.Real, pointCount),
                Imaginary: Resample(item.Result, maximumFrequency, FftMtfDataType.Imaginary, pointCount));
        }).ToArray();
        if (components.Length == 0)
            return new MtfResult(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), 0);
        if (equalWeights) totalWeight = components.Length;
        var frequency = components[0].Real.Frequency;
        return new MtfResult(frequency, Direction(true), Direction(false), active.Max(item => item.Result.CutoffFrequency));

        double[] Direction(bool tangential) => Enumerable.Range(0, frequency.Count).Select(index =>
        {
            var real = components.Sum(item => item.Weight * (tangential
                ? item.Real.Tangential[index] : item.Real.Sagittal[index])) / totalWeight;
            var imaginary = components.Sum(item => item.Weight * (tangential
                ? item.Imaginary.Tangential[index] : item.Imaginary.Sagittal[index])) / totalWeight;
            return type switch
            {
                FftMtfDataType.Real => real,
                FftMtfDataType.Imaginary => imaginary,
                FftMtfDataType.Phase => Math.Atan2(imaginary, real),
                _ => Math.Clamp(System.Numerics.Complex.Abs(new(real, imaginary)), 0, 1)
            };
        }).ToArray();
    }

    private static MtfResult Resample(
        MtfResult source,
        double maximumFrequency,
        FftMtfDataType type,
        int pointCount)
    {
        pointCount = Math.Max(2, pointCount);
        maximumFrequency = Math.Max(0, maximumFrequency);
        var frequency = Enumerable.Range(0, pointCount)
            .Select(index => maximumFrequency * index / (pointCount - 1.0))
            .ToArray();
        if (type != FftMtfDataType.SquareWave && source.TangentialOtf is { } tOtf && source.SagittalOtf is { } sOtf)
        {
            // Interpolate the complex OTF before deriving modulation/phase.
            // Interpolating magnitudes loses sign reversals at MTF zeros.
            var tAxis = source.TangentialFrequency ?? source.Frequency;
            var sAxis = source.SagittalFrequency ?? source.Frequency;
            return new MtfResult(frequency, Direction(tAxis, tOtf), Direction(sAxis, sOtf), source.CutoffFrequency);

            double[] Direction(IReadOnlyList<double> axis, IReadOnlyList<System.Numerics.Complex> otf)
            {
                var real = MtfThroughFocusAnalysis.CubicSplineInterpolate(axis, otf.Select(value => value.Real).ToArray(), frequency);
                var imaginary = MtfThroughFocusAnalysis.CubicSplineInterpolate(axis, otf.Select(value => value.Imaginary).ToArray(), frequency);
                return frequency.Select((value, index) => value > axis[^1] ? 0 : type switch
                {
                    FftMtfDataType.Real => real[index],
                    FftMtfDataType.Imaginary => imaginary[index],
                    FftMtfDataType.Phase => Math.Atan2(imaginary[index], real[index]),
                    _ => Math.Clamp(System.Numerics.Complex.Abs(new(real[index], imaginary[index])), 0, 1)
                }).ToArray();
            }
        }
        var tangential = frequency.Select(value => MtfMethodEvaluator.Sample(source, value, type, tangential: true)).ToArray();
        var sagittal = frequency.Select(value => MtfMethodEvaluator.Sample(source, value, type, tangential: false)).ToArray();
        return new MtfResult(frequency, tangential, sagittal, source.CutoffFrequency);
    }

    private static double DiffractionLimit(
        IReadOnlyList<(Wavelength Wavelength, MtfResult Result)> results,
        double frequency)
    {
        var totalWeight = results.Sum(item => item.Wavelength.Weight);
        var equalWeights = totalWeight <= 1e-30;
        if (equalWeights)
        {
            totalWeight = Math.Max(1, results.Count);
        }

        var value = 0.0;
        foreach (var item in results)
        {
            var normalized = item.Result.CutoffFrequency <= 1e-30
                ? 1
                : frequency / item.Result.CutoffFrequency;
            var monochromatic = normalized >= 1
                ? 0
                : 2 * (Math.Acos(normalized)
                    - (normalized * Math.Sqrt(Math.Max(0, 1 - (normalized * normalized))))) / Math.PI;
            value += monochromatic * (equalWeights ? 1 : item.Wavelength.Weight);
        }

        return value / totalWeight;
    }

}
