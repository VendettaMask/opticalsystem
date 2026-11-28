using System.Globalization;

namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>CIE's original 1 nm standard observer. XYZ is physical spectral power
/// weighted by the observer; the sRGB conversion is a display transform only.</summary>
public static class NonSequentialColorimetry
{
    public const string SpectrumFitProtocol = "CIE1931-2deg/1nm; XYZ projected nonnegative least squares; zero initial weights; uniform wavelength bands; unit radiometric power/2";
    public const string UniformSpectrumProtocol = "Uniform wavelength nodes; equal discrete radiometric powers/1";
    public const string BlackBodySpectrumProtocol = "Planck wavelength radiance; SI constants; uniform wavelength nodes; unit discrete radiometric power/1";
    private static readonly (double X, double Y, double Z)[] Observer = Load();
    private static (double, double, double)[] Load()
    {
        using var stream = typeof(NonSequentialColorimetry).Assembly.GetManifestResourceStream("OpticalSystemDesign.Core.Colorimetry.CIE1931.csv")
            ?? throw new InvalidDataException("Missing CIE 1931 observer.");
        using var reader = new StreamReader(stream);
        var values = new List<(double, double, double)>(); string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var p = line.Split(','); if (p.Length != 4) throw new InvalidDataException("Invalid CIE observer row.");
            if (double.Parse(p[0], CultureInfo.InvariantCulture) != 360 + values.Count) throw new InvalidDataException("Invalid CIE wavelength axis.");
            values.Add((double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture)));
        }
        if (values.Count != 471) throw new InvalidDataException("Incomplete CIE observer.");
        return values.ToArray();
    }

    public static (double X, double Y, double Z) Response(double nanometers)
    {
        if (!double.IsFinite(nanometers)) throw new ArgumentOutOfRangeException(nameof(nanometers));
        if (nanometers is < 360 or > 830) return (0, 0, 0);
        var index = Math.Min(469, (int)Math.Floor(nanometers) - 360); var t = nanometers - 360 - index;
        var a = Observer[index]; var b = Observer[index + 1];
        return (a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), a.Z + t * (b.Z - a.Z));
    }

    // The XYZ input does not uniquely determine an SPD. This declared native
    // fit uses projected least squares from zero initial weights. It is
    // reproducible and preserves the requested color, not a guessed RGB curve.
    public static IReadOnlyList<NonSequentialSpectralSample> FitSpectrum(double x, double y, double z, double fromNm, double toNm, int count)
    {
        if (count is < 3 or > 4096 || fromNm < 380 || toNm > 830 || fromNm >= toNm
            || new[] { x, y, z, fromNm, toNm }.Any(v => !double.IsFinite(v)) || Math.Min(x, Math.Min(y, z)) < 0 || x + y + z <= 0)
            throw new InvalidDataException("Invalid tristimulus spectrum definition.");
        var bands = Enumerable.Range(0, count).Select(i => fromNm + (toNm - fromNm) * i / (count - 1)).ToArray();
        var a = bands.Select(Response).ToArray(); var b = new[] { x, y, z }; var sum = x + y + z;
        for (var i = 0; i < 3; i++) b[i] /= sum;
        var weight = new double[count];
        var step = 1 / Math.Max(1e-20, a.Sum(v => v.X * v.X + v.Y * v.Y + v.Z * v.Z));
        for (var iteration = 0; iteration < 50_000; iteration++)
        {
            var rx = -b[0]; var ry = -b[1]; var rz = -b[2];
            for (var i = 0; i < count; i++) { rx += a[i].X * weight[i]; ry += a[i].Y * weight[i]; rz += a[i].Z * weight[i]; }
            if (rx * rx + ry * ry + rz * rz < 1e-24) break;
            for (var i = 0; i < count; i++) weight[i] = Math.Max(0, weight[i] - step * (a[i].X * rx + a[i].Y * ry + a[i].Z * rz));
        }
        var error = new[] { -b[0], -b[1], -b[2] };
        for (var i = 0; i < count; i++) { error[0] += a[i].X * weight[i]; error[1] += a[i].Y * weight[i]; error[2] += a[i].Z * weight[i]; }
        var total = weight.Sum();
        if (!double.IsFinite(total) || total <= 0 || error.Sum(v => v * v) > 1e-12)
            throw new InvalidDataException("The requested color has no converged nonnegative spectrum in this band.");
        return bands.Select((wave, i) => new NonSequentialSpectralSample(wave, weight[i] / total)).ToArray();
    }

    public static (double R, double G, double B) ToLinearSrgb(double x, double y, double z) =>
        (3.2406255 * x - 1.537208 * y - .4986286 * z,
            -.9689307 * x + 1.8757561 * y + .0415175 * z, .0557101 * x - .2040211 * y + 1.0569959 * z);

    public static IReadOnlyList<NonSequentialSpectralSample> SampleSpectrum(double fromNm, double toNm, int count, double? blackBodyKelvin = null)
    {
        if (!double.IsFinite(fromNm) || !double.IsFinite(toNm) || fromNm < 200 || toNm > 20_000 || fromNm >= toNm || count is < 2 or > 4096
            || blackBodyKelvin is { } t && (!double.IsFinite(t) || t <= 0)) throw new InvalidDataException("Invalid discrete source spectrum.");
        var bands = Enumerable.Range(0, count).Select(i => fromNm + (toNm - fromNm) * i / (count - 1)).ToArray();
        var logs = bands.Select(wave =>
        {
            if (blackBodyKelvin is null) return 0d;
            // hc/k in nm K, using the exact SI definitions of h, c and k.
            var x = (6.62607015e-34 * 299792458 / 1.380649e-23 * 1e9) / wave / blackBodyKelvin.Value;
            if (!double.IsFinite(x) || x <= 0) throw new InvalidDataException("Invalid blackbody temperature or wavelength.");
            var logDenominator = x > 50 ? x : Math.Log(double.ExpM1(x));
            return -5 * Math.Log(wave) - logDenominator;
        }).ToArray();
        var peak = logs.Max(); var weights = logs.Select(v => Math.Exp(v - peak)).ToArray(); var total = weights.Sum();
        return bands.Select((wave, i) => new NonSequentialSpectralSample(wave, weights[i] / total)).ToArray();
    }

    public static double PhotopicLumensPerWatt(IReadOnlyList<NonSequentialSpectralSample> spectrum)
    {
        if (spectrum.Count is < 1 or > 4096 || spectrum.Any(s => !double.IsFinite(s.Nanometers) || s.Nanometers <= 0
            || !double.IsFinite(s.RelativePower) || s.RelativePower < 0)) throw new InvalidDataException("Invalid photometric source spectrum.");
        var maximum = spectrum.Max(s => s.RelativePower);
        if (maximum <= 0) throw new InvalidDataException("Source spectrum has no power.");
        var total = spectrum.Sum(s => s.RelativePower / maximum);
        var y = spectrum.Sum(s => s.RelativePower / maximum * Response(s.Nanometers).Y) / total;
        if (y <= 0) throw new InvalidDataException("A lumen source needs nonzero photopic spectral response.");
        return 683 * y;
    }

    public static (double R, double G, double B) ToSrgb(double x, double y, double z, double exposure = 1)
    {
        double Encode(double v) { v = Math.Max(0, v * exposure); return Math.Clamp(v <= .0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - .055, 0, 1); }
        var linear = ToLinearSrgb(x, y, z);
        return (Encode(linear.R), Encode(linear.G), Encode(linear.B));
    }

    public static IReadOnlyList<double> DetectorRgb(NonSequentialDetectorFrame frame, NonSequentialDocument document, int wavelengthNumber = 0)
    {
        var xyz = new double[frame.PixelsX * frame.PixelsY * 3];
        foreach (var band in frame.PowerByWavelength)
        {
            if (wavelengthNumber != 0 && wavelengthNumber != band.Key) continue;
            var response = Response(document.Wavelengths[band.Key - 1].Nanometers);
            for (var i = 0; i < band.Value.Count; i++) { xyz[3 * i] += band.Value[i] * response.X; xyz[3 * i + 1] += band.Value[i] * response.Y; xyz[3 * i + 2] += band.Value[i] * response.Z; }
        }
        return TristimulusRgb(xyz);
    }

    public static IReadOnlyList<double> TristimulusRgb(IReadOnlyList<double> xyz)
    {
        if (xyz.Count % 3 != 0 || xyz.Any(v => !double.IsFinite(v) || v < 0))
            throw new ArgumentException("XYZ data must contain finite, nonnegative triplets.", nameof(xyz));
        // One shared linear RGB exposure avoids clipping the brighter color
        // channels before the sRGB transfer function. Numerical watts are unchanged.
        var peak = Enumerable.Range(0, xyz.Count / 3).Select(i => ToLinearSrgb(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]))
            .Select(c => Math.Max(c.R, Math.Max(c.G, c.B))).DefaultIfEmpty(0).Max();
        var exposure = peak > 0 ? 1 / peak : 1;
        var rgb = new double[xyz.Count];
        for (var i = 0; i < xyz.Count / 3; i++) { var c = ToSrgb(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2], exposure); rgb[3 * i] = c.R; rgb[3 * i + 1] = c.G; rgb[3 * i + 2] = c.B; }
        return rgb;
    }
}
