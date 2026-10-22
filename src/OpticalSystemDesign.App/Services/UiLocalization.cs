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

    // ComboBox selection content can be displayed through a template without
    // loading the original item. Track its label before it enters a popup.
    internal static void LocalizeChoiceLabel(TextBlock label)
    {
        Track(label);
        LocalizeControl(label);
    }

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

        if (source.StartsWith("行：视场    列：结构（各格叠加波长）", StringComparison.Ordinal))
        {
            return string.Join(Environment.NewLine, source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select((line, index) =>
                index == 0 ? Text(line, language) : string.Join(" · ", line.Split(" · ", StringSplitOptions.None).Select(fragment =>
                {
                    var separator = fragment.IndexOf('：');
                    return separator < 0 ? Text(fragment, language)
                        : Text(fragment[..separator], language) + (language == Japanese ? "：" : ": ") + Text(fragment[(separator + 1)..], language);
                }))));
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

        if (source.StartsWith("结构：", StringComparison.Ordinal))
            return Text("结构", language) + ": " + string.Join(" · ", source[3..].Split(" · ")
                .Select(part => Text(part, language)));

        if (source.StartsWith("结构 ", StringComparison.Ordinal)
            && source.IndexOf(" · ", StringComparison.Ordinal) is var separator && separator > 3
            && int.TryParse(source[3..separator], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return (language == Japanese ? "構成 " : "Configuration ") + source[3..(separator + 3)]
                + Text(source[(separator + 3)..], language);

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

        // Initial-structure presentation; numeric values and search identities are not localized.
        Add("自动初始结构", "Automatic Initial Structure", "初期構造の自動生成");
        Add("自动初始结构工作区", "Initial Structure Workspace", "初期構造ワークスペース");
        Add("从预设开始", "Start from a Preset", "プリセットから開始");
        Add("仅载入目标参数，不自动开始搜索。", "Loads target parameters only; search starts separately.", "目標パラメータのみ読み込み、探索は別途開始します。");
        Add("来源与适配说明", "Source and Adaptations", "出典と調整内容");
        Add("光学系统", "Optical System", "光学系");
        Add("结构规模", "Structure Limits", "構造の制約");
        Add("像质与验收", "Quality and Acceptance", "画質と合格基準");
        Add("几何与后焦", "Geometry and Back Focus", "形状とバックフォーカス");
        Add("玻璃与目录", "Glasses and Catalogs", "ガラスとカタログ");
        Add("口径与光阑", "Apertures and Stop", "口径と絞り");
        Add("搜索策略", "Search Strategy", "探索戦略");
        Add("运行预算", "Run Budget", "実行予算");
        Add("计算后端：共享 Core · C# CPU", "Backend: shared Core · C# CPU", "計算基盤：共有 Core · C# CPU");
        Add("AutoLens 参数", "AutoLens Settings", "AutoLens 設定");
        Add("起始形式与光阑位置", "Starting Form and Stop", "初期形態と絞り位置");
        Add("候选细化预算", "Refinement Budget", "候補の改良予算");
        Add("候选方案", "Candidates", "候補設計");
        Add("等待生成候选", "Waiting for Candidates", "候補の生成待ち");
        Add("完成左侧设置后，先预检查，再开始形式搜索。", "Set up the design, run a precheck, then start the form search.", "設計を設定し、事前確認してから形態探索を開始してください。");
        Add("在这里查看实际光线", "Actual Ray Layout", "実際の光線レイアウト");
        Add("选择候选方案查看结构；设置 A / B 可在同一比例下比较。", "Select a candidate to inspect its layout; set A / B to compare at one scale.", "候補を選択して構造を確認し、A / B を設定すると同じ縮尺で比較できます。");
        Add("请先启用固定后焦距。", "Enable fixed back focus first.", "先にバックフォーカスの固定を有効にしてください。");
        Add("智能初始结构实验室", "Initial Structure Laboratory", "初期構造ラボ");
        Add("定义系统与目标 → 正负形式搜索 → 比较候选 → 继续优化", "Define targets → Search forms → Compare candidates → Refine", "目標を設定 → 形態探索 → 候補を比較 → 改良");
        Add("预检查", "Precheck", "事前確認");
        Add("开始形式搜索", "Start Search", "探索を開始");
        Add("停止并保存", "Stop and Save", "停止して保存");
        Add("恢复上次", "Restore Last Run", "前回の実行を復元");
        Add("上次实验", "Last Run", "前回の実行");
        Add("继续运行", "Resume", "実行を再開");
        Add("打开记录", "Open Run", "記録を開く");
        Add("细化选中方案", "Refine Selected", "選択候補を改良");
        Add("导出结构", "Export Structure", "構造をエクスポート");
        Add("设为 A", "Set A", "A に設定");
        Add("设为 B", "Set B", "B に設定");
        Add("清除比较", "Clear Comparison", "比較をクリア");
        Add("目标", "Targets", "目標");
        Add("结构与光线", "Layout and Rays", "構造と光線");
        Add("目标对照", "Target Comparison", "目標との比較");
        Add("处方", "Prescription", "処方");
        Add("视场明细", "Field Details", "画角の詳細");
        Add("来源与差距", "Provenance and Gaps", "出典と未達項目");
        Add("搜索计划与过程", "Search Plan and History", "探索計画と履歴");
        Add("尚未生成方案。输入设计目标后开始。", "No candidates yet. Enter your design targets to begin.", "候補はまだありません。設計目標を入力して開始してください。");
        Add("尚未生成方案。", "No candidates yet.", "候補はまだありません。");
        Add("选择方案查看实际布局与光线。", "Select a candidate to view the actual layout and rays.", "候補を選択すると実際の構造と光線を表示します。");
        Add("A 为蓝色，B 为橙色；共用毫米比例。", "A is blue, B is orange; both share the same millimeter scale.", "A は青、B はオレンジで、同じミリメートル縮尺を使用します。");
        Add("预检查通过；搜索计划与过程页可查看形式覆盖范围。", "Precheck passed. See Search Plan and History for form coverage.", "事前確認に合格しました。形態の網羅範囲は探索計画と履歴で確認できます。");
        Add("波长与权重", "Wavelengths and Weights", "波長と重み");
        Add("实验名称", "Run Name", "実行名");
        Add("焦距 mm", "Focal Length / mm", "焦点距離 / mm");
        Add("最大半视场 °", "Half-Field / °", "最大半画角 / °");
        Add("入瞳直径 mm", "Pupil Diameter / mm", "入射瞳径 / mm");
        Add("孔径定义", "Aperture Input", "口径の入力");
        Add("移除", "Remove", "削除");
        Add("方案", "Candidate", "候補");
        Add("起始形式", "Start Form", "初期形態");
        Add("片数", "Elements", "枚数");
        Add("最差 RMS mm", "Worst RMS / mm", "最悪 RMS / mm");
        Add("最低通光", "Min. Transmission", "最低光線通過率");
        Add("搜索方式", "Search Mode", "探索モード");
        Add("局部优化算法", "Local Optimizer", "局所最適化手法");
        Add("QUICK · 筛选后择优优化", "QUICK · Screen then refine", "QUICK · 選別後に改良");
        Add("FULL · 逐形式完整优化", "FULL · Optimize every form", "FULL · 各形態を完全最適化");
        Add("原生信赖域（默认）", "Native Trust Region (Default)", "ネイティブ信頼領域（既定）");
        Add("AutoLens 启发式 · C# CPU 试验", "AutoLens Heuristic · C# CPU Experiment", "AutoLens ヒューリスティック · C# CPU 実験");
        Add("AutoLens · C# 自动求导", "AutoLens · C# Automatic Differentiation", "AutoLens · C# 自動微分");
        Add("最少片数", "Min. Elements", "最少枚数");
        Add("最多片数", "Max. Elements", "最多枚数");
        Add("最大总长 mm", "Max. Track / mm", "最大全長 / mm");
        Add("最差 RMS 上限 mm", "Worst RMS Limit / mm", "最悪 RMS 上限 / mm");
        Add("最大光斑半径 mm", "Max. Spot Radius / mm", "最大スポット半径 / mm");
        Add("焦距容差 %", "Focal Tolerance / %", "焦点距離許容差 / %");
        Add("F/# 容差 %", "F/# Tolerance / %", "F/# 許容差 / %");
        Add("最低通光 %", "Min. Transmission / %", "最低光線通過率 / %");
        Add("起始试验上限", "Max. Start Trials", "初期試行の上限");
        Add("保留方案数", "Retained Candidates", "候補の保持数");
        Add("评价次数上限", "Max. Evaluations", "評価回数の上限");
        Add("时间上限 min", "Time Limit / min", "時間制限 / min");
        Add("并行任务数", "Parallel Workers", "並列タスク数");
        Add("随机种子", "Random Seed", "乱数シード");
        Add("最小中心厚度 mm", "Min. Center / mm", "最小中心厚 / mm");
        Add("最小空气隙 mm", "Min. Air Gap / mm", "最小空気間隔 / mm");
        Add("最小后焦 mm", "Min. Back Focus / mm", "最小バックフォーカス / mm");
        Add("最小边厚 mm", "Min. Edge / mm", "最小縁厚 / mm");
        Add("固定后焦距", "Fixed Back Focus", "バックフォーカスを固定");
        Add("固定后焦 mm", "Fixed Back Focus / mm", "固定バックフォーカス / mm");
        Add("允许的目录玻璃（逗号分隔）", "Allowed Glasses (Comma-Separated)", "許可ガラス（コンマ区切り）");
        Add("起始玻璃", "Initial Glass", "初期ガラス");
        Add("优先玻璃目录（逗号分隔）", "Preferred Catalogs (Comma-Separated)", "優先カタログ（コンマ区切り）");
        Add("使用默认目录", "Use default catalogs", "既定のカタログを使用");
        Add("镜片直径随光线自动调整", "Size lens diameters from actual rays", "実際の光線に合わせてレンズ径を調整");
        Add("实体光阑（自动标定并瞄准）", "Physical stop (calibration and aiming)", "実体絞り（自動校正と光線照準）");
        Add("半口径裕量", "Semi-Diameter Margin", "半口径の余裕係数");
        Add("AutoLens 面型", "AutoLens Surface Profile", "AutoLens 面形状");
        Add("仅球面", "Spherical Only", "球面のみ");
        Add("偶次非球面至 r⁴", "Even Asphere through r⁴", "r⁴ までの偶数次非球面");
        Add("偶次非球面至 r⁶", "Even Asphere through r⁶", "r⁶ までの偶数次非球面");
        Add("偶次非球面至 r⁸", "Even Asphere through r⁸", "r⁸ までの偶数次非球面");
        Add("偶次非球面至 r¹⁰", "Even Asphere through r¹⁰", "r¹⁰ までの偶数次非球面");
        Add("AutoLens 连续材料优化后匹配允许玻璃", "Optimize continuous materials, then match allowed glasses", "連続材料を最適化し、許可ガラスに照合");
        Add("AutoLens 胶合间隔（从 1 编号）", "AutoLens Cemented Gaps (Starting at 1)", "AutoLens 接合間隔（1 から番号付け）");
        Add("如 1,2；为空时全部空气间隔", "e.g. 1,2; leave blank for all air gaps", "例：1,2。空欄ではすべて空気間隔");
        Add("AutoLens 初始优化步数上限", "AutoLens Initial Iteration Limit", "AutoLens 初期最適化ステップ上限");
        Add("AutoLens 目录玻璃精修步数上限", "AutoLens Catalog Refinement Limit", "AutoLens カタログガラス改良ステップ上限");
        Add("完整目标原生精修评价上限", "Native Full-Target Polish Limit", "全目標ネイティブ精修の評価上限");
        Add("起始半径 mm（0 = 10 倍目标焦距）", "Initial Radius / mm (0 = 10 × Target Focal Length)", "初期半径 / mm（0 = 目標焦点距離の10倍）");
        Add("起始厚度 mm（0 自动）", "Initial Thickness / mm (0 = Auto)", "初期厚さ / mm（0 = 自動）");
        Add("起始空气隙 mm（0 自动）", "Initial Air Gap / mm (0 = Auto)", "初期空気間隔 / mm（0 = 自動）");
        Add("起始形式 + / - / *（固定片数时）", "Start Form + / - / * (Fixed Element Count)", "初期形態 + / - / *（固定枚数の場合）");
        Add("如 +*-；空白搜索全部组合", "e.g. +*-; leave blank for all forms", "例：+*-。空欄では全組み合わせを探索");
        Add("起始光阑面（0 = 中间面）", "Initial Stop Surface (0 = Middle)", "初期絞り面（0 = 中央の面）");
        Add("继续探索其他光阑位置", "Explore other stop positions", "他の絞り位置も探索");
        Add("追加细化次数", "Additional Refinement Evaluations", "追加改良の評価回数");
        Add("新实验按均匀照明圆瞳的面积 RMS 评价像质，独立加密验收；边缘光线必须全部通过。", "New runs evaluate area RMS over a uniformly illuminated circular pupil, with independent dense acceptance sampling; all boundary rays must pass.", "新しい実行では均一照明の円形瞳の面積 RMS で画質を評価し、独立した高密度サンプルで判定します。境界光線はすべて通過する必要があります。");
        Add("镜片外径按真实光束包络增减；实体光阑的通光口径由系统入瞳标定，不随镜片裕量放大。关闭实体光阑保留旧版近轴入瞳采样方式。", "Lens diameters follow the actual ray envelope. The physical stop is calibrated to the entrance pupil, not enlarged by the lens margin. Disabling it retains legacy paraxial pupil sampling.", "レンズ外径は実際の光束包絡に合わせます。実体絞りは入射瞳で校正し、レンズ余裕係数では拡大しません。無効時は従来の近軸瞳サンプリングを維持します。");
        Add("评价和时间预算始终优先；自动分配两阶段步数，不代表每次都会完成上述步数。", "Evaluation and time budgets take priority. Phase limits are allocated automatically and are not guaranteed iteration counts.", "評価回数と時間予算を優先します。2段階のステップは自動配分され、指定回数の完了を保証しません。");
        Add("C# 自动求导：共享 Core 导数、渐进口径、真实玻璃匹配后精修。仅支持当前中心旋转对称镜片；CPU 实现，未宣称与原版数值等效。旧启发式试验仍单独保留。", "C# automatic differentiation uses shared Core derivatives, progressive apertures and real-glass refinement. Limited to supported centered rotationally symmetric lenses; CPU only, without upstream numerical-equivalence claims. The heuristic experiment remains separate.", "C# 自動微分は共有 Core の導関数、段階的な口径、実在ガラス照合後の改良を使用します。対応する中心回転対称レンズのみで、CPU 実装です。原版との数値同等性は主張せず、旧ヒューリスティック実験は別に保持します。");
        Add("枚举正负初始形式，再比较与优化候选。+ / - 指起始方向，优化中可改变。自动厚度与空气隙使用目标页中的最小值。", "Enumerate signed starting forms, then compare and refine candidates. + / - are starting directions and may change during optimization. Automatic thickness and air gaps use the Targets tab minima.", "正負の初期形態を列挙し、候補を比較・最適化します。+ / - は初期方向であり最適化中に変わる場合があります。自動厚さ・空気間隔には目標タブの最小値を使用します。");
        Add("追加细化使用选中方案的原目标和新预算；不改变原实验。", "Refinement uses the selected candidate's original targets and a new budget; the source run stays unchanged.", "追加改良では選択候補の元の目標と新予算を使用し、元の実行は変更しません。");

        // Official AutoLens targets remain explicitly distinguished from upstream solutions.
        Add("AutoLens 官方目标预设", "AutoLens Official Target Presets", "AutoLens 公式目標プリセット");
        Add("载入预设", "Load Preset", "プリセットを読み込む");
        Add("预设来源与适配说明", "Preset Source and Adaptations", "プリセットの出典と調整内容");
        Add("选择官方目标预设；载入不会自动开始搜索。", "Choose an official target; loading does not start a search.", "公式目標を選択してください。読み込みだけでは探索を開始しません。");
        Add("请先选择官方目标预设。", "Select an official target preset first.", "先に公式目標プリセットを選択してください。");
        Add("已载入目标预设；尚未运行，不代表设计达标。", "Target preset loaded; not run yet. Design acceptance is not implied.", "目標を読み込みました。まだ実行しておらず、設計合格を意味しません。");
        Add("原生 C# CPU 适配 · 非官方成功处方 · 每次上限 4000 次评价 / 1 分钟", "Native C# CPU adaptation · Not an official solution · Limit: 4000 evaluations / 1 minute per run", "C# CPU ネイティブ調整版 · 公式の成功処方ではありません · 各実行の上限：4000 評価 / 1 分");
        Add("无限远物方 · 中心旋转对称定焦系统；半视场为光轴到边缘的角度。", "Infinite conjugate · Centered rotationally symmetric fixed-focus system; half-field is the axis-to-edge angle.", "無限遠物体 · 中心回転対称の単焦点系。半画角は光軸から端までの角度です。");
        Add("官方目标 1 · 广角 4.55 mm F/2", "Official Target 1 · Wide-Angle 4.55 mm F/2", "公式目標 1 · 広角 4.55 mm F/2");
        Add("官方目标 2 · 全画幅 50 mm F/2", "Official Target 2 · Full-Frame 50 mm F/2", "公式目標 2 · フルサイズ 50 mm F/2");
        Add("官方目标 3 · 广角 20 随机种子", "Official Target 3 · Wide-Angle, 20 Seeds", "公式目標 3 · 広角、20 シード");
        Add("官方目标 4A · F/1.2 相机（适配）", "Official Target 4A · F/1.2 Camera (Adapted)", "公式目標 4A · F/1.2 カメラ（調整版）");
        Add("官方目标 4B · 120° 手机（适配）", "Official Target 4B · 120° Mobile (Adapted)", "公式目標 4B · 120° モバイル（調整版）");
        Add("官方目标：4.55 mm、F/2、全视场 80°。片数、几何、玻璃、波长、像质门槛与 CPU 预算为本地适配；不是官方处方或成功保证。", "Published target: 4.55 mm, F/2, 80° full field. Element count, geometry, glasses, wavelengths, quality gates and CPU budget are local choices, not an upstream prescription or success guarantee.", "公式目標：4.55 mm、F/2、全画角 80°。枚数、形状、ガラス、波長、画質基準、CPU 予算はローカル調整であり、公式処方や成功保証ではありません。");
        Add("采用官方正文的 F/2，不采用动画替代文字中的 F/3。43.3 mm 对角线按理想无畸变关系换算半视场；实际像面覆盖尚非独立验收项。其余约束为本地适配。", "Uses F/2 from the README body, not F/3 from the animation alt text. The 43.3 mm diagonal is converted to a half-field assuming ideal rectilinear imaging; actual image coverage is not independently certified. Other constraints are local adaptations.", "本文の F/2 を採用し、動画の代替テキストの F/3 は採用しません。対角 43.3 mm は理想的な無歪曲投影で半画角に換算し、実際の像面範囲は独立認証していません。他の制約はローカル調整です。");
        Add("使用与目标 1 相同的光学指标。官方未公布图中 20 个种子的编号；本地协议使用 1–20。载入只设置首个种子，完整批量验证由专用验收工具执行。", "Uses Target 1 optics. The 20 upstream seed ids are unpublished; this protocol uses 1–20. Loading selects the first seed only; the dedicated validation tool runs the complete batch.", "目標 1 と同じ光学仕様です。公式図の 20 シード番号は未公開のため、ローカルでは 1–20 を使用します。読み込みは最初のシードのみを設定し、全件検証は専用ツールで行います。");
        Add("官方仅明确相机 F/1.2；本地补充 50 mm 和 43.3 mm 对角线，其余约束为适配。官方极端规格图未公布八组完整处方，不能宣称逐幅复现。", "Only camera F/1.2 is explicitly published. This adaptation adds 50 mm and a 43.3 mm diagonal with local constraints. The eight ultra-example prescriptions are unpublished, so image-by-image reproduction is not claimed.", "公式に明記されたカメラ仕様は F/1.2 のみです。50 mm、対角 43.3 mm と制約はローカル補完です。極端仕様図の 8 処方は未公開のため、各図の再現とは称しません。");
        Add("官方仅明确手机全视场 120°；本地补充 4.55 mm、F/2 及几何材料约束。使用当前真实目录玻璃，而非假定已支持官方塑料材料或原始处方。", "Only the 120° full field is explicitly published for the mobile example. This adaptation adds 4.55 mm, F/2 and geometry/material constraints. It uses supported real catalog glasses, not assumed upstream plastics or prescriptions.", "公式に明記されたモバイル仕様は全画角 120° のみです。4.55 mm、F/2、形状・材料制約はローカル補完です。公式の樹脂や処方を仮定せず、対応する実在カタログガラスを使用します。");

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
        Add("行：视场    列：结构（各格叠加波长）", "Rows: fields    Columns: configurations (wavelengths overlaid in each cell)", "行：視野    列：構成（各セルに波長を重ね合わせ）");
        Add("结构数", "Configurations", "構成数");
        Add("结构", "Configuration", "構成");
        Add("当前的", "Current", "現在の構成");
        Add("所有", "All", "すべて");
        Add("设置已更改，请同步", "Settings changed; refresh required", "設定が変更されました。同期してください");
        Add("各结构独立计算，按图例叠加显示。\n各结构指标见“数据”页；完整结果见“文本”页。",
            "Each configuration is calculated independently and overlaid by legend.\nSee Data for per-configuration metrics and Text for complete results.",
            "各構成を個別に計算し、凡例に従って重ね合わせます。\n構成ごとの指標はデータ、完全な結果はテキストで確認できます。");
        Add("点列图数", "Spot diagrams", "スポット図数");
        Add("每格波长数（最多）", "Wavelengths per cell (maximum)", "各セルの波長数（最大）");
        Add("单位", "Unit", "単位");
        Add("参考", "Reference", "基準");
        Add("主光线", "Chief ray", "主光線");
        Add("质心", "Centroid", "重心");
        Add("此结构未定义该视场", "This field is not defined in this configuration", "この構成にはこの視野が定義されていません");
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
        Add("导出图片（PNG）", "Export image (PNG)", "画像を書き出す（PNG）");
        Add("导出分析图片", "Export analysis image", "解析画像を書き出す");
        Add("PNG 图片", "PNG image", "PNG 画像");
        Add("分析图片已导出", "Analysis image exported", "解析画像を書き出しました");
        Add("正在计算，完成后可导出图片", "Calculating; image export is available when complete", "計算完了後に画像を書き出せます");
        Add("请先显示分析绘图页", "Display the analysis plot page first", "先に解析プロットページを表示してください");
        Add("分析图像尚未显示", "The analysis image is not displayed yet", "解析画像はまだ表示されていません");
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
