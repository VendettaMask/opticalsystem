using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record SlantedEdgeSettings(double WavelengthNanometers = 550, int FieldNumber = 1,
    double PixelPitchMicrometers = 3, int Width = 96, int Height = 96,
    double EdgeAngleDegrees = 5, double DarkLevel = .2, double BrightLevel = .8,
    double EdgeOffsetPixels = .2, int PupilSampling = 256, int ImageSamplesPerPixel = 8,
    int PsfRadiusPixels = 32, SfrRegion? Roi = null, int EdgeHalfWindowPixels = 24, bool UseLensWavelengths = false);
public sealed record SlantedEdgeSpectralBand(int WavelengthNumber, double WavelengthNanometers,
    double Weight, double NormalizedWeight, bool IsPrimary);
public sealed record SlantedEdgeFrame(SlantedEdgeSettings Settings, IReadOnlyList<double> Source,
    IReadOnlyList<double> LinearPixels, SlantedEdgeMeasurement Measurement,
    IReadOnlyList<SfrSample> OpticalMtf, IReadOnlyList<SfrSample> PixelIntegratedMtf,
    double PsfSpacingMicrometers, int PsfGridSize, double RelativePsfReadoutIntegral,
    double MaximumSfrReferenceDifference)
{
    public IReadOnlyList<SfrSample>? FullPupilMtf { get; init; }
    public IReadOnlyList<SfrSample>? FullPupilPixelIntegratedMtf { get; init; }
    public double? MaximumSfrFullPupilDifference { get; init; }
    public double? MaximumWindowFullPupilDifference { get; init; }
    public IReadOnlyList<SlantedEdgeSpectralBand>? Spectrum { get; init; }
    public double? ReferenceWavelengthNanometers { get; init; }
}
public sealed record SfrSamplingComparison(string Axis, SlantedEdgeSettings Requested, SlantedEdgeFrame? Refined,
    double? MaximumImageDifference, double? MaximumSfrDifference, double? MaximumMtfDifference,
    double? RelativeReadoutIntegralDifference, bool WithinCriteria, string Message)
{
    public double? MaximumFullPupilMtfDifference { get; init; }
}
public sealed record SlantedEdgeExperiment(SlantedEdgeFrame Baseline, IReadOnlyList<SfrSamplingComparison> Sampling,
    double DifferenceThreshold, bool? SamplingConverged)
{
    public const string Algorithm = "MonochromeLocalFourierEdge/3";
    public const string PolychromeAlgorithm = "PolychromeLocalFourierEdge/1";
}

/// <summary>Incoherent, locally isoplanatic image of an infinite analytic edge at a declared field.
/// Uses the real lens's traced OPD pupil Fourier intensity PSF. Full square pixels are integrated before measurement; no CFA/ADC/ISP.</summary>
public static partial class MonochromeSlantedEdge
{
    public static SlantedEdgeExperiment Run(Optic optic, SlantedEdgeSettings settings,
        bool checkSampling = true, double differenceThreshold = .01)
    {
        if (!double.IsFinite(differenceThreshold) || differenceThreshold is <= 0 or > .1)
            throw new ArgumentOutOfRangeException(nameof(differenceThreshold));
        var baseline = Render(optic, settings);
        var comparisons = new List<SfrSamplingComparison>();
        if (checkSampling)
        {
            var refinements = new[]
            {
                ("Pupil", settings with { PupilSampling = checked(settings.PupilSampling * 2) }),
                ("ImagePlane", settings with { ImageSamplesPerPixel = checked(settings.ImageSamplesPerPixel * 2) }),
                ("PsfWindow", settings with { PsfRadiusPixels = checked(settings.PsfRadiusPixels * 2) })
            };
            foreach (var (axis, requested) in refinements)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                try
                {
                    var refined = Render(optic, requested);
                    var contrast = settings.BrightLevel - settings.DarkLevel;
                    var image = baseline.LinearPixels.Zip(refined.LinearPixels, (a, b) => Math.Abs(a - b) / contrast).Max();
                    var sfr = baseline.Measurement.Curve.Zip(refined.Measurement.Curve, (a, b) => Math.Abs(a.Response - b.Response)).Max();
                    var mtf = baseline.PixelIntegratedMtf.Zip(refined.PixelIntegratedMtf, (a, b) => Math.Abs(a.Response - b.Response)).Max();
                    var fullMtf = baseline.FullPupilPixelIntegratedMtf!.Zip(refined.FullPupilPixelIntegratedMtf!, (a, b) => Math.Abs(a.Response - b.Response)).Max();
                    var integral = Math.Abs(baseline.RelativePsfReadoutIntegral - refined.RelativePsfReadoutIntegral) / refined.RelativePsfReadoutIntegral;
                    var passed = image <= differenceThreshold && sfr <= differenceThreshold && mtf <= differenceThreshold
                        && fullMtf <= differenceThreshold && (axis != "PsfWindow" || integral <= differenceThreshold);
                    comparisons.Add(new(axis, requested, refined, image, sfr, mtf, integral, passed,
                        "One-axis doubled-sampling comparison; thresholds describe numerical differences, not absolute optical accuracy.")
                        { MaximumFullPupilMtfDifference = fullMtf });
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    comparisons.Add(new(axis, requested, null, null, null, null, null, false, exception.Message));
                }
            }
        }
        return new(baseline, comparisons.AsReadOnly(), differenceThreshold,
            checkSampling ? comparisons.All(c => c.Refined is not null && c.WithinCriteria) : null);
    }

    public static SlantedEdgeFrame Render(Optic optic, SlantedEdgeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(optic); ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);
        if (optic.ImageSpaceAfocal) throw new NotSupportedException("This SFR workflow requires a focal image plane with length units.");
        if (settings.FieldNumber > optic.Fields.Count || optic.Wavelengths.Count == 0 || optic.SurfaceGroup.Items.Count < 3)
            throw new ArgumentException("Select an existing field and a valid sequential lens.");
        if (settings.UseLensWavelengths) return RenderSpectrum(optic, settings);
        var grid = 2 * settings.PsfRadiusPixels * settings.ImageSamplesPerPixel + 1;
        var fieldPoint = optic.Fields[settings.FieldNumber - 1];
        var normalized = FieldCoordinates.Normalize(optic.Fields, fieldPoint.X, fieldPoint.Y);
        var wave = new Wavelength { Micrometers = settings.WavelengthNanometers / 1000, Weight = 1, IsPrimary = true };
        var psf = DiffractionEngine.ComputeLocalIncoherentPsf(optic, (normalized.X, normalized.Y), wave,
            settings.PupilSampling, grid, settings.PixelPitchMicrometers / settings.ImageSamplesPerPixel);
        var frame = RenderPsf(psf, settings);
        var norm = Math.Sqrt(1 + frame.Measurement.EdgeSlope * frame.Measurement.EdgeSlope);
        var nx = 1 / norm; var ny = -frame.Measurement.EdgeSlope / norm;
        var full = DiffractionEngine.ComputeLocalPupilMtfAlongDirection(optic, (normalized.X, normalized.Y), wave,
            settings.PupilSampling, frame.Measurement.Curve.Select(p => p.CyclesPerMillimeter).ToArray(), nx, ny);
        var optical = frame.Measurement.Curve.Select((p, i) => p with { Response = full[i] }).ToArray();
        var integrated = frame.Measurement.Curve.Select((p, i) => p with { Response = full[i]
            * SlantedEdgeSfr.Sinc(p.CyclesPerPixel * nx) * SlantedEdgeSfr.Sinc(p.CyclesPerPixel * ny) }).ToArray();
        return frame with { FullPupilMtf = Array.AsReadOnly(optical), FullPupilPixelIntegratedMtf = Array.AsReadOnly(integrated),
            MaximumSfrFullPupilDifference = frame.Measurement.Curve.Zip(integrated, (a, b) => Math.Abs(a.Response - b.Response)).Max(),
            MaximumWindowFullPupilDifference = frame.OpticalMtf.Zip(optical, (a, b) => Math.Abs(a.Response - b.Response)).Max() };
    }

    internal static SlantedEdgeFrame RenderPsf(PsfResult psf, SlantedEdgeSettings settings)
    {
        ValidateSettings(settings);
        if (psf.SampleSpacingUnit != AnalysisAxisUnit.Micrometer || psf.GridSize != 2 * settings.PsfRadiusPixels * settings.ImageSamplesPerPixel + 1
            || Math.Abs(psf.SampleSpacingMicrometers - settings.PixelPitchMicrometers / settings.ImageSamplesPerPixel) > 1e-9)
            throw new ArgumentException("PSF grid and physical image sampling differ.");
        var mass = psf.Values.Cast<double>().Sum();
        if (!(mass > 0) || psf.Values.Cast<double>().Any(v => !double.IsFinite(v) || v < 0)) throw new InvalidOperationException("Lens PSF has no finite positive intensity.");
        var theta = settings.EdgeAngleDegrees * Math.PI / 180;
        var nx = Math.Cos(theta); var ny = -Math.Sin(theta);
        var coverage = new ProjectedEdgeKernel(psf, settings.ImageSamplesPerPixel, nx, ny, mass);
        var contrast = settings.BrightLevel - settings.DarkLevel;
        var pixels = new double[settings.Width * settings.Height];
        var ideal = new double[pixels.Length];
        for (var y = 0; y < settings.Height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < settings.Width; x++)
            {
                var distance = nx * (x + .5 - settings.Width / 2.0 - settings.EdgeOffsetPixels)
                    + ny * (y + .5 - settings.Height / 2.0);
                var value = settings.DarkLevel + contrast * coverage.At(distance);
                if (!double.IsFinite(value) || value < settings.DarkLevel - 1e-9 || value > settings.BrightLevel + 1e-9)
                    throw new InvalidOperationException("Linear image escaped the analytic target's radiometric bounds.");
                pixels[y * settings.Width + x] = value;
                ideal[y * settings.Width + x] = settings.DarkLevel + contrast * SquareCoverage(
                    nx * (x + .5 - settings.Width / 2.0 - settings.EdgeOffsetPixels) + ny * (y + .5 - settings.Height / 2.0), nx, ny, 1);
            }
        }
        var measurement = SlantedEdgeSfr.Measure(pixels, settings.Width, settings.Height,
            settings.Roi ?? new(0, 0, settings.Width, settings.Height), settings.PixelPitchMicrometers, settings.EdgeHalfWindowPixels);
        var fittedNorm = Math.Sqrt(1 + measurement.EdgeSlope * measurement.EdgeSlope);
        var mx = 1 / fittedNorm; var my = -measurement.EdgeSlope / fittedNorm;
        var optical = DiffractionEngine.ComputePsfMtfAlongDirection(psf, measurement.Curve.Select(p => p.CyclesPerMillimeter).ToArray(), mx, my);
        var optics = measurement.Curve.Select((p, i) => p with { Response = optical[i] }).ToArray();
        var integrated = measurement.Curve.Select((p, i) => p with
            { Response = optical[i] * SlantedEdgeSfr.Sinc(p.CyclesPerPixel * mx) * SlantedEdgeSfr.Sinc(p.CyclesPerPixel * my) }).ToArray();
        return new(settings, Array.AsReadOnly(ideal), Array.AsReadOnly(pixels), measurement, optics, integrated,
            psf.SampleSpacingMicrometers, psf.GridSize, mass * psf.SampleSpacingMicrometers * psf.SampleSpacingMicrometers,
            measurement.Curve.Zip(integrated, (a, b) => Math.Abs(a.Response - b.Response)).Max());
    }

    // Exact area of the ideal half-plane inside a physical square pixel, without an sRGB raster.
    internal static double SquareCoverage(double distance, double nx, double ny, double pitch)
    {
        var a = Math.Max(Math.Abs(nx), Math.Abs(ny)) * pitch / 2;
        var b = Math.Min(Math.Abs(nx), Math.Abs(ny)) * pitch / 2;
        if (distance <= -a - b) return 0;
        if (distance >= a + b) return 1;
        if (b < 1e-14) return .5 + distance / (2 * a);
        if (distance < -a + b) return Math.Pow(distance + a + b, 2) / (8 * a * b);
        if (distance <= a - b) return .5 + distance / (2 * a);
        return 1 - Math.Pow(a + b - distance, 2) / (8 * a * b);
    }

    public static void ValidateSettings(SlantedEdgeSettings s)
    {
        if (!double.IsFinite(s.WavelengthNanometers) || s.WavelengthNanometers is < 200 or > 20000 || s.FieldNumber < 1
            || !double.IsFinite(s.PixelPitchMicrometers) || s.PixelPitchMicrometers is < .1 or > 100
            || s.Width is < 32 or > 256 || s.Height is < 32 or > 256
            || !double.IsFinite(s.EdgeAngleDegrees) || Math.Abs(s.EdgeAngleDegrees) is < 2 or > 10
            || !double.IsFinite(s.DarkLevel) || !double.IsFinite(s.BrightLevel) || s.DarkLevel < 0 || s.BrightLevel > 1 || s.BrightLevel - s.DarkLevel < .05
            || !double.IsFinite(s.EdgeOffsetPixels) || Math.Abs(s.EdgeOffsetPixels) > s.Width / 4.0
            || s.PupilSampling is < 4 or > 512 || s.ImageSamplesPerPixel is not (1 or 2 or 4 or 8 or 16) || s.PsfRadiusPixels is < 2 or > 64)
            throw new ArgumentException("Invalid monochrome slanted-edge settings or explicit sampling limit reached.");
        var roi = s.Roi ?? new(0, 0, s.Width, s.Height);
        if (s.EdgeHalfWindowPixels is < 8 or > 48 || roi.X < 0 || roi.Y < 0 || roi.Width < 32 || roi.Height < 32
            || (long)roi.X + roi.Width > s.Width || (long)roi.Y + roi.Height > s.Height)
            throw new ArgumentException("Invalid single ROI or ESF half-window.");
    }
}

