using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Core.Tolerancing;

public enum ToleranceMtfDirection { Average, Tangential, Sagittal, Minimum }

/// <summary>Manufacturing measurements delegate to the shared formal MTF service.</summary>
public sealed class MtfToleranceMetric
{
    private readonly Optic _optic;
    private readonly MtfMetricKind _method;
    private readonly ToleranceMtfDirection _direction;
    private readonly int _sampling;
    private readonly int _wave;
    private readonly double _frequency;

    public MtfToleranceMetric(Optic optic, MtfMetricKind method, ToleranceMtfDirection direction,
        double frequency, int sampling = 1, int wave = 0)
    {
        ArgumentNullException.ThrowIfNull(optic);
        if (optic.ImageSpaceAfocal) throw new NotSupportedException("MTF 公差当前使用 cycles/mm，尚不支持无焦像空间。");
        if (method is not (MtfMetricKind.Geometric or MtfMetricKind.Fourier)) throw new ArgumentOutOfRangeException(nameof(method));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!double.IsFinite(frequency) || frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (sampling is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(sampling));
        if (wave < 0 || wave > optic.Wavelengths.Count || optic.Wavelengths.Count == 0) throw new ArgumentOutOfRangeException(nameof(wave));
        if (optic.Fields.Count == 0 || !optic.Fields.Any(field => field.Weight > 0)) throw new ArgumentException("MTF 公差需要至少一个正权重视场。");
        _optic = optic; _method = method; _direction = direction;
        _frequency = frequency; _sampling = sampling; _wave = wave;
    }

    public IReadOnlyList<ToleranceFieldValue> EvaluateFields() =>
        Enumerable.Range(1, _optic.Fields.Count).Select(EvaluateField).ToArray();

    public double Evaluate()
    {
        var fields = EvaluateFields();
        // A failed field must never disappear through weight renormalization.
        if (fields.Any(field => !double.IsFinite(field.Value))) return double.NegativeInfinity;
        var weight = _optic.Fields.Sum(field => Math.Max(0, field.Weight));
        return fields.Sum(field => field.Value * Math.Max(0, _optic.Fields[field.FieldNumber - 1].Weight)) / weight;
    }

    public void Configure(Tolerancing tolerancing, bool includeZeroWeightFields = false)
    {
        tolerancing.HigherIsBetter = true;
        tolerancing.SetCriterionEvaluator(Evaluate);
        tolerancing.SetFieldEvaluator(EvaluateFields);
        for (var field = 1; field <= _optic.Fields.Count; field++)
        {
            var number = field;
            var weight = Math.Max(0, _optic.Fields[number - 1].Weight);
            if (includeZeroWeightFields && weight == 0) weight = 1;
            if (weight > 0) tolerancing.AddOperand(new Operand($"MTF field {number}", 1, weight,
                () => { var value = EvaluateField(number).Value; return double.IsFinite(value) ? value : 0; }));
        }
    }

    private ToleranceFieldValue EvaluateField(int field)
    {
        try
        {
            var value = MtfMetrics.EvaluateGrid(_optic, _method, _sampling, _wave, field, _frequency, requireValidData: true);
            var selected = _direction switch
            {
                ToleranceMtfDirection.Tangential => value.Tangential,
                ToleranceMtfDirection.Sagittal => value.Sagittal,
                ToleranceMtfDirection.Minimum => Math.Min(value.Tangential, value.Sagittal),
                _ => (value.Tangential + value.Sagittal) / 2
            };
            if (!double.IsFinite(selected) || selected < 0 || selected > 1 + 1e-9)
                throw new InvalidOperationException("MTF 不是有效的 0..1 调制度。");
            return new(field, Math.Min(1, selected));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
            or ArithmeticException or KeyNotFoundException or NotSupportedException)
        {
            return new(field, double.NegativeInfinity, exception.Message);
        }
    }
}
