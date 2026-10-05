using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateCoatingData(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var field = IntegerDataParameter(definition, 0, definition.Field);
        var px = FiniteParameter(definition, 1, definition.Px);
        var py = FiniteParameter(definition, 2, definition.Py);
        var data = IntegerDataParameter(definition, 3, 0);
        if (wave <= 0) throw new NotSupportedException("CODA 需要明确正波长编号；Wave=0 原生规则尚未核实。");
        if (field < 1 || field > optic.Fields.Count || surface < 0)
            throw new ArgumentOutOfRangeException(nameof(definition), "CODA 需要有效视场及表面编号（Surf=0 为像面）。");
        if (px * px + py * py > 1 + 1e-12)
            throw new ArgumentOutOfRangeException(nameof(definition), "CODA 光瞳坐标须在单位圆内。");
        var target = surface == 0 ? optic.SurfaceGroup.Items.Count - 1
            : optic.SurfaceGroup.Items.ToList().FindIndex(s => s.Number == surface);
        if (target < 0) throw new ArgumentOutOfRangeException(nameof(definition), "CODA 目标表面不存在。");
        var selected = optic.Fields[field - 1];
        var normalized = FieldCoordinates.Normalize(optic.Fields, selected.X, selected.Y);
        var generate = optic.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(normalized.X, normalized.Y,
            RequireWavelength(optic, wave).Micrometers, aimAtStop: optic.RayAimingEnabled);
        var trace = optic.SequentialRayTracer.TracePolarized(generate(px, py), optic.Polarization.Input,
            targetSurfaceIndex: target, inputCoordinates: optic.SurfaceGroup.Items[0].CoordinateSystem,
            includePropagationPhase: Math.Abs((long)data) is >= 101 and <= 106 or >= 111 and <= 113);
        return CoatingRayMetrics.Value(trace, data);
    }

    private static double EvaluateRmsRetardance(Optic optic, MeritOperandDefinition definition)
    {
        if (definition.ZemaxDataParameters.Length < 6)
            throw new ArgumentException("RRET 需要完整八槽 Ring/Wave/Hx/Hy/Jx/Jy/X-Phase/Y-Phase；请在编辑器中补全参数。");
        return PolarizationMetrics.RmsRetardance(optic,
            ZemaxIntegerParameter(definition, 1, definition.Wavelength),
            FiniteParameter(definition, 0, 0), FiniteParameter(definition, 1, 0),
            ZemaxIntegerParameter(definition, 0, definition.PupilRings),
            new JonesInputState(FiniteParameter(definition, 2, 0), FiniteParameter(definition, 3, 0),
                FiniteParameter(definition, 4, 0), FiniteParameter(definition, 5, 0), optic.Polarization.ReferenceAxis));
    }
}
