using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.Services;

public sealed record ElementPowerAssignmentResult(bool Succeeded, string Outcome, OpticSnapshot? Snapshot,
    double RequestedPowerPerMillimeter, double? ActualPowerPerMillimeter, double? FrontCurvaturePerMillimeter, int MatrixRequests);

/// <summary>Inverse thick-element power on an actual Core snapshot. Holds the back face, thickness,
/// media and every other surface fixed. Uses the affine front-curvature response of the shared matrix;
/// all three probes execute the ordinary Core transfer, never a separate lensmaker implementation.</summary>
public static class ElementPowerAssignment
{
    public static ElementPowerAssignmentResult Assign(Optic optic, int frontIndex, int backIndex,
        double powerPerMillimeter, double maximumAbsoluteCurvature, CancellationToken token = default,
        Action? charge = null)
    {
        ArgumentNullException.ThrowIfNull(optic);
        if (!double.IsFinite(powerPerMillimeter) || !double.IsFinite(maximumAbsoluteCurvature) || maximumAbsoluteCurvature <= 0)
            throw new ArgumentOutOfRangeException(nameof(powerPerMillimeter));
        var snapshot = optic.ToSnapshot(); var calls = 0;
        var parameter = new OpticalDesignParameter(frontIndex, OpticalDesignParameterKind.Curvature, 1);
        try
        {
            var lower = Probe(-maximumAbsoluteCurvature);
            var upper = Probe(maximumAbsoluteCurvature);
            var difference = upper.Power - lower.Power;
            if (!double.IsFinite(difference) || Math.Abs(difference) < 1e-14)
                return Result(false, "SingularPowerResponse");
            var curvature = -maximumAbsoluteCurvature + (powerPerMillimeter - lower.Power) * (2 * maximumAbsoluteCurvature) / difference;
            if (!double.IsFinite(curvature) || Math.Abs(curvature) > maximumAbsoluteCurvature)
                return Result(false, "RequestedPowerOutsideCurvatureBounds");
            var final = Probe(curvature);
            var succeeded = double.IsFinite(final.Power) && Math.Abs(final.Power - powerPerMillimeter) <= 1e-10 * Math.Max(1, Math.Abs(powerPerMillimeter));
            return new(succeeded, succeeded ? "AssignedAndVerified" : "PowerVerificationFailed",
                succeeded ? final.Optic.ToSnapshot() : null, powerPerMillimeter, final.Power, curvature, calls);
        }
        catch (NotSupportedException error) { return Result(false, "Unsupported:" + error.Message); }
        catch (Exception error) when (error is ArithmeticException or ArgumentException or InvalidOperationException)
        { return Result(false, "Domain:" + error.Message); }

        (Optic Optic, double Power) Probe(double curvature)
        {
            token.ThrowIfCancellationRequested(); charge?.Invoke(); calls++;
            var updated = OpticalDesignParameters.Apply(snapshot, [parameter], [curvature]);
            return (updated, updated.Paraxial.ElementOpticalPower(frontIndex, backIndex));
        }
        ElementPowerAssignmentResult Result(bool success, string outcome) => new(success, outcome, null, powerPerMillimeter, null, null, calls);
    }
}
