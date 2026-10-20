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

    public static string Text(string source) => Text(source, CurrentLanguage);

    public static string Text(string source, string language)
    {
        language = NormalizeLanguage(language);
        if (language == Chinese || string.IsNullOrEmpty(source))
        {
            return source;
        }

        if (Texts.TryGetValue(source, out var translation))
        {
            return language == Japanese ? translation.Japanese : translation.English;
        }

        // Form labels commonly append a full-width colon to a translated caption.
        if (source.EndsWith('：') && Texts.TryGetValue(source[..^1], out translation))
        {
            var localized = language == Japanese ? translation.Japanese : translation.English;
            return localized + (language == Japanese ? "：" : ":");
        }

        // Accelerator labels use the same visible text as their ordinary ribbon tab.
        var acceleratorStart = source.LastIndexOf("(_", StringComparison.Ordinal);
        if (acceleratorStart > 0 && source.EndsWith(')'))
        {
            var prefix = source[..acceleratorStart];
            if (Texts.TryGetValue(prefix, out translation))
            {
                var localized = language == Japanese ? translation.Japanese : translation.English;
                return localized + source[acceleratorStart..];
            }
        }

        // Cards assemble summaries from canonical fragments at runtime. Translate only the
        // recognized fragments so numeric values, units and imported text remain untouched.
        if (source.Contains(" · ", StringComparison.Ordinal))
        {
            return string.Join(" · ", source.Split(" · ", StringSplitOptions.None).Select(part => Text(part, language)));
        }

        if (source.StartsWith("表面 ", StringComparison.Ordinal)
            && source.EndsWith(" 属性", StringComparison.Ordinal))
        {
            var number = source[3..^3];
            return language == Japanese
                ? $"面 {number} プロパティ"
                : $"Surface {number} Properties";
        }

        if (source.StartsWith("结构 ", StringComparison.Ordinal)
            && source[3..].Split('/') is [var current, var total]
            && int.TryParse(current, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && int.TryParse(total, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return (language == Japanese ? "構成 " : "Configuration ") + source[3..];
        }

        if (source.StartsWith("结构 ", StringComparison.Ordinal)
            && int.TryParse(source[3..].TrimEnd('*'), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return (language == Japanese ? "構成 " : "Configuration ") + source[3..];

        if (source.StartsWith("活动: ", StringComparison.Ordinal)
            && source[4..].Split('/') is [var active, var count]
            && int.TryParse(active, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return (language == Japanese ? "アクティブ: " : "Active: ") + source[4..];

        if (source.StartsWith("操作数 ", StringComparison.Ordinal) && source.EndsWith(" 属性", StringComparison.Ordinal)
            && int.TryParse(source[4..^3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return language == Japanese ? $"オペランド {source[4..^3]} プロパティ" : $"Operand {source[4..^3]} Properties";

        if (TryTranslatePrefixedValue(source, language, "版本 ", "Version ", "バージョン ", out var prefixed)
            || TryTranslatePrefixedValue(source, language, "未切换结构：", "Configuration not switched: ", "構成を切り替えられません：", out prefixed)
            || TryTranslatePrefixedValue(source, language, "波长 ", "Wavelength ", "波長 ", out prefixed)
            || TryTranslatePrefixedValue(source, language, "视场 ", "Field ", "視野 ", out prefixed)
            || TryTranslatePrefixedValue(source, language, "权重 ", "Weight ", "重み ", out prefixed))
        {
            return prefixed;
        }

        return source;
    }

    private static bool TryTranslatePrefixedValue(
        string source,
        string language,
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

        var localizedPrefix = language == Japanese ? japanesePrefix : englishPrefix;
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
        Add("停靠到左侧", "Dock Left", "左側にドッキング");
        Add("停靠到右侧", "Dock Right", "右側にドッキング");
        Add("停靠到上方", "Dock Above", "上側にドッキング");
        Add("停靠到下方", "Dock Below", "下側にドッキング");
        Add("合并为标签页", "Merge as Tabs", "タブとしてまとめる");
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
        Add("检查更新", "Check for Updates", "更新を確認");
        Add("当前版本", "Installed version", "現在のバージョン");
        Add("最新正式版本", "Latest stable version", "最新の安定版");
        Add("打开发布页", "Open release page", "リリースページを開く");
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
        Add("删除此视场", "Delete Field", "この視野を削除");
        Add("删除此波长", "Delete Wavelength", "この波長を削除");
        Add("X 相位 / °", "X Phase / °", "X 位相 / °");
        Add("Y 相位 / °", "Y Phase / °", "Y 位相 / °");
        Add("因子", "Factor", "係数");
        Add("余弦立方", "Cosine Cubed", "余弦の三乗");
        Add("旧版设置（保留）", "Legacy Setting (Preserved)", "旧設定（保持）");
        Add("应用系统偏振设置", "Apply System Polarization Settings", "システム偏光設定を適用");
        Add("系统非偏振", "System Unpolarized", "システムの非偏光設定");
        Add("偏振参考轴", "Polarization Reference Axis", "偏光基準軸");
        Add("Jones X 相位（度）", "Jones X Phase (degrees)", "Jones X 位相（度）");
        Add("Jones Y 相位（度）", "Jones Y Phase (degrees)", "Jones Y 位相（度）");
        Add("偏振设置已更新。", "Polarization settings updated.", "偏光設定を更新しました。");
        Add("偏振参数必须为有限数值；小数使用英文句点。", "Polarization parameters must be finite numbers; use a decimal point.", "偏光パラメーターには有限の数値を入力し、小数点にはピリオドを使用してください。");
        Add("CODA 始终使用所填 Jones 输入；RRET 使用参考轴。照度偏振计算遵循非偏振开关。", "CODA always uses the entered Jones input; RRET uses the reference axis. Polarized irradiance calculations follow the unpolarized setting.", "CODA は入力された Jones 値を、RRET は基準軸を使用します。照度の偏光計算は非偏光設定に従います。");
        Add("当前列表按从上到下的顺序解析未指定厂商的玻璃名称；至少保留一个目录。", "Glass names without a manufacturer are resolved from top to bottom; keep at least one catalog.", "メーカー未指定のガラス名は上から順に検索します。カタログを少なくとも一つ残してください。");
        Add("将选中的可用玻璃库加入当前系统。", "Add the selected available glass catalog to the current system.", "選択した利用可能なガラスカタログを現在のシステムに追加します。");
        Add("将选中的当前玻璃库移回可用列表。", "Move the selected current glass catalog back to the available list.", "選択した現在のガラスカタログを利用可能な一覧に戻します。");
        Add("提高当前玻璃库的同名玻璃解析优先级。", "Increase this catalog's priority when resolving glass names.", "同名ガラスの検索でこのカタログの優先度を上げます。");
        Add("降低当前玻璃库的同名玻璃解析优先级。", "Decrease this catalog's priority when resolving glass names.", "同名ガラスの検索でこのカタログの優先度を下げます。");
        Add("当前仅保存环境参数，暂不启用温度补偿计算。", "Environment parameters are saved only; temperature compensation is not enabled.", "環境パラメーターは保存のみ行い、温度補償計算は有効になりません。");
        Add("Ctrl+Q 聚焦；输入命令名称、类别或标识；上下选择，Enter 执行，Esc 关闭。", "Ctrl+Q focuses search. Enter a command name, category, or ID; use arrow keys to select, Enter to run, and Esc to close.", "Ctrl+Q で検索に移動します。コマンド名、分類、ID を入力し、上下キーで選択、Enter で実行、Esc で閉じます。");
        Add("搜索命令、面板或动作", "Search commands, panels, or actions", "コマンド、パネル、操作を検索");
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
        Add("多重结构", "Multi-Configuration", "マルチコンフィグレーション");
        Add("配置概览", "Configuration Overview", "構成一覧");
        Add("操作数行表", "Operand Matrix", "オペランド行列");
        Add("新增配置", "Add Configuration", "構成を追加");
        Add("激活配置", "Activate Configuration", "構成をアクティブ化");
        Add("应用厚度", "Apply Thickness", "厚さを適用");
        Add("系统孔径属性", "System Aperture Properties", "システム開口プロパティ");
        Add("厚度差异属性", "Thickness Difference Properties", "厚さ差分プロパティ");
        Add("未登记的有限厚度差异也会显示为 THIC 行，可编辑厚度但不占用 Op#；设置变量或拾取前需新增对应操作数行。", "Unregistered finite thickness differences also appear as THIC rows. Their thickness is editable without an Op#; add the corresponding operand row before setting variables or pickups.", "未登録の有限な厚さ差分も THIC 行として表示します。厚さは編集できますが Op# はありません。変数やピックアップを設定する前に、対応するオペランド行を追加してください。");
        Add("厚度差异尚未登记为操作数行；可新增相同类型和表面号后设置变量或拾取。", "This thickness difference is not an operand row; add its type and surface before setting variables or pickups.", "この厚さ差分はオペランド行に未登録です。同じタイプと面を追加してから変数やピックアップを設定してください。");
        Add("系统孔径（只读）", "System Aperture (read-only)", "システム開口（読み取り専用）");
        Add("展开操作数属性", "Expand Operand Properties", "オペランドプロパティを展開");
        Add("收起操作数属性", "Collapse Operand Properties", "オペランドプロパティを折りたたむ");
        Add("上一个操作数", "Previous Operand", "前のオペランド");
        Add("下一个操作数", "Next Operand", "次のオペランド");
        Add("已是第一行", "Already at the first row", "最初の行です");
        Add("已是最后一行", "Already at the last row", "最後の行です");
        Add("双击列标题激活结构", "Double-click the column header to activate this configuration", "列見出しをダブルクリックして構成をアクティブ化");
        Add("单击选择单元格，双击展开属性编辑。", "Click to select a cell; double-click to open its properties.", "クリックでセル選択、ダブルクリックでプロパティを表示します。");
        Add("APER 为只读系统孔径，请在系统选项中修改。", "APER is a read-only system aperture; edit it in System Options.", "APER は読み取り専用のシステム開口です。システムオプションで編集してください。");
        Add("请选择可编辑操作数；APER 请在系统选项中修改。", "Select an editable operand; edit APER in System Options.", "編集可能なオペランドを選択してください。APER はシステムオプションで編集します。");
        Add("Op# 为可编辑操作数行号。APER 为只读系统孔径，请在系统选项中修改；它不占用 Op#，不改变拾取或评价函数行引用。", "Op# identifies editable operand rows. APER shows the read-only system aperture; edit it in System Options. It has no Op# and does not shift pickup or merit-function row references.", "Op# は編集可能なオペランド行番号です。APER は読み取り専用のシステム開口で、システムオプションで編集します。Op# を消費せず、ピックアップや評価関数の行参照は変わりません。");
        Add("新增行", "Add Row", "行を追加");
        Add("上移", "Move Up", "上へ移動");
        Add("下移", "Move Down", "下へ移動");
        Add("删除行", "Remove Row", "行を削除");
        Add("配置号", "Configuration", "構成番号");
        Add("应用数值", "Apply Value", "値を適用");
        Add("还原输入", "Restore Input", "入力を元に戻す");
        Add("设为变量", "Set Variable", "変数に設定");
        Add("取消变量", "Clear Variable", "変数を解除");
        Add("V = 联合变量；P = 拾取；其他配置无需先激活", "V = joint variable; P = pickup; other configurations need not be activated first", "V = 連成変数、P = ピックアップ。別構成をアクティブ化せず編集できます");
        Add("拾取设置（P）", "Pickup Settings (P)", "ピックアップ設定（P）");
        Add("应用拾取", "Apply Pickup", "ピックアップを適用");
        Add("移除拾取", "Remove Pickup", "ピックアップを削除");
        Add("源行", "Source Row", "参照元行");
        Add("源配置", "Source Configuration", "参照元構成");
        Add("比例", "Scale", "倍率");
        Add("偏移", "Offset", "オフセット");
        Add("当前单元格 = 源值 × 比例 + 偏移。源行号、源配置号均不能晚于目标；移除拾取保留当前数值。", "Cell = source value × scale + offset. Source row and configuration cannot follow the target; removing a pickup keeps the current value.", "セル = 参照元の値 × 倍率 + オフセット。参照元の行・構成は参照先より後にはできません。解除時は現在値を保持します。");
        Add("请先应用或还原当前单元格输入。", "Apply or restore the current cell input first.", "先に現在のセル入力を適用するか元に戻してください。");
        Add("请先应用拾取设置或还原输入。", "Apply the pickup settings or restore the input first.", "先にピックアップ設定を適用するか入力を元に戻してください。");
        Add("工程已变化；当前输入已保留，请还原输入后重新编辑。", "The project changed; your input is preserved. Restore it before editing again.", "プロジェクトが変更されました。入力は保持されています。元に戻してから再編集してください。");
        Add("已重新读取当前单元格。", "Current cell reloaded.", "現在のセルを再読み込みしました。");
        Add("多配置行表已更新。", "Configuration matrix updated.", "構成行列を更新しました。");
        Add("厚度（mm）", "Thickness (mm)", "厚さ（mm）");
        Add("曲率（mm⁻¹）", "Curvature (mm⁻¹)", "曲率（mm⁻¹）");
        Add("半口径（mm）", "Semi-diameter (mm)", "半径（mm）");
        Add("多配置行类型", "Configuration Operand Type", "構成オペランドタイプ");
        Add("多配置行表面号", "Configuration Operand Surface", "構成オペランド面番号");
        Add("编辑配置号（从 1 开始）", "Editing Configuration (1-based)", "編集する構成番号（1から）");
        Add("多配置单元格值", "Configuration Cell Value", "構成セルの値");
        Add("拾取源行号", "Pickup Source Row", "ピックアップ参照元行番号");
        Add("拾取源配置号", "Pickup Source Configuration", "ピックアップ参照元構成番号");
        Add("拾取比例", "Pickup Scale", "ピックアップ倍率");
        Add("拾取偏移", "Pickup Offset", "ピックアップオフセット");
        Add("请输入有限数值；曲率单位为 mm⁻¹，厚度和半口径为 mm。", "Enter a finite number; curvature is in mm⁻¹, thickness and semi-diameter in mm.", "有限値を入力してください。曲率は mm⁻¹、厚さと半径は mm です。");
        Add("输入仍属于之前选择的单元格；请先还原输入，再编辑当前单元格。", "This input still belongs to the previous cell; restore it before editing the current cell.", "入力は前のセルに属しています。元に戻してから現在のセルを編集してください。");
        Add("打开多重结构编辑器", "Open Multi-Configuration Editor", "マルチコンフィグレーションエディタを開く");
        Add("上一个结构", "Previous Configuration", "前の構成");
        Add("下一个结构", "Next Configuration", "次の構成");
        Add("正在切换结构", "Switching configurations", "構成を切り替えています");
        Add("只有一个结构，无需切换", "Only one configuration; no switching is needed", "構成は1つだけのため、切り替えは不要です");
        Add("已是第一个结构", "Already at the first configuration", "最初の構成です");
        Add("已是最后一个结构", "Already at the last configuration", "最後の構成です");
        Add("请先修正当前单元格的输入，或按 Esc 还原后再切换结构。", "Correct the current cell or press Esc to restore it before switching configurations.", "構成を切り替える前に、現在のセルを修正するか Esc で元に戻してください。");
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
