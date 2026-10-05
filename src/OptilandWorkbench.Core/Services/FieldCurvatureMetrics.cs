using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

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
        var fieldX = hx; var fieldY = hy;
        if (optic.FieldDefinition == FieldDefinitionKind.Angle)
        {
            var scale = FieldCoordinates.MaximumRadius(optic.Fields) * Math.PI / 180;
            fieldX = Math.Tan(hx * scale); fieldY = Math.Tan(hy * scale);
        }
        var magnitude = Math.Sqrt(fieldX * fieldX + fieldY * fieldY);
        var x = magnitude == 0 ? 0 : fieldX / magnitude;
        var y = magnitude == 0 ? 1 : fieldY / magnitude;
        if (sagittal) (x, y) = (y, -x);
        return ImagePlaneDelta(optic, hx, hy, wavelength, 1e-5, x, y, requireUnvignetted: true);
    }

    internal static double ImagePlaneDelta(Optic optic, double hx, double hy, double wavelength,
        double pupilDelta, double axisX, double axisY, bool requireUnvignetted = false)
    {
        var first = AnalysisTrace.FinalSample(optic, hx, hy, -pupilDelta * axisX, -pupilDelta * axisY,
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
}
