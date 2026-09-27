using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class BlueThemeInteractionTests
{
    [Fact]
    public Task PrimaryAndOrdinaryButtonsRestoreHoverAndDoNotLatchPressed() => Run(window =>
    {
        var ordinary = new Button { Content = "普通操作" };
        var primary = new Button { Content = "应用" };
        primary.Classes.Add("accent");
        var count = 0;
        ordinary.Click += (_, _) => count++;
        primary.Click += (_, _) => count++;
        Host(window, ordinary, primary);
        foreach (var (button, normal, hover, pressed) in new[]
        {
            (ordinary, "#FFFFFF", "#EAF1FF", "#CDDDFA"),
            (primary, "#2F65CC", "#2554AF", "#1D448F")
        })
        {
            Away(window); Equal(normal, Presenter(button).Background);
            Move(window, button); Equal(hover, Presenter(button).Background);
            window.MouseDown(Point(button, window), MouseButton.Left); Tick();
            Equal(pressed, Presenter(button).Background);
            window.MouseUp(Point(button, window), MouseButton.Left); Tick();
            Equal(hover, Presenter(button).Background);
            Away(window); Equal(normal, Presenter(button).Background);
            button.Focus(NavigationMethod.Tab); Tick();
            Assert.NotNull(button.FocusAdorner);
            button.IsEnabled = false;
            Move(window, button); Equal("#F0F3F8", Presenter(button).Background);
            Equal("#9BA9BF", Presenter(button).Foreground);
            Click(window, button);
        }
        Assert.Equal(2, count);
    });

    [Fact]
    public Task ToggleAndCheckBoxKeepSelectionButDisabledWins() => Run(window =>
    {
        var toggle = new ToggleButton { Content = "切换" };
        var check = new CheckBox { Content = "已勾选", IsChecked = true };
        Host(window, toggle, check);
        Click(window, toggle);
        Assert.True(toggle.IsChecked);
        Equal("#D5E3FC", Presenter(toggle).Background);
        window.MouseDown(Point(toggle, window), MouseButton.Left); Tick();
        Equal("#B9CFF5", Presenter(toggle).Background);
        window.MouseUp(Point(toggle, window), MouseButton.Left); Tick();
        Assert.False(toggle.IsChecked);
        toggle.IsChecked = true; Away(window);
        Equal("#DFEAFE", Presenter(toggle).Background);
        toggle.IsEnabled = false; Move(window, toggle);
        Equal("#F0F3F8", Presenter(toggle).Background);
        Click(window, toggle); Assert.True(toggle.IsChecked);
        Away(window);
        Equal("#2F65CC", Part<Border>(check, "NormalRectangle").Background);
        Move(window, check); Equal("#2554AF", Part<Border>(check, "NormalRectangle").Background);
        window.MouseDown(Point(check, window), MouseButton.Left); Tick();
        Equal("#1D448F", Part<Border>(check, "NormalRectangle").Background);
        window.MouseUp(Point(check, window), MouseButton.Left); Tick();
        Assert.False(check.IsChecked);
        check.IsChecked = true; check.IsEnabled = false; Move(window, check);
        Equal("#F0F3F8", Part<Border>(check, "NormalRectangle").Background);
        Click(window, check); Assert.True(check.IsChecked);
    });

    [Fact]
    public Task TextSelectionErrorFocusAndDisabledUseDistinctVisuals() => Run(window =>
    {
        var input = new TextBox { Text = "12.346" };
        var next = new Button { Content = "下一项" };
        Host(window, input, next);
        Away(window);
        Equal("#FFFFFF", Part<Border>(input, "PART_BorderElement").Background);
        Equal("#8297B6", Part<Border>(input, "PART_BorderElement").BorderBrush);
        input.Focus(NavigationMethod.Tab); input.SelectAll(); Tick();
        var border = Part<Border>(input, "PART_BorderElement");
        Equal("#2F65CC", border.BorderBrush);
        Assert.Equal(new Thickness(2), border.BorderThickness);
        Equal("#BED3FA", input.SelectionBrush);
        Equal("#22334C", input.SelectionForegroundBrush);
        DataValidationErrors.SetErrors(input, new[] { new Exception("验证失败") }); Tick();
        Equal("#AD3F3B", border.BorderBrush);
        Assert.NotEqual(default, border.BoxShadow);
        Key(window, Avalonia.Input.Key.Tab);
        Assert.True(next.IsFocused);
        Equal("#AD3F3B", border.BorderBrush);
        Assert.Equal(default, border.BoxShadow);
        input.IsEnabled = false; Move(window, input);
        Equal("#F0F3F8", border.Background);
        Equal("#9BA9BF", input.Foreground);
    });

    [Fact]
    public Task ComboKeyboardSelectionEscapeAndDisabledSelectedOptionAreThemed() => Run(window =>
    {
        var combo = new ComboBox { ItemsSource = new[] { "角度", "物高", "像高" }, SelectedIndex = 0, Width = 240 };
        Host(window, combo, new Button { Content = "后续" });
        combo.Focus(NavigationMethod.Tab); Tick();
        Click(window, combo); Assert.True(combo.IsDropDownOpen);
        Equal("#2F65CC", Part<Border>(combo, "Background").BorderBrush);
        var popup = Part<Popup>(combo, "PART_Popup");
        var selected = popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>().Single(item => item.IsSelected);
        Assert.Contains(selected.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "✓" && t.IsVisible);
        Equal("#DFEAFE", Presenter(selected).Background);
        Key(window, Avalonia.Input.Key.Down);
        Key(window, Avalonia.Input.Key.Enter);
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.True(combo.IsFocused);
        Click(window, combo); Assert.True(combo.IsDropDownOpen);
        Key(window, Avalonia.Input.Key.Escape);
        Assert.False(combo.IsDropDownOpen); Assert.True(combo.IsFocused);
        combo.IsEnabled = false; Move(window, combo);
        Equal("#F0F3F8", Part<Border>(combo, "Background").Background);

        var option = new ComboBoxItem { Content = "不可用的已选项", IsSelected = true, IsEnabled = false };
        Host(window, option); Move(window, option);
        Equal("#F0F3F8", Presenter(option).Background);
        Equal("#9BA9BF", Presenter(option).Foreground);
    });

    [Fact]
    public Task TableSelectionCurrentCellEditCancelAndRefocusKeepBlueStates() => Run(window =>
    {
        var rows = new[] { new Row { Name = "Object" }, new Row { Name = "Image" } };
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, Height = 220 };
        grid.Columns.Add(new DataGridTextColumn { Header = "标注", Binding = new Binding(nameof(Row.Name)) });
        var next = new TextBox { Text = "其他输入" };
        Host(window, grid, next);
        grid.SelectedIndex = 0; grid.CurrentColumn = grid.Columns[0]; grid.Focus(); Tick();
        var row = grid.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.IsSelected);
        Away(window); Equal("#DFEAFE", Part<Rectangle>(row, "BackgroundRectangle").Fill);
        Move(window, row); Equal("#D5E3FC", Part<Rectangle>(row, "BackgroundRectangle").Fill);
        var cell = row.GetVisualDescendants().OfType<DataGridCell>().First();
        var currency = Part<Rectangle>(cell, "CurrencyVisual");
        Assert.True(currency.IsVisible); Equal("#2F65CC", currency.Stroke); Assert.Equal(2, currency.StrokeThickness);
        next.Focus(); Away(window);
        Equal("#DFEAFE", Part<Rectangle>(row, "BackgroundRectangle").Fill);
        grid.Focus(); Assert.True(grid.BeginEdit()); Tick();
        var editor = grid.GetVisualDescendants().OfType<TextBox>().Single();
        editor.Focus(); editor.SelectAll(); window.KeyTextInput("Changed"); Tick();
        Equal("#BED3FA", editor.SelectionBrush);
        Key(window, Avalonia.Input.Key.Escape);
        Assert.Equal("Object", rows[0].Name);
        window.SetRenderScaling(2);
        window.Width = 640; Tick();
        Assert.True(grid.Bounds.Width <= 620);
        Assert.True(cell.Bounds.Height >= 32);
    });

    [Fact]
    public Task PopupMenusAndLateWindowsResolveBlueResourcesAcrossThemeSwitches() => Run(window =>
    {
        var menu = new MenuFlyout();
        var selected = new MenuItem { Header = "Cooke", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        var disabled = new MenuItem { Header = "禁用", IsEnabled = false };
        menu.Items.Add(selected); menu.Items.Add(disabled);
        var trigger = new DropDownButton { Content = "示例", Flyout = menu };
        trigger.Classes.Add("ribbon-command");
        Host(window, trigger);
        Click(window, trigger); Assert.True(menu.IsOpen);
        Assert.Contains(":flyout-open", trigger.Classes);
        // Opening the native popup transfers pointer presence out of the host.
        Equal("#DFEAFE", Part<Border>(trigger, "RootBorder").Background);
        // PopupRoot owns native pointer dispatch; check owner state combinations separately.
        var triggerStates = (IPseudoClasses)trigger.Classes;
        triggerStates.Set(":pointerover", true); Tick();
        Equal("#D5E3FC", Part<Border>(trigger, "RootBorder").Background);
        triggerStates.Set(":pressed", true); Tick();
        Equal("#B9CFF5", Part<Border>(trigger, "RootBorder").Background);
        triggerStates.Set(":pressed", false); Tick();
        Equal("#D5E3FC", Part<Border>(trigger, "RootBorder").Background);
        triggerStates.Set(":pointerover", false); Tick();
        Key(window, Avalonia.Input.Key.Escape); Assert.False(menu.IsOpen);
        foreach (var theme in new[] { "Dark", "Pixel", "Light", "System", "Light" })
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            Tick();
        }
        var dialog = new Window { Width = 260, Height = 150, Content = new Button { Content = "确定" } };
        dialog.Show(); Tick();
        try
        {
            Equal("#FFFFFF", Presenter((Button)dialog.Content!).Background);
            Assert.True(selected.IsChecked); Assert.False(disabled.IsEnabled);
        }
        finally { dialog.Close(); }
        window.Activate(); Away(window);
        Equal("#FFFFFF", Part<Border>(trigger, "RootBorder").Background);
    });

    [Fact]
    public Task TabsAndRadioButtonsRetainTheirOwnSelectionSemantics() => Run(window =>
    {
        var first = new TabItem { Header = "表格", Content = new TextBlock { Text = "数据" } };
        var second = new TabItem { Header = "二维", Content = new TextBlock { Text = "视图" } };
        var tabs = new TabControl { ItemsSource = new[] { first, second } };
        var radio = new RadioButton { Content = "按波长", IsChecked = true };
        Host(window, tabs, radio);
        Away(window);
        Equal("#DFEAFE", Part<Border>(first, "PART_LayoutRoot").Background);
        Equal("#2446F0", Presenter(first).Foreground);
        Move(window, first); Equal("#D5E3FC", Part<Border>(first, "PART_LayoutRoot").Background);
        Equal("#1C3ED8", Presenter(first).Foreground);
        window.MouseDown(Point(first, window), MouseButton.Left); Tick();
        Equal("#B9CFF5", Part<Border>(first, "PART_LayoutRoot").Background);
        Equal("#1734BB", Presenter(first).Foreground);
        window.MouseUp(Point(first, window), MouseButton.Left); Tick();
        Equal("#D5E3FC", Part<Border>(first, "PART_LayoutRoot").Background);
        Equal("#1C3ED8", Presenter(first).Foreground);
        Away(window); Equal("#2446F0", Presenter(first).Foreground);
        Click(window, second); Assert.True(second.IsSelected); Assert.False(first.IsSelected);
        second.IsEnabled = false; Move(window, second);
        Equal("#F0F3F8", Part<Border>(second, "PART_LayoutRoot").Background);
        Equal("#9BA9BF", Presenter(second).Foreground);
        Away(window); Equal("#2F65CC", Part<Ellipse>(radio, "CheckOuterEllipse").Fill);
        Move(window, radio); Equal("#2554AF", Part<Ellipse>(radio, "CheckOuterEllipse").Fill);
        window.MouseDown(Point(radio, window), MouseButton.Left); Tick();
        Equal("#1D448F", Part<Ellipse>(radio, "CheckOuterEllipse").Fill);
        window.MouseUp(Point(radio, window), MouseButton.Left); Tick(); Assert.True(radio.IsChecked);
        radio.IsEnabled = false; Move(window, radio);
        Equal("#F0F3F8", Part<Ellipse>(radio, "CheckOuterEllipse").Fill);
    });

    [Fact]
    public Task SectionAndFieldEmphasisRestoresTransientColorsWithoutChangingChrome() => Run(window =>
    {
        using var application = WorkbenchApplication.Create();
        using var panel = new SystemPropertiesPanel(application.Prescription, application.Materials, application.Events);
        window.Height = 1100;
        window.Content = panel; Tick();
        var section = panel.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("system-section-header"));
        var field = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("field-editor-header"));
        var label = section.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("emphasis-label"));
        var arrow = section.GetVisualDescendants().OfType<LocalIcon>().Single();
        var fieldLabel = field.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("emphasis-label"));
        var summary = field.GetVisualDescendants().OfType<TextBlock>().Single(t => !t.Classes.Contains("emphasis-label"));
        var summaryColor = summary.Foreground;
        Away(window);
        var size = section.Bounds.Size;
        var border = section.BorderBrush;
        Equal("#2446F0", label.Foreground); Equal("#2446F0", arrow.Stroke);
        Equal("#22334C", fieldLabel.Foreground);
        Move(window, section);
        Equal("#1C3ED8", label.Foreground); Equal("#1C3ED8", arrow.Stroke);
        window.MouseDown(Point(section, window), MouseButton.Left); Tick();
        Equal("#1734BB", label.Foreground); Equal("#1734BB", arrow.Stroke);
        window.MouseUp(Point(section, window), MouseButton.Left); Tick();
        Equal("#1C3ED8", label.Foreground);
        Away(window); Equal("#2446F0", label.Foreground);
        Assert.Equal(size, section.Bounds.Size); Assert.Equal(border, section.BorderBrush);
        Equal("#FFFFFF", section.Background);
        Click(window, field); Away(window);
        Equal("#2446F0", fieldLabel.Foreground);
        Equal("#DFEAFE", Presenter(field).Background);
        Move(window, field); Equal("#1C3ED8", fieldLabel.Foreground);
        Equal("#D5E3FC", Presenter(field).Background);
        window.MouseDown(Point(field, window), MouseButton.Left); Tick();
        Equal("#1734BB", fieldLabel.Foreground);
        Equal("#B9CFF5", Presenter(field).Background);
        window.MouseUp(Point(field, window), MouseButton.Left); Tick();
        Equal("#1C3ED8", fieldLabel.Foreground);
        Equal("#EAF1FF", Presenter(field).Background);
        Away(window); Equal("#22334C", fieldLabel.Foreground);
        Assert.Equal(summaryColor, summary.Foreground);
        section.IsEnabled = false; Move(window, section);
        Equal("#9BA9BF", label.Foreground); Equal("#9BA9BF", arrow.Stroke);
        ThemeApplicationService.Apply(Avalonia.Application.Current!, "Dark"); Tick();
        ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); Tick();
        section.IsEnabled = true; Away(window); Equal("#2446F0", label.Foreground);
    });

    [Fact]
    public Task DockAndMenuHeadersUseEmphasisWithoutRecoloringOtherContent() => Run(window =>
    {
        var strip = new DocumentTabStrip
        {
            ItemsSource = new[] { new Dock.Model.Mvvm.Controls.Document { Id = "lens", Title = "镜头数据", CanClose = false } },
            HeaderTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Dock.Model.Mvvm.Controls.Document>((d, _) => new TextBlock { Text = d?.Title }),
            SelectedIndex = 0,
            IsActive = true
        };
        var header = new LocalIconLabel("file", "选中菜单");
        var menuLabel = header.Children.OfType<TextBlock>().Single();
        menuLabel.Classes.Add("ribbon-menu-label");
        var item = new MenuItem { Header = header, IsChecked = true, ToggleType = MenuItemToggleType.CheckBox };
        var ribbon = new TabItem { Header = "文件", IsSelected = true };
        ribbon.Classes.Add("ribbon-tab");
        Host(window, strip, item, new TabControl { ItemsSource = new[] { ribbon } });
        var tab = strip.GetVisualDescendants().OfType<DocumentTabStripItem>().Single();
        var dockLabel = Part<ContentPresenter>(tab, "PART_HeaderPresenter");
        var dockText = dockLabel.GetVisualDescendants().OfType<TextBlock>().Single();
        var menuArrow = Part<Avalonia.Controls.Shapes.Path>(item, "PART_ChevronPath");
        var menuIcon = header.Children.OfType<LocalIcon>().Single();
        var originalIcon = menuIcon.Stroke;
        Away(window);
        foreach (var (control, text, background) in new (Control, Func<IBrush?>, Func<IBrush?>)[]
        {
            (tab, () => dockText.Foreground, () => tab.Background),
            (item, () => menuLabel.Foreground, () => Part<Border>(item, "PART_LayoutRoot").Background)
        })
        {
            Away(window); Equal("#2446F0", text());
            Equal("#DFEAFE", background());
            Move(window, control); Equal("#1C3ED8", text());
            Equal("#D5E3FC", background());
            window.MouseDown(Point(control, window), MouseButton.Left); Tick();
            Equal("#1734BB", text());
            Equal("#B9CFF5", background());
            window.MouseUp(Point(control, window), MouseButton.Left); Tick();
            Equal("#1C3ED8", text());
            Equal("#D5E3FC", background());
            Away(window); Equal("#2446F0", text());
            Equal("#DFEAFE", background());
        }
        Equal("#2446F0", menuArrow.Fill);
        Assert.Equal(originalIcon, menuIcon.Stroke);
        // Document selection remains blue even while keyboard focus is elsewhere.
        Equal("#DFEAFE", tab.Background);
        tab.IsActive = true; Tick(); Equal("#DFEAFE", tab.Background);
        item.IsChecked = false; item.IsSelected = true; Away(window);
        Equal("#2446F0", menuLabel.Foreground);
        Move(window, item); Equal("#1C3ED8", menuLabel.Foreground);
        Equal("#235AB3", Part<Border>(ribbon, "PART_LayoutRoot").Background);
        Equal("#F5F8FF", Presenter(ribbon).Foreground);
        item.IsEnabled = false; Move(window, item);
        Equal("#9BA9BF", menuLabel.Foreground); Equal("#9BA9BF", menuArrow.Fill);
        ribbon.IsEnabled = false; Move(window, ribbon); Equal("#D1DDF0", Presenter(ribbon).Foreground);
    });

    [Fact]
    public Task TopMenuSeparatesHoverPressAndOpenWithoutChangingLightTabText() => Run(window =>
    {
        var first = new TabItem { Header = "文件", Content = new TextBlock { Text = "文件工具栏" } };
        var second = new TabItem { Header = "设置", Content = new TextBlock { Text = "设置工具栏" } };
        first.Classes.Add("ribbon-tab"); second.Classes.Add("ribbon-tab");
        var ribbon = new TabControl { ItemsSource = new[] { first, second }, SelectedIndex = 0 };
        ribbon.Classes.Add("ribbon-tabs");
        Host(window, ribbon);
        var firstBody = Part<Border>(first, "PART_LayoutRoot");
        var secondBody = Part<Border>(second, "PART_LayoutRoot");
        var size = first.Bounds.Size;
        Away(window);
        Equal("#235AB3", firstBody.Background);
        Equal("#2F6FD1", secondBody.Background);
        Equal("#F5F8FF", Presenter(first).Foreground);
        Assert.False(Part<Border>(first, "PART_SelectedPipe").IsVisible);
        Move(window, first); Equal("#235AB3", firstBody.Background);
        window.MouseDown(Point(first, window), MouseButton.Left); Tick();
        Equal("#235AB3", firstBody.Background);
        Assert.False(Part<Border>(first, "PART_SelectedPipe").IsVisible);
        window.MouseUp(Point(first, window), MouseButton.Left); Tick();
        Equal("#F5F8FF", Presenter(first).Foreground);
        Move(window, second); Equal("#2864C3", secondBody.Background);
        Away(window); Equal("#2F6FD1", secondBody.Background);
        Move(window, second);
        window.MouseDown(Point(second, window), MouseButton.Left); Tick();
        Equal("#235AB3", secondBody.Background);
        window.MouseUp(Point(second, window), MouseButton.Left); Tick();
        Assert.True(second.IsSelected); Assert.False(first.IsSelected);
        Assert.False(Part<Border>(second, "PART_SelectedPipe").IsVisible);
        Away(window); Equal("#235AB3", secondBody.Background); Equal("#2F6FD1", firstBody.Background);
        Assert.Equal(size, first.Bounds.Size);
        second.IsEnabled = false; Move(window, second);
        Equal("#D1DDF0", Presenter(second).Foreground);
        Assert.False(Part<Border>(second, "PART_SelectedPipe").IsVisible);
        foreach (var theme in new[] { "Dark", "Pixel", "Light" })
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
        second.IsEnabled = true; Away(window); Tick();
        Equal("#235AB3", secondBody.Background); Equal("#F5F8FF", Presenter(second).Foreground);
        second.Focus(NavigationMethod.Tab); Tick();
        Assert.False(Part<Border>(second, "PART_SelectedPipe").IsVisible);
        Assert.NotNull(second.FocusAdorner);
        // Ordinary document/analysis tabs retain their existing selection indicator.
        var ordinary = new TabItem { Header = "分析", IsSelected = true };
        Host(window, ordinary);
        Assert.True(Part<Border>(ordinary, "PART_SelectedPipe").IsVisible);
    });

    [Fact]
    public Task ScrollThumbAndTooltipKeepTheirPaletteDuringInteraction() => Run(window =>
    {
        var scroll = new ScrollBar
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Maximum = 100,
            Value = 30,
            ViewportSize = 20,
            Width = 300,
            Height = 24,
            AllowAutoHide = false
        };
        var target = new Button { Content = "提示" };
        var tooltip = new ToolTip { Content = "参数说明" };
        ToolTip.SetTip(target, tooltip);
        Host(window, scroll, target);
        var thumb = scroll.GetVisualDescendants().OfType<Thumb>().First(t => t.IsVisible && t.Bounds.Width > 0);
        var border = thumb.GetVisualDescendants().OfType<Border>().First();
        Away(window); Equal("#8297B6", border.Background);
        Move(window, thumb); Equal("#2554AF", border.Background);
        window.MouseDown(Point(thumb, window), MouseButton.Left); Tick();
        Equal("#1D448F", border.Background);
        window.MouseUp(Point(thumb, window), MouseButton.Left); Tick();
        Equal("#2554AF", border.Background);
        scroll.IsEnabled = false; Tick(); Equal("#9BA9BF", border.Background);
        ToolTip.SetIsOpen(target, true); Tick();
        Equal("#FFFFFF", tooltip.Background); Equal("#22334C", tooltip.Foreground);
        ToolTip.SetIsOpen(target, false);
    });

    [Fact]
    public Task NarrowWorkspaceKeepsSystemEditorsUsableAndTableScrollable() => Run(window =>
    {
        using var application = OptilandWorkbench.Application.Services.WorkbenchApplication.Create();
        using var panels = new OptilandWorkbench.App.Services.PanelManager(application,
            new OptilandWorkbench.App.Services.AppSettings());
        window.Width = 640;
        window.Content = panels.WorkspaceControl;
        Tick();
        var system = window.GetVisualDescendants().OfType<OptilandWorkbench.App.Panels.SystemPropertiesPanel>().Single();
        Assert.InRange(system.Bounds.Width, 236, 280);
        var scroll = Assert.IsType<ScrollViewer>(system.Content);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.True(scroll.Viewport.Width >= 218);
        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.InRange(grid.Bounds.Width, 1, 404);
        Assert.Contains(grid.GetVisualDescendants().OfType<ScrollBar>(),
            bar => bar.Orientation == Avalonia.Layout.Orientation.Horizontal && bar.Maximum > 0);
    });

    [Fact]
    public void BlueTextAndKeyboardFocusHaveReadableContrast()
    {
        foreach (var (foreground, background) in new[]
        {
            ("#22334C", "#FFFFFF"), ("#5C6F8A", "#FFFFFF"), ("#22334C", "#DFEAFE"),
            ("#FFFFFF", "#2F6FD1"), ("#F5F8FF", "#2F6FD1"), ("#F5F8FF", "#2864C3"),
            ("#F5F8FF", "#235AB3"), ("#FFFFFF", "#2F65CC"), ("#2446F0", "#DFEAFE"),
            ("#AD3F3B", "#FBEDEC")
        }) Assert.True(Contrast(foreground, background) >= 4.5, $"{foreground} on {background}");
        Assert.True(Contrast("#2F65CC", "#FFFFFF") >= 3);
        static double Contrast(string first, string second)
        {
            static double Luminance(string hex)
            {
                var color = Color.Parse(hex);
                static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
                return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
            }
            var a = Luminance(first); var b = Luminance(second);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }
    }

    private static async Task Run(Action<Window> action)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            var window = new Window { Width = 680, Height = 460 };
            window.Show();
            try { action(window); }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    private static void Host(Window window, params Control[] controls)
    {
        window.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { } };
        foreach (var control in controls) ((StackPanel)window.Content).Children.Add(control);
        Tick();
    }
    private static void Tick() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static Point Point(Control control, Window window) => control.TranslatePoint(new Point(18, control.Bounds.Height / 2), window)!.Value;
    private static void Move(Window window, Control control) { window.MouseMove(Point(control, window)); Tick(); }
    private static void Away(Window window) { window.MouseMove(new Point(window.Bounds.Width - 4, window.Bounds.Height - 4)); Tick(); }
    private static void Click(Window window, Control control)
    {
        Move(window, control); window.MouseDown(Point(control, window), MouseButton.Left);
        window.MouseUp(Point(control, window), MouseButton.Left); Tick();
    }
    private static void Key(Window window, Key key) { window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); Tick(); }
    private static T Part<T>(Control control, string name) where T : Control =>
        control.GetVisualDescendants().OfType<T>().First(p => p.Name == name);
    private static ContentPresenter Presenter(Control control) => Part<ContentPresenter>(control, "PART_ContentPresenter");
    private static void Equal(string expected, IBrush? actual) => Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    private sealed class Row { public string Name { get; set; } = ""; }
}
