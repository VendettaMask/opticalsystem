using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Differentiation;

public sealed record DifferentiableParaxialTransferMatrix(ParaxialTransferMatrix Nominal,
    DifferentialScalar A, DifferentialScalar B, DifferentialScalar C, DifferentialScalar D)
{
    public DifferentialScalar? InverseFNumber { get; init; }
    /// <summary>Whole-system optical power, present when whole-system pupil metrics are requested.</summary>
    public DifferentialScalar? OpticalPowerPerMillimeter { get; init; }
}

/// <summary>Forward centered fixed-plane transfer derivatives, with the same endpoint convention as Core's nominal matrix.</summary>
public static class DifferentiableParaxialMetrics
{
    /// <summary>The optional inverse F number uses the whole system and primary wavelength, independently of the selected matrix span.</summary>
    public static DifferentiableParaxialTransferMatrix TransferMatrix(Optic optic,
        IReadOnlyList<OpticalDesignParameter> parameters, int inputSurfaceIndex, int outputSurfaceIndex,
        double? wavelengthNanometers = null, bool includeInverseFNumber = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(parameters);
        var nominal = optic.Paraxial.GetTransferMatrix(inputSurfaceIndex, outputSurfaceIndex, wavelengthNanometers);
        var context = new OpticalDifferentialContext(optic, parameters);
        var matrix = context.TransferMatrix(inputSurfaceIndex, outputSurfaceIndex, nominal.WavelengthNanometers);
        OpticalDifferentialContext.Verify(matrix.A, nominal.A, "fixed-plane matrix A");
        OpticalDifferentialContext.Verify(matrix.B, nominal.B, "fixed-plane matrix B");
        OpticalDifferentialContext.Verify(matrix.C, nominal.C, "fixed-plane matrix C");
        OpticalDifferentialContext.Verify(matrix.D, nominal.D, "fixed-plane matrix D");
        DifferentialScalar? inverse = null;
        DifferentialScalar? power = null;
        if (includeInverseFNumber)
        {
            var firstOrder = context.Paraxial();
            var formalPower = optic.Paraxial.EstimateOpticalPower();
            OpticalDifferentialContext.Verify(firstOrder.Power, formalPower, "whole-system optical power");
            power = firstOrder.Power.WithValue(formalPower);
            var measured = DifferentialScalar.Abs(firstOrder.Power) * firstOrder.EntrancePupilDiameter;
            var fNumber = optic.Paraxial.EstimateFNumber();
            if (fNumber > 0 && double.IsFinite(fNumber))
            {
                OpticalDifferentialContext.Verify(measured, 1 / fNumber, "fixed-stop inverse f-number");
                measured = measured.WithValue(1 / fNumber);
            }
            else if (firstOrder.Power.Value == 0) OpticalDifferentialContext.Verify(measured, 0, "zero-power inverse f-number");
            else throw new ArithmeticException("The whole-system inverse F number is unavailable.");
            inverse = measured;
        }
        return new(nominal, matrix.A.WithValue(nominal.A), matrix.B.WithValue(nominal.B), matrix.C.WithValue(nominal.C), matrix.D.WithValue(nominal.D))
        { InverseFNumber = inverse, OpticalPowerPerMillimeter = power };
    }
}
