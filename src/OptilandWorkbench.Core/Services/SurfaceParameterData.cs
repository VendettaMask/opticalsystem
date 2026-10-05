using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.Services;

public static class SurfaceParameterData
{
    public static double ZemaxParameter(OpticalSurface surface, int parameter)
    {
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        if (parameter is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(parameter), "当前面型参数支持 Even/Odd Asphere 的 Param 1..8。");
        var coefficients = surface.Geometry switch
        {
            EvenAsphereGeometry even => even.Coefficients,
            OddAsphereGeometry odd => odd.Coefficients,
            _ => throw new NotSupportedException("该面型尚无已验证的 Zemax Param 映射；不能把原始导入参数当作当前计算值。")
        };
        // Omitted polynomial terms are mathematically zero, not unavailable data.
        var value = parameter <= coefficients.Count ? coefficients[parameter - 1] : 0;
        return double.IsFinite(value) ? value : throw new InvalidOperationException("面型参数不是有限数值。");
    }
}
