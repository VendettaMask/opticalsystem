namespace OptilandWorkbench.Core.Analysis;

internal static class MtfDataTypeSupport
{
    public static FftMtfDataType Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "real" or "实部" => FftMtfDataType.Real,
            "imaginary" or "虚部" => FftMtfDataType.Imaginary,
            "phase" or "相位" => FftMtfDataType.Phase,
            "squarewave" or "square wave" or "方波" => FftMtfDataType.SquareWave,
            _ => FftMtfDataType.Modulation
        };
    }

    public static string Name(FftMtfDataType type)
    {
        return type switch
        {
            FftMtfDataType.Real => "Real",
            FftMtfDataType.Imaginary => "Imaginary",
            FftMtfDataType.Phase => "Phase",
            FftMtfDataType.SquareWave => "SquareWave",
            _ => "Modulation"
        };
    }

    public static string Label(FftMtfDataType type, string modulationLabel)
    {
        return type switch
        {
            FftMtfDataType.Real => "Real MTF",
            FftMtfDataType.Imaginary => "Imaginary MTF",
            FftMtfDataType.Phase => "Phase (radians)",
            FftMtfDataType.SquareWave => "Square Wave MTF",
            _ => modulationLabel
        };
    }
}
