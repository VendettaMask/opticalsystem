using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static int IntegerDataParameter(MeritOperandDefinition definition, int index, double fallback)
    {
        var value = FiniteParameter(definition, index, fallback);
        return value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue
            ? (int)value : throw new ArgumentOutOfRangeException(nameof(definition), "参数必须为范围内的整数。");
    }

    private static double EvaluateSurfaceParameter(Optic optic, MeritOperandDefinition definition) =>
        SurfaceParameterData.ZemaxParameter(ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface)),
            ZemaxIntegerParameter(definition, 1, definition.Wavelength));

    private static double EvaluateGlassCost(Optic optic, MeritOperandDefinition definition)
    {
        var material = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface)).MaterialAfter;
        return material is CatalogGlassMaterial { ZemaxData.OtherData.Count: > 0 } glass
            && double.IsFinite(glass.ZemaxData.OtherData[0]) && glass.ZemaxData.OtherData[0] >= 0
            ? glass.ZemaxData.OtherData[0]
            : throw new InvalidOperationException("该玻璃目录没有有效相对成本（AGF OD 首项）。");
    }

    private static double EvaluateCylinderVolume(Optic optic, MeritOperandDefinition definition)
    {
        if (ZemaxIntegerParameter(definition, 1, definition.Wavelength) < 0)
            throw new ArgumentOutOfRangeException(nameof(definition), "终止表面不能为负数。");
        return SurfaceManufacturingMetrics.BoundingCylinderVolume(
            SurfaceRange(optic, definition, includeEndSurface: true), DiameterMode(definition));
    }

    private static double EvaluateCardinalData(Optic optic, MeritOperandDefinition definition)
    {
        var wave = IntegerDataParameter(definition, 0, definition.Hx);
        var orientation = IntegerDataParameter(definition, 1, definition.Hy);
        var data = IntegerDataParameter(definition, 2, definition.Px);
        if (data is < 0 or > 11) throw new ArgumentOutOfRangeException(nameof(definition), "CARD Data 必须为 0..11。");
        var result = optic.Paraxial.CardinalPlanes(ZemaxIntegerParameter(definition, 0, definition.Surface),
            ZemaxIntegerParameter(definition, 1, definition.Wavelength), RequireWavelength(optic, wave).Micrometers, orientation);
        return data switch
        {
            0 => result.ObjectFocalLength,
            1 => result.ImageFocalLength,
            2 => result.ObjectFocalPlane,
            3 => result.ImageFocalPlane,
            4 => result.ObjectPrincipalPlane,
            5 => result.ImagePrincipalPlane,
            6 => result.ObjectAntiPrincipalPlane,
            7 => result.ImageAntiPrincipalPlane,
            8 => result.ObjectNodalPlane,
            9 => result.ImageNodalPlane,
            10 => result.ObjectAntiNodalPlane,
            _ => result.ImageAntiNodalPlane
        };
    }

    private static double EvaluateIncidenceExtreme(Optic optic, MeritOperandDefinition definition, bool maximum)
    {
        var surfaceNumber = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var field = IntegerDataParameter(definition, 0, definition.Hx);
        var symmetry = IntegerDataParameter(definition, 1, definition.Hy);
        var data = IntegerDataParameter(definition, 2, definition.Px);
        if (surfaceNumber < 0 || wave < 0 || wave > optic.Wavelengths.Count
            || field < 0 || field > optic.Fields.Count || symmetry is < 0 or > 2 || data is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(definition), "MNAI/MXAI 表面、波长、视场、Symmetry 或 Data 超出范围。");
        var surfaces = surfaceNumber == 0 ? optic.SurfaceGroup.Items.Skip(1).ToArray()
            : new[] { ResolveSurface(optic, surfaceNumber) };
        var waves = wave == 0 ? Enumerable.Range(1, optic.Wavelengths.Count) : new[] { wave };
        var fields = field == 0 ? Enumerable.Range(1, optic.Fields.Count) : new[] { field };
        var pupils = new[] { (0.0, 0.0), (0.0, 1.0), (0.0, -1.0), (1.0, 0.0), (-1.0, 0.0) };
        var retained = surfaces.Select(surface => surface.Number).ToArray();
        var best = maximum ? double.NegativeInfinity : double.PositiveInfinity;
        var winningRay = 0; var winningField = 0; var winningWave = 0; var winningSurface = 0;
        // Strict comparison preserves deterministic first-in-order tie resolution.
        foreach (var waveIndex in waves)
            foreach (var fieldIndex in fields)
                for (var rayIndex = 0; rayIndex < pupils.Length; rayIndex++)
                {
                    if ((symmetry == 1 && rayIndex >= 3) || (symmetry == 2 && rayIndex is 1 or 2)) continue;
                    ComputationCancellation.ThrowIfCancellationRequested();
                    var coordinates = ResolveNormalizedField(optic, new MeritOperandDefinition { Field = fieldIndex });
                    var ray = new MeritOperandDefinition
                    {
                        Wavelength = waveIndex,
                        Field = 0,
                        Hx = coordinates.X,
                        Hy = coordinates.Y,
                        Px = pupils[rayIndex].Item1,
                        Py = pupils[rayIndex].Item2
                    };
                    var samples = RequireRaySamples(optic, ray, retained);
                    for (var surfaceIndex = 0; surfaceIndex < surfaces.Length; surfaceIndex++)
                    {
                        var surface = surfaces[surfaceIndex];
                        var sample = samples[surfaceIndex];
                        var normal = PositiveZNormal(surface, surface.CoordinateSystem.ToLocalPoint(sample.Position));
                        var incident = UnitVector(surface.CoordinateSystem.ToLocalDirection(sample.IncidentDirection
                            ?? throw new InvalidOperationException("追迹没有记录该面的入射方向。")));
                        var angle = Math.Acos(Math.Clamp(Math.Abs(Dot(normal, incident)), 0, 1)) * 180 / Math.PI;
                        if (!double.IsFinite(angle)) throw new InvalidOperationException("入射角不是有限数值。");
                        if ((maximum && angle > best) || (!maximum && angle < best))
                        {
                            best = angle; winningRay = rayIndex; winningField = fieldIndex;
                            winningWave = waveIndex; winningSurface = surface.Number;
                        }
                    }
                }
        if (!double.IsFinite(best)) throw new InvalidOperationException("没有可用的视场、波长或表面。");
        return data switch
        {
            0 => maximum ? BoundaryLessThanOrEqual(best, definition.Target) : BoundaryGreaterThanOrEqual(best, definition.Target),
            1 => winningRay,
            2 => winningField,
            3 => winningWave,
            _ => winningSurface
        };
    }
}
