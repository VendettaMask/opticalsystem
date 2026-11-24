using Avalonia.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class LensEditorPanel
{
    private readonly StackPanel _crystalProperties = new() { Name = "SurfaceCrystalProperties", Spacing = 6 };
    private readonly TextBlock _crystalMode = new() { Name = "SurfaceCrystalMode" };
    private readonly TextBlock _crystalAxis = new() { Name = "SurfaceCrystalAxis" };
    private readonly TextBlock _crystalAxisLength = new() { Name = "SurfaceCrystalAxisLength" };

    private void BuildCrystalProperties()
    {
        _crystalProperties.Children.Add(PropertyRow("追迹模式", _crystalMode));
        _crystalProperties.Children.Add(PropertyRow("晶轴方向", _crystalAxis));
        _crystalProperties.Children.Add(PropertyRow("晶轴显示长度", _crystalAxisLength));
        _crystalProperties.Children.Add(PropertyNote("特殊面型参数按导入值保留；此处只读。"));
        _crystalAxis.BindThemeResource(TextBlock.FontFamilyProperty, ProductFonts.NumericResource);
        _crystalAxisLength.BindThemeResource(TextBlock.FontFamilyProperty, ProductFonts.NumericResource);
    }

    private void LoadCrystalProperties(SurfaceEditorRow row)
    {
        _crystalProperties.IsVisible = row.Birefringent is { IsExit: false };
        if (row.Birefringent is not { IsExit: false } model) return;
        _crystalMode.Text = model.TraceExtraordinary == true ? "非寻常光" : "寻常光";
        _crystalAxis.Text = $"{NumericDisplayFormatter.Format(model.AxisX ?? 0)}, {NumericDisplayFormatter.Format(model.AxisY ?? 0)}, {NumericDisplayFormatter.Format(model.AxisZ ?? 0)}";
        _crystalAxisLength.Text = NumericDisplayFormatter.Format(model.AxisDisplayLength / row.LensLengthUnit.Millimeters()) + " " + row.LengthUnitSymbol;
    }
}
