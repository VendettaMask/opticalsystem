namespace OpticalSystemDesign.Core.Analysis;

public sealed class ColorFocusShiftAnalysis : BaseAnalysis
{
    private readonly double _maximumShift;
    private readonly double _pupilZone;
    private readonly int _sampleCount;

    public ColorFocusShiftAnalysis(
        Optic optic,
        double maximumShiftMicrometers = 0,
        double pupilZone = 0,
        int sampleCount = 101) : base(optic)
    {
        // Retain the public parameter name for existing callers. The numeric scale
        // uses the current image-space unit: micrometers for focused systems, D for afocal.
        _maximumShift = Math.Max(0, maximumShiftMicrometers);
        _pupilZone = Math.Clamp(pupilZone, 0, 1);
        _sampleCount = Math.Max(21, sampleCount);
    }

    public override string Name => "Color Focus Shift";

    public override AnalysisData GenerateData()
    {
        var definedWavelengths = Optic.Wavelengths.ToArray();
        if (definedWavelengths.Length == 0)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var primary = definedWavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? definedWavelengths[0];
        var minimumWavelength = definedWavelengths.Min(wavelength => wavelength.Micrometers);
        var maximumWavelength = definedWavelengths.Max(wavelength => wavelength.Micrometers);
        if (maximumWavelength - minimumWavelength <= 1e-12)
        {
            minimumWavelength = Math.Max(0.1, primary.Micrometers * 0.9);
            maximumWavelength = primary.Micrometers * 1.1;
        }

        var afocal = Optic.ImageSpaceAfocal;
        // Convert each afocal focus to vergence before subtracting the primary
        // reference. A collimated wavelength has zero vergence, not a focal pole.
        // Real-ray zones retain the same primary paraxial reference.
        var primaryFocus = afocal
            ? ParaxialVergenceDiopters(primary.Micrometers)
            : ParaxialFocusMicrometers(primary.Micrometers);
        if (afocal && !double.IsFinite(primaryFocus))
        {
            return AnalysisData.Unavailable(Name, "No finite primary paraxial vergence");
        }
        var points = Enumerable.Range(0, _sampleCount)
            .Select(index =>
            {
                var fraction = index / (double)(_sampleCount - 1);
                var wavelength = minimumWavelength
                    + (fraction * (maximumWavelength - minimumWavelength));
                var focus = afocal ? FocusVergenceDiopters(wavelength) : FocusShiftMicrometers(wavelength);
                return new AnalysisPoint(focus - primaryFocus, wavelength);
            })
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToArray();
        if (afocal && points.Length == 0)
        {
            return AnalysisData.Unavailable(Name, "No valid afocal chromatic-focus samples");
        }

        var minimumShift = points.Select(point => point.X).DefaultIfEmpty(0).Min();
        var maximumShift = points.Select(point => point.X).DefaultIfEmpty(0).Max();
        var maximumChange = maximumShift - minimumShift;
        var automaticLimit = NiceAxisLimit(
            points.Select(point => Math.Abs(point.X)).DefaultIfEmpty(1).Max());
        var axisLimit = _maximumShift > 0
            ? _maximumShift
            : automaticLimit;
        var values = afocal
            ? new Dictionary<string, object>
            {
                ["MaximumFocalShiftChangeDiopters"] = maximumChange,
                ["PupilZone"] = _pupilZone,
                ["Reference"] = "PrimaryParaxialVergence",
                ["PrimaryParaxialVergenceDiopters"] = primaryFocus,
                ["MaximumShiftDiopters"] = _maximumShift,
                ["ImageSpaceAfocal"] = true,
                ["FocusShiftUnit"] = "D"
            }
            : new Dictionary<string, object>
            {
                ["MaximumFocalShiftChangeMicrometers"] = maximumChange,
                ["DiffractionLimitChangeMicrometers"] = 4
                    * primary.Micrometers
                    * Math.Pow(DiffractionEngine.WorkingFNumber(Optic, (0, 0), primary, aimAtStop: Optic.RayAimingEnabled), 2),
                ["PupilZone"] = _pupilZone,
                ["Reference"] = "PrimaryParaxialFocus",
                ["PrimaryParaxialFocusMicrometers"] = primaryFocus,
                ["MaximumShiftMicrometers"] = _maximumShift
            };
        values["MinimumWavelengthMicrometers"] = minimumWavelength;
        values["MaximumWavelengthMicrometers"] = maximumWavelength;
        values["SampleCount"] = points.Length;
        var series = new AnalysisSeries(
            afocal ? "焦移：D" : "焦移：µm",
            "波长：µm",
            points,
            Name: "色焦移",
            ColorIndex: 0,
            LineWidth: 1.4,
            XQuantity: AnalysisAxisQuantity.Defocus,
            XUnit: afocal ? AnalysisAxisUnit.Diopter : AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisAxisQuantity.Wavelength,
            YUnit: AnalysisAxisUnit.Micrometer);
        return new AnalysisData(
            Name,
            values,
            series,
            new[] { series },
            new AnalysisPlotOptions(
                XMinimum: -axisLimit,
                XMaximum: axisLimit,
                YMinimum: minimumWavelength,
                YMaximum: maximumWavelength,
                ShowVerticalZeroLine: true,
                VerticalZeroLineWidth: 1,
                ShowLegend: false,
                HideTopAndRightAxes: true));
    }

    private double FocusVergenceDiopters(double wavelengthMicrometers)
    {
        if (_pupilZone <= 1e-12)
        {
            return ParaxialVergenceDiopters(wavelengthMicrometers);
        }

        var positive = TraceZonalVergenceDiopters(_pupilZone, wavelengthMicrometers);
        var negative = TraceZonalVergenceDiopters(-_pupilZone, wavelengthMicrometers);
        var valid = new[] { positive, negative }.Where(double.IsFinite).ToArray();
        return valid.Length == 0 ? double.NaN : valid.Average();
    }

    private double ParaxialVergenceDiopters(double wavelengthMicrometers)
    {
        var trace = Optic.Paraxial.MarginalRay(wavelengthMicrometers);
        if (trace.Heights.Count == 0 || trace.Slopes.Count == 0)
        {
            return double.NaN;
        }

        var height = trace.Heights[^1][0];
        var slope = trace.Slopes[^1][0];
        return Math.Abs(height) <= 1e-15 ? double.NaN : -1000 * slope / height;
    }

    private double TraceZonalVergenceDiopters(double pupilY, double wavelengthMicrometers)
    {
        try
        {
            var sample = Optic.TraceGenericFinalSample(0, 0, 0, pupilY, wavelengthMicrometers, aimAtStop: Optic.RayAimingEnabled);
            if (sample is null || sample.Vignetted || sample.Intensity <= 0
                || Math.Abs(sample.Position.Y) <= 1e-15 || Math.Abs(sample.Direction.Z) <= 1e-15)
            {
                return double.NaN;
            }

            // Signed reciprocal axial intercept in meters. Parallel output is
            // valid and naturally gives zero, including through a color crossing.
            return -1000 * sample.Direction.Y / (sample.Position.Y * sample.Direction.Z);
        }
        catch (InvalidOperationException)
        {
            return double.NaN;
        }
    }

    private double FocusShiftMicrometers(double wavelengthMicrometers)
    {
        if (_pupilZone <= 1e-12)
        {
            return ParaxialFocusMicrometers(wavelengthMicrometers);
        }

        var positive = TraceZonalRay(_pupilZone, wavelengthMicrometers);
        var negative = TraceZonalRay(-_pupilZone, wavelengthMicrometers);
        var valid = new[] { positive, negative }.Where(double.IsFinite).ToArray();
        return valid.Length == 0 ? double.NaN : valid.Average();
    }

    private double ParaxialFocusMicrometers(double wavelengthMicrometers)
    {
        var trace = Optic.Paraxial.MarginalRay(wavelengthMicrometers);
        if (trace.Heights.Count == 0 || trace.Slopes.Count == 0)
        {
            return 0;
        }

        var height = trace.Heights[^1][0];
        var slope = trace.Slopes[^1][0];
        return Math.Abs(slope) <= 1e-15 ? 0 : (-height / slope) * 1000;
    }

    private double TraceZonalRay(double pupilY, double wavelengthMicrometers)
    {
        try
        {
            var sample = Optic.TraceGenericFinalSample(0, 0, 0, pupilY, wavelengthMicrometers, aimAtStop: Optic.RayAimingEnabled);
            if (sample is null || sample.Intensity <= 0 || Math.Abs(sample.Direction.Y) <= 1e-15)
            {
                return double.NaN;
            }

            return (-sample.Position.Y * sample.Direction.Z / sample.Direction.Y) * 1000;
        }
        catch (InvalidOperationException)
        {
            return double.NaN;
        }
    }

    private static double NiceAxisLimit(double maximum)
    {
        if (!double.IsFinite(maximum) || maximum <= 1e-12)
        {
            return 1;
        }

        var target = maximum * 1.12;
        var exponent = Math.Floor(Math.Log10(target));
        var scale = Math.Pow(10, exponent);
        var normalized = target / scale;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * scale;
    }
}
