namespace OptilandWorkbench.Core.Phase;

public interface IPhaseProfile
{
    string Kind { get; }

    double Phase(double x, double y, double wavelengthNanometers);

    (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers);
}

public sealed class PolynomialPhaseProfile : IPhaseProfile
{
    public PolynomialPhaseProfile(IReadOnlyDictionary<(int X, int Y), double> coefficients)
    {
        Coefficients = new Dictionary<(int X, int Y), double>(coefficients);
    }

    public string Kind => "polynomial_phase";

    public IReadOnlyDictionary<(int X, int Y), double> Coefficients { get; }

    public double Phase(double x, double y, double wavelengthNanometers)
    {
        return Coefficients.Sum(term => term.Value * Math.Pow(x, term.Key.X) * Math.Pow(y, term.Key.Y));
    }

    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers)
    {
        var dx = 0.0;
        var dy = 0.0;
        foreach (var term in Coefficients)
        {
            if (term.Key.X > 0)
            {
                dx += term.Value * term.Key.X * Math.Pow(x, term.Key.X - 1) * Math.Pow(y, term.Key.Y);
            }

            if (term.Key.Y > 0)
            {
                dy += term.Value * term.Key.Y * Math.Pow(x, term.Key.X) * Math.Pow(y, term.Key.Y - 1);
            }
        }

        return (dx, dy);
    }
}
