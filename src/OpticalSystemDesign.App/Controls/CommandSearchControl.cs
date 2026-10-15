using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Compact caption search backed by the same actions as the command palette.</summary>
internal sealed class CommandSearchControl : Grid
{
    private readonly ActionManager _actions;
    internal TextBox SearchBox { get; }
    internal ListBox Results { get; }
    internal Popup ResultsPopup { get; }
    private readonly TextBlock _empty;
    private readonly LocalIcon _icon;
    private bool _executing;
    private bool _suppressNextRefresh;

    internal CommandSearchControl(ActionManager actions)
    {
        _actions = actions;
        Name = "MainWindowCommandSearch";
        MinHeight = 28;
        SearchBox = new TextBox
        {
            Name = "MainWindowSearchBox",
            PlaceholderText = "搜索功能 (Ctrl+Q)",
            FontSize = DisplayTypography.BodySmall,
            MinHeight = 28,
            Padding = new Thickness(28, 3, 7, 3)
        };
        SearchBox.Classes.Add("caption-search");
        AutomationProperties.SetName(SearchBox, "搜索功能");
        AutomationProperties.SetHelpText(SearchBox, "Ctrl+Q 聚焦；输入命令名称、类别或标识；上下选择，Enter 执行，Esc 关闭。");
        ToolTip.SetTip(SearchBox, "搜索功能 (Ctrl+Q)");
        WindowDecorationProperties.SetElementRole(SearchBox, WindowDecorationsElementRole.User);
        Children.Add(SearchBox);
        _icon = new LocalIcon
        {
            IconName = "search",
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
            IsHitTestVisible = false
        };
        Children.Add(_icon);
        Results = new ListBox
        {
            Name = "MainWindowSearchResults",
            MaxHeight = 320,
            ItemTemplate = new FuncDataTemplate<AppAction>((action, _) => new TextBlock
            {
                Text = action is null ? string.Empty : $"{action.Text}   ·   {action.Category}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 4)
            })
        };
        AutomationProperties.SetName(Results, "功能搜索结果");
        _empty = new TextBlock { Text = "没有匹配的功能", Margin = new Thickness(12), IsVisible = false };
        var resultsBorder = new Border
        {
            Width = 360,
            Padding = new Thickness(4),
            Child = new StackPanel { Children = { Results, _empty } }
        };
        resultsBorder.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        resultsBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        ThemeChrome.Apply(resultsBorder, ThemeChromeRole.ControlFrame, shadow: false);
        ResultsPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            IsLightDismissEnabled = true,
            Child = resultsBorder
        };
        Children.Add(ResultsPopup);
        SearchBox.TextChanged += (_, _) =>
        {
            var allowOpen = !_suppressNextRefresh && !_executing;
            _suppressNextRefresh = false;
            Refresh(allowOpen);
        };
        SearchBox.GotFocus += (_, _) => Refresh();
        SearchBox.PointerPressed += (_, _) => Refresh();
        SearchBox.KeyDown += OnSearchKeyDown;
        Results.KeyDown += OnSearchKeyDown;
        Results.Tapped += async (_, args) =>
        {
            if (args.Source is Control control && (control is ListBoxItem || control.FindAncestorOfType<ListBoxItem>() is not null))
                await RunSelectedAsync();
        };
        ActualThemeVariantChanged += (_, _) => UpdateColors();
        DetachedFromVisualTree += (_, _) => ResultsPopup.IsOpen = false;
        UpdateColors();
    }

    internal void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
        Refresh();
    }

    internal void CloseResults() => ResultsPopup.IsOpen = false;

    internal bool TryHandleShortcut(KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Q || !IsEffectivelyVisible
            || !(args.KeyModifiers.HasFlag(KeyModifiers.Control) || args.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            return false;
        FocusSearch();
        args.Handled = true;
        return true;
    }

    private void Refresh(bool allowOpen = true)
    {
        var matches = _actions.Search(SearchBox.Text ?? string.Empty);
        Results.ItemsSource = matches;
        Results.SelectedIndex = matches.Count > 0 ? 0 : -1;
        Results.IsVisible = matches.Count > 0;
        _empty.IsVisible = matches.Count == 0;
        ResultsPopup.IsOpen = allowOpen && SearchBox.IsFocused && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null;
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            args.Handled = true;
            ResultsPopup.IsOpen = false;
        }
        else if (args.Key is Key.Down or Key.Up)
        {
            args.Handled = true;
            ResultsPopup.IsOpen = true;
            var delta = args.Key == Key.Down ? 1 : -1;
            if (Results.ItemCount > 0)
            {
                Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, Results.ItemCount - 1);
                Results.ScrollIntoView(Results.SelectedIndex);
            }
        }
        else if (args.Key == Key.Enter)
        {
            args.Handled = true;
            if (!ResultsPopup.IsOpen)
            {
                Refresh();
                return;
            }
            await RunSelectedAsync();
        }
    }

    internal async Task RunSelectedAsync()
    {
        if (_executing || Results.SelectedItem is not AppAction action) return;
        _executing = true;
        ResultsPopup.IsOpen = false;
        try
        {
            if (await _actions.ExecuteAsync(action) && !string.IsNullOrEmpty(SearchBox.Text))
            {
                _suppressNextRefresh = true;
                SearchBox.Text = string.Empty;
            }
        }
        finally
        {
            _executing = false;
            ResultsPopup.IsOpen = false;
        }
    }

    private void UpdateColors()
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var palette = ThemeRegistry.FromActualVariant(ActualThemeVariant).Palette!;
        SearchBox.Foreground = new SolidColorBrush(light ? BlueThemeTokens.TitleText : palette.TextPrimary);
        SearchBox.Background = new SolidColorBrush(light ? BlueThemeTokens.CaptionSearchBackground : palette.Surface);
        SearchBox.BorderBrush = new SolidColorBrush(light ? BlueThemeTokens.CaptionSearchBorder : palette.Border);
        SearchBox.CaretBrush = SearchBox.Foreground;
        SearchBox.PlaceholderForeground = new SolidColorBrush(light ? BlueThemeTokens.CaptionSearchPlaceholder : palette.TextMuted);
        SearchBox.Resources["TextControlPlaceholderOpacity"] = 1d;
        _icon.Stroke = SearchBox.Foreground;
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            SearchBox.Resources[key] = SearchBox.Foreground;
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            SearchBox.Resources[key] = SearchBox.Background;
        SearchBox.Resources["TextControlPlaceholderForeground"] = SearchBox.PlaceholderForeground;
        SearchBox.Resources["TextControlPlaceholderForegroundFocused"] = SearchBox.Resources["TextControlPlaceholderForeground"];
    }
}
