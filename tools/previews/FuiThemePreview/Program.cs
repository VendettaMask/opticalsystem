using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.FuiThemePreview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var captureIndex = Array.IndexOf(args, "--capture");
        var capture = captureIndex >= 0 ? Path.GetFullPath(args[captureIndex + 1]) : null;
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture is null) builder.UsePlatformDetect();
        else builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        builder.UseHarfBuzz().SetupWithoutStarting();
        PreviewPalette.UseRegisteredTheme = args.Contains("--lumen");
        PreviewPalette.Apply();
        DisplayTypography.Configure(new AppSettings { Theme = PreviewPalette.UseRegisteredTheme ? "Lumen" : "Dark", FontSize = 13 });
        using var preview = new FuiPreviewWindow(fitScreen: capture is null, engineeringFirst: args.Contains("--engineering"), registeredTheme: PreviewPalette.UseRegisteredTheme);
        using var lifetime = new CancellationTokenSource();
        preview.Closed += (_, _) => lifetime.Cancel();
        preview.Show();
        var exitCode = 0;
        if (capture is not null)
        {
            DispatcherTimer.RunOnce(async () =>
            {
                try
                {
                    preview.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var report = await preview.VerifyAsync();
                    Dispatcher.UIThread.RunJobs();
                    preview.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Directory.CreateDirectory(Path.GetDirectoryName(capture)!);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(3200, 2000), new Vector(192, 192));
                    bitmap.Render(preview);
                    bitmap.Save(capture, PngBitmapEncoderOptions.Default);
                    if (new FileInfo(capture).Length < 1000) throw new InvalidOperationException("Empty render.");
                    if (PreviewPalette.UseRegisteredTheme)
                    {
                        var gallery = new ThemeControlGalleryWindow();
                        try
                        {
                            gallery.Show(preview);
                            Dispatcher.UIThread.RunJobs(); gallery.UpdateLayout();
                            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                            using var galleryBitmap = new RenderTargetBitmap(new PixelSize(1400, 1300), new Vector(192, 192));
                            galleryBitmap.Render(gallery);
                            galleryBitmap.Save(Path.Combine(Path.GetDirectoryName(capture)!, Path.GetFileNameWithoutExtension(capture) + "-controls.png"), PngBitmapEncoderOptions.Default);
                        }
                        finally { gallery.Close(); }
                    }
                    File.WriteAllText(Path.ChangeExtension(capture, ".json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.ChangeExtension(capture, ".error.txt"), exception.ToString());
                    exitCode = 1;
                }
                finally { preview.Close(); }
            }, TimeSpan.FromSeconds(3));
        }
        try { Dispatcher.UIThread.MainLoop(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        return exitCode;
    }
}

internal sealed class PreviewWindow : Window, IDisposable
{
    private readonly WorkbenchApplication _workbench;
    private readonly LensEditorPanel _editor;
    private readonly SystemPropertiesPanel _sidebar;
    private readonly ViewerPanel _viewer;
    private readonly DataGrid _table;
    private readonly Grid _workspace;

    internal PreviewWindow(bool fitScreen)
    {
        // This process has no desktop application lifetime: the product's normal startup,
        // profile loader, recovery writer, file associations and installer are never entered.
        var isolatedCatalog = Path.Combine(Path.GetTempPath(), "OpticalSystemDesign-FuiPreview", "catalogs");
        _workbench = WorkbenchApplication.Create(userCatalogDirectory: isolatedCatalog);
        CreateSample();
        var selection = new SurfaceSelectionService();
        _editor = new LensEditorPanel(_workbench.Prescription, _workbench.Events, selection);
        _sidebar = new SystemPropertiesPanel(_workbench.Prescription, _workbench.Materials, _workbench.Events);
        _viewer = new ViewerPanel(_workbench.Visualization, _workbench.Events, selection, SceneDimension.TwoDimensional);
        _table = _editor.GetLogicalDescendants().OfType<DataGrid>().Single();
        _editor.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("glass-material-row"))
        {
            Setters = { new Setter(DataGridRow.BackgroundProperty, PreviewPalette.Brush("#163A55")) }
        });
        _editor.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("glass-material-row").Class(":selected"))
        {
            Setters = { new Setter(DataGridRow.BackgroundProperty, PreviewPalette.Brush("#24516B")) }
        });
        Title = "光域 FUI · C# 交互预览 · 不修改正式主题 — Optical System Design";
        Width = 1600;
        Height = 1000;
        MinWidth = 1100;
        MinHeight = 760;
        Background = PreviewPalette.Brush("#071216");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (fitScreen && Screens.Primary is { } screen)
        {
            Width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - 40));
            Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 70));
        }
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 7, Margin = new Thickness(10) };
        body.Children.Add(TitleRow());
        var ribbon = BuildRibbon();
        Grid.SetRow(ribbon, 1);
        body.Children.Add(ribbon);
        _workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("256,*"), ColumnSpacing = 8 };
        var sidebar = Frame("系统选项", _sidebar);
        _workspace.Children.Add(sidebar);
        var documents = new Grid { RowDefinitions = new RowDefinitions("0.9*,6,1.1*") };
        var lensFrame = Frame("镜头数据  /  原生可编辑表格", _editor);
        documents.Children.Add(lensFrame);
        var divider = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(divider, 1);
        documents.Children.Add(divider);
        var viewer = Frame("二维视图  /  共享 Core 实际光路", _viewer);
        Grid.SetRow(viewer, 2);
        documents.Children.Add(viewer);
        Grid.SetColumn(documents, 1);
        _workspace.Children.Add(documents);
        Grid.SetRow(_workspace, 2);
        body.Children.Add(_workspace);
        var status = new TextBlock
        {
            Text = "仅供主题走查  ·  独立内存工程  ·  可编辑表格 / 缩放光路 / 调整分栏  ·  关闭即丢弃，不保存工程与设置",
            Foreground = PreviewPalette.Brush("#90ADB5"),
            Margin = new Thickness(8, 4),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(status, 3);
        body.Children.Add(status);
        Content = body;
        DisplayTypography.Apply(this);
    }

    private void CreateSample()
    {
        for (var i = 0; i < 6; i++) _workbench.Prescription.AddSurface();
        var rows = _workbench.Prescription.GetSurfaces().ToArray();
        _workbench.Prescription.UpdateSurface(rows[0] with { Thickness = double.PositiveInfinity, SemiDiameter = 8 });
        double[] radii = [32, -32, 48, -48, 60, -60];
        double[] thicknesses = [5, 12, 4, 14, 5, 35];
        for (var i = 0; i < 6; i++)
            _workbench.Prescription.UpdateSurface(rows[i + 1] with
            {
                Label = $"S{i + 1}",
                Radius = radii[i],
                Thickness = thicknesses[i],
                Material = i % 2 == 0 ? "N-BK7" : "Air",
                SemiDiameter = 8,
                IsStop = i == 0
            });
        _workbench.Prescription.UpdateSurface(rows[^1] with { Label = "Image", Thickness = 0, SemiDiameter = 12 });
        var settings = _workbench.Prescription.GetSystemSettings();
        _workbench.Prescription.UpdateSystemSettings(settings with { ApertureValue = 10 });
    }

    private Control TitleRow()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 2, 8, 4) };
        row.Children.Add(new TextBlock { Text = "Optical System Design  /  光域 FUI", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = PreviewPalette.Brush("#DBE7E5") });
        var badge = new Border { Padding = new Thickness(12, 5), BorderThickness = new Thickness(1), BorderBrush = PreviewPalette.Brush("#E3AC63"), CornerRadius = new CornerRadius(3), Child = new TextBlock { Text = "C# 原生控件 · 独立预览", Foreground = PreviewPalette.Brush("#E3AC63") } };
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);
        return row;
    }

    private Control BuildRibbon()
    {
        var menu = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        var commands = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
        foreach (var name in new[] { "文件", "设置", "视图", "分析", "优化", "公差", "加工与图纸", "数据库", "实验室", "窗口", "帮助" })
        {
            var button = new Button { Content = name, Padding = new Thickness(12, 7) };
            menu.Children.Add(button);
            if (name == "文件") button.Background = PreviewPalette.Brush("#17373F");
            button.Click += (_, _) => ToolTip.SetTip(button, "仅预览菜单状态；不会执行正式工作台命令。");
        }
        var palette = new Button { Content = "控件配色：光域 / 暗夜", MinWidth = 166 };
        var fui = true;
        palette.Click += (_, _) => { fui = !fui; if (fui) PreviewPalette.Apply(); else PreviewPalette.Restore(); };
        commands.Children.Add(palette);
        var reset = new Button { Content = "还原预览工程", MinWidth = 120 };
        reset.Click += (_, _) => { _workbench.Documents.NewBlank(); CreateSample(); };
        commands.Children.Add(reset);
        var run = new Button { Content = "重绘视图", MinWidth = 106 };
        run.Classes.Add("accent");
        run.Click += (_, _) => _viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single().InvalidateVisual();
        commands.Children.Add(run);
        var reserved = new Button { Content = "安装主题（未启用）", IsEnabled = false };
        ToolTip.SetTip(reserved, "本轮只预览，不注册、安装或保存主题。");
        Avalonia.Automation.AutomationProperties.SetHelpText(reserved, "本轮只预览，不注册、安装或保存主题。");
        commands.Children.Add(reserved);
        var host = new StackPanel { Spacing = 8, Children = { menu, commands }, Margin = new Thickness(8) };
        return new Border { Background = PreviewPalette.Brush("#0D1A20"), BorderBrush = PreviewPalette.Brush("#27434B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = host };
    }

    private static Control Frame(string title, Control content)
    {
        var panel = new DockPanel();
        var header = new Border { Padding = new Thickness(10, 6), BorderThickness = new Thickness(0, 0, 0, 1), Child = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = PreviewPalette.Brush("#69CDD0") } };
        header.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        header.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(content);
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = panel };
        border.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        border.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        return border;
    }

    internal object Verify()
    {
        var scene = _viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single();
        if (_workbench.Prescription.GetSurfaces().Count != 8 || _table.Columns.Count < 12 ||
            scene.Scene?.TwoDimensional is not { Rays.Count: > 0 } || scene.Scene.SourceRevision != _workbench.Events.Revision)
            throw new InvalidOperationException("Real prescription/editor/scene was not populated.");
        var scroll = (ScrollViewer)_sidebar.Content!;
        if (_sidebar.Bounds.Width > 280 || scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Hidden)
            throw new InvalidOperationException("Sidebar contract changed.");
        var materials = _editor.GetVisualDescendants().OfType<DataGridRow>().Where(row => row.Classes.Contains("glass-material-row")).ToArray();
        if (materials.Length != 3) throw new InvalidOperationException("Material rows not realized.");
        if (materials.Any(row => row.Background is not ISolidColorBrush brush || brush.Color.B < brush.Color.G + 5))
            throw new InvalidOperationException("Material row blue cue was lost.");
        PreviewPalette.Restore();
        PreviewPalette.Apply();
        return new { Passed = true, RealCSharpControls = true, SurfaceRows = 8, GridColumns = _table.Columns.Count, MaterialRows = materials.Length, SidebarWidth = _sidebar.Bounds.Width, SceneRays = scene.Scene.TwoDimensional.Rays.Count, SameRevisionScene = true, ThemeSwitchExercised = true, ProductFilesChangedByPreview = false, UserSettingsWritten = false, OpticalAcceptanceClaimed = false };
    }

    public void Dispose() { _viewer.Dispose(); _sidebar.Dispose(); _editor.Dispose(); _workbench.Dispose(); }
}

internal static class PreviewPalette
{
    internal static bool UseRegisteredTheme { get; set; }
    private static readonly Dictionary<object, object?> Originals = new();
    internal static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    internal static void Apply()
    {
        var app = Avalonia.Application.Current!;
        var assembly = typeof(global::OpticalSystemDesign.App.App).Assembly;
        if (UseRegisteredTheme)
        {
            assembly.GetType("OpticalSystemDesign.App.Theming.ThemeApplicationService")!.GetMethod("Apply")!.Invoke(null, [app, "Lumen"]);
            return;
        }
        assembly.GetType("OpticalSystemDesign.App.Theming.ThemeApplicationService")!.GetMethod("Apply")!.Invoke(null, [app, "Dark"]);
        var paletteType = assembly.GetType("OpticalSystemDesign.App.Theming.ThemePalette")!;
        var source = paletteType.GetProperty("DarkOpticStudio")!.GetValue(null)!;
        var palette = paletteType.GetMethod("<Clone>$")!.Invoke(source, null)!;
        var colors = new Dictionary<string, string>
        {
            ["Surface"] = "#0A1216",
            ["SubtleSurface"] = "#0C181D",
            ["SettingsSurface"] = "#091317",
            ["SettingsOverlaySurface"] = "#0E1D23",
            ["Workspace"] = "#050C10",
            ["Border"] = "#26383D",
            ["TextPrimary"] = "#D5DCD9",
            ["TextSecondary"] = "#98ADAE",
            ["TextMuted"] = "#728D91",
            ["TextDisabled"] = "#69818A",
            ["TextAccent"] = "#BFCDC8",
            ["TextOnAccent"] = "#071216",
            ["Hover"] = "#183047",
            ["HoverBorder"] = "#866D46",
            ["RibbonHover"] = "#142229",
            ["RibbonHoverBorder"] = "#52666A",
            ["RibbonTabHover"] = "#142229",
            ["TextWarning"] = "#E3AC63",
            ["PlotBackground"] = "#DCE7E6",
            ["PlotText"] = "#17353C",
            ["SceneBackground"] = "#DCE7E6",
            ["SceneBackgroundAlt"] = "#DCE7E6",
            ["SceneReference"] = "#43616B",
            ["SceneAxis"] = "#68868C"
        };
        foreach (var (name, color) in colors) paletteType.GetProperty(name)!.SetValue(palette, Color.Parse(color));
        var resources = (ResourceDictionary)paletteType.GetMethod("ToResourceDictionary")!.Invoke(palette, null)!;
        foreach (var (key, value) in resources) Override(key, value);
        Override(ThemeResourceBindings.SectionHeaderBackground, Brushes.Transparent);
        Override(ThemeResourceBindings.SectionHeaderEmphasizedBackground, Brushes.Transparent);
        Override(ThemeResourceBindings.SectionHeaderEmphasizedForeground, Brush("#C6D3CE"));
        Override(ThemeLayoutResources.SystemSectionsMargin, new Thickness(4, 4));
        Override(ThemeLayoutResources.SystemSectionSpacing, 4d);
        Override("ControlCornerRadius", new CornerRadius(0));
        foreach (var role in new[] { "Ribbon", "Workspace", "SettingsCard", "SurfaceCard", "ControlFrame", "StatusBar", "Dialog", "Viewport" })
        {
            Override($"OpticalSystemDesignChrome{role}CornerRadius", new CornerRadius(0));
            Override($"OpticalSystemDesignChrome{role}BoxShadow", default(BoxShadows));
            Override($"OpticalSystemDesignChrome{role}BorderBrush", Brush("#26383D"));
            if (role == "SettingsCard") Override($"OpticalSystemDesignChrome{role}BorderThickness", new Thickness(0, 0, 0, 1));
        }
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorLight1" }) Override(key, Color.Parse("#BE965A"));
        foreach (var key in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "DockTabActiveIndicatorBrush", "DockTargetIndicatorBrush" }) Override(key, Brush("#BE965A"));
        foreach (var key in new[] { "ControlFillColorDefaultBrush", "TextControlBackground", "ComboBoxBackground", "ButtonBackground" }) Override(key, Brush("#101E24"));
        foreach (var key in new[] { "TextControlBorderBrush", "ComboBoxBorderBrush", "ButtonBorderBrush", "ControlStrokeColorDefaultBrush", "ControlStrongStrokeColorDefaultBrush" }) Override(key, Brush("#486166"));
        foreach (var key in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed" }) Override(key, Brush("#E3AC63"));
        foreach (var key in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" }) Override(key, Brush("#071216"));
    }

    private static void Override(object key, object? value)
    {
        var resources = Avalonia.Application.Current!.Resources;
        if (!Originals.ContainsKey(key)) Originals[key] = resources.TryGetResource(key, ThemeVariant.Dark, out var original) ? original : null;
        resources[key] = value;
    }

    internal static void Restore()
    {
        if (UseRegisteredTheme)
        {
            typeof(global::OpticalSystemDesign.App.App).Assembly.GetType("OpticalSystemDesign.App.Theming.ThemeApplicationService")!
                .GetMethod("Apply")!.Invoke(null, [Avalonia.Application.Current!, "Light"]);
            return;
        }
        var resources = Avalonia.Application.Current!.Resources;
        foreach (var (key, value) in Originals) { if (value is null) resources.Remove(key); else resources[key] = value; }
        Originals.Clear();
    }
}
