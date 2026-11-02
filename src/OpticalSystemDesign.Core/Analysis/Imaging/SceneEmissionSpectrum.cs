namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record SceneEmissionNode(double WavelengthNanometers, double SpectralFraction,
    double BrightRadiance, double DarkRadiance);
public sealed record SceneEmissionQuadrature(double ShapeAreaNanometers, double InputShapeMaximum,
    int Order, IReadOnlyList<SceneEmissionNode> Nodes);

/// <summary>Piecewise-linear relative source shape, normalized only at the source to a declared band radiance.
/// Weights include wavelength integration. No image, throughput, detector response or photon-energy weighting.</summary>
public static class SceneEmissionSpectrum
{
    public const string Algorithm = "SceneRadianceQuadrature/1; piecewise-linear source; Gauss-Legendre 2/3";
    public static SceneEmissionQuadrature Integrate(IReadOnlyList<ScalarSpectrumSample> shape,
        double brightBandRadiance, double darkBandRadiance, int order = 2)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.Count is < 2 or > 4096 || shape.Any(s => s is null || !double.IsFinite(s.Value) || s.Value < 0)
            || !double.IsFinite(brightBandRadiance) || !double.IsFinite(darkBandRadiance)
            || brightBandRadiance is < 0 or > 1e6 || darkBandRadiance is < 0 or > 1e6 || order is not (2 or 3))
            throw new ArgumentException("Invalid source shape, integrated radiance or quadrature order.");
        var maximum = shape.Max(s => s.Value);
        if (maximum <= 0) throw new ArgumentException("Source shape has zero integrated support.");
        var curve = new ScalarSpectrum(ScalarSpectrumKind.Reflectance, shape.Select(s => s with { Value = s.Value / maximum }));
        double area = 0;
        for (var i = 1; i < curve.Samples.Count; i++)
            area += (curve.Samples[i].WavelengthNanometers - curve.Samples[i - 1].WavelengthNanometers)
                * (curve.Samples[i].Value + curve.Samples[i - 1].Value) / 2;
        if (!double.IsFinite(area) || area <= 0) throw new ArgumentException("Source spectral area is invalid.");
        var abscissas = order == 2 ? new[] { -1 / Math.Sqrt(3), 1 / Math.Sqrt(3) } : new[] { -Math.Sqrt(.6), 0, Math.Sqrt(.6) };
        var weights = order == 2 ? new[] { 1d, 1d } : new[] { 5 / 9d, 8 / 9d, 5 / 9d };
        var nodes = new List<SceneEmissionNode>();
        for (var i = 1; i < curve.Samples.Count; i++)
        {
            var left = curve.Samples[i - 1]; var right = curve.Samples[i];
            if (left.Value == 0 && right.Value == 0) continue;
            var half = (right.WavelengthNanometers - left.WavelengthNanometers) / 2;
            var middle = (right.WavelengthNanometers + left.WavelengthNanometers) / 2;
            for (var n = 0; n < order; n++)
            {
                var wavelength = middle + half * abscissas[n];
                var fraction = half * weights[n] * curve.Evaluate(wavelength) / area;
                nodes.Add(new(wavelength, fraction, brightBandRadiance * fraction, darkBandRadiance * fraction));
            }
        }
        if (nodes.Count > 16) throw new ArgumentException("Source integration exceeds 16 nodes per task; retain the declared spectrum and report the limit.");
        return new(area, maximum, order, Array.AsReadOnly(nodes.ToArray()));
    }
}
