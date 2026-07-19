using OptilandWorkbench.Application.Contracts;

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
        SurfaceRole = Number == 0
            ? "物面"
            : isLastSurface
                ? "像面"
                : IsStop
                    ? "光阑"
                    : "(孔径)";
        SurfaceType = GeometryKind is "平面" or "标准球面/圆锥" ? "标准面" : GeometryKind;
        MechanicalSemiDiameter = SemiDiameter;
    }

    public int Number { get; }
    public string Label { get; set; }
    public double Radius { get; set; }
    public double Thickness { get; set; }
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
        ThinLensFocalLength);

    public override string ToString() => $"{Number}: {Label}";
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
