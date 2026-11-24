using Avalonia.Controls;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class LensEditorPanel
{
    private readonly StackPanel _paraxialLensProperties = new() { Name = "SurfaceParaxialProperties", Spacing = 6 };
    private readonly TextBlock _paraxialLensSummary = PropertyNote("");

    private void BuildParaxialLensProperties()
    {
        _paraxialLensSummary.Name = "SurfaceParaxialSummary";
        _paraxialLensProperties.Children.Add(_paraxialLensSummary);
        _paraxialLensProperties.Children.Add(PropertyNote("近轴参数按导入值保留；此处只读。"));
    }

    private void LoadParaxialLensProperties(SurfaceEditorRow row)
    {
        if (row.ParaxialLens is not { } model) { _paraxialLensSummary.Text = ""; return; }
        string Focal(double power) => NumericDisplayFormatter.Format(power == 0
            ? double.PositiveInfinity : 1 / power / row.LensLengthUnit.Millimeters());
        _paraxialLensSummary.Text = model.PowerXPerMillimeter == model.PowerYPerMillimeter
            ? $"焦距：{Focal(model.PowerYPerMillimeter)} {row.LengthUnitSymbol}\nOPD 模式：{model.OpdMode}"
            : $"X 焦距：{Focal(model.PowerXPerMillimeter)} {row.LengthUnitSymbol}\nY 焦距：{Focal(model.PowerYPerMillimeter)} {row.LengthUnitSymbol}\nOPD 模式：{model.OpdMode}";
    }
}
