using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class LensEditorPanel
{
    private readonly StackPanel _polynomialProperties = new() { Name = "SurfacePolynomialProperties", Spacing = 6 };
    private readonly StackPanel _biconicProperties = new() { Name = "SurfaceBiconicProperties", Spacing = 6 };
    private readonly TextBox _polynomialRadius = new() { Name = "PolynomialNormalizationRadius" };
    private readonly NumericUpDown _polynomialCount = new() { Name = "PolynomialTermCount", Minimum = 0, Maximum = 240, Increment = 1, FormatString = "0" };
    private readonly TextBox _polynomialCoefficients = new()
    {
        Name = "PolynomialCoefficients",
        AcceptsReturn = true,
        Height = 90,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };
    private readonly TextBox _biconicRadiusX = new() { Name = "BiconicRadiusX" };
    private readonly TextBox _biconicConicX = new() { Name = "BiconicConicX" };

    private void BuildPolynomialProperties()
    {
        _polynomialProperties.Children.Add(PropertyRow("归一化半径 (mm)", _polynomialRadius));
        _polynomialProperties.Children.Add(PropertyRow("有效项数", _polynomialCount));
        _polynomialProperties.Children.Add(PropertyRow("系数 (mm)", _polynomialCoefficients));
        _polynomialProperties.Children.Add(PropertyNote("按 A1、A2…顺序输入，以空格或换行分隔。有效项数之后的系数保留。"));
        _biconicProperties.Children.Add(PropertyRow("X 半径 (mm)", _biconicRadiusX));
        _biconicProperties.Children.Add(PropertyRow("X 圆锥系数", _biconicConicX));
        _biconicProperties.Children.Add(PropertyNote("Y 半径和 Y 圆锥系数在镜头数据表中编辑。"));
        foreach (var input in new[] { _polynomialRadius, _polynomialCoefficients, _biconicRadiusX, _biconicConicX })
            input.BindThemeResource(TextBox.FontFamilyProperty, ProductFonts.NumericResource);
    }

    private void LoadPolynomialProperties(SurfaceEditorRow row)
    {
        var numbers = row.GeometryDefinition?.Numbers;
        _polynomialRadius.Text = numbers?.GetValueOrDefault("normalizationRadius", Math.Max(1, row.SemiDiameter))
            .ToString("R", CultureInfo.InvariantCulture) ?? "1";
        var count = (int)(numbers?.GetValueOrDefault("termCount") ?? 0);
        _polynomialCount.Value = count;
        var storedCount = (int)(numbers?.GetValueOrDefault("coefficientCount") ?? 0);
        _polynomialCoefficients.Text = string.Join(" ", Enumerable.Range(0, storedCount)
            .Select(index => numbers![$"c{index}"].ToString("R", CultureInfo.InvariantCulture)));
        var radiusX = numbers?.GetValueOrDefault("radiusX", row.Radius) ?? row.Radius;
        _biconicRadiusX.Text = double.IsInfinity(radiusX) || radiusX == 0 ? "∞" : radiusX.ToString("R", CultureInfo.InvariantCulture);
        _biconicConicX.Text = (numbers?.GetValueOrDefault("conicX", row.Conic) ?? row.Conic)
            .ToString("R", CultureInfo.InvariantCulture);
    }

    private NormalizedPolynomialUpdateDto? PolynomialUpdate()
    {
        if (!_polynomialProperties.IsVisible) return null;
        var coefficients = (_polynomialCoefficients.Text ?? "").Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(value => PropertyNumber(value)).ToArray();
        var count = _polynomialCount.Value ?? 0;
        if (count != decimal.Truncate(count)) throw new ArgumentException("有效项数须为整数。");
        return new NormalizedPolynomialUpdateDto(PropertyNumber(_polynomialRadius.Text), (int)count, coefficients);
    }

    private static double PropertyNumber(string? text, bool allowInfinity = false)
    {
        text = text?.Trim() ?? "";
        if (allowInfinity && text is "∞" or "inf" or "Infinity") return double.PositiveInfinity;
        if ((double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            && double.IsFinite(value))
            return value;
        throw new ArgumentException("面型参数请输入有效数值。");
    }
}
