using OpticalSystemDesign.Core.Propagation;

namespace OpticalSystemDesign.Core.Materials;

/// <summary>Three-descriptor Conrady design glass. Protocol constants are independently
/// verified against 2026 R1 model-solve index data; this is never a catalog-glass substitution.</summary>
public sealed class ModelGlassMaterial : IMaterial
{
    public const string Protocol = "conrady-nd-vd-dpgf/1";
    public static readonly IReadOnlyList<string> ParameterNames = Array.AsReadOnly(new[] { "nd", "vd", "dpgf" });

    public ModelGlassMaterial(string name, double nd, double vd, double dpgf,
        bool varyNd = false, bool varyVd = false, bool varyDPgF = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!double.IsFinite(nd) || nd <= 1 || !double.IsFinite(vd) || vd < 0 || !double.IsFinite(dpgf))
            throw new ArgumentException("Model glass requires finite Nd > 1, Vd >= 0 and dPgF.");
        Name = name; Nd = nd; Vd = vd; DPgF = dpgf;
        VaryNd = varyNd; VaryVd = varyVd; VaryDPgF = varyDPgF;
        Variables = Array.AsReadOnly(ParameterNames.Where(key => key == "nd" ? varyNd : key == "vd" ? varyVd : varyDPgF).ToArray());
    }

    public string Name { get; }
    public double Nd { get; }
    public double Vd { get; }
    public double DPgF { get; }
    public bool VaryNd { get; }
    public bool VaryVd { get; }
    public bool VaryDPgF { get; }
    public IReadOnlyList<string> Variables { get; }
    public IPropagationModel PropagationModel { get; } = new HomogeneousPropagationModel();

    public double RefractiveIndex(double wavelengthNanometers)
    {
        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0) throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers));
        if (Vd == 0) return Nd;
        var dispersion = (Nd - 1) / Vd;
        var a = 1 + dispersion * (-1.431304222 + .98511546058 * Vd + 8.84931 * DPgF);
        var b = dispersion * (.499823474 + .01163400714 * Vd - 6.91677 * DPgF);
        var c = dispersion * (.090308377 - .00076432603 * Vd + .454415 * DPgF);
        var wavelength = wavelengthNanometers / 1000;
        var index = a + b / wavelength + c / Math.Pow(wavelength, 3.5);
        if (!double.IsFinite(index) || index <= 0) throw new ArithmeticException("The model-glass index is outside its physical domain.");
        return index;
    }
    public double ExtinctionCoefficient(double wavelengthNanometers) => 0;
    public IMaterial Clone() => new ModelGlassMaterial(Name, Nd, Vd, DPgF, VaryNd, VaryVd, VaryDPgF);
}
