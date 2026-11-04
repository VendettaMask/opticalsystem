namespace OpticalSystemDesign.Core.NonSequential;

public enum NonSequentialFieldComponent { X, Y, Z }
public enum NonSequentialFieldViewKind { Real, Imaginary, Magnitude, PhaseDegrees }
public sealed record NonSequentialFieldSelection(int WavelengthNumber, string CoherenceGroup, int Mode,
    NonSequentialFieldComponent Component, NonSequentialFieldViewKind Kind);
public sealed record NonSequentialFieldView(NonSequentialFieldSelection Selection, IReadOnlyList<double> Values,
    IReadOnlyList<bool> Valid, string Unit, string Protocol = "NscJonesFieldView/1; detector-local axes; raw ray amplitude sum; wrapped phase [-180,180]");

/// <summary>Read-only view of one complex field. Different wavelengths, incoherent groups or
/// mutually incoherent Jones modes cannot be combined into a single amplitude or phase.</summary>
public static class NonSequentialFieldViewBuilder
{
    public static NonSequentialFieldView Extract(NonSequentialDetectorFrame frame, int wavelengthNumber, string? coherenceGroup,
        int mode, NonSequentialFieldComponent component, NonSequentialFieldViewKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (wavelengthNumber < 1 || mode is < 0 or > 1 || !Enum.IsDefined(component) || !Enum.IsDefined(kind))
            throw new ArgumentException("复场需要单个正整数波长编号、单个 Jones 模态和有效分量。");
        var count = checked(frame.PixelsX * frame.PixelsY);
        var wave = frame.CoherentByWavelength?.GetValueOrDefault(wavelengthNumber);
        if (coherenceGroup is null && wave?.Modes.Count == 1) coherenceGroup = wave.Modes[0].CoherenceGroup;
        if (coherenceGroup is null) throw new ArgumentException("请选择单个相干组；互不相干的场没有共同相位。");
        var selected = wave?.Modes.SingleOrDefault(m => StringComparer.Ordinal.Equals(m.CoherenceGroup, coherenceGroup));
        var fields = mode == 0 ? selected?.First : selected?.Second;
        if (fields is not null && fields.Count != count) throw new InvalidDataException("复场像素数量不匹配。");
        if (selected?.SummedRayAmplitudes is { } sums && (sums.Count != count || sums.Any(v => !double.IsFinite(v) || v < 0))
            || selected?.HitCounts is { } counts && (counts.Count != count || counts.Any(v => v < 0)))
            throw new InvalidDataException("复场采样计数或误差尺度无效。");
        var values = new double[count]; var valid = new bool[count];
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = fields?[i].Field ?? default;
            var field = component switch { NonSequentialFieldComponent.X => vector.X, NonSequentialFieldComponent.Y => vector.Y, _ => vector.Z };
            var magnitude = field.Magnitude;
            if (!double.IsFinite(magnitude)) throw new InvalidDataException("复场包含非有限值。");
            // Screen the ordinary summation roundoff scale, with a guard for forming contributions.
            // This display mask is not a certification of total OPL/phase error or a physical power cutoff.
            const double epsilon = 2.2204460492503131e-16;
            var n = selected?.HitCounts?[i] ?? 0;
            var gamma = (n + 8d) * epsilon;
            var error = gamma < 1 ? gamma / (1 - gamma) * (selected?.SummedRayAmplitudes?[i] ?? 0) : double.PositiveInfinity;
            valid[i] = kind != NonSequentialFieldViewKind.PhaseDegrees || magnitude > error;
            values[i] = !valid[i] ? double.NaN : kind switch
            {
                NonSequentialFieldViewKind.Real => field.Real, NonSequentialFieldViewKind.Imaginary => field.Imaginary,
                NonSequentialFieldViewKind.Magnitude => magnitude,
                _ => Math.Atan2(field.Imaginary, field.Real) * 180 / Math.PI
            };
        }
        return new(new(wavelengthNumber, coherenceGroup, mode, component, kind), Array.AsReadOnly(values), Array.AsReadOnly(valid),
            kind == NonSequentialFieldViewKind.PhaseDegrees ? "deg" : "sqrt(W)");
    }
}
