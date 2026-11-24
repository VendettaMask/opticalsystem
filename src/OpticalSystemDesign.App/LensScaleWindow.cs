using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.App;

public sealed class LensScaleWindow : Window
{
    private readonly IPrescriptionService _prescription;
    private readonly LensScaleSettingsDto _settings;
    private readonly RadioButton _factorMode = new() { Name = "LensScaleFactorMode", Content = "因子缩放", GroupName = "LensScaleMode", IsChecked = true };
    private readonly RadioButton _unitMode = new() { Name = "LensScaleUnitMode", Content = "单位换算", GroupName = "LensScaleMode" };
    private readonly TextBox _factor = new() { Name = "LensScaleFactor", Text = "1", MinWidth = 210, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _first = new() { Name = "LensScaleFirstSurface", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _last = new() { Name = "LensScaleLastSurface", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _targetUnit = new() { Name = "LensScaleTargetUnit", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _error = new() { Name = "LensScaleError", TextWrapping = TextWrapping.Wrap, IsVisible = false };

    public LensScaleWindow(IPrescriptionService prescription)
    {
        _prescription = prescription;
        _settings = prescription.GetLensScaleSettings();
        SettingsPanelChrome.ApplyInputStyles(this);
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Surface);
        this.BindThemeResource(ForegroundProperty, ThemeResourceBindings.TextPrimary);
        Title = "镜头缩放";
        Width = 530; Height = 480; MinWidth = 430; MinHeight = 350;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _first.ItemsSource = _last.ItemsSource = _settings.Surfaces;
        var template = new FuncDataTemplate<LensScaleSurfaceDto>((surface, _) => new TextBlock
        {
            Text = surface is null ? "" : SurfaceLabel(surface),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        _first.ItemTemplate = _last.ItemTemplate = template;
        _first.SelectedIndex = 0; _last.SelectedIndex = _settings.Surfaces.Count - 1;
        _targetUnit.ItemsSource = Enum.GetValues<LensLengthUnit>();
        _targetUnit.ItemTemplate = new FuncDataTemplate<LensLengthUnit>((unit, _) => new TextBlock { Text = UnitLabel(unit) });
        _targetUnit.SelectedItem = _settings.Unit;
        _error.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextError);
        var factorFields = new StackPanel { Spacing = 10, Children = { Row("起始面", _first), Row("终止面", _last) } };
        var confirm = new Button { Name = "LensScaleConfirm", Content = "确定", MinWidth = 100, IsDefault = true };
        var cancel = new Button { Name = "LensScaleCancel", Content = "取消", MinWidth = 100, IsCancel = true };
        confirm.Click += (_, _) => Apply();
        cancel.Click += (_, _) => Close(false);
        _factorMode.IsCheckedChanged += (_, _) => UpdateMode();
        _unitMode.IsCheckedChanged += (_, _) => UpdateMode();
        var hint = new TextBlock
        {
            Name = "LensScaleHint",
            Text = "按因子改变所选表面的尺寸；选择全部表面时，同时缩放系统孔径与高度视场。波长、材料和评价目标保持原值。",
            TextWrapping = TextWrapping.Wrap
        };
        hint.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        var unitsHint = new TextBlock { Text = "单位换算保持实际尺寸不变，镜头数据表使用新单位；分析使用各自标注的单位。", TextWrapping = TextWrapping.Wrap };
        unitsHint.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        var separator = new Border { Height = 1 };
        separator.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border);
        var body = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(22, 18),
                Spacing = 14,
                Children =
                {
                    Pair(_factorMode, _factor), factorFields, hint,
                    separator,
                    Pair(_unitMode, _targetUnit),
                    new TextBlock { Text = $"{UiLocalization.Text("当前单位")}：{UnitLabel(_settings.Unit)}" }, unitsHint,
                    new TextBlock { Text = _settings.ConfigurationCount > 1 ? $"{UiLocalization.Text("应用于全部配置")}（{_settings.ConfigurationCount}）" : "", IsVisible = _settings.ConfigurationCount > 1 },
                    _error
                }
            }
        };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(22, 12, 22, 18),
            Children = { confirm, cancel }
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { body } };
        Grid.SetRow(footer, 1); layout.Children.Add(footer); Content = layout;
        _factorMode.Content = new TextBlock { Text = "因子缩放", TextWrapping = TextWrapping.Wrap };
        _unitMode.Content = new TextBlock { Text = "单位换算", TextWrapping = TextWrapping.Wrap };
        UpdateMode();
        UiLocalization.Attach(this);
        DisplayTypography.Apply(this);
    }

    private string SurfaceLabel(LensScaleSurfaceDto surface) => surface.Number == _settings.Surfaces[0].Number
        ? $"{surface.Number} · {UiLocalization.Text("物面")}"
        : surface.Number == _settings.Surfaces[^1].Number ? $"{surface.Number} · {UiLocalization.Text("像面")}"
        : $"{surface.Number} · {surface.Label}";

    private static string UnitLabel(LensLengthUnit unit) => $"{UiLocalization.Text(unit switch
    {
        LensLengthUnit.Millimeter => "毫米",
        LensLengthUnit.Centimeter => "厘米",
        LensLengthUnit.Meter => "米",
        LensLengthUnit.Inch => "英寸",
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    })} ({unit.Symbol()})";

    private void UpdateMode()
    {
        var factor = _factorMode.IsChecked == true;
        _factor.IsEnabled = _first.IsEnabled = _last.IsEnabled = factor;
        _targetUnit.IsEnabled = !factor;
        _error.IsVisible = false;
    }

    private void Apply()
    {
        try
        {
            if (_first.SelectedItem is not LensScaleSurfaceDto first || _last.SelectedItem is not LensScaleSurfaceDto last)
                throw new ArgumentException("请选择起始面和终止面。");
            LensScaleRequestDto request;
            if (_factorMode.IsChecked == true)
            {
                if (!(double.TryParse(_factor.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var factor)
                    || double.TryParse(_factor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out factor)) || !double.IsFinite(factor) || factor <= 0)
                    throw new ArgumentException("缩放因子必须是大于零的有限数值。");
                request = new(factor, first.Number, last.Number);
            }
            else request = new(1, first.Number, last.Number, (LensLengthUnit)_targetUnit.SelectedItem!);
            _prescription.ScaleLens(request, _settings.Revision);
            Close(true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _error.Text = UiLocalization.Text(exception.Message); _error.IsVisible = true;
        }
    }

    private static Grid Row(string label, Control value) => Pair(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(25, 0, 0, 0) }, value);
    private static Grid Pair(Control label, Control value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 12 };
        grid.Children.Add(label); Grid.SetColumn(value, 1); grid.Children.Add(value); return grid;
    }
}
