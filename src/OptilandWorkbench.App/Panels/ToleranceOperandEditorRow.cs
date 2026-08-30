using System.ComponentModel;
using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

public sealed class ToleranceOperandEditorRow : INotifyPropertyChanged
{
    private int _index;
    private bool _enabled;
    private ToleranceOperandKind _kind;
    private int _surfaceNumber;
    private double _minimum;
    private double _maximum;
    private ToleranceDistribution _distribution;
    private string _comment = string.Empty;
    private int _endSurfaceNumber;
    private int _parameterIndex;

    public ToleranceOperandEditorRow(ToleranceOperandDto source)
    {
        Index = source.Index;
        Enabled = source.Enabled;
        Kind = source.Kind;
        SurfaceNumber = source.SurfaceNumber;
        Minimum = source.Minimum;
        Maximum = source.Maximum;
        Distribution = source.Distribution;
        Comment = source.Comment;
        EndSurfaceNumber = source.EndSurfaceNumber;
        ParameterIndex = source.ParameterIndex;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index
    {
        get => _index;
        set => SetField(ref _index, value, nameof(Index));
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value, nameof(Enabled));
    }

    public ToleranceOperandKind Kind
    {
        get => _kind;
        set
        {
            if (SetField(ref _kind, value, nameof(Kind)))
            {
                OnPropertyChanged(nameof(Code));
            }
        }
    }

    public string Code
    {
        get => CodeFor(Kind);
        set
        {
            if (TryParseCode(value, out var kind))
            {
                Kind = kind;
                return;
            }

            OnPropertyChanged(nameof(Code));
        }
    }

    public int SurfaceNumber
    {
        get => _surfaceNumber;
        set => SetField(ref _surfaceNumber, value, nameof(SurfaceNumber));
    }

    public int EndSurfaceNumber
    {
        get => _endSurfaceNumber;
        set => SetField(ref _endSurfaceNumber, value, nameof(EndSurfaceNumber));
    }

    public int ParameterIndex
    {
        get => _parameterIndex;
        set => SetField(ref _parameterIndex, value, nameof(ParameterIndex));
    }

    public double Minimum
    {
        get => _minimum;
        set => SetField(ref _minimum, value, nameof(Minimum));
    }

    public double Maximum
    {
        get => _maximum;
        set => SetField(ref _maximum, value, nameof(Maximum));
    }

    public ToleranceDistribution Distribution
    {
        get => _distribution;
        set
        {
            if (SetField(ref _distribution, value, nameof(Distribution)))
            {
                OnPropertyChanged(nameof(DistributionText));
            }
        }
    }

    public string DistributionText
    {
        get => Distribution == ToleranceDistribution.Normal ? "正态" : "均匀";
        set
        {
            if (TryParseDistribution(value, out var distribution))
            {
                Distribution = distribution;
                return;
            }

            OnPropertyChanged(nameof(DistributionText));
        }
    }

    public string Comment
    {
        get => _comment;
        set => SetField(ref _comment, value ?? string.Empty, nameof(Comment));
    }

    public ToleranceOperandDto ToDto() => new(
        Index,
        Enabled,
        Kind,
        SurfaceNumber,
        Minimum,
        Maximum,
        Distribution,
        Comment,
        EndSurfaceNumber,
        ParameterIndex);

    public static string CodeFor(ToleranceOperandKind kind) => kind switch
    {
        ToleranceOperandKind.Radius => "TRAD",
        ToleranceOperandKind.Thickness => "TTHI",
        ToleranceOperandKind.Conic => "TCON",
        ToleranceOperandKind.DecenterX => "TSDX",
        ToleranceOperandKind.DecenterY => "TSDY",
        ToleranceOperandKind.TiltX => "TSTX",
        ToleranceOperandKind.TiltY => "TSTY",
        ToleranceOperandKind.ElementDecenterX => "TEDX",
        ToleranceOperandKind.ElementDecenterY => "TEDY",
        ToleranceOperandKind.ElementTiltX => "TETX",
        ToleranceOperandKind.ElementTiltY => "TETY",
        ToleranceOperandKind.AsphereCoefficient => "TPAR",
        ToleranceOperandKind.RefractiveIndex => "TIND",
        ToleranceOperandKind.AbbeNumber => "TABB",
        ToleranceOperandKind.Compensator => "COMP",
        _ => kind.ToString().ToUpperInvariant()
    };

    private static bool TryParseCode(string? text, out ToleranceOperandKind kind)
    {
        kind = (text ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "TRAD" => ToleranceOperandKind.Radius,
            "TTHI" => ToleranceOperandKind.Thickness,
            "TCON" => ToleranceOperandKind.Conic,
            "TSDX" => ToleranceOperandKind.DecenterX,
            "TSDY" => ToleranceOperandKind.DecenterY,
            "TSTX" => ToleranceOperandKind.TiltX,
            "TSTY" => ToleranceOperandKind.TiltY,
            "TEDX" => ToleranceOperandKind.ElementDecenterX,
            "TEDY" => ToleranceOperandKind.ElementDecenterY,
            "TETX" => ToleranceOperandKind.ElementTiltX,
            "TETY" => ToleranceOperandKind.ElementTiltY,
            "TPAR" => ToleranceOperandKind.AsphereCoefficient,
            "TIND" => ToleranceOperandKind.RefractiveIndex,
            "TABB" => ToleranceOperandKind.AbbeNumber,
            "COMP" => ToleranceOperandKind.Compensator,
            _ => default
        };
        return CodeFor(kind).Equals(text?.Trim(), StringComparison.OrdinalIgnoreCase)
            || Enum.TryParse(text, ignoreCase: true, out kind);
    }

    private static bool TryParseDistribution(string? text, out ToleranceDistribution distribution)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Contains("均", StringComparison.Ordinal)
            || normalized.Equals("uniform", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("u", StringComparison.OrdinalIgnoreCase))
        {
            distribution = ToleranceDistribution.Uniform;
            return true;
        }

        if (normalized.Contains("正", StringComparison.Ordinal)
            || normalized.Equals("normal", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("gaussian", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            distribution = ToleranceDistribution.Normal;
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out distribution);
    }

    private bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
