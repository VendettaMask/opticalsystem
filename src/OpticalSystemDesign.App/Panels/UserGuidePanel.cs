using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Panels;

/// <summary>
/// Searchable in-product manual. Conceptual chapters explain complete workflows, while the
/// generated command and analysis reference guarantees that every registered feature remains
/// discoverable as the application grows.
/// </summary>
public sealed class UserGuidePanel : UserControl
{
    private readonly IReadOnlyList<GuideChapter> _chapters;
    private readonly HashSet<string> _coveredCommandIds;
    private readonly TextBox _search = new() { Name = "UserGuideSearch" };
    private readonly TreeView _contents = new() { Name = "UserGuideTree", AutoScrollToSelectedItem = true };
    private readonly TextBlock _resultCount = new() { Name = "UserGuideResultCount" };
    private readonly TextBlock _path = new() { Name = "UserGuidePath", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _title = new()
    {
        Name = "UserGuideTitle",
        FontSize = DisplayTypography.LargeTitle,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _summary = new() { Name = "UserGuideSummary", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _sections = new() { Name = "UserGuideSections", Spacing = 18 };
    private readonly ScrollViewer _detailScroller = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly Dictionary<string, TreeViewItem> _chapterItems = new(StringComparer.Ordinal);
    private string? _selectedChapterId;
    private bool _updatingTree;
    private bool _listeningForLanguage;

    public UserGuidePanel(IReadOnlyList<AppAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        _coveredCommandIds = actions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
        _chapters = BuildChapters(actions);

        _search.TextChanged += (_, _) => RebuildContents();
        _contents.SelectionChanged += (_, _) =>
        {
            if (!_updatingTree)
            {
                ShowSelectedChapter();
            }
        };
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        ScrollViewer.SetHorizontalScrollBarVisibility(_contents, ScrollBarVisibility.Disabled);
        Content = BuildContent();
        RefreshLanguage();
    }

    internal IReadOnlySet<string> CoveredCommandIds => _coveredCommandIds;
    internal int ChapterCount => _chapters.Count;
    internal int IllustratedChapterCount => _chapters.Count(chapter => Enum.IsDefined(chapter.Figure));

    private Control BuildContent()
    {
        _resultCount.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _path.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _summary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);

        var navigation = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 8,
            Margin = new Thickness(10),
            Children = { _search, _resultCount, _contents }
        };
        Grid.SetRow(_resultCount, 1);
        Grid.SetRow(_contents, 2);

        var navigationBorder = new Border
        {
            Child = navigation,
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        navigationBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var detail = new StackPanel
        {
            Margin = new Thickness(24, 18, 28, 28),
            Spacing = 8,
            Children = { _path, _title, _summary, new Border { Height = 8 }, _sections }
        };
        _detailScroller.Content = detail;

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("280,*"),
            Children = { navigationBorder, _detailScroller }
        };
        Grid.SetColumn(_detailScroller, 1);
        AutomationProperties.SetName(_contents, "用户指南目录");
        AutomationProperties.SetHelpText(_contents, "按章节浏览；使用方向键展开、折叠和选择。");
        return root;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_listeningForLanguage)
        {
            return;
        }

        UiLocalization.LanguageChanged += OnLanguageChanged;
        _listeningForLanguage = true;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (!_listeningForLanguage)
        {
            return;
        }

        UiLocalization.LanguageChanged -= OnLanguageChanged;
        _listeningForLanguage = false;
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => RefreshLanguage();

    private void RefreshLanguage()
    {
        _search.PlaceholderText = Current(
            T("搜索章节、命令、分析或关键词", "Search chapters, commands, analyses, or keywords", "章、コマンド、解析、キーワードを検索"));
        AutomationProperties.SetName(_search, Current(T("搜索用户指南", "Search the user guide", "ユーザーガイドを検索")));
        AutomationProperties.SetName(this, Current(T("用户指南", "User Guide", "ユーザーガイド")));
        RebuildContents();
    }

    private void RebuildContents()
    {
        var query = _search.Text?.Trim();
        var visible = string.IsNullOrEmpty(query)
            ? _chapters
            : _chapters.Where(chapter => Matches(chapter, query)).ToArray();
        var previous = _selectedChapterId;
        _updatingTree = true;
        try
        {
            _contents.SelectedItem = null;
            _chapterItems.Clear();
            var roots = new List<TreeViewItem>();
            foreach (var group in visible.GroupBy(chapter => Current(chapter.Group)))
            {
                var root = new TreeViewItem
                {
                    Header = group.Key,
                    IsExpanded = true,
                    ItemsSource = group.Select(CreateChapterItem).ToArray()
                };
                AutomationProperties.SetName(root, group.Key);
                roots.Add(root);
            }

            _contents.ItemsSource = roots;
            var selectedId = previous is not null && _chapterItems.ContainsKey(previous)
                ? previous
                : visible.FirstOrDefault()?.Id;
            if (selectedId is not null)
            {
                _contents.SelectedItem = _chapterItems[selectedId];
            }
        }
        finally
        {
            _updatingTree = false;
        }

        _resultCount.Text = CurrentLanguage() switch
        {
            UiLocalization.English => $"{visible.Count} of {_chapters.Count} chapters",
            UiLocalization.Japanese => $"{visible.Count} / {_chapters.Count} 章",
            _ => $"显示 {visible.Count} / {_chapters.Count} 章"
        };
        ShowSelectedChapter();
    }

    private TreeViewItem CreateChapterItem(GuideChapter chapter)
    {
        var item = new TreeViewItem { Header = Current(chapter.Title), Tag = chapter };
        AutomationProperties.SetName(item, Current(chapter.Title));
        ToolTip.SetTip(item, Current(chapter.Summary));
        _chapterItems[chapter.Id] = item;
        return item;
    }

    private void ShowSelectedChapter()
    {
        if ((_contents.SelectedItem as TreeViewItem)?.Tag is not GuideChapter chapter)
        {
            _selectedChapterId = null;
            _path.Text = string.Empty;
            _title.Text = Current(T("没有匹配的章节", "No matching chapters", "一致する章はありません"));
            _summary.Text = Current(T("请清除或更改搜索条件。", "Clear or change the search query.", "検索条件をクリアまたは変更してください。"));
            _sections.Children.Clear();
            return;
        }

        _selectedChapterId = chapter.Id;
        _detailScroller.Offset = default;
        _path.Text = $"{Current(chapter.Group)}  ›  {Current(chapter.Title)}";
        _title.Text = Current(chapter.Title);
        _summary.Text = Current(chapter.Summary);
        _sections.Children.Clear();
        _sections.Children.Add(UserGuideIllustration.Build(chapter.Figure, CurrentLanguage()));
        foreach (var section in chapter.Sections)
        {
            var block = new StackPanel { Spacing = 6 };
            block.Children.Add(new TextBlock
            {
                Text = Current(section.Title),
                FontSize = DisplayTypography.SectionTitle,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            block.Children.Add(new TextBlock
            {
                Text = Current(section.Body),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
            });
            _sections.Children.Add(block);
        }
    }

    private static bool Matches(GuideChapter chapter, string query)
    {
        static bool Contains(GuideText text, string value) => text.AllText.Contains(value, StringComparison.OrdinalIgnoreCase);
        return chapter.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
               || Contains(chapter.Group, query)
               || Contains(chapter.Title, query)
               || Contains(chapter.Summary, query)
               || chapter.Sections.Any(section => Contains(section.Title, query) || Contains(section.Body, query));
    }

    private static IReadOnlyList<GuideChapter> BuildChapters(IReadOnlyList<AppAction> actions)
    {
        var chapters = CoreChapters().ToList();
        var analysisById = WorkbenchAnalysisCatalog.AllRibbonCommands.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var group in actions.Where(action => !analysisById.ContainsKey(action.Id)).GroupBy(action => action.Category))
        {
            var sections = group.OrderBy(action => action.Text, StringComparer.Ordinal)
                .Select(action => new GuideSection(
                    CommandName(action),
                    CommandDescription(action)))
                .ToArray();
            chapters.Add(new GuideChapter(
                $"commands-{Slug(group.Key)}",
                T("完整命令参考", "Complete Command Reference", "完全コマンドリファレンス"),
                CategoryName(group.Key),
                T(
                    "列出当前版本实际注册的功能命令。每个命令均可在对应功能区找到，或通过 Ctrl+Q 命令面板搜索。",
                    "Lists the feature commands actually registered by this version. Find each command on its ribbon tab or with the Ctrl+Q command palette.",
                    "このバージョンで実際に登録されている機能コマンドを一覧します。リボンまたは Ctrl+Q コマンドパレットから検索できます。"),
                UserGuideFigureKind.CommandReference,
                sections));
        }

        foreach (var group in WorkbenchAnalysisCatalog.AllRibbonCommands.GroupBy(command => command.Group))
        {
            var sections = group.Select(command => new GuideSection(
                new GuideText(command.Label),
                AnalysisDescription(command)))
            .ToArray();
            chapters.Add(new GuideChapter(
                $"analyses-{Slug(group.Key)}",
                T("完整分析参考", "Complete Analysis Reference", "完全解析リファレンス"),
                AnalysisGroupName(group.Key),
                T(
                    "本章逐项列出该分析组中的全部命令。分析使用当前工程快照，参数、单位、导出内容与适用范围以分析页中的控件和结果元数据为准。",
                    "This chapter lists every command in the analysis group. Analyses use the current project snapshot; parameters, units, exports, and scope are defined by the controls and result metadata on each analysis page.",
                    "この章では解析グループの全コマンドを列挙します。解析は現在のプロジェクトスナップショットを使用し、パラメーター、単位、エクスポート、適用範囲は各解析ページのコントロールと結果メタデータに従います。"),
                UserGuideFigureKind.AnalysisReference,
                sections));
        }

        return chapters;
    }

    private static IEnumerable<GuideChapter> CoreChapters()
    {
        yield return Chapter("start", "入门", "Getting Started", "はじめに", "推荐工作流", "Recommended Workflow", "推奨ワークフロー",
            "从建立系统到保存结果的完整起点。", "The complete starting point from creating a system to saving results.", "システム作成から結果保存までの基本手順です。",
            S("基本步骤", "Basic steps", "基本手順",
                "1. 新建、打开或导入光学系统。\n2. 在系统属性中设置孔径、视场、波长、环境与偏振。\n3. 在镜头数据或非序列对象数据中建立模型。\n4. 用二维/三维视图检查结构，再运行分析。\n5. 需要时设置变量、评价函数和公差。\n6. 保存为 STAROPT 工程并导出所需结果。",
                "1. Create, open, or import an optical system.\n2. Set aperture, fields, wavelengths, environment, and polarization in System Properties.\n3. Build the model in Lens Data or Non-Sequential Object Data.\n4. Inspect it in 2D/3D views, then run analyses.\n5. Add variables, merit operands, and tolerances when needed.\n6. Save a STAROPT project and export the required results.",
                "1. 光学系を新規作成、開く、またはインポートします。\n2. システムプロパティで開口、視野、波長、環境、偏光を設定します。\n3. レンズデータまたはノンシーケンシャルオブジェクトデータでモデルを構築します。\n4. 2D/3D ビューで構造を確認してから解析を実行します。\n5. 必要に応じて変数、メリットオペランド、公差を設定します。\n6. STAROPT プロジェクトを保存し、必要な結果をエクスポートします。"),
            S("结果可信度", "Result integrity", "結果の信頼性",
                "先确认材料、波长、孔径、视场和单位，再解释分析结果。页面显示的警告、不可用状态和导入兼容提示属于结果边界，不能忽略。",
                "Confirm materials, wavelengths, aperture, fields, and units before interpreting results. Warnings, unavailable states, and import-compatibility notices define result boundaries and must not be ignored.",
                "結果を解釈する前に、材料、波長、開口、視野、単位を確認してください。警告、利用不可状態、インポート互換性の注意は結果の適用範囲を示すため、無視しないでください。"));

        yield return Chapter("interface", "入门", "Getting Started", "はじめに", "界面与工作模式", "Interface and Workbench Modes", "画面とワークベンチモード",
            "理解功能区、系统侧栏、文档页、状态栏以及顺序/非序列模式。", "Understand the ribbon, system sidebar, document pages, status bar, and sequential/non-sequential modes.", "リボン、システムサイドバー、ドキュメントページ、ステータスバー、シーケンシャル／ノンシーケンシャルモードを理解します。",
            S("主界面", "Main window", "メイン画面",
                "顶部功能区按文件、设置、分析、优化、公差、数据库、窗口和帮助组织命令。左侧系统属性管理全局光学条件，中部文档区承载编辑器、视图和分析页，底部状态栏显示当前工程与一阶摘要。",
                "The ribbon organizes commands for files, settings, analyses, optimization, tolerancing, databases, windows, and help. The system sidebar controls global optical conditions, the document area hosts editors/views/analyses, and the status bar shows the current project and first-order summary.",
                "上部リボンはファイル、設定、解析、最適化、公差、データベース、ウィンドウ、ヘルプを整理します。システムサイドバーは全体条件、中央は編集・ビュー・解析、下部はプロジェクトと一次特性を表示します。"),
            S("两种模式", "Two modes", "2 つのモード",
                "顺序模式按表面次序追迹，适合成像镜头；非序列模式按对象与相交路径追迹，适合杂散光、照明、探测器和多路径系统。切换模式不会把一种模型自动转换成另一种模型。",
                "Sequential mode traces ordered surfaces and is suited to imaging lenses. Non-sequential mode traces objects and intersection paths for stray light, illumination, detectors, and multipath systems. Switching modes does not automatically convert one model into the other.",
                "シーケンシャルモードは面順に追跡し結像レンズ向けです。ノンシーケンシャルモードはオブジェクトと交差経路を追跡し、迷光、照明、検出器、多経路系向けです。モード切替でモデルは自動変換されません。"));

        yield return Chapter("projects", "工程与数据", "Projects and Data", "プロジェクトとデータ", "新建、打开、保存与恢复", "Create, Open, Save, and Recover", "新規作成、開く、保存、復元",
            "管理原生工程、光学快照、商业格式导入和未保存恢复。", "Manage native projects, optical snapshots, commercial imports, and unsaved recovery.", "ネイティブプロジェクト、光学スナップショット、商用形式のインポート、未保存データの復元を管理します。",
            S("镜头示例", "Lens examples", "レンズサンプル",
                "从“文件 → 示例”按原目录分类选择镜头。现有示例为 Zemax Sequential 的 14 类、95 个 ZMX 文件，替换原先生成的示例入口；也可搜索镜头名称打开。文件随软件提供，无需本机安装 Zemax。示例使用同一个导入器，菜单收录不表示全部面型、分析或数值精度均已支持。修改后保存为自己的 STAROPT 工程。",
                "Choose a lens by its original folder under File → Samples, or search for its name. The 14 categories contain 95 Zemax Sequential ZMX files, replacing the former generated sample entries. Files ship with the application and do not require a Zemax installation. They use the same importer; listing a sample does not certify all surface types, analyses, or numerical accuracy. Save edits as your own STAROPT project.",
                "ファイル → サンプルから元のフォルダー別にレンズを選択するか、名前で検索します。従来の生成サンプルに代わり、Zemax Sequential の 14 分類・95 個の ZMX を同梱しており、Zemax のインストールは不要です。通常と同じインポーターを使用し、掲載は全ての面型・解析・数値精度への対応を保証しません。編集後は自分の STAROPT プロジェクトとして保存してください。"),
            S("原生工程", "Native projects", "ネイティブプロジェクト",
                "STAROPT 工程保存处方、系统设置、配置、分析设置以及内置模块数据。保存前检查当前路径和修改状态；源码更新不会自动替换已安装程序或用户工程。",
                "STAROPT projects store prescriptions, system settings, configurations, analysis settings, and built-in module data. Check the path and modified state before saving; source updates do not replace installed applications or user projects automatically.",
                "STAROPT プロジェクトは処方、システム設定、構成、解析設定、内蔵モジュールデータを保存します。保存前にパスと変更状態を確認してください。ソース更新はインストール済みアプリやユーザープロジェクトを自動置換しません。"),
            S("导入与导出", "Import and export", "インポートとエクスポート",
                "可导入受支持范围内的 ZMX、SEQ、LEN 和文本处方；不受支持的数据会保留兼容信息或给出明确提示。STEP 导出面向机械交换，不等同于保存完整光学工程。异常退出后可在帮助页使用恢复未保存工程。",
                "Supported subsets of ZMX, SEQ, LEN, and text prescriptions can be imported. Unsupported data is retained with compatibility information or reported explicitly. STEP export is for mechanical exchange and is not a complete optical-project save. Use Recover Unsaved Project from Help after an abnormal exit.",
                "対応範囲の ZMX、SEQ、LEN、テキスト処方をインポートできます。未対応データは互換情報を保持するか明示的に通知します。STEP は機械交換用で、完全な光学プロジェクト保存ではありません。異常終了後はヘルプの未保存プロジェクト復元を使用します。"));

        yield return Chapter("sequential-model", "系统建模", "System Modeling", "システムモデリング", "顺序系统与镜头数据", "Sequential Systems and Lens Data", "シーケンシャル系とレンズデータ",
            "设置系统级条件并编辑物面、光学面、光阑面和像面。", "Set system-level conditions and edit object, optical, stop, and image surfaces.", "システム条件を設定し、物体面、光学面、絞り面、像面を編集します。",
            S("三维布局与实体模型", "3D Layout and Solid Model", "3D レイアウトとソリッドモデル",
                "三维布局以线框显示镜片边缘、面截线和真实三维光线。实体模型单独提供实体、透明、线框及剖切。两者共用实际三维几何：按各面的局部坐标计算完整矢高，再变换到全局，保留偏心、倾斜和非旋转对称形状；机械延伸区保持局部平肩。剖切封口来自实体网格与切面的真实交线。当前尚未提供完整光线模式、三维多结构叠加/偏移、光瞳显示或三轴数值旋转设置。",
                "3D Layout shows lens rims, surface meridians and real 3D rays as an engineering wireframe. Solid Model separately provides solid, transparent, wireframe and section views. Both use the same geometry: evaluate full local surface sag, then transform points to global coordinates, preserving decenter, tilt and non-rotational shapes. Mechanical extensions retain flat local shoulders. Section caps follow actual mesh/plane intersections. Full ray-pattern choices, 3D configuration overlays/offsets, pupil display and numeric three-axis rotation settings remain unavailable.",
                "3D レイアウトはレンズ外縁、面の断面線、実際の 3D 光線をワイヤーフレームで表示します。ソリッドモデルではソリッド、透明、ワイヤーフレーム、断面を選択できます。両者は各面のローカル座標で完全なサグを計算してグローバル座標へ変換し、偏心、傾斜、非回転対称形状を保持します。機械的拡張部はローカル平肩を保持し、断面の封口はメッシュと切断平面の交線から生成します。光線パターンの全選択肢、3D 構成重ね合わせ・オフセット、瞳表示、3 軸の数値回転設定は未提供です。"),
            S("系统属性", "System properties", "システムプロパティ",
                "系统孔径支持入瞳直径、像方 F 数、物方数值孔径和按光阑面尺寸浮动。视场与波长卡片定义采样点、权重和主波长；环境与偏振设置会影响相应计算路径。",
                "System aperture supports entrance-pupil diameter, image-space F/#, object-space numerical aperture, and floating by stop-surface size. Field and wavelength cards define samples, weights, and the primary wavelength; environment and polarization affect applicable calculations.",
                "システム開口は入射瞳径、像側 F ナンバー、物体側開口数、絞り面サイズ連動に対応します。視野と波長カードはサンプル、重み、主波長を定義し、環境と偏光は対応する計算に影響します。"),
            S("镜头数据编辑器", "Lens Data Editor", "レンズデータエディター",
                "每行表示一个表面。编辑面型、曲率半径、厚度、材料、膜层、净口径、延伸区、机械半径和圆锥系数；右键单元格设置变量或求解。表面属性区提供几何、材料、膜层、偏振和导入状态的详细编辑。",
                "Each row represents a surface. Edit type, radius, thickness, material, coating, clear semi-diameter, extension zone, mechanical radius, and conic; use cell context menus for variables and solves. Surface Properties provides detailed geometry, material, coating, polarization, and import-state editing.",
                "各行は 1 面を表します。面タイプ、曲率半径、厚さ、材料、コーティング、有効半径、拡張領域、機械半径、円錐係数を編集し、セルのコンテキストメニューで変数やソルブを設定します。面プロパティでは詳細設定を行います。"),
            S("导入后的厚度差异", "Imported thickness differences", "インポート後の厚さ差分",
                "没有登记操作数行的导入模型也会显示有限的厚度差异。例如 Zoom 镜头的表面 8 和 15 会显示为 THIC 行，厚度可编辑，但不自动分配 Op# 或更改拾取、评价函数引用。设置变量或拾取前，需新增对应类型和表面号的操作数行；登记后不会重复显示该差异。",
                "Imported models also show finite thickness differences even without registered operand rows. For example, Zoom surfaces 8 and 15 appear as editable THIC rows without automatically assigning an Op# or changing pickup or merit-function references. Add the matching operand type and surface before using variables or pickups; a registered parameter is not duplicated as a difference row.",
                "オペランド行が未登録でも、インポートしたモデルの有限な厚さ差分を表示します。例えば Zoom の面 8・15 は編集可能な THIC 行として表示され、Op# の自動割当てやピックアップ・評価関数参照の変更は行いません。変数・ピックアップの利用前に対応するタイプと面のオペランド行を追加してください。登録済みのパラメーターは差分行に重複表示しません。"),
            S("多配置", "Multi-configuration", "マルチコンフィグレーション",
                "镜头数据表上方的工具栏在浅色主题中使用蓝灰底色，结构切换位于工具栏正中央，表面属性保留在左侧；空间不足时结构切换移到下一行并保持居中，不缩小字体。结构序号以粗体显示在左右箭头中间，按钮切换活动结构，孔径、处方和视图同步刷新。顶部“设置 → 系统 → 多重结构”默认打开操作数行、结构列的矩阵；活动结构列带 *，可用箭头或双击列标题激活结构。单击数值选中单元格，双击或展开属性后编辑数值、变量和拾取；无效或未应用输入会阻止切换，先应用或还原。APER 显示各结构的真实系统孔径，只读且不占用 Op#，通过系统选项修改；可编辑操作数行号和拾取/评价函数引用不变。配置概览保留原厚度编辑。镜头页切换前提交有效输入，错误可修正或按 Esc 还原。二维图、三维布局和实体模型顶部设置和刷新旁的结构下拉框仅列出 1 / N、2 / N…，单结构隐藏，不提供全部。打开时跟随活动结构；手动选择后仅固定各自视图，镜片、光线和摘要一起更新，不激活主工程其他结构。重置设置或换文件后恢复跟随。先确认所选结构再分析、优化或导出。",
                "The Light theme uses a blue-gray Lens Data header. Configuration navigation is centered across it, with Surface Properties on the left. When space is limited, navigation moves to a centered second row without reducing font size. The configuration position is bold and sits between the arrows, which switch the active configuration and refresh aperture, prescription and views. Settings → System → Multi-Configuration opens an operand-row, configuration-column matrix; * marks the active column. Use arrows or double-click a column header to activate it. Click a value to select a cell, then double-click or expand Properties to edit values, variables and pickups. Apply or restore pending input before switching. APER shows each configuration's real system aperture read-only; edit it in System Options. It has no Op# and does not shift editable operand, pickup or merit-function references. Configuration Overview retains the original thickness editing. Lens Data commits valid input before switching; correct errors or restore with Esc. The 2D, 3D Layout and Shaded Model toolbars each have a configuration picker beside Settings and Refresh, listing only 1 / N, 2 / N, and so on; it is hidden for one configuration and has no All choice. It initially follows the active configuration. A manual selection pins this view only and updates lenses, rays and summary together without activating another workspace configuration. Reset settings or open another file to follow the active configuration again. Confirm the selected configuration before analysis, optimization or export.",
                "ライトテーマのレンズデータヘッダーは青灰色です。構成切替はヘッダー全体の中央に配置し、面プロパティは左側に残します。狭い場合は構成切替を次の行の中央に移し、文字サイズを維持します。構成番号は左右の矢印の間に太字で表示します。左右ボタンでアクティブ構成を切り替え、開口・処方・ビューを更新します。「設定 → システム → マルチコンフィグレーション」はオペランドが行、構成が列の行列を初期表示し、アクティブ列に * を付けます。矢印または列見出しのダブルクリックで構成を切り替えます。値をクリックしてセルを選択し、ダブルクリックまたはプロパティ展開で値・変数・ピックアップを編集します。切替前に入力を適用または復元してください。APER は各構成の実際のシステム開口を読み取り専用で表示し、システムオプションで編集します。Op# を消費せず、編集行やピックアップ・評価関数参照は変わりません。構成一覧には既存の厚さ編集を残します。レンズデータでは有効な入力を切替前に確定し、エラーは修正または Esc で復元します。2D ビュー・3D レイアウト・シェーディングモデルの設定と更新の横に、1 / N、2 / N…のみを表示する構成リストがあります。単一構成では非表示で、「すべて」はありません。初期状態はアクティブ構成に追従し、手動選択後はこのビューのみ固定してレンズ・光線・概要を更新します。主工程の構成は変更しません。設定のリセットまたはファイルの切替で追従に戻ります。解析・最適化・出力前に選択構成を確認してください。"));

        yield return Chapter("non-sequential-model", "系统建模", "System Modeling", "システムモデリング", "非序列对象、光源与探测器", "Non-Sequential Objects, Sources, and Detectors", "ノンシーケンシャルオブジェクト、光源、検出器",
            "建立基于对象、相交路径、光源和探测器的系统。", "Build systems based on objects, intersection paths, sources, and detectors.", "オブジェクト、交差経路、光源、検出器に基づく系を構築します。",
            S("对象模型", "Object model", "オブジェクトモデル",
                "对象数据编辑器定义对象类型、位置、姿态、材料、尺寸和对象专用参数。对象顺序不是顺序模式的面顺序；光线按实际相交、散射、反射、折射和终止条件传播。",
                "Object Data defines object type, position, orientation, material, dimensions, and object-specific parameters. Object order is not a sequential surface order; rays propagate through actual intersections, scattering, reflection, refraction, and termination rules.",
                "オブジェクトデータは種類、位置、姿勢、材料、寸法、固有パラメーターを定義します。並び順は面順ではなく、光線は実際の交差、散乱、反射、屈折、終了条件で伝搬します。"),
            S("追迹与结果", "Tracing and results", "追跡と結果",
                "先在追迹控制中设置光线数、分裂、散射、偏振和数据库选项，再运行追迹。探测器查看器显示累积结果；清空探测器会移除旧累积。光线数据库和路径分析用于检查单条路径、功率与交互类型。",
                "Set ray count, splitting, scattering, polarization, and database options in Trace Control before tracing. Detector Viewer shows accumulated results; Clear Detectors removes previous accumulation. Ray Database and Path Analysis inspect individual paths, power, and interaction types.",
                "追跡前に光線数、分割、散乱、偏光、データベースを設定します。検出器ビューアーは累積結果を表示し、検出器クリアで以前の累積を削除します。光線データベースと経路解析で個別経路、パワー、相互作用を確認します。"));

        yield return Chapter("analyses", "分析与结果", "Analyses and Results", "解析と結果", "运行和解释分析", "Run and Interpret Analyses", "解析の実行と解釈",
            "统一理解报告、光线、像差、波前、PSF、MTF、能量和图像分析。", "A common workflow for reports, rays, aberrations, wavefront, PSF, MTF, energy, and image analyses.", "レポート、光線、収差、波面、PSF、MTF、エネルギー、画像解析に共通する手順です。",
            S("分析页面", "Analysis pages", "解析ページ",
                "从分析功能区选择类型后，页面使用当前工程快照。设置视场、波长、采样、参考、偏振等参数并运行；工程修订变化后旧结果会失效或清除。锁定页面只控制更新行为，不改变计算定义。",
                "Choose an analysis from the ribbon to create a page using the current project snapshot. Set field, wavelength, sampling, reference, polarization, and other parameters, then run it. Old results become stale or clear after a project revision. Page lock controls updating, not calculation definitions.",
                "リボンから解析を選ぶと現在のプロジェクトスナップショットを使用するページが作成されます。視野、波長、サンプリング、基準、偏光などを設定して実行します。プロジェクト変更後、古い結果は無効化または消去されます。ロックは更新のみを制御します。"),
            S("分析结构选择", "Analysis configuration selection", "解析の構成選択",
                "顺序分析的设置在多结构文件中显示“结构”，单结构时整项隐藏。“当前的”跟随活动结构，“1 / N、2 / N…”固定计算指定结构，不切换镜头数据。支持比较的分析还提供“所有”：点列、光线像差、MTF、RMS、能量等按结构叠加，结构矩阵按视场行、结构列显示。图例可独立开关；多结构结果的 CSV 保留独立结构号列。Huygens 相干求和尚未接入，其结构选项为当前或单个结构。结构或文件变化后选项会更新，旧结果标记过期或清除。",
                "Sequential analysis settings show Configuration only for documents with multiple configurations. Current follows the active configuration; 1 / N, 2 / N, etc. pin the analysis to a configuration without switching Lens Data. Supported comparisons also offer All: spot, ray-fan, MTF, RMS and energy results are overlaid, while Configuration Matrix uses field rows and configuration columns. Legends toggle independently and multi-configuration CSV results include a configuration-number column. Coherent Huygens summation is not connected; Huygens analyses offer Current or a single configuration. Options update when configurations or files change, and old results become stale or clear.",
                "順次解析では構成が複数ある場合のみ設定に構成が表示されます。現在の構成はアクティブな構成に追従し、1 / N、2 / N などはレンズデータを切り替えずに指定構成を計算します。対応する比較では、すべてを選ぶとスポット、光線収差、MTF、RMS、エネルギーを重ね合わせ、構成マトリクスは視野行・構成列で表示します。凡例を個別に切り替え、CSV に構成番号を保持します。Huygens のコヒーレント加算は未接続のため、現在または単一構成を選択します。構成やファイルの変更で選択肢を更新し、古い結果を無効化または消去します。"),
            S("单位与导出", "Units and export", "単位とエクスポート",
                "以坐标轴和结果元数据给出的物理量与单位为准，不从标题文字猜测单位。导出数据前确认活动配置、结果修订、归一化、参考波长和采样密度。完整分析命令见本指南的“完整分析参考”。",
                "Use the quantities and units published by axes and result metadata; do not infer units from titles. Before export, confirm active configuration, result revision, normalization, reference wavelength, and sampling density. See Complete Analysis Reference for every analysis command.",
                "軸と結果メタデータが示す物理量・単位を使用し、タイトルから推測しないでください。エクスポート前に構成、結果リビジョン、正規化、基準波長、サンプリング密度を確認します。全解析は完全解析リファレンスを参照してください。"));

        yield return Chapter("optimization", "设计与验证", "Design and Verification", "設計と検証", "优化与评价函数", "Optimization and Merit Functions", "最適化とメリット関数",
            "建立变量和评价函数，选择优化器并检查约束。", "Create variables and merit functions, choose an optimizer, and inspect constraints.", "変数とメリット関数を作成し、最適化手法を選び、制約を確認します。",
            S("变量与操作数", "Variables and operands", "変数とオペランド",
                "可从镜头数据单元格、表面属性或批量命令设置曲率、厚度及支持参数为变量。评价函数编辑器用操作数、目标、权重和启用状态定义目标；操作数帮助列出每个代码的定义、参数和计算支持状态。",
                "Set radii, thicknesses, and supported parameters as variables from Lens Data cells, Surface Properties, or batch commands. The Merit Function Editor defines goals with operands, targets, weights, and enabled states. Operand Help documents every code, parameter, and calculation-support state.",
                "レンズデータセル、面プロパティ、または一括コマンドで曲率、厚さ、対応パラメーターを変数にします。メリット関数エディターはオペランド、目標、重み、有効状態で目標を定義します。オペランドヘルプに各コードの定義と対応状態があります。"),
            S("执行优化", "Run optimization", "最適化の実行",
                "快速聚焦用于焦面调整；DLS、随机扰动、优化向导、滑块和可视化优化器服务于不同设计阶段。运行前保存工程并检查变量边界、玻璃选择和评价函数；比较优化前后处方及多个分析，不只看单一评价值。",
                "Quick Focus adjusts focus. DLS, random perturbation, the optimization wizard, sliders, and the visual optimizer serve different design stages. Save first and inspect variable bounds, glass choices, and the merit function; compare prescriptions and multiple analyses before and after, not only one merit value.",
                "クイックフォーカスは焦点調整用です。DLS、ランダム摂動、最適化ウィザード、スライダー、可視化最適化は段階に応じて使います。事前保存し、変数境界、ガラス、メリット関数を確認し、単一値だけでなく処方と複数解析を比較します。"));

        yield return Chapter("tolerancing", "设计与验证", "Design and Verification", "設計と検証", "公差、制造与图纸", "Tolerancing, Manufacturing, and Drawings", "公差、製造、図面",
            "从公差定义到良率、可加工性和光学图纸的完整链路。", "The complete path from tolerance definitions to yield, manufacturability, and optical drawings.", "公差定義から歩留まり、製造性、光学図面までの流れです。",
            S("公差流程", "Tolerance workflow", "公差ワークフロー",
                "在公差数据编辑器中定义参数、分布、上下限、补偿量和评价指标，保存后运行分析。数据查看器、报告、直方图和良率页面分别提供试验明细、文本汇总、分布和同时通过全部限值的总体良率。",
                "Define parameters, distributions, limits, compensators, and metrics in the Tolerance Data Editor, save, then run. Data Viewer, Report, Histogram, and Yield provide trial details, text summaries, distributions, and overall yield requiring simultaneous passage of all limits.",
                "公差データエディターでパラメーター、分布、限界、補償量、評価指標を定義して保存後に実行します。データ表示、レポート、ヒストグラム、歩留まりは試行詳細、要約、分布、全限界同時合格率を示します。"),
            S("制造输出", "Manufacturing output", "製造出力",
                "可加工性评估检查曲面域、边厚、材料和其他制造约束。ISO 10110 与 GB/T 图纸使用当前处方和公差数据；生成图纸或 STEP 前必须复核单位、口径、材料、膜层、基准和标准版本。",
                "Manufacturability Assessment checks surface domains, edge thickness, materials, and other constraints. ISO 10110 and GB/T drawings use the current prescription and tolerance data. Verify units, apertures, materials, coatings, datums, and standard version before drawings or STEP export.",
                "製造性評価は曲面領域、縁厚、材料などを確認します。ISO 10110 と GB/T 図面は現在の処方と公差データを使用します。図面または STEP 前に単位、開口、材料、コーティング、基準、規格版を確認します。"));

        yield return Chapter("libraries", "资源与模块", "Resources and Modules", "リソースとモジュール", "材料库、镜头库与内置模块", "Libraries and Built-in Modules", "ライブラリと内蔵モジュール",
            "使用材料、商业镜头、初始结构、镀膜和装调功能。", "Use materials, commercial lenses, initial structure, coating, and alignment features.", "材料、商用レンズ、初期構成、コーティング、組立調整機能を使用します。",
            S("数据库", "Databases", "データベース",
                "材料库提供折射率、色散和透过率数据以及色散图、玻璃图和无热化玻璃图。镜头库、库存镜头查看与匹配用于浏览或引入目录处方；导入后应核对材料目录、单位、口径和有效波长范围。",
                "Material Library provides refractive-index, dispersion, and transmission data plus dispersion, glass, and athermal maps. Lens Library, Stock Lens Browser, and Matching browse or introduce catalog prescriptions. After import, verify catalogs, units, apertures, and valid wavelength ranges.",
                "材料ライブラリは屈折率、分散、透過率と各種マップを提供します。レンズライブラリ、在庫レンズ表示、照合でカタログ処方を利用できます。インポート後に材料カタログ、単位、開口、有効波長範囲を確認します。"),
            S("内置实验室", "Built-in laboratories", "内蔵ラボ",
                "AI 初始结构生成并筛选候选起始处方，光学镀膜设计编辑膜系并计算 R/T/A，光学装调处理装调测量与流程。它们都是主程序内置模块，数据边界各自明确；候选或实验结果仍需回到主工作台验证。",
                "AI Initial Structure generates and screens starting prescriptions, Optical Coating Design edits stacks and calculates R/T/A, and Optical Alignment supports alignment measurements and workflow. All are built-in modules with explicit data boundaries; candidates and experiments still require validation in the main workbench.",
                "AI 初期構成は開始処方を生成・選別し、光学コーティング設計は膜系と R/T/A、光学組立調整は測定と手順を扱います。すべて内蔵モジュールでデータ境界があり、結果はメインワークベンチで検証します。"),
            S("初始结构界面与操作", "Initial structure workspace", "初期構造の操作画面",
                "初始结构的设置按系统、目标、搜索分成卡片。系统页的“从预设开始”载入目标，“来源与适配说明”保留来源及限制；目标页集中片数、总长和像质门槛，几何后焦、玻璃目录、口径光阑按需展开；搜索页集中算法与预算，AutoLens 参数、起始形式和追加细化预算分别折叠。收起分组不删除参数或恢复默认值。开始、预检查、停止保存与记录操作在顶部，细化和导出位于候选旁，实际光线和详情在下方。窄窗口上下排列并滚动访问，不改变计算与验收规则。",
                "Settings use System, Targets and Search cards. Start from a Preset loads targets; Source and Adaptations retains provenance and limits. Targets groups element count, track and quality gates, with expandable geometry/back focus, glasses/catalogs and aperture/stop sections. Search groups strategy and budgets, with separate AutoLens, starting-form and refinement sections. Collapsing does not remove inputs or reset values. Run, precheck, stop/save and record commands are above; refine/export are beside candidates; actual rays and details are below. Narrow windows stack and scroll without changing calculations or acceptance rules.",
                "設定はシステム、目標、探索のカードに整理されています。プリセットから目標を読み込み、出典と調整内容で制約を確認します。目標には枚数、全長、画質基準を集め、形状・バックフォーカス、ガラス・カタログ、口径・絞りを展開できます。探索には手法と予算があり、AutoLens、初期形態、追加改良は別々に展開します。折りたたんでも値は消去・初期化されません。実行・事前確認・停止保存・記録操作は上部、改良・出力は候補の横、実際の光線と詳細は下部です。狭いウィンドウでは上下配置とスクロールになり、計算や合格基準は変わりません。"),
            S("AutoLens 搜索", "AutoLens search", "AutoLens 探索",
                "AI 初始结构统一使用 AutoLens 的 C# 自动求导实现；旧信赖域、启发式及原生目标精修选项已隐藏。新建、预设搜索、继续运行和候选细化只使用 AutoLens，原生目标精修预算为零，固定启用实体光阑与光线瞄准。旧算法及混合算法记录可查看、比较和导出，不能继续运行或细化；重新开始会新建 AutoLens 实验，不改写旧记录。",
                "AI Initial Structure uses only managed AutoLens automatic differentiation. Former trust-region, heuristic and native target-polish choices are hidden. New and preset searches, resume and refinement disable native target polish and require a physical stop with ray aiming. Legacy and hybrid records remain available for viewing, comparison and export; they cannot resume or refine. Starting again creates a new AutoLens run and preserves the original record.",
                "AI 初期構成は AutoLens の C# 自動微分実装のみを使用します。従来の信頼領域法、ヒューリスティック、ネイティブ目標精修の選択肢は非表示です。新規・プリセット探索、再開、候補改良ではネイティブ目標精修を無効にし、実体絞りと光線照準を使用します。旧・混合アルゴリズムの記録は表示・比較・出力のみ可能で、再開・改良はできません。再実行では元の記録を保持して新しい AutoLens 実験を作成します。"),
            S("GPU 计算设备", "GPU compute device", "GPU 計算デバイス",
                "在搜索页选择自动（优先 GPU）、GPU 或 CPU。新窗口默认自动，支持可用 NVIDIA CUDA 显卡的双精度瞄准、追迹和批量梯度；无设备或 GPU 范围限制时自动模式用 CPU 并记录原因，强制 GPU 不可用则阻止开始。预设保留设备选择，记录恢复原模式，手动切换会新建实验。候选详情显示实际设备。适用面型的正式光线、瞄准和密集物理验收共用 Core 的 CUDA 路径；原采样、裁光与验收标准不变。少数延拓分支的 CPU 恢复会单独计数，完整流程提速以相同输入实测为准。",
                "Choose Automatic (prefer GPU), GPU or CPU on Search. New windows default to Automatic and use an available NVIDIA CUDA device for double-precision aiming, tracing and batched gradients. Automatic records CPU fallback when a device or supported range is unavailable; forced GPU blocks instead. Presets keep the selected device, saved runs restore their mode, and manual changes start a new experiment. Candidate details show the actual device. Eligible surfaces use shared Core CUDA functions for formal rays, aiming and dense physical validation, with unchanged sampling, clipping and acceptance criteria. CPU recovery on exceptional continuation branches is counted separately; whole-workflow speed is measured with identical inputs.",
                "探索ページで自動（GPU 優先）、GPU、CPU を選択します。新しい画面では自動が既定で、利用可能な NVIDIA CUDA GPU で倍精度の照準、追跡、勾配を並列計算します。利用不可や範囲制限時は自動で CPU に戻り理由を記録し、GPU 指定時は開始を停止します。プリセットは選択デバイスを保持し、記録は元のモードを復元します。手動変更は新実験になります。候補詳細に実際のデバイスを表示します。対応面型の光線、照準、密な物理判定は共有 Core の CUDA 関数を使い、サンプリングと判定基準を維持します。例外的な延拓の CPU 復旧を別に数え、探索全体の速度は同一入力で測定します。"),
            S("AutoLens 官方目标预设", "AutoLens official target presets", "AutoLens 公式目標プリセット",
                "在 AI 初始结构的系统页选择官方目标预设并点击载入，再预检查和开始形式搜索。五个条目包含广角、全画幅、20 随机种子协议、F/1.2 相机及 120° 手机目标适配。载入只设置参数并清除旧结果，不自动运行、不修改主工程。它们是原生 C# CPU 目标，而非官方成功处方；缺失参数均为明确的本地补充，来源说明保留正文与图片说明的冲突。每次默认上限为 4000 次评价和 1 分钟，另做完整口径物理复核。20 种子条目在界面载入首个种子，完整系列由专用验收工具验证。未达标候选不可当作可用设计。",
                "On the AI Initial Structure System tab, choose an official target, load it, preflight, and start form search. Five entries cover wide-angle, full-frame, a 20-seed protocol, and adapted F/1.2 camera and 120° mobile targets. Loading only sets parameters and clears stale results; it neither starts a search nor changes the main project. These are native C# CPU targets, not official successful prescriptions. Missing parameters are explicit local choices and conflicting upstream descriptions are documented. Each run defaults to 4000 evaluations and 1 minute, followed by independent full-aperture physical verification. The 20-seed entry loads its first seed in the UI; a dedicated validation tool runs the complete series. Failed candidates are not usable accepted designs.",
                "AI 初期構成のシステムタブで公式目標を選んで読み込み、事前確認後に形式探索を開始します。5 項目は広角、フルサイズ、20 シード検証、および F/1.2 カメラと 120° モバイルの調整版です。読み込みはパラメーター設定と古い結果の消去のみで、自動実行や主プロジェクトの変更は行いません。C# CPU の目標であり公式の成功処方ではありません。不足仕様はローカル補完として明記し、公式説明の矛盾も保持します。各実行の既定上限は 4000 評価・1 分で、全開口の独立物理検証を別途行います。20 シード項目は UI で最初のシードを読み込み、全件は専用ツールで検証します。不合格候補を使用可能な合格設計として扱わないでください。"));

        yield return Chapter("workspace", "操作参考", "Operation Reference", "操作リファレンス", "窗口、布局与快捷键", "Windows, Layouts, and Shortcuts", "ウィンドウ、レイアウト、ショートカット",
            "管理停靠页面、保存布局并使用键盘高效操作。", "Manage docked pages, save layouts, and work efficiently with the keyboard.", "ドッキングページ、レイアウト保存、キーボード操作を管理します。",
            S("页面与布局", "Pages and layouts", "ページとレイアウト",
                "页面可停靠、分栏、浮动、平铺、层叠、克隆或锁定。保存默认布局和槽位布局用于显式恢复；普通启动只加载当前模式规定的基础页面，不自动恢复分析页。关闭其他页会保留镜头数据等基础页面。",
                "Pages can dock, split, float, tile, cascade, clone, or lock. Saved default and slot layouts are restored explicitly; normal startup loads only the base pages defined for the current mode and does not restore analysis pages automatically. Close Other Pages preserves base pages such as Lens Data.",
                "ページはドッキング、分割、フロート、整列、カスケード、複製、ロックが可能です。既定・スロットレイアウトは明示的に復元します。通常起動は基本ページのみで解析ページを自動復元しません。他ページを閉じてもレンズデータ等は保持します。"),
            S("拖动页面与停靠预览", "Dragging pages and docking previews", "ページのドラッグとドッキングプレビュー",
                "拖动文档页签，或平铺/层叠页面标题左侧的名称，到其他文档区域。十字方向提示提供上、下、左、右分割；鼠标移到方向按钮后，半透明灰框显示松开后的区域和尺寸。移到有效的中央按钮可合并为标签页；Esc 取消本次拖动。页面内容和设置不因移动而重建。调整后可显式保存默认布局或布局槽位。",
                "Drag a document tab, or the name on the left of a tiled/cascaded page title, over another document area. The compass offers top, bottom, left, and right splits; hovering a direction shows a translucent gray frame for the resulting position and size. Use an available center target to merge as tabs; Esc cancels the drag. Moving a page preserves its content and settings. Save the default layout or a layout slot explicitly to reuse the arrangement.",
                "文書タブ、または整列・カスケードページのタイトル左側の名前を、別の文書領域へドラッグします。十字ガイドで上下左右に分割でき、方向ボタンに重ねると配置後の位置とサイズを半透明の灰色枠で表示します。有効な中央ボタンでタブにまとめ、Esc でドラッグを中止します。移動しても内容や設定は保持されます。再利用するには既定レイアウトまたはスロットへ明示的に保存します。"),
            S("系统选项与文档区域", "System options and document area", "システムオプションと文書領域",
                "默认系统选项固定在左侧并占满工作区高度。文档只在右侧文档区域分割或合并，拖放方向和灰框不跨越侧栏，也不能把文档合并到系统选项中。侧栏仍可通过原有操作固定或浮动，宽度保持 240–280 DIP。旧的显式保存布局会补全停靠分组，但保留原来的布局位置；需要回到默认排列时使用重置系统布局。",
                "By default, System Options stays docked on the left at the full workspace height. Documents split or merge only within the document area on the right; docking targets and gray previews do not span the sidebar, and documents cannot merge into System Options. Existing sidebar pin and float actions remain available, with its width constrained to 240–280 DIP. Older explicitly saved layouts receive docking groups without changing their saved positions; reset the system layout to return to the default arrangement.",
                "既定ではシステムオプションは左側にドッキングし、作業領域の高さ全体を使用します。文書の分割・結合は右側の文書領域内に限定され、方向ガイドと灰色枠はサイドバーをまたぎません。文書をシステムオプションに結合することもできません。サイドバーの固定・フロート操作と幅 240–280 DIP は維持されます。以前に明示保存したレイアウトにはドッキンググループを補い、保存位置は変更しません。既定配置に戻すにはシステムレイアウトをリセットします。"),
            S("分析图表自动适配", "Automatic plot fitting", "解析グラフの自動フィット",
                "调整分栏或页面尺寸后，绘图区域会重新布局；空间不足时，图形、坐标、图例和底部说明统一等比缩小，扩大页面后恢复正常比例。此适配不修改分析数据、单位或设置，也不重置手动平移和缩放。数据和文本页保持正常字号并支持滚动。区域特别小时可扩大页面查看细节。",
                "Resizing a pane relays out its plot page. When space is limited, the plot, axes, legend, and footer scale down together; expanding the page restores the normal scale. This does not change analysis data, units, or settings, or reset manual pan and zoom. Data and Text tabs retain normal text size and scrolling. Enlarge a very small pane to inspect details.",
                "ペインのサイズ変更に合わせて描画ページを再配置します。狭い場合は図、座標軸、凡例、フッターをまとめて等比縮小し、広げると通常倍率に戻ります。解析データ、単位、設定、手動の移動・ズーム状態は変更しません。データ・テキストタブは通常の文字サイズとスクロールを維持します。細部はペインを広げて確認してください。"),
            S("常用快捷键", "Common shortcuts", "主なショートカット",
                "Ctrl+Q 打开命令面板；方向键选择，Enter 执行，Esc 关闭。常规文本编辑遵循平台快捷键。图表获得焦点后可用方向键平移、加号/减号缩放、Home 重置视图。",
                "Ctrl+Q opens the command palette; use arrow keys to select, Enter to run, and Esc to close. Text editing follows platform conventions. When a chart has focus, use arrow keys to pan, plus/minus to zoom, and Home to reset.",
                "Ctrl+Q でコマンドパレットを開き、矢印で選択、Enter で実行、Esc で閉じます。テキスト編集はプラットフォーム標準です。グラフは矢印で移動、＋／－でズーム、Home でリセットします。"));

        yield return Chapter("help", "操作参考", "Operation Reference", "操作リファレンス", "帮助系统与范围说明", "Help System and Scope", "ヘルプシステムと適用範囲",
            "区分用户指南、操作数帮助、状态提示和版本信息。", "Distinguish the User Guide, Operand Help, status messages, and version information.", "ユーザーガイド、オペランドヘルプ、状態メッセージ、バージョン情報を区別します。",
            S("帮助入口", "Help entries", "ヘルプ項目",
                "用户指南讲解完整工作流并自动列出全部注册命令和分析；操作数帮助逐项说明评价函数代码；关于页面显示产品版本与运行时。控件工具提示和不可用原因提供当前上下文说明。",
                "User Guide explains complete workflows and automatically lists every registered command and analysis. Operand Help documents merit-function codes. About shows product and runtime versions. Tooltips and unavailable reasons provide context-specific guidance.",
                "ユーザーガイドは全ワークフローと登録済み全コマンド・解析を一覧し、オペランドヘルプはメリット関数コードを説明します。バージョン情報は製品とランタイムを表示し、ツールチップは現在の状況を説明します。"),
            S("版本与更新", "Version and updates", "バージョンと更新",
                "在“帮助 → 关于”查看版本与构建编号，手动检查更新时显示进度及结果。“包含测试版”默认勾选，同版本测试包按更高的签名云端构建编号更新；同一构建不重复提示，不回退旧版本或旧构建。取消勾选只检查正式版。可信更新下载验证后，另行选择“安装更新并重启”；有未保存修改时可保存、不保存并继续，或取消更新。选择保存后取消保存对话框会停止本次更新，不强制关闭。开发/便携版、无可信签名仍需手动下载。旧稳定通道客户端先手动安装一次新引导包；日常检查、下载和安装都需主动操作。",
                "Help → About shows version and build numbers, visible check progress and results. Include test releases is checked by default. A same-version test package can update only to a higher signed cloud build number; the installed build is not offered again, and older versions/builds are rejected. Uncheck it for stable releases only. After a trusted download is verified, choose Install update and restart. For unsaved changes, choose Save, Don't save, or Cancel. Canceling a chosen save stops the update; windows are never forcibly closed. Development, portable or unsigned-metadata packages still require manual installation. Older stable-only clients need one manual installation of the new bootstrap. All update actions are user initiated.",
                "ヘルプ → バージョン情報でバージョン・ビルド番号を表示し、確認中の進捗と結果を明示します。「テスト版を含める」は既定で有効です。同じバージョンのテスト版は署名付きクラウドビルド番号が増えた場合のみ更新し、同一ビルドの再通知や旧版への逆戻りは行いません。解除すると安定版のみ確認します。検証後に「更新して再起動」を選択します。未保存の変更は保存、保存せずに続行、更新の中止を選べます。保存ダイアログのキャンセル時は更新せず、強制終了もしません。開発版・ポータブル版・署名情報なしは手動取得が必要です。旧クライアントは新しい導入版を一度手動インストールしてください。すべての更新操作は明示操作です。"),
            S("适用范围", "Scope", "適用範囲",
                "指南描述当前程序中实际可见和注册的功能，不把计划功能、兼容保留数据或外部软件行为写成已实现能力。具体算法限制、采样定义和验证范围以对应分析页、专题文档及版本状态为准。",
                "The guide describes features actually visible and registered in the current application. Planned features, compatibility-only data, and external-software behavior are not presented as implemented. Algorithm limits, sampling definitions, and validation scope remain governed by the relevant analysis page, topic documentation, and version status.",
                "本ガイドは現在のアプリで実際に表示・登録される機能を説明します。計画機能、互換保持データ、外部ソフトの動作を実装済みとは扱いません。アルゴリズム制限、サンプリング定義、検証範囲は各解析ページと専門文書、バージョン状態に従います。"));
    }

    private static GuideChapter Chapter(
        string id,
        string groupZh, string groupEn, string groupJa,
        string titleZh, string titleEn, string titleJa,
        string summaryZh, string summaryEn, string summaryJa,
        params GuideSection[] sections) =>
        new(
            id,
            T(groupZh, groupEn, groupJa),
            T(titleZh, titleEn, titleJa),
            T(summaryZh, summaryEn, summaryJa),
            id switch
            {
                "start" => UserGuideFigureKind.Workflow,
                "interface" => UserGuideFigureKind.Interface,
                "projects" => UserGuideFigureKind.ProjectFlow,
                "sequential-model" => UserGuideFigureKind.SequentialModel,
                "non-sequential-model" => UserGuideFigureKind.NonSequentialModel,
                "analyses" => UserGuideFigureKind.AnalysisFlow,
                "optimization" => UserGuideFigureKind.OptimizationLoop,
                "tolerancing" => UserGuideFigureKind.ToleranceFlow,
                "libraries" => UserGuideFigureKind.LibrariesAndModules,
                "workspace" => UserGuideFigureKind.WorkspaceLayout,
                _ => UserGuideFigureKind.HelpSystem
            },
            sections);

    private static GuideSection S(
        string titleZh, string titleEn, string titleJa,
        string bodyZh, string bodyEn, string bodyJa) =>
        new(T(titleZh, titleEn, titleJa), T(bodyZh, bodyEn, bodyJa));

    private static GuideText CommandName(AppAction action)
    {
        if (CommandNames.TryGetValue(action.Id, out var value)) return value;
        if (action.Id.StartsWith("example-", StringComparison.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(action.Id["example-".Length..]);
            return T($"打开示例：{name}", $"Open example: {name}", $"サンプルを開く：{name}");
        }
        return new GuideText(action.Text, action.Id.Replace('-', ' '), action.Id.Replace('-', ' '));
    }

    private static GuideText CommandDescription(AppAction action)
    {
        var name = CommandName(action);
        return T(
            $"从“{action.Category}”功能区或 Ctrl+Q 命令面板执行“{name.Chinese}”。命令标识：{action.Id}。具体输入、禁用原因和结果边界以打开页面或对话框中的说明为准。",
            $"Run {name.English ?? name.Chinese} from its ribbon area or the Ctrl+Q command palette. Command ID: {action.Id}. Inputs, disabled reasons, and result boundaries are defined by the opened page or dialog.",
            $"リボンまたは Ctrl+Q コマンドパレットから「{name.Japanese ?? name.Chinese}」を実行します。コマンド ID: {action.Id}。入力、無効理由、結果範囲は開いたページまたはダイアログの説明に従います。");
    }

    private static GuideText AnalysisDescription(AnalysisRibbonCommand command)
    {
        var group = AnalysisGroupName(command.Group);
        return T(
            $"“{command.Label}”属于“{group.Chinese}”组。打开后设置页面参数并运行；它使用当前工程快照，且不会自动修改原处方。命令标识：{command.Id}。",
            $"This command belongs to the {group.English ?? group.Chinese} analysis group. Set its page parameters and run it; it uses the current project snapshot and does not automatically modify the prescription. Command ID: {command.Id}.",
            $"このコマンドは {group.Japanese ?? group.Chinese} 解析グループに属します。ページのパラメーターを設定して実行します。現在のプロジェクトスナップショットを使用し、処方を自動変更しません。コマンド ID: {command.Id}。");
    }

    private static GuideText AnalysisGroupName(string group) => group switch
    {
        "光线迹点" => T("光线迹点", "Ray Tracing and Spots", "光線追跡とスポット"),
        "报告" => T("报告", "Reports", "レポート"),
        "像差分析" => T("像差分析", "Aberration Analysis", "収差解析"),
        "波前" => T("波前", "Wavefront", "波面"),
        "点扩散函数" => T("点扩散函数", "Point Spread Function", "点像分布関数"),
        "MTF 曲线" => T("MTF 曲线", "MTF Curves", "MTF 曲線"),
        "RMS" => T("RMS", "RMS", "RMS"),
        "圈入能量" => T("圈入能量", "Encircled Energy", "包囲エネルギー"),
        "扩展图像分析" => T("扩展图像分析", "Extended Image Analysis", "拡張画像解析"),
        "光线追迹" => T("光线追迹", "Ray Trace", "光線追跡"),
        "探测器" => T("探测器", "Detectors", "検出器"),
        "光线数据库" => T("光线数据库", "Ray Database", "光線データベース"),
        "布局" => T("布局", "Layout", "レイアウト"),
        _ => new GuideText(group)
    };

    private static GuideText CategoryName(string category) => category switch
    {
        "文件" => T("文件", "File", "ファイル"),
        "示例" => T("示例", "Samples", "サンプル"),
        "编辑" => T("编辑", "Edit", "編集"),
        "面板" => T("面板", "Panels", "パネル"),
        "模式" => T("模式", "Modes", "モード"),
        "设置" => T("设置", "Settings", "設定"),
        "视图" => T("视图", "View", "表示"),
        "数据库" => T("数据库", "Databases", "データベース"),
        "加工与图纸" => T("加工与图纸", "Manufacturing and Drawings", "製造と図面"),
        "优化" => T("优化", "Optimization", "最適化"),
        "实验室" => T("实验室", "Laboratory", "ラボ"),
        "公差" => T("公差", "Tolerancing", "公差"),
        "布局" => T("布局", "Layouts", "レイアウト"),
        "窗口" => T("窗口", "Window", "ウィンドウ"),
        "工具" => T("工具", "Tools", "ツール"),
        "帮助" => T("帮助", "Help", "ヘルプ"),
        _ => new GuideText(category)
    };

    private static string Slug(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'));
    private static string Current(GuideText text) => text.Value(CurrentLanguage());
    private static string CurrentLanguage() => UiLocalization.CurrentLanguage;
    private static GuideText T(string chinese, string english, string japanese) => new(chinese, english, japanese);

    private static readonly IReadOnlyDictionary<string, GuideText> CommandNames =
        new Dictionary<string, GuideText>(StringComparer.Ordinal)
        {
            ["new"] = T("新建空白系统", "New Blank System", "空のシステムを新規作成"),
            ["open"] = T("打开光学系统", "Open Optical System", "光学系を開く"),
            ["import-zemax"] = T("导入 Zemax ZMX", "Import Zemax ZMX", "Zemax ZMX をインポート"),
            ["save-as"] = T("保存项目", "Save Project", "プロジェクトを保存"),
            ["export-cad"] = T("导出 CAD（STEP）", "Export CAD (STEP)", "CAD（STEP）をエクスポート"),
            ["exit"] = T("退出", "Exit", "終了"),
            ["undo"] = T("撤销", "Undo", "元に戻す"),
            ["redo"] = T("重做", "Redo", "やり直す"),
            ["show-lens-editor"] = T("镜头数据编辑器", "Lens Data Editor", "レンズデータエディター"),
            ["show-non-sequential-objects"] = T("非序列对象数据", "Non-Sequential Object Data", "ノンシーケンシャルオブジェクトデータ"),
            ["enter-sequential-mode"] = T("进入顺序模式", "Enter Sequential Mode", "シーケンシャルモードへ"),
            ["enter-non-sequential-mode"] = T("进入非序列模式", "Enter Non-Sequential Mode", "ノンシーケンシャルモードへ"),
            ["show-system"] = T("系统属性", "System Properties", "システムプロパティ"),
            ["display-settings"] = T("显示格式设置", "Display Format Settings", "表示形式設定"),
            ["show-viewer"] = T("系统视图", "System View", "システムビュー"),
            ["show-viewer-2d"] = T("二维布局", "2D Layout", "2D レイアウト"),
            ["show-viewer-3d"] = T("三维布局", "3D Layout", "3D レイアウト"),
            ["show-solid-model"] = T("实体模型", "Solid Model", "ソリッドモデル"),
            ["show-material-library"] = T("材料库", "Material Library", "材料ライブラリ"),
            ["show-lens-library"] = T("镜头库", "Lens Library", "レンズライブラリ"),
            ["show-stock-lens-catalog"] = T("库存镜头查看", "Stock Lens Browser", "在庫レンズ表示"),
            ["show-stock-lens-matching"] = T("库存镜头匹配", "Stock Lens Matching", "在庫レンズ照合"),
            ["show-material-dispersion-diagram"] = T("色散图", "Dispersion Diagram", "分散図"),
            ["show-material-glass-map"] = T("玻璃图", "Glass Map", "ガラスマップ"),
            ["show-material-athermal-map"] = T("无热化玻璃图", "Athermal Glass Map", "アサーマルガラスマップ"),
            ["show-material-transmission"] = T("内部透过率 vs. 波长", "Internal Transmittance vs. Wavelength", "内部透過率 vs. 波長"),
            ["show-material-dispersion-wavelength"] = T("色散 vs. 波长", "Dispersion vs. Wavelength", "分散 vs. 波長"),
            ["show-manufacturability"] = T("可加工性评估", "Manufacturability Assessment", "製造性評価"),
            ["show-optical-drawing-iso"] = T("ISO 10110 光学制图", "ISO 10110 Optical Drawing", "ISO 10110 光学図面"),
            ["show-optical-drawing-gb"] = T("GB/T 13323—2009 光学制图", "GB/T 13323—2009 Optical Drawing", "GB/T 13323—2009 光学図面"),
            ["show-optical-drawing-gb-1991"] = T("GB/T 13323—1991 光学制图", "GB/T 13323—1991 Optical Drawing", "GB/T 13323—1991 光学図面"),
            ["show-analysis"] = T("分析面板", "Analysis Panel", "解析パネル"),
            ["show-optimization"] = T("优化面板", "Optimization Panel", "最適化パネル"),
            ["quick-focus"] = T("快速聚焦", "Quick Focus", "クイックフォーカス"),
            ["quick-adjust"] = T("快速调整", "Quick Adjust", "クイック調整"),
            ["optimization-slider"] = T("滑块", "Slider", "スライダー"),
            ["show-visual-optimizer"] = T("可视化优化器", "Visual Optimizer", "可視化オプティマイザー"),
            ["show-merit-editor"] = T("评价函数编辑器", "Merit Function Editor", "メリット関数エディター"),
            ["show-optimization-wizard"] = T("优化向导", "Optimization Wizard", "最適化ウィザード"),
            ["run-optimization"] = T("执行优化", "Run Optimization", "最適化を実行"),
            ["clear-optimization-variables"] = T("移除所有变量", "Remove All Variables", "すべての変数を削除"),
            ["set-all-radius-variables"] = T("设全部半径变量", "Set All Radius Variables", "全半径を変数に設定"),
            ["set-all-thickness-variables"] = T("设全部厚度变量", "Set All Thickness Variables", "全厚さを変数に設定"),
            ["run-random-perturbation"] = T("随机扰动搜索", "Random Perturbation Search", "ランダム摂動探索"),
            ["glass-replacement-template"] = T("玻璃替换模板", "Glass Replacement Template", "ガラス置換テンプレート"),
            ["open-optical-assembly-lab"] = T("光学装调", "Optical Alignment", "光学組立調整"),
            ["launch-initial-structure-lab"] = T("AI 初始结构", "AI Initial Structure", "AI 初期構成"),
            ["launch-coating-design-lab"] = T("光学镀膜设计", "Optical Coating Design", "光学コーティング設計"),
            ["show-tolerancing"] = T("公差数据编辑器", "Tolerance Data Editor", "公差データエディター"),
            ["run-tolerancing"] = T("运行公差分析", "Run Tolerance Analysis", "公差解析を実行"),
            ["show-tolerance-data-viewer"] = T("公差数据查看器", "Tolerance Data Viewer", "公差データビューアー"),
            ["show-tolerance-report"] = T("公差报告", "Tolerance Report", "公差レポート"),
            ["show-tolerance-histogram"] = T("公差直方图", "Tolerance Histogram", "公差ヒストグラム"),
            ["show-tolerance-yield"] = T("公差良率", "Tolerance Yield", "公差歩留まり"),
            ["show-multiconfig"] = T("多配置编辑器", "Multi-Configuration Editor", "マルチコンフィグレーションエディター"),
            ["reset-layout"] = T("重置为系统初始布局", "Reset to Initial Layout", "初期レイアウトにリセット"),
            ["save-layout-1"] = T("保存布局到槽位 1", "Save Layout to Slot 1", "レイアウトをスロット 1 に保存"),
            ["save-layout-2"] = T("保存布局到槽位 2", "Save Layout to Slot 2", "レイアウトをスロット 2 に保存"),
            ["load-layout-1"] = T("加载布局槽位 1", "Load Layout Slot 1", "レイアウトスロット 1 を読み込む"),
            ["load-layout-2"] = T("加载布局槽位 2", "Load Layout Slot 2", "レイアウトスロット 2 を読み込む"),
            ["analysis-dock-all"] = T("保留分栏停靠", "Keep Split Docking", "分割ドッキングを維持"),
            ["dock-single-pane"] = T("合并单窗格", "Merge into One Pane", "単一ペインに統合"),
            ["analysis-float-all"] = T("全部独立浮动", "Float All", "すべてフロート"),
            ["analysis-tile-all"] = T("平铺全部", "Tile All", "すべて並べて表示"),
            ["analysis-cascade-all"] = T("层叠全部", "Cascade All", "すべて重ねて表示"),
            ["analysis-clone"] = T("克隆分析", "Clone Analysis", "解析を複製"),
            ["toggle-page-lock"] = T("切换锁定", "Toggle Lock", "ロック切替"),
            ["close-all-pages"] = T("关闭其他页", "Close Other Pages", "他のページを閉じる"),
            ["save-default-layout"] = T("保存默认", "Save Default", "既定として保存"),
            ["restore-default-layout"] = T("载入默认", "Load Default", "既定を読み込む"),
            ["command-palette"] = T("命令面板", "Command Palette", "コマンドパレット"),
            ["show-user-guide"] = T("用户指南", "User Guide", "ユーザーガイド"),
            ["show-operand-help"] = T("操作数帮助", "Operand Help", "オペランドヘルプ"),
            ["recover-document"] = T("恢复未保存工程", "Recover Unsaved Project", "未保存プロジェクトを復元"),
            ["about"] = T("关于 Optical System Design", "About Optical System Design", "Optical System Design について")
        };

    internal sealed record GuideText(string Chinese, string? English = null, string? Japanese = null)
    {
        public string AllText => $"{Chinese}\n{English}\n{Japanese}";

        public string Value(string language) => language switch
        {
            UiLocalization.English => English ?? UiLocalization.Text(Chinese),
            UiLocalization.Japanese => Japanese ?? UiLocalization.Text(Chinese),
            _ => Chinese
        };
    }

    internal sealed record GuideSection(GuideText Title, GuideText Body);
    internal sealed record GuideChapter(
        string Id,
        GuideText Group,
        GuideText Title,
        GuideText Summary,
        UserGuideFigureKind Figure,
        IReadOnlyList<GuideSection> Sections);
}
