using System.Globalization;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;

namespace OptilandWorkbench.App.ViewModels;

public sealed class SurfaceEditorRow
{
    public SurfaceEditorRow(SurfaceRowDto source, bool isLastSurface = false)
    {
        Number = source.Number;
        Label = source.Label;
        Radius = source.Radius;
        Thickness = source.Thickness;
        Material = source.Material;
        Coating = source.Coating;
        SemiDiameter = source.SemiDiameter;
        SemiDiameterFixed = source.SemiDiameterFixed;
        Conic = source.Conic;
        IsStop = source.IsStop;
        GeometryKind = source.GeometryKind;
        CoatingKind = source.CoatingKind;
        InteractionKind = source.InteractionKind;
        ApertureKind = source.ApertureKind;
        GratingOrder = source.GratingOrder;
        GratingPeriodMicrometers = source.GratingPeriodMicrometers;
        GrooveOrientationAngleDegrees = source.GrooveOrientationAngleDegrees;
        ThinLensFocalLength = source.ThinLensFocalLength;
        RadiusVariable = source.RadiusVariable;
        ThicknessVariable = source.ThicknessVariable;
        SurfaceRole = Number == 0
            ? "物面"
            : isLastSurface
                ? "像面"
                : IsStop
                    ? "光阑"
                    : "(孔径)";
        SurfaceType = GeometryKind is "平面" or "标准球面/圆锥" ? "标准面" : GeometryKind;
        MechanicalSemiDiameter = SemiDiameter;
        CanOptimize = Number > 0 && !isLastSurface;
        IsLastSurface = isLastSurface;
    }

    public int Number { get; }
    public string Label { get; set; }
    public double Radius { get; set; }
    public double Thickness { get; set; }
    public bool IsLastSurface { get; }
    public string RadiusDisplay
    {
        get => !double.IsFinite(Radius) || Math.Abs(Radius) <= 1e-15
            ? "无限"
            : NumericDisplayFormatter.Format(Radius);
        set
        {
            var text = value?.Trim() ?? string.Empty;
            if (text.Length == 0
                || text.Equals("无限", StringComparison.OrdinalIgnoreCase)
                || text.Equals("∞", StringComparison.OrdinalIgnoreCase)
                || text.Equals("inf", StringComparison.OrdinalIgnoreCase)
                || text.Equals("infinity", StringComparison.OrdinalIgnoreCase))
            {
                Radius = 0;
                return;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
            {
                Radius = Math.Abs(current) <= 1e-15 ? 0 : current;
            }
        }
    }

    public string ThicknessDisplay
    {
        get => IsLastSurface
            ? "-"
            : NumericDisplayFormatter.Format(Thickness);
        set
        {
            if (IsLastSurface)
            {
                return;
            }

            var text = value?.Trim() ?? string.Empty;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
            {
                Thickness = current;
            }
        }
    }
    public string Material { get; set; }
    public string MaterialDisplay
    {
        get => HasOpticalMaterial ? Material : string.Empty;
        set => Material = string.IsNullOrWhiteSpace(value) ? "Air" : value.Trim();
    }

    public bool HasOpticalMaterial =>
        !string.IsNullOrWhiteSpace(Material)
        && !string.Equals(Material, "Air", StringComparison.OrdinalIgnoreCase);
    public string Coating { get; set; }
    public double SemiDiameter { get; set; }
    public bool SemiDiameterFixed { get; set; }
    public string SemiDiameterDisplay
    {
        get => NumericDisplayFormatter.Format(SemiDiameter);
        set
        {
            var text = value?.Trim() ?? string.Empty;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
            {
                SemiDiameter = Math.Max(0.1, current);
            }
        }
    }
    public double Conic { get; set; }
    public bool IsStop { get; set; }
    public string GeometryKind { get; set; }
    public string CoatingKind { get; set; }
    public string InteractionKind { get; set; }
    public string ApertureKind { get; set; }
    public int GratingOrder { get; set; }
    public double GratingPeriodMicrometers { get; set; }
    public double GrooveOrientationAngleDegrees { get; set; }
    public double ThinLensFocalLength { get; set; }
    public bool RadiusVariable { get; set; }
    public bool ThicknessVariable { get; set; }
    public bool CanOptimize { get; }
    public string SurfaceRole { get; }
    public string SurfaceType { get; }
    public double ExtensionZone { get; } = 0;
    public double MechanicalSemiDiameter { get; set; }
    public string ThermalExpansionDisplay => string.Equals(Material, "Air", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(Material)
            ? "0.000"
            : "-";

    public SurfaceRowDto ToDto() => new(
        Number,
        Label,
        Radius,
        Thickness,
        Material,
        Coating,
        SemiDiameter,
        Conic,
        IsStop,
        GeometryKind,
        CoatingKind,
        InteractionKind,
        ApertureKind,
        GratingOrder,
        GratingPeriodMicrometers,
        GrooveOrientationAngleDegrees,
        ThinLensFocalLength,
        RadiusVariable,
        ThicknessVariable,
        SemiDiameterFixed);

    public override string ToString() => $"{Number}: {Label}";
}

public sealed class MeritOperandEditorRow
{
    public MeritOperandEditorRow(MeritOperandRowDto source)
    {
        Index = source.Index;
        Enabled = source.Enabled;
        Type = source.Type;
        Surface = source.Surface;
        Field = source.Field;
        Wavelength = source.Wavelength;
        Hx = source.Hx;
        Hy = source.Hy;
        Px = source.Px;
        Py = source.Py;
        Target = source.Target;
        Weight = source.Weight;
        Value = source.Value;
        Contribution = source.Contribution;
        Comment = source.Comment;
        Error = source.Error;
        PupilRings = source.PupilRings;
        PupilArms = source.PupilArms;
        PupilObscuration = source.PupilObscuration;
        PupilSampling = source.PupilSampling;
        SpatialFrequency = source.SpatialFrequency;
        IgnoreLateralColor = source.IgnoreLateralColor;
        PolychromaticReference = source.PolychromaticReference;
    }

    public int Index { get; set; }
    public bool Enabled { get; set; }
    public string Type { get; set; }
    public int Surface { get; set; }
    public int Field { get; set; }
    public int Wavelength { get; set; }
    public double Hx { get; set; }
    public double Hy { get; set; }
    public double Px { get; set; }
    public double Py { get; set; }
    public double Target { get; set; }
    public double Weight { get; set; }
    public double Value { get; set; }
    public double Contribution { get; set; }
    public string Comment { get; set; }
    public string Error { get; set; }
    public int PupilRings { get; set; }
    public int PupilArms { get; set; }
    public double PupilObscuration { get; set; }
    public string PupilSampling { get; set; }
    public double SpatialFrequency { get; set; }
    public bool IgnoreLateralColor { get; set; }
    public bool PolychromaticReference { get; set; }

    public bool IsDirective => Type.Equals("DMFS", StringComparison.OrdinalIgnoreCase);

    public bool IsComment => Type.Equals("BLNK", StringComparison.OrdinalIgnoreCase);

    public bool IsBlank => IsDirective || IsComment;

    public string ValueDisplay => double.IsFinite(Value) ? NumericDisplayFormatter.Format(Value) : "-";

    public string ContributionDisplay => double.IsFinite(Contribution) ? NumericDisplayFormatter.Format(Contribution) : "-";

    public MeritOperandRowDto ToDto() => new(
        Index,
        Enabled,
        Type,
        Surface,
        Field,
        Wavelength,
        Hx,
        Hy,
        Px,
        Py,
        Target,
        Weight,
        Value,
        Contribution,
        Comment,
        Error,
        PupilRings,
        PupilArms,
        PupilObscuration,
        PupilSampling,
        SpatialFrequency,
        IgnoreLateralColor,
        PolychromaticReference);
}

public sealed class FieldEditorRow
{
    public FieldEditorRow(FieldRowDto source)
    {
        Index = source.Index;
        Label = source.Label;
        X = source.X;
        Y = source.Y;
        VignetteFactorX = source.VignetteFactorX;
        VignetteFactorY = source.VignetteFactorY;
        Weight = source.Weight;
    }

    public int Index { get; }
    public string Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double VignetteFactorX { get; set; }
    public double VignetteFactorY { get; set; }
    public double Weight { get; set; }

    public FieldRowDto ToDto() => new(Index, Label, X, Y, VignetteFactorX, VignetteFactorY, Weight);
}

public sealed class WavelengthEditorRow
{
    public WavelengthEditorRow(WavelengthRowDto source)
    {
        Index = source.Index;
        Label = source.Label;
        Nanometers = source.Nanometers;
        Weight = source.Weight;
        IsPrimary = source.IsPrimary;
    }

    public int Index { get; }
    public string Label { get; set; }
    public double Nanometers { get; set; }
    public double Weight { get; set; }
    public bool IsPrimary { get; set; }

    public WavelengthRowDto ToDto() => new(Index, Label, Nanometers, Weight, IsPrimary);
}
