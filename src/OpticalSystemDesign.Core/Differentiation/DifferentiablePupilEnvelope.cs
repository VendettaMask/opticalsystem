using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Differentiation;

public sealed record DifferentiablePupilFootprint(int SurfaceIndex, double NormalizedField, double PupilCoordinate,
    DifferentialScalar HeightMillimeters, DifferentialScalar ClearanceMillimeters);
public sealed record DifferentiablePupilEnvelope(IReadOnlyList<DifferentiablePupilFootprint> Samples, int MatrixEvaluations);

/// <summary>First-order, meridional fixed-stop footprints for construction/preconditioning only.
/// This approximation never certifies physical transmission or image quality.</summary>
public static class DifferentiablePupilEnvelopes
{
    public static DifferentiablePupilEnvelope Evaluate(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        int inputSurfaceIndex, IReadOnlyList<double> normalizedFields, double? wavelengthNanometers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(normalizedFields);
        cancellationToken.ThrowIfCancellationRequested();
        DifferentiableOpticalMetrics.EnsureSupported(optic, physicalStop: true);
        OpticalDesignParameters.Validate(optic, parameters);
        if (normalizedFields.Count is < 1 or > 21 || normalizedFields.Any(f => !double.IsFinite(f) || Math.Abs(f) > 1))
            throw new ArgumentException("Bounded normalized meridional fields are required.");
        var surfaces = optic.SurfaceGroup.Items;
        var stop = surfaces.ToList().FindIndex(s => s.IsStop);
        if (inputSurfaceIndex < 1 || stop < inputSurfaceIndex || surfaces[stop].PhysicalAperture is not CircularAperture stopAperture)
            throw new NotSupportedException("The fixed reference must precede the circular stop.");
        var nominal = optic.Paraxial.GetTransferMatrix(inputSurfaceIndex, stop, wavelengthNanometers);
        var context = new OpticalDifferentialContext(optic, parameters);
        var stopMatrix = context.TransferMatrix(inputSurfaceIndex, stop, nominal.WavelengthNanometers);
        if (Math.Abs(stopMatrix.A.Value) < 1e-12) throw new ArithmeticException("The fixed-stop entrance bundle is singular.");
        // Include wavelength-resolving nominal and differentiable stop requests.
        var matrices = 2;
        var samples = new List<DifferentiablePupilFootprint>();
        for (var index = inputSurfaceIndex; index < surfaces.Count - 1; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (surfaces[index].PhysicalAperture is not CircularAperture aperture) continue;
            var matrix = context.TransferMatrix(inputSurfaceIndex, index, nominal.WavelengthNanometers); matrices++;
            foreach (var field in normalizedFields)
            {
                var angle = FieldCoordinates.Denormalize(optic.Fields, 0, field).Y;
                var slope = Math.Tan(angle * Math.PI / 180);
                foreach (var pupil in new[] { -1.0, 1.0 })
                {
                    DifferentialScalar target = stopAperture.Radius * pupil;
                    var launch = (target - stopMatrix.B * slope) / stopMatrix.A;
                    var height = index == stop ? target : matrix.A * launch + matrix.B * slope;
                    samples.Add(new(index, field, pupil, height, aperture.Radius - DifferentialScalar.Abs(height)));
                }
            }
        }
        return new(samples, matrices);
    }
}
