using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed partial class SystemPropertiesPanel
{
    private readonly CheckBox _unpolarized = new() { Content = "非偏振" };
    private readonly ComboBox _polarizationAxis = new() { ItemsSource = new[] { "X", "Y", "Z" } };
    private readonly TextBox _jonesX = new();
    private readonly TextBox _jonesY = new();
    private readonly TextBox _jonesXPhase = new();
    private readonly TextBox _jonesYPhase = new();
    private readonly TextBlock _polarizationMessage = new() { TextWrapping = TextWrapping.Wrap, FontSize = DisplayTypography.BodySmall };

    private Control BuildPolarizationSection()
    {
        var apply = CommandButton("check", "应用偏振", 108);
        apply.Classes.Add("accent");
        AutomationProperties.SetName(apply, "应用系统偏振设置");
        foreach (var (control, name) in new (Control, string)[] {
            (_unpolarized, "系统非偏振"), (_polarizationAxis, "偏振参考轴"), (_jonesX, "Jones Jx"),
            (_jonesY, "Jones Jy"), (_jonesXPhase, "Jones X 相位（度）"), (_jonesYPhase, "Jones Y 相位（度）") })
        {
            AutomationProperties.SetName(control, name);
            ToolTip.SetTip(control, name);
        }
        apply.Click += (_, _) =>
        {
            try
            {
                var settings = new PolarizationSettingsDto(_unpolarized.IsChecked == true,
                    Read(_jonesX), Read(_jonesY), Read(_jonesXPhase), Read(_jonesYPhase),
                    _polarizationAxis.SelectedItem as string ?? "X");
                ApplyLocalChange(() => _prescription.UpdatePolarizationSettings(settings));
                _polarizationMessage.Text = "偏振设置已更新。";
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            {
                _polarizationMessage.Text = error.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                _polarizationMessage.UpdateLayout();
                _polarizationMessage.BringIntoView();
            }, DispatcherPriority.Loaded);
        };
        return new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
        {
            _unpolarized,
            Form(("参考轴", _polarizationAxis), ("Jx", _jonesX), ("Jy", _jonesY),
                ("X 相位 / °", _jonesXPhase), ("Y 相位 / °", _jonesYPhase)),
            MutedText("CODA 始终使用所填 Jones 输入；RRET 使用参考轴。照度偏振计算遵循非偏振开关。", DisplayTypography.BodySmall),
            apply, _polarizationMessage
        }
        };

        static double Read(TextBox input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) ? value : throw new FormatException("偏振参数必须为有限数值；小数使用英文句点。");
    }

    private void RefreshPolarization()
    {
        var settings = _prescription.GetPolarizationSettings();
        _unpolarized.IsChecked = settings.Unpolarized;
        _polarizationAxis.SelectedItem = settings.ReferenceAxis;
        _jonesX.Text = settings.Jx.ToString("G17", CultureInfo.InvariantCulture);
        _jonesY.Text = settings.Jy.ToString("G17", CultureInfo.InvariantCulture);
        _jonesXPhase.Text = settings.XPhaseDegrees.ToString("G17", CultureInfo.InvariantCulture);
        _jonesYPhase.Text = settings.YPhaseDegrees.ToString("G17", CultureInfo.InvariantCulture);
        _polarizationMessage.Text = string.Empty;
    }
}
