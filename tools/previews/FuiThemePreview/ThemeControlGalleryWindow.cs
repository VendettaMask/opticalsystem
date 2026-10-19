using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.FuiThemePreview;

/// <summary>Live product-styled controls, with no file or settings commands.</summary>
internal sealed class ThemeControlGalleryWindow : Window
{
    internal ThemeControlGalleryWindow()
    {
        Title = "光域 · 控件与对话框";
        Width = 700; Height = 650; MinWidth = 540; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Surface);
        var root = new StackPanel { Margin = new Thickness(28), Spacing = 18 };
        root.Children.Add(new TextBlock { Text = "光域 / LUMEN", FontSize = 23, FontWeight = FontWeight.Light });
        root.Children.Add(new TextBlock { Text = "原生控件与交互状态 · 仅用于外观预览，不保存设置", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new Separator());
        root.Children.Add(new TextBlock { Text = "输入与选择" });
        root.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,16,*"),
            Children =
            {
                new TextBox { Text = "25.460", PlaceholderText = "数值输入" },
                InColumn(new ComboBox { ItemsSource = new[] { "N-BK7", "N-SF6", "熔融石英" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }, 2)
            }
        });
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24,
            Children = { new CheckBox { Content = "显示光线", IsChecked = true }, new CheckBox { Content = "辅助标注" }, new RadioButton { Content = "自动适配", IsChecked = true } }
        });
        root.Children.Add(new ListBox { ItemsSource = new[] { "当前方案 · 已选中", "候选方案", "参考方案" }, SelectedIndex = 0, Height = 130 });
        root.Children.Add(new TextBlock { Text = "焦点、悬停与禁用状态" });
        var disabled = new Button { Content = "在线安装（预留）", IsEnabled = false };
        ToolTip.SetTip(disabled, "此窗口仅演示禁用外观，不提供安装操作。");
        AutomationProperties.SetHelpText(disabled, "此窗口仅演示禁用外观，不提供安装操作。");
        root.Children.Add(disabled);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var reset = new Button { Content = "重置示例" };
        var close = new Button { Content = "关闭" }; close.Click += (_, _) => Close();
        var apply = new Button { Content = "执行预览" }; apply.Classes.Add("accent");
        var result = new TextBlock { Text = "未执行实际光学计算", TextWrapping = TextWrapping.Wrap };
        apply.Click += (_, _) => result.Text = "点击状态正常 · 未执行计算、写文件或保存设置";
        reset.Click += (_, _) => result.Text = "未执行实际光学计算";
        actions.Children.Add(reset); actions.Children.Add(close); actions.Children.Add(apply);
        root.Children.Add(result); root.Children.Add(actions);
        Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        DisplayTypography.Apply(this);
    }
    private static T InColumn<T>(T control, int column) where T : Control { Grid.SetColumn(control, column); return control; }
}
