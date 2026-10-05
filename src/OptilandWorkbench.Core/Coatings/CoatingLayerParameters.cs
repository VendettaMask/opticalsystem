namespace OptilandWorkbench.Core.Coatings;

public enum CoatingLayerParameter { Multiplier, IndexOffset, ExtinctionOffset }

/// <summary>Per-surface adjustments; base film thickness and material dispersion stay unchanged.</summary>
public sealed record CoatingLayerParameters(
    double Multiplier = 1, double IndexOffset = 0, double ExtinctionOffset = 0,
    bool MultiplierVariable = false, bool IndexVariable = false, bool ExtinctionVariable = false)
{
    public void Validate()
    {
        if (!double.IsFinite(Multiplier) || Multiplier < 0 || Multiplier > 10)
            throw new ArgumentException("膜层厚度倍率必须在 0 到 10 之间。");
        if (!double.IsFinite(IndexOffset) || !double.IsFinite(ExtinctionOffset))
            throw new ArgumentException("膜层折射率及消光系数偏移必须是有限数值。");
    }

    public double Value(CoatingLayerParameter kind) => kind switch
    {
        CoatingLayerParameter.Multiplier => Multiplier,
        CoatingLayerParameter.IndexOffset => IndexOffset,
        CoatingLayerParameter.ExtinctionOffset => ExtinctionOffset,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public bool IsVariable(CoatingLayerParameter kind) => kind switch
    {
        CoatingLayerParameter.Multiplier => MultiplierVariable,
        CoatingLayerParameter.IndexOffset => IndexVariable,
        CoatingLayerParameter.ExtinctionOffset => ExtinctionVariable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public CoatingLayerParameters WithValue(CoatingLayerParameter kind, double value) => kind switch
    {
        CoatingLayerParameter.Multiplier => this with { Multiplier = value },
        CoatingLayerParameter.IndexOffset => this with { IndexOffset = value },
        CoatingLayerParameter.ExtinctionOffset => this with { ExtinctionOffset = value },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
