using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;

namespace OpticalSystemDesign.Core.Services;

/// <summary>Forward centered, homogeneous, refractive transfer in [height (mm), slope dy/dz].
/// A,D are dimensionless, B is mm and C is 1/mm. Column rays map by M * [y,u];
/// successive surfaces left-multiply. Includes both endpoint refractions and all intervening
/// propagation, but excludes thickness AFTER the output plane. Coordinates use Core's frame.</summary>
public sealed record ParaxialTransferMatrix(double A, double B, double C, double D,
    double InputIndex, double OutputIndex, double InputZMillimeters, double OutputZMillimeters,
    int InputSurfaceIndex, int OutputSurfaceIndex, double WavelengthNanometers)
{
    public (double HeightMillimeters, double Slope) Map(double heightMillimeters, double slope) =>
        (A * heightMillimeters + B * slope, C * heightMillimeters + D * slope);
}

public sealed partial class Paraxial
{
    /// <summary>Read the existing Core transfer matrix immediately before input refraction
    /// through immediately after output refraction. Use an explicit fixed reference-plane
    /// surface for structure comparisons; the first real lens need not be that plane.</summary>
    public ParaxialTransferMatrix GetTransferMatrix(int inputSurfaceIndex, int outputSurfaceIndex,
        double? wavelengthNanometers = null)
    {
        EnsureComputable();
        var surfaces = _optic.SurfaceGroup.Items;
        if (inputSurfaceIndex < 1 || outputSurfaceIndex < inputSurfaceIndex || outputSurfaceIndex >= surfaces.Count)
            throw new ArgumentOutOfRangeException(nameof(inputSurfaceIndex), "Require 1 <= input <= output < surface count; the Object plane is not a finite reference.");
        var wavelength = wavelengthNanometers ?? PrimaryWavelengthNanometers();
        if (!double.IsFinite(wavelength) || wavelength <= 0) throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers));
        if (HasGradientIndex) throw new NotSupportedException("This fixed-plane matrix contract requires homogeneous media.");
        for (var index = inputSurfaceIndex; index <= outputSurfaceIndex; index++)
        {
            var surface = surfaces[index];
            if (surface.IsReflective || surface.InteractionModel is not RefractiveReflectiveInteractionModel { IsReflective: false }
                || surface.Geometry is not (PlaneGeometry or StandardGeometry or EvenAsphereGeometry)
                || surface.CoordinateSystem.Origin.X != 0 || surface.CoordinateSystem.Origin.Y != 0
                || surface.CoordinateSystem.ToGlobalDirection(new(0, 0, 1)) != new Vector3D(0, 0, 1)
                || index < outputSurfaceIndex && surface.Thickness < 0)
                throw new NotSupportedException("This matrix contract requires centered forward refractive surfaces.");
        }
        var inputIndex = surfaces[inputSurfaceIndex - 1].MaterialAfter.RefractiveIndex(wavelength);
        var outputIndex = surfaces[outputSurfaceIndex].MaterialAfter.RefractiveIndex(wavelength);
        if (!double.IsFinite(inputIndex) || inputIndex <= 0 || !double.IsFinite(outputIndex) || outputIndex <= 0)
            throw new ArithmeticException("Matrix endpoint media require finite positive refractive indices.");
        var matrix = TraceMatrix(inputSurfaceIndex, outputSurfaceIndex + 1, wavelength, includeTrailingTranslation: false);
        if (!double.IsFinite(matrix.A) || !double.IsFinite(matrix.B) || !double.IsFinite(matrix.C) || !double.IsFinite(matrix.D))
            throw new ArithmeticException("The fixed-plane transfer is outside the finite Core domain.");
        return new(matrix.A, matrix.B, matrix.C, matrix.D, inputIndex, outputIndex,
            surfaces[inputSurfaceIndex].CoordinateSystem.Origin.Z, surfaces[outputSurfaceIndex].CoordinateSystem.Origin.Z,
            inputSurfaceIndex, outputSurfaceIndex, wavelength);
    }
}
