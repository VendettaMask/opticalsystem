using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public enum ParabasalRayPair { Symmetric, ChiefAndPositive }

/// <summary>Parabasal focal-plane displacement shared by field scans and merit operands.</summary>
public static class FieldCurvatureMetrics
{
    public static double Evaluate(Optic optic, double wavelength, double hx, double hy, bool sagittal)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(wavelength) || wavelength <= 0) throw new ArgumentOutOfRangeException(nameof(wavelength));
        if (!double.IsFinite(hx) || !double.IsFinite(hy) || Math.Abs(hx) > 1 || Math.Abs(hy) > 1)
            throw new ArgumentOutOfRangeException(nameof(hx), "Hx/Hy 必须为 [-1,1] 内的有限坐标。");
        if (optic.ImageSpaceAfocal) throw new NotSupportedException("FCGS/FCGT 当前仅支持有焦像空间。");
        var image = optic.SurfaceGroup.Items.LastOrDefault() ?? throw new InvalidOperationException("未定义像面。");
        if (image.Geometry is not PlaneGeometry && !(image.Geometry is StandardGeometry standard && double.IsInfinity(standard.Radius)))
            throw new NotSupportedException("FCGS/FCGT 当前需要平面像面。");
        if (image.CoordinateSystem.RotationXDegrees != 0 || image.CoordinateSystem.RotationYDegrees != 0)
            throw new NotSupportedException("FCGS/FCGT 当前尚未支持倾斜像面。");
        // FCGS/FCGT use the chief ray and a one-sided parabasal pupil ray.
        // A symmetric differential pair removes the finite-step coma term and
        // therefore computes a different tangential focal displacement.
        // The generalized components are fixed X (FCGS) and Y (FCGT), including
        // X, negative and oblique fields; they do not rotate with field azimuth.
        return ImagePlaneDelta(optic, hx, hy, wavelength, 1e-3, sagittal ? 1 : 0, sagittal ? 0 : 1, requireUnvignetted: true,
            rayPair: ParabasalRayPair.ChiefAndPositive);
    }

    internal static double ImagePlaneDelta(Optic optic, double hx, double hy, double wavelength,
        double pupilDelta, double axisX, double axisY, bool requireUnvignetted = false,
        ParabasalRayPair rayPair = ParabasalRayPair.Symmetric)
    {
        if (!Enum.IsDefined(rayPair)) throw new ArgumentOutOfRangeException(nameof(rayPair));
        if (rayPair == ParabasalRayPair.ChiefAndPositive)
        {
            var components = GeneralizedImagePlaneDeltas(optic, hx, hy, wavelength, pupilDelta, requireUnvignetted);
            if (axisY == 0 && axisX != 0) return components.Sagittal;
            if (axisX == 0 && axisY != 0) return components.Tangential;
            throw new ArgumentException("Generalized curvature requires a Cartesian X or Y component.");
        }
        var firstDelta = -pupilDelta;
        var first = AnalysisTrace.FinalSample(optic, hx, hy, firstDelta * axisX, firstDelta * axisY,
            wavelength, requireUnvignetted);
        var second = AnalysisTrace.FinalSample(optic, hx, hy, pupilDelta * axisX, pupilDelta * axisY,
            wavelength, requireUnvignetted);
        if (requireUnvignetted)
        {
            var axis = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalDirection(new Vector3D(axisX, axisY, 0));
            axisX = axis.X; axisY = axis.Y;
        }
        var firstDirection = axisX * first.Direction.X + axisY * first.Direction.Y;
        var secondDirection = axisX * second.Direction.X + axisY * second.Direction.Y;
        var firstPosition = axisX * first.Position.X + axisY * first.Position.Y;
        var secondPosition = axisX * second.Position.X + axisY * second.Position.Y;
        var denominator = firstDirection * second.Direction.Z - secondDirection * first.Direction.Z;
        if (Math.Abs(denominator) <= 1e-30)
        {
            if (requireUnvignetted) throw new InvalidOperationException("傍轴光线平行，没有有限场曲焦点。");
            return 0; // Preserve the established scan behavior; strict operands report an error.
        }
        var value = ((secondDirection * first.Position.Z) - (secondDirection * second.Position.Z)
            - (second.Direction.Z * firstPosition) + (second.Direction.Z * secondPosition)) / denominator * first.Direction.Z;
        return double.IsFinite(value) ? value : throw new InvalidOperationException("场曲焦点不是有限数值。");
    }

    // Native-facing generalized Cartesian components. The chief and the two
    // positive parabasal rays supply the complete coupled response. The additive
    // cross terms are part of this compatibility convention; this is not a
    // closest-approach focus or an eigenfocus of a rotated sagittal/tangential frame.
    // No reference values, fitted gain or lens-specific correction enter tracing.
    internal static (double Sagittal, double Tangential) GeneralizedImagePlaneDeltas(
        Optic optic, double hx, double hy, double wavelength, double pupilDelta, bool requireUnvignetted = false)
    {
        var chief = AnalysisTrace.FinalSample(optic, hx, hy, 0, 0, wavelength, requireUnvignetted);
        var xRay = AnalysisTrace.FinalSample(optic, hx, hy, pupilDelta, 0, wavelength, requireUnvignetted);
        var yRay = AnalysisTrace.FinalSample(optic, hx, hy, 0, pupilDelta, wavelength, requireUnvignetted);
        var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var xAxis = frame.ToLocalDirection(new Vector3D(1, 0, 0));
        var yAxis = frame.ToLocalDirection(new Vector3D(0, 1, 0));
        static double Project(Vector3D value, Vector3D axis) => value.X * axis.X + value.Y * axis.Y;
        var xSlopeX = Project(xRay.Direction, xAxis) / xRay.Direction.Z;
        var xSlopeY = Project(xRay.Direction, yAxis) / xRay.Direction.Z;
        var ySlopeX = Project(yRay.Direction, xAxis) / yRay.Direction.Z;
        var ySlopeY = Project(yRay.Direction, yAxis) / yRay.Direction.Z;
        var axx = xSlopeX - Project(chief.Direction, xAxis) / chief.Direction.Z;
        var ayx = xSlopeY - Project(chief.Direction, yAxis) / chief.Direction.Z;
        var axy = ySlopeX - Project(chief.Direction, xAxis) / chief.Direction.Z;
        var ayy = ySlopeY - Project(chief.Direction, yAxis) / chief.Direction.Z;
        var xPosition = xRay.Position - chief.Position;
        var yPosition = yRay.Position - chief.Position;
        // Bring both intercepts to the chief's image-Z plane before forming the
        // response, retaining the established treatment of nonzero local sag.
        var pxx = Project(xPosition, xAxis) - xSlopeX * xPosition.Z;
        var pyx = Project(xPosition, yAxis) - xSlopeY * xPosition.Z;
        var pxy = Project(yPosition, xAxis) - ySlopeX * yPosition.Z;
        var pyy = Project(yPosition, yAxis) - ySlopeY * yPosition.Z;
        var denominator = axx * ayy + axy * ayx;
        if (Math.Abs(denominator) <= 1e-30)
        {
            if (requireUnvignetted) throw new InvalidOperationException("傍轴光线平行，没有有限场曲焦点。");
            return (0, 0);
        }
        var sagittal = -(pxx * ayy + pxy * ayx) / denominator;
        var tangential = -(pyy * axx + pyx * axy) / denominator;
        if (!double.IsFinite(sagittal) || !double.IsFinite(tangential))
            throw new InvalidOperationException("场曲焦点不是有限数值。");
        return (sagittal, tangential);
    }
}
