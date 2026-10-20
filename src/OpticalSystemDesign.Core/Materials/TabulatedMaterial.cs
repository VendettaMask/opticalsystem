using OpticalSystemDesign.Core.Propagation;

namespace OpticalSystemDesign.Core.Materials;

/// <summary>Explicit real-index samples with local cubic interpolation and no extrapolation.</summary>
public sealed class TabulatedMaterial : IMaterial
{
    private readonly double[] _wavelengths;
    private readonly double[] _indices;

    public TabulatedMaterial(string name, IReadOnlyList<double> wavelengthsNanometers,
        IReadOnlyList<double> indices, string provenance, IPropagationModel? propagationModel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance);
        ArgumentNullException.ThrowIfNull(wavelengthsNanometers);
        ArgumentNullException.ThrowIfNull(indices);
        if (wavelengthsNanometers.Count < 4 || wavelengthsNanometers.Count != indices.Count
            || wavelengthsNanometers.Any(value => !double.IsFinite(value) || value <= 0)
            || indices.Any(value => !double.IsFinite(value) || value <= 0)
            || wavelengthsNanometers.Zip(wavelengthsNanometers.Skip(1)).Any(pair => pair.First >= pair.Second))
            throw new ArgumentException("A material table requires at least four ordered, finite wavelength/index pairs.");
        Name = name;
        Provenance = provenance;
        _wavelengths = wavelengthsNanometers.ToArray();
        _indices = indices.ToArray();
        WavelengthsNanometers = Array.AsReadOnly(_wavelengths);
        Indices = Array.AsReadOnly(_indices);
        PropagationModel = propagationModel?.Clone() ?? new HomogeneousPropagationModel();
    }

    public string Name { get; }
    public string Provenance { get; }
    public IReadOnlyList<double> WavelengthsNanometers { get; }
    public IReadOnlyList<double> Indices { get; }
    public IPropagationModel PropagationModel { get; }

    public double RefractiveIndex(double wavelengthNanometers)
    {
        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers < _wavelengths[0]
            || wavelengthNanometers > _wavelengths[^1])
            throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers),
                $"Material '{Name}' is tabulated only from {_wavelengths[0]} to {_wavelengths[^1]} nm.");
        var found = Array.BinarySearch(_wavelengths, wavelengthNanometers);
        if (found >= 0) return _indices[found];
        var first = Math.Clamp(~found - 2, 0, _wavelengths.Length - 4);
        // Neville's local polynomial interpolation avoids a global dispersion
        // fit. At a captured wavelength the original index is returned exactly.
        Span<double> values = stackalloc double[4];
        _indices.AsSpan(first, 4).CopyTo(values);
        for (var order = 1; order < 4; order++)
            for (var i = 0; i < 4 - order; i++)
            {
                var left = _wavelengths[first + i];
                var right = _wavelengths[first + i + order];
                values[i] = ((right - wavelengthNanometers) * values[i]
                    + (wavelengthNanometers - left) * values[i + 1]) / (right - left);
            }
        return values[0];
    }

    public double ExtinctionCoefficient(double wavelengthNanometers) => 0;
    public IMaterial Clone() => new TabulatedMaterial(Name, _wavelengths, _indices, Provenance, PropagationModel);
}
