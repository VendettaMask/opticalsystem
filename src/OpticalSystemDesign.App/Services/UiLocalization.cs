using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace OpticalSystemDesign.App.Services;

/// <summary>
/// Applies the selected UI language to controls as they are loaded.  Existing screens in the
/// application are built in code, so keeping the canonical Chinese text here lets a language
/// change update the current visual tree as well as windows and flyouts created afterwards.
/// </summary>
internal static class UiLocalization
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";
    public const string Japanese = "ja-JP";

    private sealed record Translation(string English, string Japanese);

    private sealed class PropertyTranslation
    {
        public required string Source { get; set; }
        public string? LastApplied { get; set; }
    }

    private sealed class ControlTranslationState
    {
        public bool Applying { get; set; }
        public bool IsAttached { get; set; } = true;
        public int OwnerThreadId { get; set; } = Environment.CurrentManagedThreadId;
        public Dictionary<AvaloniaProperty, PropertyTranslation> Properties { get; } = new();
    }

    private static readonly ConditionalWeakTable<Control, ControlTranslationState> States = new();
    private static readonly ConditionalWeakTable<Window, object> AttachedWindows = new();
    private static readonly List<WeakReference<Control>> TrackedControls = new();
    private static bool _installed;

    private static readonly IReadOnlyDictionary<string, Translation> Texts = BuildTexts();

    public static string CurrentLanguage { get; private set; } = Chinese;

    public static IReadOnlyList<(string Code, string DisplayName)> SupportedLanguages { get; } =
    [
        (Chinese, "中文"),
        (English, "English"),
        (Japanese, "日本語")
    ];

    public static event EventHandler? LanguageChanged;

    public static string NormalizeLanguage(string? language) => language switch
    {
        English => English,
        Japanese => Japanese,
        _ => Chinese
    };

    public static void InstallGlobalHooks()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Control.LoadedEvent.AddClassHandler<Control>(OnControlLoaded, handledEventsToo: true);
        Window.WindowOpenedEvent.AddClassHandler<Window>(OnWindowOpened, handledEventsToo: true);
    }

    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!AttachedWindows.TryGetValue(window, out _))
        {
            AttachedWindows.Add(window, new object());
            window.LayoutUpdated += OnWindowLayoutUpdated;
            window.Closed += (_, _) => window.LayoutUpdated -= OnWindowLayoutUpdated;
        }

        LocalizeTree(window);
    }

    public static void Apply(string? language)
    {
        var normalized = NormalizeLanguage(language);
        CurrentLanguage = normalized;
        var culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        for (var index = TrackedControls.Count - 1; index >= 0; index--)
        {
            if (!TrackedControls[index].TryGetTarget(out var control))
            {
                TrackedControls.RemoveAt(index);
                continue;
            }

            if (States.TryGetValue(control, out var state)
                && (!state.IsAttached || state.OwnerThreadId != Environment.CurrentManagedThreadId))
            {
                continue;
            }

            LocalizeControl(control);
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Text(string source)
    {
        if (CurrentLanguage == Chinese || string.IsNullOrEmpty(source))
        {
            return source;
        }

        if (Texts.TryGetValue(source, out var translation))
        {
            return CurrentLanguage == Japanese ? translation.Japanese : translation.English;
        }

        // Form labels commonly append a full-width colon to a translated caption.
        if (source.EndsWith('：') && Texts.TryGetValue(source[..^1], out translation))
        {
            var localized = CurrentLanguage == Japanese ? translation.Japanese : translation.English;
            return localized + (CurrentLanguage == Japanese ? "：" : ":");
        }

        // Accelerator labels use the same visible text as their ordinary ribbon tab.
        var acceleratorStart = source.LastIndexOf("(_", StringComparison.Ordinal);
        if (acceleratorStart > 0 && source.EndsWith(')'))
        {
            var prefix = source[..acceleratorStart];
            if (Texts.TryGetValue(prefix, out translation))
            {
                var localized = CurrentLanguage == Japanese ? translation.Japanese : translation.English;
                return localized + source[acceleratorStart..];
            }
        }

        // Cards assemble summaries from canonical fragments at runtime. Translate only the
        // recognized fragments so numeric values, units and imported text remain untouched.
        if (source.Contains(" · ", StringComparison.Ordinal))
        {
            return string.Join(" · ", source.Split(" · ", StringSplitOptions.None).Select(Text));
        }

        if (source.StartsWith("表面 ", StringComparison.Ordinal)
            && source.EndsWith(" 属性", StringComparison.Ordinal))
        {
            var number = source[3..^3];
            return CurrentLanguage == Japanese
                ? $"面 {number} プロパティ"
                : $"Surface {number} Properties";
        }

        if (TryTranslatePrefixedValue(source, "波长 ", "Wavelength ", "波長 ", out var prefixed)
            || TryTranslatePrefixedValue(source, "视场 ", "Field ", "視野 ", out prefixed)
            || TryTranslatePrefixedValue(source, "权重 ", "Weight ", "重み ", out prefixed))
        {
            return prefixed;
        }

        return source;
    }

    private static bool TryTranslatePrefixedValue(
        string source,
        string chinesePrefix,
        string englishPrefix,
        string japanesePrefix,
        out string translated)
    {
        if (!source.StartsWith(chinesePrefix, StringComparison.Ordinal))
        {
            translated = source;
            return false;
        }

        var localizedPrefix = CurrentLanguage == Japanese ? japanesePrefix : englishPrefix;
        translated = localizedPrefix + source[chinesePrefix.Length..];
        return true;
    }

    public static string Count(int count, string chineseNoun)
    {
        if (CurrentLanguage == Chinese)
        {
            return $"{count} 个{chineseNoun}";
        }

        var noun = Text(chineseNoun);
        if (CurrentLanguage == Japanese)
        {
            return $"{count} {noun}";
        }

        var plural = count == 1 ? noun : noun + "s";
        return $"{count} {plural}";
    }

    private static void OnControlLoaded(Control control, Avalonia.Interactivity.RoutedEventArgs args)
    {
        Track(control);
        LocalizeControl(control);
    }

    private static void OnWindowOpened(Window window, Avalonia.Interactivity.RoutedEventArgs args) => Attach(window);

    private static void OnWindowLayoutUpdated(object? sender, EventArgs args)
    {
        if (sender is Window window)
        {
            LocalizeTree(window);
        }
    }

    private static void LocalizeTree(Window window)
    {
        Track(window);
        LocalizeControl(window);
        foreach (var control in window.GetLogicalDescendants().OfType<Control>())
        {
            Track(control);
            LocalizeControl(control);
        }
    }

    private static void Track(Control control)
    {
        if (States.TryGetValue(control, out _))
        {
            return;
        }

        States.Add(control, new ControlTranslationState());
        TrackedControls.Add(new WeakReference<Control>(control));
        control.PropertyChanged += OnControlPropertyChanged;
        control.AttachedToVisualTree += (_, _) =>
        {
            var state = States.GetOrCreateValue(control);
            state.IsAttached = true;
            state.OwnerThreadId = Environment.CurrentManagedThreadId;
        };
        control.DetachedFromVisualTree += (_, _) => States.GetOrCreateValue(control).IsAttached = false;
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (sender is not Control control || !IsLocalizableProperty(control, args.Property))
        {
            return;
        }

        LocalizeProperty(control, args.Property);
    }

    private static void LocalizeControl(Control control)
    {
        if (control is TextBlock)
        {
            LocalizeProperty(control, TextBlock.TextProperty);
        }

        if (control is Window)
        {
            LocalizeProperty(control, Window.TitleProperty);
        }

        if (control is ContentControl)
        {
            LocalizeProperty(control, ContentControl.ContentProperty);
        }

        if (control is HeaderedContentControl)
        {
            LocalizeProperty(control, HeaderedContentControl.HeaderProperty);
        }

        if (control is HeaderedSelectingItemsControl)
        {
            LocalizeProperty(control, HeaderedSelectingItemsControl.HeaderProperty);
        }

        if (control is TextBox)
        {
            LocalizeProperty(control, TextBox.PlaceholderTextProperty);
        }

        LocalizeProperty(control, ToolTip.TipProperty);
        LocalizeProperty(control, AutomationProperties.NameProperty);
        LocalizeProperty(control, AutomationProperties.HelpTextProperty);
    }

    private static bool IsLocalizableProperty(Control control, AvaloniaProperty property) =>
        (control is TextBlock && property == TextBlock.TextProperty)
        || (control is Window && property == Window.TitleProperty)
        || (control is ContentControl && property == ContentControl.ContentProperty)
        || (control is HeaderedContentControl && property == HeaderedContentControl.HeaderProperty)
        || (control is HeaderedSelectingItemsControl && property == HeaderedSelectingItemsControl.HeaderProperty)
        || (control is TextBox && property == TextBox.PlaceholderTextProperty)
        || property == ToolTip.TipProperty
        || property == AutomationProperties.NameProperty
        || property == AutomationProperties.HelpTextProperty;

    private static void LocalizeProperty(Control control, AvaloniaProperty property)
    {
        var state = States.GetOrCreateValue(control);
        if (state.Applying || control.GetValue(property) is not string current)
        {
            return;
        }

        if (!state.Properties.TryGetValue(property, out var propertyState))
        {
            propertyState = new PropertyTranslation { Source = current };
            state.Properties[property] = propertyState;
        }
        else if (!string.Equals(current, propertyState.LastApplied, StringComparison.Ordinal)
                 && !string.Equals(current, propertyState.Source, StringComparison.Ordinal))
        {
            // A status label or bound header has supplied a new canonical value.
            propertyState.Source = current;
        }

        var translated = Text(propertyState.Source);
        propertyState.LastApplied = translated;
        if (string.Equals(current, translated, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            state.Applying = true;
            control.SetCurrentValue(property, translated);
        }
        finally
        {
            state.Applying = false;
        }
    }

    private static IReadOnlyDictionary<string, Translation> BuildTexts()
    {
        var values = new Dictionary<string, Translation>(StringComparer.Ordinal);
        void Add(string source, string english, string japanese) =>
            values[source] = new Translation(english, japanese);

        // Application shell and global commands.
        Add("文件", "File", "ファイル");
        Add("设置", "Settings", "設定");
        Add("视图", "View", "表示");
        Add("分析", "Analysis", "解析");
        Add("优化", "Optimize", "最適化");
        Add("公差", "Tolerance", "公差");
        Add("加工与图纸", "Manufacturing & Drawings", "製造と図面");
        Add("数据库", "Database", "データベース");
        Add("实验室", "Laboratory", "ラボ");
        Add("窗口", "Window", "ウィンドウ");
        Add("帮助", "Help", "ヘルプ");
        Add("语言", "Language", "言語");
        Add("选择界面语言", "Choose interface language", "表示言語を選択");
        Add("工作模式", "Workspace Mode", "作業モード");
        Add("系统", "System", "システム");
        Add("显示", "Display", "表示");
        Add("系统布局", "System Layout", "システムレイアウト");
        Add("非序列布局", "Non-Sequential Layout", "ノンシーケンシャルレイアウト");
        Add("手动调整", "Manual Adjustment", "手動調整");
        Add("自动优化", "Automatic Optimization", "自動最適化");
        Add("搜索算法", "Search Algorithms", "探索アルゴリズム");
        Add("公差分析", "Tolerance Analysis", "公差解析");
        Add("公差数据可视化", "Tolerance Visualization", "公差データ可視化");
        Add("制造准备", "Manufacturing Preparation", "製造準備");
        Add("光学制图", "Optical Drawings", "光学製図");
        Add("光学材料", "Optical Materials", "光学材料");
        Add("镜头设计", "Lens Design", "レンズ設計");
        Add("结构生成", "Structure Generation", "構成生成");
        Add("镀膜设计", "Coating Design", "コーティング設計");
        Add("装调计算", "Alignment Calculation", "組立調整計算");
        Add("页面窗口布局", "Page Window Layout", "ページウィンドウ配置");
        Add("页面", "Pages", "ページ");
        Add("布局", "Layout", "レイアウト");
        Add("支持", "Support", "サポート");
        Add("编辑", "Edit", "編集");
        Add("交换与示例", "Exchange & Samples", "交換とサンプル");

        Add("新建", "New", "新規");
        Add("打开", "Open", "開く");
        Add("保存", "Save", "保存");
        Add("导入", "Import", "インポート");
        Add("导出", "Export", "エクスポート");
        Add("示例", "Samples", "サンプル");
        Add("撤销", "Undo", "元に戻す");
        Add("重做", "Redo", "やり直す");
        Add("当前没有可撤销的操作", "There is nothing to undo", "元に戻せる操作はありません");
        Add("当前没有可重做的操作", "There is nothing to redo", "やり直せる操作はありません");
        Add("非序列模式", "Non-Sequential Mode", "ノンシーケンシャルモード");
        Add("顺序模式", "Sequential Mode", "シーケンシャルモード");
        Add("非序列", "Non-Sequential", "ノンシーケンシャル");
        Add("对象", "Objects", "オブジェクト");
        Add("对象数据", "Object Data", "オブジェクトデータ");
        Add("文档", "Documents", "ドキュメント");
        Add("工作区", "Workspace", "ワークスペース");
        Add("系统选项", "System Options", "システムオプション");
        Add("镜头数据", "Lens Data", "レンズデータ");
        Add("多配置", "Multi-Configuration", "マルチコンフィグ");
        Add("格式与字体", "Format & Font", "書式とフォント");
        Add("2D视图", "2D View", "2Dビュー");
        Add("3D视图", "3D View", "3Dビュー");
        Add("实体模型", "Solid Model", "ソリッドモデル");
        Add("快速聚焦", "Quick Focus", "クイックフォーカス");
        Add("快速调整", "Quick Adjust", "クイック調整");
        Add("滑块", "Slider", "スライダー");
        Add("可视化优化器", "Visual Optimizer", "ビジュアル最適化");
        Add("评价函数编辑器", "Merit Function Editor", "メリット関数エディター");
        Add("优化向导", "Optimization Wizard", "最適化ウィザード");
        Add("执行优化", "Run Optimization", "最適化を実行");
        Add("移除所有变量", "Remove All Variables", "すべての変数を削除");
        Add("设全部半径变量", "Set All Radius Variables", "全半径を変数に設定");
        Add("设全部厚度变量", "Set All Thickness Variables", "全厚さを変数に設定");
        Add("随机扰动搜索", "Random Perturbation Search", "ランダム摂動探索");
        Add("玻璃替换模板", "Glass Replacement Template", "ガラス置換テンプレート");
        Add("公差数据编辑器", "Tolerance Data Editor", "公差データエディター");
        Add("公差数据查看器", "Tolerance Data Viewer", "公差データビューアー");
        Add("直方图", "Histogram", "ヒストグラム");
        Add("良率", "Yield", "歩留まり");
        Add("公差报告", "Tolerance Report", "公差レポート");
        Add("可加工性评估", "Manufacturability Assessment", "製造性評価");
        Add("材料库", "Material Library", "材料ライブラリ");
        Add("材料分析", "Material Analysis", "材料解析");
        Add("色散图", "Dispersion Diagram", "分散図");
        Add("玻璃图", "Glass Map", "ガラスマップ");
        Add("无热化玻璃图", "Athermal Glass Map", "アサーマルガラスマップ");
        Add("内部透过率 vs. 波长", "Internal Transmittance vs. Wavelength", "内部透過率 vs. 波長");
        Add("色散 vs. 波长", "Dispersion vs. Wavelength", "分散 vs. 波長");
        Add("镜头库", "Lens Library", "レンズライブラリ");
        Add("库存镜头查看", "Stock Lens Browser", "在庫レンズ表示");
        Add("库存镜头匹配", "Stock Lens Matching", "在庫レンズ照合");
        Add("AI 初始结构", "AI Initial Structure", "AI 初期構成");
        Add("光学镀膜设计", "Optical Coating Design", "光学コーティング設計");
        Add("光学装调", "Optical Alignment", "光学組立調整");
        Add("保留分栏停靠", "Keep Split Docking", "分割ドッキングを維持");
        Add("合并单窗格", "Merge into One Pane", "単一ペインに統合");
        Add("全部独立浮动", "Float All", "すべてフロート");
        Add("平铺全部", "Tile All", "すべて並べて表示");
        Add("层叠全部", "Cascade All", "すべて重ねて表示");
        Add("克隆分析", "Clone Analysis", "解析を複製");
        Add("切换锁定", "Toggle Lock", "ロック切替");
        Add("关闭其他页", "Close Other Pages", "他のページを閉じる");
        Add("保存默认", "Save Default", "既定として保存");
        Add("载入默认", "Load Default", "既定を読み込む");
        Add("操作数帮助", "Operand Help", "オペランドヘルプ");
        Add("用户指南", "User Guide", "ユーザーガイド");
        Add("恢复未保存工程", "Recover Unsaved Project", "未保存プロジェクトを復元");
        Add("软件版本与更新", "Software Version and Updates", "ソフトウェアのバージョンと更新");
        Add("检查更新", "Check for Updates", "更新を確認");
        Add("当前版本", "Installed version", "現在のバージョン");
        Add("最新正式版本", "Latest stable version", "最新の安定版");
        Add("打开 GitHub 发布页", "Open GitHub Releases", "GitHub リリースを開く");
        Add("在线升级（预留）", "Online upgrade (reserved)", "オンライン更新（準備中）");
        Add("点击检查以查询 GitHub 上的正式版本。", "Click Check to query the stable release on GitHub.", "確認を押すと GitHub の安定版を照会します。");
        Add("在线自动安装尚未开放。请在 GitHub 下载完整安装包，关闭软件后手动安装。", "Automatic installation is not available. Download the full installer from GitHub, close the app, then install manually.", "自動インストールは未対応です。GitHub から完全なインストーラーを取得し、アプリを終了して手動でインストールしてください。");
        Add("仅手动检查；不会后台联网、自动下载或安装。未签名安装包不提供发布者身份认证。", "Manual checks only; no background requests, automatic downloads or installation. Unsigned installers do not authenticate the publisher.", "手動確認のみです。バックグラウンド通信、自動ダウンロード、自動インストールは行いません。未署名インストーラーは発行元を認証しません。");
        Add("正在检查更新，请稍候。", "Checking for updates. Please wait.", "更新を確認中です。お待ちください。");
        Add("发现新版本，可在 GitHub 发布页下载对应安装包。", "A new version is available. Download the matching installer from GitHub Releases.", "新しいバージョンがあります。GitHub リリースから対応するインストーラーを取得してください。");
        Add("当前版本无需升级（没有更高的正式版本）。", "No upgrade needed (no newer stable release).", "更新は不要です（より新しい安定版はありません）。");
        Add("暂未找到公开的正式版本（也可能仓库不可访问）。", "No public stable release found (the repository may also be inaccessible).", "公開安定版が見つかりません（リポジトリにアクセスできない場合もあります）。");
        Add("发现新版本，但尚无匹配的完整安装包。", "A new version was found, but no matching full installer is available yet.", "新しいバージョンはありますが、対応する完全なインストーラーはまだありません。");
        Add("发现新版本，此平台暂不提供受支持的安装包检测。", "A new version was found; installer detection is not supported on this platform yet.", "新しいバージョンはありますが、このプラットフォームのインストーラー確認は未対応です。");
        Add("GitHub 访问受限或请求过于频繁，请稍后重试。", "GitHub access is restricted or rate-limited. Try again later.", "GitHub へのアクセスが制限されています。しばらくしてから再試行してください。");
        Add("检查更新超时，请检查网络后重试。", "The update check timed out. Check your connection and try again.", "更新確認がタイムアウトしました。ネットワークを確認して再試行してください。");
        Add("无法连接 GitHub，请检查网络后重试。", "Cannot connect to GitHub. Check your connection and try again.", "GitHub に接続できません。ネットワークを確認して再試行してください。");
        Add("当前软件版本无效，无法比较版本。", "The installed version is invalid and cannot be compared.", "現在のバージョンが無効なため比較できません。");
        Add("发布信息不完整或不可信，已拒绝此更新信息。", "Release information is incomplete or untrusted and has been rejected.", "リリース情報が不完全または信頼できないため拒否しました。");
        Add("无法打开浏览器，请稍后重试。", "Cannot open the browser. Try again later.", "ブラウザーを開けません。後で再試行してください。");
        Add("关于", "About", "バージョン情報");

        // Analysis ribbon categories and analysis names.
        Add("光线迹点", "Ray Tracing", "光線追跡");
        Add("报告", "Reports", "レポート");
        Add("像差分析", "Aberration Analysis", "収差解析");
        Add("波前", "Wavefront", "波面");
        Add("点扩散函数", "Point Spread Function", "点像分布関数");
        Add("MTF 曲线", "MTF Curves", "MTF 曲線");
        Add("圈入能量", "Encircled Energy", "包囲エネルギー");
        Add("扩展图像分析", "Extended Image Analysis", "拡張画像解析");
        Add("光线追迹", "Ray Trace", "光線追跡");
        Add("探测器", "Detectors", "検出器");
        Add("光线数据库", "Ray Database", "光線データベース");
        Add("单光线追迹", "Single Ray Trace", "単一光線追跡");
        Add("表面数据报告", "Prescription Report", "面データレポート");
        Add("系统数据报告", "System Data Report", "システムデータレポート");
        Add("分类数据报告", "Classified Data Report", "分類データレポート");
        Add("系统数据摘要", "First Order", "一次特性");
        Add("光线像差图", "Ray Fan", "光線収差図");
        Add("标准点列图", "Standard Spot Diagram", "標準スポットダイアグラム");
        Add("光迹图", "Footprint Diagram", "フットプリント図");
        Add("离焦点列图", "Through-Focus Spot Diagram", "焦点通過スポット図");
        Add("全视场点列图", "Full-Field Spot Diagram", "全視野スポット図");
        Add("矩阵点列图", "Matrix Spot Diagram", "マトリクススポット図");
        Add("结构矩阵点列图", "Configuration Matrix Spot Diagram", "構成マトリクススポット図");
        Add("基面数据", "Cardinal Points Data", "基準面データ");
        Add("渐晕图", "Vignetting Diagram", "ビネッティング図");
        Add("网格畸变", "Grid Distortion", "グリッド歪曲");
        Add("场曲/畸变", "Field Curvature / Distortion", "像面湾曲／歪曲");
        Add("轴向像差", "Axial Aberration", "軸上収差");
        Add("垂轴色差", "Lateral Color", "倍率色収差");
        Add("色焦移", "Chromatic Focal Shift", "色焦点移動");
        Add("赛德尔系数", "Seidel Coefficients", "ザイデル係数");
        Add("赛德尔图", "Seidel Diagram", "ザイデル図");
        Add("衍射", "Diffraction", "回折");
        Add("几何", "Geometric", "幾何");
        Add("几何线/边缘扩散", "Geometric Line/Edge Spread", "幾何線／エッジ広がり");
        Add("扩展光源", "Extended Source", "拡張光源");
        Add("光瞳像差", "Pupil Aberration", "瞳収差");
        Add("全视场像差", "Full-Field Aberration", "全視野収差");
        Add("RMS vs. 视场", "RMS vs. Field", "RMS vs. 視野");
        Add("RMS vs. 波长", "RMS vs. Wavelength", "RMS vs. 波長");
        Add("RMS vs. 离焦", "RMS vs. Defocus", "RMS vs. デフォーカス");
        Add("二维视场RMS图", "2D Field RMS Map", "2D 視野 RMS マップ");
        Add("入射角 vs. 像高", "Incidence Angle vs. Image Height", "入射角 vs. 像高");
        Add("FFT PSF截面图", "FFT PSF Cross Section", "FFT PSF 断面図");
        Add("FFT 线/边缘扩散", "FFT Line/Edge Spread", "FFT 線／エッジ広がり");
        Add("惠更斯PSF", "Huygens PSF", "ホイヘンス PSF");
        Add("惠更斯PSF截面图", "Huygens PSF Cross Section", "ホイヘンス PSF 断面図");
        Add("傅里叶 MTF", "Fourier MTF", "フーリエ MTF");
        Add("傅里叶离焦 MTF", "Fourier Through-Focus MTF", "フーリエ焦点通過 MTF");
        Add("傅里叶 MTF VS 视场", "Fourier MTF vs. Field", "フーリエ MTF vs. 視野");
        Add("惠更斯 MTF", "Huygens MTF", "ホイヘンス MTF");
        Add("惠更斯离焦 MTF", "Huygens Through-Focus MTF", "ホイヘンス焦点通過 MTF");
        Add("惠更斯 MTF VS 视场", "Huygens MTF vs. Field", "ホイヘンス MTF vs. 視野");
        Add("几何 MTF", "Geometric MTF", "幾何 MTF");
        Add("几何离焦 MTF", "Geometric Through-Focus MTF", "幾何焦点通過 MTF");
        Add("几何 MTF VS 视场", "Geometric MTF vs. Field", "幾何 MTF vs. 視野");
        Add("光程差图", "Optical Path Difference", "光路差図");
        Add("波前图", "Wavefront Map", "波面図");
        Add("干涉图", "Interferogram", "干渉図");
        Add("傅科分析", "Foucault Analysis", "フーコー解析");
        Add("对比度损失图", "Contrast Loss", "コントラスト損失図");
        Add("质心球波前", "Centroid-Reference Wavefront", "重心基準波面");
        Add("最佳拟合波前", "Best-Fit Wavefront", "最適合波面");
        Add("图像模拟", "Image Simulation", "画像シミュレーション");
        Add("几何图像分析", "Geometric Image Analysis", "幾何画像解析");
        Add("几何位图图像分析", "Geometric Bitmap Image Analysis", "幾何ビットマップ画像解析");
        Add("光源分析", "Light Source Analysis", "光源解析");
        Add("部分相干图像分析", "Partially Coherent Image Analysis", "部分コヒーレント画像解析");
        Add("相对照度", "Relative Illumination", "相対照度");
        Add("非相干照度", "Incoherent Irradiance", "インコヒーレント照度");
        Add("辐射强度", "Radiant Intensity", "放射強度");
        Add("IMA和BIM图片浏览器", "IMA/BIM Image Viewer", "IMA/BIM 画像ビューアー");
        Add("位图文件查看器", "Bitmap File Viewer", "ビットマップファイルビューアー");
        Add("追迹控制", "Trace Control", "追跡制御");
        Add("探测器查看器", "Detector Viewer", "検出器ビューアー");
        Add("清空探测器", "Clear Detectors", "検出器をクリア");
        Add("光线数据库查看器", "Ray Database Viewer", "光線データベースビューアー");
        Add("路径分析", "Path Analysis", "経路解析");
        Add("非序列 3D 布局", "Non-Sequential 3D Layout", "ノンシーケンシャル 3D レイアウト");
        Add("STAROPT 光学设计项目", "STAROPT Optical Design Project", "STAROPT 光学設計プロジェクト");
        Add("光学数据快照", "Optical Data Snapshot", "光学データスナップショット");
        Add("序列光学格式", "Sequential Optical Formats", "シーケンシャル光学形式");
        Add("Zemax 光学系统", "Zemax Optical System", "Zemax 光学システム");
        Add("序列光学文本", "Sequential Optical Text", "シーケンシャル光学テキスト");
        Add("STEP CAD 模型", "STEP CAD Model", "STEP CAD モデル");

        // Common panels, editors and dialogs.
        Add("名称", "Name", "名前");
        Add("类型", "Type", "種類");
        Add("值", "Value", "値");
        Add("状态", "Status", "状態");
        Add("参数", "Parameters", "パラメーター");
        Add("材料", "Material", "材料");
        Add("表面", "Surface", "面");
        Add("表面数", "Surface Count", "面数");
        Add("视场数", "Field Count", "視野数");
        Add("波长数", "Wavelength Count", "波長数");
        Add("半径", "Radius", "半径");
        Add("厚度", "Thickness", "厚さ");
        Add("半口径", "Semi-Diameter", "半口径");
        Add("波长", "Wavelength", "波長");
        Add("视场", "Field", "視野");
        Add("权重", "Weight", "重み");
        Add("备注", "Notes", "備考");
        Add("常规", "General", "一般");
        Add("高级", "Advanced", "詳細");
        Add("系统孔径", "System Aperture", "システム開口");
        Add("孔径", "Aperture", "開口");
        Add("孔径类型", "Aperture Type", "開口タイプ");
        Add("孔径值", "Aperture Value", "開口値");
        Add("入瞳直径", "Entrance Pupil Diameter", "入射瞳径");
        Add("像方 F 数", "Image-Space F/#", "像側 F ナンバー");
        Add("物方数值孔径", "Object-Space Numerical Aperture", "物体側開口数");
        Add("按光阑面尺寸浮动", "Float by Stop Surface Size", "絞り面サイズに連動");
        Add("光瞳切趾", "Pupil Apodization", "瞳アポダイゼーション");
        Add("切趾参数", "Apodization Parameter", "アポダイゼーション係数");
        Add("视场类型", "Field Type", "視野タイプ");
        Add("视场数据", "Field Data", "視野データ");
        Add("波长数据", "Wavelength Data", "波長データ");
        Add("偏振", "Polarization", "偏光");
        Add("环境", "Environment", "環境");
        Add("计算后端", "Compute Backend", "計算バックエンド");
        Add("当前玻璃库", "Current Glass Catalogs", "現在のガラスカタログ");
        Add("可用玻璃库", "Available Glass Catalogs", "利用可能なガラスカタログ");
        Add("加入当前", "Add to Current", "現在の一覧に追加");
        Add("移出当前", "Remove from Current", "現在の一覧から削除");
        Add("优先级上移", "Move Priority Up", "優先度を上げる");
        Add("优先级下移", "Move Priority Down", "優先度を下げる");
        Add("添加视场", "Add Field", "視野を追加");
        Add("添加波长", "Add Wavelength", "波長を追加");
        Add("标签", "Label", "ラベル");
        Add("X 渐晕", "X Vignetting", "X ビネッティング");
        Add("Y 渐晕", "Y Vignetting", "Y ビネッティング");
        Add("X 瞳孔偏移", "X Pupil Shift", "X 瞳シフト");
        Add("Y 瞳孔偏移", "Y Pupil Shift", "Y 瞳シフト");
        Add("瞳孔旋转 (°)", "Pupil Rotation (°)", "瞳回転 (°)");
        Add("主波长", "Primary Wavelength", "主波長");
        Add("温度 (°C)", "Temperature (°C)", "温度 (°C)");
        Add("压力 (ATM)", "Pressure (ATM)", "圧力 (ATM)");
        Add("非偏振", "Unpolarized", "非偏光");
        Add("参考轴", "Reference Axis", "基準軸");
        Add("物方远心", "Object-Space Telecentric", "物体側テレセントリック");
        Add("像方无焦", "Image-Space Afocal", "像側アフォーカル");
        Add("折射率数据与环境匹配", "Match Index Data to Environment", "屈折率データを環境に合わせる");
        Add("标注", "Comment", "注記");
        Add("表面类型", "Surface Type", "面タイプ");
        Add("曲率半径", "Radius of Curvature", "曲率半径");
        Add("净口径", "Clear Semi-Diameter", "有効半径");
        Add("物面", "Object", "物体面");
        Add("像面", "Image", "像面");
        Add("标准面", "Standard", "標準面");
        Add("无限", "Infinity", "無限");
        Add("膜层", "Coating", "コーティング");
        Add("延伸区", "Extension Zone", "拡張領域");
        Add("机械半直径", "Mechanical Semi-Diameter", "機械半径");
        Add("圆锥系数", "Conic", "円錐係数");
        Add("表面属性", "Surface Properties", "面プロパティ");
        Add("上一面", "Previous Surface", "前の面");
        Add("下一面", "Next Surface", "次の面");
        Add("展开表面属性", "Expand Surface Properties", "面プロパティを展開");
        Add("收起表面属性", "Collapse Surface Properties", "面プロパティを折りたたむ");
        Add("应用偏振", "Apply Polarization", "偏光を適用");
        Add("添加膜层", "Add Coating Layer", "膜層を追加");
        Add("应用膜层", "Apply Coating", "膜層を適用");
        Add("删除此层", "Delete This Layer", "この層を削除");
        Add("基础厚度", "Base Thickness", "基準厚さ");
        Add("厚度倍率", "Thickness Multiplier", "厚さ倍率");
        Add("折射率偏移", "Index Offset", "屈折率オフセット");
        Add("消光系数偏移", "Extinction Offset", "消衰係数オフセット");
        Add("搜索功能 (Ctrl+Q)", "Search commands (Ctrl+Q)", "コマンドを検索 (Ctrl+Q)");
        Add("搜索功能", "Search Commands", "コマンド検索");
        Add("功能搜索结果", "Command Search Results", "コマンド検索結果");
        Add("没有匹配的功能", "No matching commands", "一致するコマンドはありません");
        Add("搜索", "Search", "検索");
        Add("刷新", "Refresh", "更新");
        Add("添加", "Add", "追加");
        Add("删除", "Delete", "削除");
        Add("复制", "Copy", "コピー");
        Add("关闭", "Close", "閉じる");
        Add("取消", "Cancel", "キャンセル");
        Add("确定", "OK", "OK");
        Add("应用", "Apply", "適用");
        Add("重置", "Reset", "リセット");
        Add("继续", "Continue", "続行");
        Add("是", "Yes", "はい");
        Add("否", "No", "いいえ");
        Add("启用", "Enabled", "有効");
        Add("禁用", "Disabled", "無効");
        Add("固定", "Fixed", "固定");
        Add("变量", "Variable", "変数");
        Add("自动", "Automatic", "自動");
        Add("手动", "Manual", "手動");
        Add("无", "None", "なし");
        Add("全部", "All", "すべて");
        Add("默认", "Default", "既定");
        Add("主题", "Theme", "テーマ");
        Add("从主题列表选择外观，下方可预览配色", "Choose a theme; preview its colours below", "テーマを選択し、下で配色をプレビューできます");
        Add("字体", "Font", "フォント");
        Add("字号", "Font Size", "フォントサイズ");
        Add("小数位数", "Decimal Places", "小数点以下の桁数");
        Add("操作失败", "Operation Failed", "操作に失敗しました");
        Add("正在计算…", "Calculating…", "計算中…");
        Add("计算", "Calculate", "計算");
        Add("结果", "Results", "結果");
        Add("数据", "Data", "データ");
        Add("图表", "Chart", "グラフ");
        Add("设置已保存", "Settings saved", "設定を保存しました");
        Add("保存设置失败：", "Could not save settings: ", "設定を保存できませんでした: ");
        Add("就绪", "Ready", "準備完了");
        Add("已修改", "Modified", "変更済み");
        Add("角度", "Angle", "角度");
        Add("均匀", "Uniform", "均一");
        Add("高斯", "Gaussian", "ガウス");
        Add("面向光学系统设计、光线追迹与像质分析的桌面软件。",
            "Desktop software for optical system design, ray tracing, and image-quality analysis.",
            "光学システム設計、光線追跡、像質解析のためのデスクトップソフトウェアです。");
        Add("S.T.A.R. Labs 出品", "Created by S.T.A.R. Labs", "S.T.A.R. Labs 制作");

        return values;
    }
}
