namespace OpticalSystemDesign.Core.Domain;

/// <summary>Prescription editor units. Optical calculations always use millimeters.</summary>
public enum LensLengthUnit { Millimeter, Centimeter, Meter, Inch }

public static class LensLengthUnits
{
    public static double Millimeters(this LensLengthUnit unit) => unit switch
    {
        LensLengthUnit.Millimeter => 1,
        LensLengthUnit.Centimeter => 10,
        LensLengthUnit.Meter => 1000,
        LensLengthUnit.Inch => 25.4,
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    public static string Symbol(this LensLengthUnit unit) => unit switch
    {
        LensLengthUnit.Millimeter => "mm",
        LensLengthUnit.Centimeter => "cm",
        LensLengthUnit.Meter => "m",
        LensLengthUnit.Inch => "in",
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };
}
