using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Panels;

internal static class UserGuideScreenshots
{
    internal sealed record Screenshot(string Key, string Chinese, string English, string Japanese)
    {
        internal string Title(string language) => language switch { UiLocalization.English => English, UiLocalization.Japanese => Japanese, _ => Chinese };
    }
    internal static IReadOnlyList<Screenshot> All { get; } = new[]
    {
        new Screenshot("workbench", "主工作台真实界面截图", "Actual main-workbench screenshot", "メインワークベンチの実画面"),
        new Screenshot("projects", "文件与工程入口", "Files and projects", "ファイルとプロジェクト"),
        new Screenshot("layout-2d", "二维光路检查", "Inspecting the 2D layout", "2D 光路の確認"),
        new Screenshot("solid-model", "实体模型与视角操作", "Solid Model and view presets", "ソリッドモデルと視点操作"),
        new Screenshot("spot", "点列图：视场、波长与结果", "Spot diagram: fields, wavelengths and results", "スポット図：視野、波長、結果"),
        new Screenshot("mtf", "傅里叶 MTF 曲线", "Fourier MTF curves", "フーリエ MTF 曲線"),
        new Screenshot("wavefront", "波前图与参考设置", "Wavefront map and reference", "波面図と基準設定"),
        new Screenshot("optimization", "评价函数编辑器", "Merit Function Editor", "メリット関数エディター"),
        new Screenshot("tolerancing", "公差数据编辑器", "Tolerance Data Editor", "公差データエディター"),
        new Screenshot("tolerance-settings", "公差分析设置", "Tolerance analysis settings", "公差解析設定"),
        new Screenshot("lens-library", "镜头库与处方预览", "Lens library and prescription preview", "レンズライブラリと処方プレビュー"),
        new Screenshot("drawing", "光学制图与标题栏", "Optical drawing and title block", "光学図面と表題欄"),
        new Screenshot("camera", "相机成像实验室", "Camera imaging laboratory", "カメラ結像ラボ"),
        new Screenshot("coating", "镀膜设计与光谱结果", "Coating design and spectrum", "薄膜設計とスペクトル"),
        new Screenshot("initial-structure", "自动初始结构工作区", "Initial Structure workspace", "初期構成ワークスペース"),
        new Screenshot("assembly", "光学装调工作区", "Optical Assembly workspace", "光学組立ワークスペース"),
        new Screenshot("non-sequential", "非序列对象与探测器设置", "Non-sequential objects and detectors", "ノンシーケンシャルオブジェクトと検出器"),
        new Screenshot("detector", "追迹后的探测器结果", "Detector results after tracing", "光線追跡後の検出器結果")
    };
    internal static Uri Resource(string key, string language) => new($"avares://OpticalSystemDesign.App/Assets/UserGuide/{language}/{key}.png");
    internal static IReadOnlyList<string> ForChapter(string id) => id switch
    {
        "start" or "interface" or "workspace" => ["workbench"],
        "projects" => ["projects"],
        "sequential-model" => ["layout-2d", "solid-model"],
        "non-sequential-model" => ["non-sequential", "detector"],
        "analyses" => ["spot", "mtf"],
        "optimization" => ["optimization"],
        "tolerancing" => ["tolerance-settings", "drawing"],
        "libraries" => ["lens-library", "initial-structure", "coating", "assembly"],
        "help" => ["projects"],
        _ => []
    };
}

internal sealed class UserGuideScreenshot : Border
{
    private readonly string _key;
    private readonly string _language;
    private readonly Image _image = new() { Name = "UserGuideScreenshot", Stretch = Stretch.Uniform, MaxHeight = 420 };
    private Bitmap? _bitmap;

    internal UserGuideScreenshot(string key, string language)
    {
        _key = key; _language = language;
        var entry = UserGuideScreenshots.All.Single(item => item.Key == key);
        var title = entry.Title(language);
        var hint = L("界面截图 · 2026-10-27 · 点击放大", "UI screenshot · 2026-10-27 · Click to enlarge", "実画面・2026-10-27・クリックで拡大");
        var enlarge = new Button
        {
            Name = "UserGuideEnlargeScreenshot",
            Content = _image,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        enlarge.BindThemeResource(BorderBrushProperty, ThemeResourceBindings.Border);
        enlarge.Click += async (_, _) => await Enlarge(title);
        AutomationProperties.SetName(enlarge, title + " · " + L("放大截图", "Enlarge screenshot", "画面を拡大"));
        AutomationProperties.SetName(_image, title);
        var caption = new TextBlock { Text = title, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        caption.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        var note = new TextBlock { Text = L("点击放大 ↗", "Enlarge ↗", "拡大 ↗"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        note.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        var captionRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 14 };
        captionRow.Children.Add(caption); captionRow.Children.Add(note); Grid.SetColumn(note, 1);
        ToolTip.SetTip(enlarge, hint);
        Child = new StackPanel { Spacing = 9, Children = { enlarge, captionRow } };
        Padding = new Thickness(0); BorderThickness = new Thickness(0);
        AttachedToVisualTree += (_, _) =>
        {
            using var stream = AssetLoader.Open(UserGuideScreenshots.Resource(_key, _language));
            _image.Source = _bitmap = Bitmap.DecodeToWidth(stream, 1280);
        };
        DetachedFromVisualTree += (_, _) => { _image.Source = null; _bitmap?.Dispose(); _bitmap = null; };
    }

    private async Task Enlarge(string title)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        using var stream = AssetLoader.Open(UserGuideScreenshots.Resource(_key, _language));
        using var bitmap = new Bitmap(stream);
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        var scroll = new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var fit = new Button { Name = "UserGuideScreenshotFit", Content = L("适应窗口", "Fit to window", "ウィンドウに合わせる") };
        var actual = new Button { Name = "UserGuideScreenshotActualSize", Content = "100%" };
        var close = new Button { Content = L("关闭", "Close", "閉じる") };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12), Children = { fit, actual, close } };
        var content = new DockPanel { Children = { toolbar, scroll } }; DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        var dialog = new Window { Name = "UserGuideScreenshotWindow", Title = title, Width = 1200, Height = 820, MinWidth = 400, MinHeight = 300, Content = content, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.BindThemeResource(Window.BackgroundProperty, ThemeResourceBindings.Workspace);
        fit.Click += (_, _) => { image.Width = double.NaN; image.Height = double.NaN; image.Stretch = Stretch.Uniform; scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; };
        actual.Click += (_, _) => { image.Width = bitmap.PixelSize.Width; image.Height = bitmap.PixelSize.Height; image.Stretch = Stretch.None; scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; };
        close.Click += (_, _) => dialog.Close();
        UiLocalization.Attach(dialog);
        await dialog.ShowDialog(owner);
    }

    private string L(string zh, string en, string ja) => _language switch { UiLocalization.English => en, UiLocalization.Japanese => ja, _ => zh };
}
