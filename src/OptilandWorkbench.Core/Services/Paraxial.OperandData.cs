using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Services;

public sealed partial class Paraxial
{
    public double AngularMagnification(double wavelengthMicrometers)
    {
        EnsureCenteredOperandSystem();
        var surfaces = _optic.SurfaceGroup.Items;
        var position = ObjectConjugate.IsInfinite(surfaces[0])
            ? surfaces[1].CoordinateSystem.Origin.Z : surfaces[0].CoordinateSystem.Origin.Z;
        var height = position - EstimateEntrancePupilLocation(wavelengthMicrometers);
        // Unit object-space chief-ray slope avoids the undefined 0/0 ratio of an axial field.
        var ray = TraceGeneric(new[] { height }, new[] { 1.0 }, position, wavelengthMicrometers);
        return ray.Slopes[^1][0];
    }

    public double LagrangeInvariant(double wavelengthMicrometers)
    {
        EnsureCenteredOperandSystem();
        var marginal = MarginalRay(wavelengthMicrometers);
        var chief = ChiefRay(wavelengthMicrometers);
        var index = _optic.SurfaceGroup.Items[1].MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000);
        return index * (marginal.Heights[1][0] * chief.Slopes[1][0]
            - chief.Heights[1][0] * marginal.Slopes[1][0]);
    }

    public double ParaxialImageHeight(double wavelengthMicrometers)
    {
        EnsureCenteredOperandSystem();
        var marginal = MarginalRay(wavelengthMicrometers);
        var chief = ChiefRay(wavelengthMicrometers);
        var slope = marginal.Slopes[^1][0];
        if (!double.IsFinite(slope) || Math.Abs(slope) <= 1e-15)
            throw new InvalidOperationException("边缘光线没有有限近轴焦点，不能计算 PIMH。");
        var distance = -marginal.Heights[^1][0] / slope;
        return chief.Heights[^1][0] + distance * chief.Slopes[^1][0];
    }

    public double ObjectNumericalAperture()
    {
        EnsureCenteredOperandSystem();
        var surface = _optic.SurfaceGroup.Items[0];
        if (ObjectConjugate.IsInfinite(surface))
            throw new InvalidOperationException("OBSN 需要有限物距。");
        var wavelength = PrimaryWavelengthNanometers();
        if (Math.Abs(EstimateEntrancePupilLocation(wavelength / 1000) - surface.CoordinateSystem.Origin.Z) <= 1e-15)
            throw new InvalidOperationException("物面与入瞳重合，无法定义有限物方边缘光线角。");
        var slope = MarginalRay(wavelength / 1000).Slopes[0][0];
        return Math.Abs(surface.MaterialAfter.RefractiveIndex(wavelength) * Math.Sin(Math.Atan(slope)));
    }

    private void EnsureCenteredOperandSystem()
    {
        EnsureComputable();
        if (_optic.SurfaceGroup.Items.Count < 2)
            throw new InvalidOperationException("一阶操作数需要物面和实际光学面。");
        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            var frame = surface.CoordinateSystem;
            if (Math.Abs(frame.Origin.X) > 1e-12 || Math.Abs(frame.Origin.Y) > 1e-12
                || Math.Abs(frame.RotationXDegrees) > 1e-12 || Math.Abs(frame.RotationYDegrees) > 1e-12
                || surface.Geometry is not (PlaneGeometry or StandardGeometry or EvenAsphereGeometry
                    or OddAsphereGeometry or ForbesQGeometry)
                || surface.InteractionModel is not (RefractiveReflectiveInteractionModel or ThinLensInteractionModel))
                throw new NotSupportedException("该一阶操作数当前要求同轴旋转对称折射/反射面或理想薄透镜；偏心、非对称及相位/衍射系统不能用此标量近轴结果代替。");
            if (surface.MaterialAfter.PropagationModel is not HomogeneousPropagationModel)
                throw new NotSupportedException("该一阶操作数当前不支持梯度折射率传播介质。");
        }
    }
}
