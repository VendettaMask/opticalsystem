using Avalonia;
using OpticalSystemDesign.App.Theming;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Panels;

internal enum UserGuideFigureKind
{
    Workflow,
    Interface,
    ProjectFlow,
    SequentialModel,
    NonSequentialModel,
    AnalysisFlow,
    OptimizationLoop,
    ToleranceFlow,
    LibrariesAndModules,
    WorkspaceLayout,
    HelpSystem,
    CommandReference,
    AnalysisReference
}

internal static class UserGuideIllustration
{
    private const double Width = 760;
    private const double Height = 220;
    private static readonly Lazy<Bitmap> WorkbenchScreenshot = new(() =>
    {
        var uri = new Uri("avares://OpticalSystemDesign.App/Assets/UserGuide/workbench-light.png");
        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    });

    public static Control Build(UserGuideFigureKind kind, string language)
    {
        Control visual;
        if (kind == UserGuideFigureKind.Interface && language == UiLocalization.Chinese)
        {
            visual = InterfaceScreenshot(language);
        }
        else
        {
            var canvas = new Canvas { Width = Width, Height = Height };
            switch (kind)
            {
                case UserGuideFigureKind.Workflow: Workflow(canvas, language); break;
                case UserGuideFigureKind.Interface: Interface(canvas, language); break;
                case UserGuideFigureKind.ProjectFlow: ProjectFlow(canvas, language); break;
                case UserGuideFigureKind.SequentialModel: SequentialModel(canvas, language); break;
                case UserGuideFigureKind.NonSequentialModel: NonSequentialModel(canvas, language); break;
                case UserGuideFigureKind.AnalysisFlow: AnalysisFlow(canvas, language); break;
                case UserGuideFigureKind.OptimizationLoop: OptimizationLoop(canvas, language); break;
                case UserGuideFigureKind.ToleranceFlow: ToleranceFlow(canvas, language); break;
                case UserGuideFigureKind.LibrariesAndModules: LibrariesAndModules(canvas, language); break;
                case UserGuideFigureKind.WorkspaceLayout: WorkspaceLayout(canvas, language); break;
                case UserGuideFigureKind.HelpSystem: HelpSystem(canvas, language); break;
                case UserGuideFigureKind.CommandReference: CommandReference(canvas, language); break;
                case UserGuideFigureKind.AnalysisReference: AnalysisReference(canvas, language); break;
            }
            visual = new Viewbox
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxHeight = 240,
                Child = canvas
            };
        }

        var caption = Caption(kind, language);
        var captionBlock = new TextBlock
        {
            Text = caption,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        captionBlock.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        var figure = new Border
        {
            Name = "UserGuideIllustration",
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 4, 0, 8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    visual,
                    captionBlock
                }
            }
        };
        figure.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        figure.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        AutomationProperties.SetName(figure, caption);
        AutomationProperties.SetHelpText(figure, caption);
        return figure;
    }

    private static Control InterfaceScreenshot(string language)
    {
        var image = new Image
        {
            Name = "UserGuideScreenshot",
            Source = WorkbenchScreenshot.Value,
            Width = 760,
            Height = 487,
            Stretch = Stretch.Uniform
        };
        AutomationProperties.SetName(image, L(language, "主工作台真实界面截图", "Actual main-workbench screenshot", "メインワークベンチの実画面"));
        var canvas = new Canvas { Width = 760, Height = 487, Children = { image } };
        Node(canvas, 18, 14, 150, 42, L(language, "1  功能区", "1  Ribbon", "1  リボン"), null, true);
        Node(canvas, 18, 145, 150, 42, L(language, "2  系统属性", "2  System Properties", "2  システム設定"), null, true);
        Node(canvas, 560, 145, 175, 42, L(language, "3  文档编辑区", "3  Document Area", "3  ドキュメント領域"), null, true);
        Node(canvas, 560, 426, 175, 42, L(language, "4  状态栏", "4  Status Bar", "4  ステータスバー"), null, true);
        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxHeight = 390,
            Child = canvas
        };
    }

    private static void Workflow(Canvas canvas, string language)
    {
        var labels = new[]
        {
            L(language, "新建 / 打开", "New / Open", "新規 / 開く"),
            L(language, "系统属性", "System Properties", "システム設定"),
            L(language, "建立模型", "Build Model", "モデル作成"),
            L(language, "视图与分析", "Views & Analyses", "表示と解析"),
            L(language, "优化与公差", "Optimize & Tolerance", "最適化と公差"),
            L(language, "保存 / 导出", "Save / Export", "保存 / 出力")
        };
        for (var index = 0; index < labels.Length; index++)
        {
            var x = 10 + index * 125;
            if (index > 0) Arrow(canvas, x - 25, 110, x - 5, 110);
            Node(canvas, x, 76, 105, 68, labels[index], index + 1, index is 0 or 5);
        }
    }

    private static void Interface(Canvas canvas, string language)
    {
        Panel(canvas, 90, 25, 590, 168, string.Empty, false);
        Panel(canvas, 90, 25, 590, 38, L(language, "1  功能区：命令与模式", "1  Ribbon: commands and modes", "1  リボン：コマンドとモード"), true);
        Panel(canvas, 90, 69, 155, 96, L(language, "2  系统属性\n孔径 · 视场 · 波长", "2  System Properties\nAperture · Field · Wave", "2  システム設定\n開口・視野・波長"), false);
        Panel(canvas, 253, 69, 427, 96, L(language, "3  文档区\n镜头数据 · 视图 · 分析", "3  Document Area\nLens Data · Views · Analyses", "3  ドキュメント領域\nレンズ・表示・解析"), false);
        Panel(canvas, 90, 171, 590, 22, L(language, "4  状态栏：工程、模式与一阶摘要", "4  Status: project, mode, first-order summary", "4  状態：プロジェクト、モード、一次特性"), true);
    }

    private static void ProjectFlow(Canvas canvas, string language)
    {
        Node(canvas, 20, 35, 145, 52, L(language, "ZMX / SEQ / LEN", "ZMX / SEQ / LEN", "ZMX / SEQ / LEN"), 1, false);
        Node(canvas, 20, 130, 145, 52, L(language, "新建与样例", "New & Examples", "新規とサンプル"), 2, false);
        Arrow(canvas, 170, 61, 285, 91); Arrow(canvas, 170, 156, 285, 126);
        Node(canvas, 290, 72, 180, 78, L(language, "STAROPT 工程\n完整光学数据", "STAROPT Project\nComplete optical data", "STAROPT プロジェクト\n完全な光学データ"), 3, true);
        Arrow(canvas, 475, 91, 590, 61); Arrow(canvas, 475, 126, 590, 156);
        Node(canvas, 595, 35, 145, 52, L(language, "保存与恢复", "Save & Recover", "保存と復元"), 4, false);
        Node(canvas, 595, 130, 145, 52, L(language, "STEP 与结果导出", "STEP & Result Export", "STEP と結果出力"), 5, false);
    }

    private static void SequentialModel(Canvas canvas, string language)
    {
        Axis(canvas, 30, 112, 730, 112);
        Node(canvas, 28, 77, 90, 70, L(language, "物面", "Object", "物体面"), 1, false);
        Lens(canvas, 245, 55); Lens(canvas, 365, 45); Lens(canvas, 490, 58);
        Panel(canvas, 590, 67, 28, 90, L(language, "光阑", "Stop", "絞り"), true);
        Node(canvas, 655, 77, 78, 70, L(language, "像面", "Image", "像面"), 4, false);
        Ray(canvas, 118, 92, 245, 72, 365, 82, 490, 92, 655, 106);
        Ray(canvas, 118, 132, 245, 148, 365, 138, 490, 130, 655, 116);
        Callout(canvas, 210, 20, "2", L(language, "曲率 · 厚度 · 材料", "Radius · Thickness · Material", "曲率・厚さ・材料"));
        Callout(canvas, 445, 174, "3", L(language, "净口径与表面属性", "Clear aperture & properties", "有効径と面プロパティ"));
    }

    private static void NonSequentialModel(Canvas canvas, string language)
    {
        Node(canvas, 28, 77, 120, 66, L(language, "光源", "Source", "光源"), 1, true);
        Panel(canvas, 255, 40, 105, 32, L(language, "对象 A", "Object A", "オブジェクト A"), false);
        Panel(canvas, 345, 145, 105, 32, L(language, "对象 B", "Object B", "オブジェクト B"), false);
        Panel(canvas, 585, 60, 125, 100, L(language, "探测器\n累积功率", "Detector\nAccumulated power", "検出器\n累積パワー"), true);
        Ray(canvas, 148, 110, 255, 56, 397, 145, 585, 90);
        Ray(canvas, 148, 118, 285, 150, 455, 90, 585, 128);
        Callout(canvas, 250, 8, "2", L(language, "相交 · 反射 · 折射 · 散射", "Intersect · Reflect · Refract · Scatter", "交差・反射・屈折・散乱"));
        Callout(canvas, 516, 176, "3", L(language, "路径与光线数据库", "Paths & ray database", "経路と光線データベース"));
    }

    private static void AnalysisFlow(Canvas canvas, string language)
    {
        Node(canvas, 25, 78, 145, 68, L(language, "工程快照", "Project Snapshot", "プロジェクト\nスナップショット"), 1, false);
        Arrow(canvas, 175, 112, 265, 112);
        Node(canvas, 270, 55, 185, 114, L(language, "分析参数\n视场 · 波长 · 采样\n参考 · 偏振", "Analysis Settings\nField · Wave · Sampling\nReference · Polarization", "解析設定\n視野・波長・サンプル\n基準・偏光"), 2, true);
        Arrow(canvas, 460, 112, 550, 112);
        Node(canvas, 555, 42, 175, 140, L(language, "结果页面\n曲线 · 网格 · 表格\n单位 · 导出 · 锁定", "Result Page\nPlot · Grid · Table\nUnits · Export · Lock", "結果ページ\n曲線・グリッド・表\n単位・出力・ロック"), 3, false);
    }

    private static void OptimizationLoop(Canvas canvas, string language)
    {
        Node(canvas, 30, 75, 140, 70, L(language, "设计变量", "Design Variables", "設計変数"), 1, false);
        Arrow(canvas, 175, 110, 270, 110);
        Node(canvas, 275, 75, 140, 70, L(language, "光线追迹", "Ray Trace", "光線追跡"), 2, false);
        Arrow(canvas, 420, 110, 515, 110);
        Node(canvas, 520, 75, 185, 70, L(language, "评价函数与约束", "Merit Function & Constraints", "メリット関数と制約"), 3, true);
        Arrow(canvas, 610, 150, 610, 188); Arrow(canvas, 610, 188, 100, 188); Arrow(canvas, 100, 188, 100, 150);
        Label(canvas, 300, 191, 210, 24, L(language, "优化器更新变量并重复计算", "Optimizer updates variables and repeats", "最適化器が変数を更新して反復"), true);
    }

    private static void ToleranceFlow(Canvas canvas, string language)
    {
        Node(canvas, 20, 76, 130, 72, L(language, "名义处方与公差", "Nominal Design & Tolerances", "公称設計と公差"), 1, false);
        Arrow(canvas, 155, 112, 235, 112);
        Node(canvas, 240, 76, 145, 72, L(language, "Monte Carlo\n补偿与验收", "Monte Carlo\nCompensate & Accept", "Monte Carlo\n補償と判定"), 2, true);
        Arrow(canvas, 390, 112, 470, 112);
        Histogram(canvas, 485, 48, 110, 115);
        Arrow(canvas, 600, 112, 650, 112);
        Node(canvas, 655, 53, 85, 118, L(language, "良率\n报告\n图纸", "Yield\nReport\nDrawing", "歩留まり\nレポート\n図面"), 4, false);
        Callout(canvas, 465, 176, "3", L(language, "分布、直方图与失败样本", "Distributions, histogram, failed trials", "分布、ヒストグラム、失敗試行"));
    }

    private static void LibrariesAndModules(Canvas canvas, string language)
    {
        Node(canvas, 285, 74, 190, 78, L(language, "当前光学系统", "Current Optical System", "現在の光学系"), 1, true);
        var nodes = new[]
        {
            (25d, 28d, L(language, "材料库", "Materials", "材料")),
            (25d, 150d, L(language, "镜头与库存库", "Lens & Stock Libraries", "レンズ・在庫ライブラリ")),
            (595d, 28d, L(language, "AI 初始结构", "AI Initial Structure", "AI 初期構成")),
            (595d, 150d, L(language, "镀膜与装调", "Coating & Alignment", "コーティングと組立"))
        };
        foreach (var (x, y, label) in nodes) Node(canvas, x, y, 140, 48, label, null, false);
        Arrow(canvas, 165, 52, 285, 94); Arrow(canvas, 165, 174, 285, 132);
        Arrow(canvas, 595, 52, 475, 94); Arrow(canvas, 595, 174, 475, 132);
    }

    private static void WorkspaceLayout(Canvas canvas, string language)
    {
        Panel(canvas, 70, 25, 485, 170, string.Empty, false);
        Panel(canvas, 70, 25, 485, 30, L(language, "功能区", "Ribbon", "リボン"), true);
        Panel(canvas, 70, 60, 145, 105, L(language, "侧栏", "Sidebar", "サイドバー"), false);
        Panel(canvas, 220, 60, 162, 105, L(language, "文档 A", "Document A", "ドキュメント A"), false);
        Panel(canvas, 387, 60, 168, 105, L(language, "文档 B", "Document B", "ドキュメント B"), false);
        Panel(canvas, 70, 170, 485, 25, L(language, "状态栏", "Status", "状態"), true);
        Node(canvas, 610, 60, 120, 75, L(language, "浮动页面", "Floating Page", "フロートページ"), 3, false);
        Callout(canvas, 570, 155, "1", L(language, "停靠 · 分栏 · 平铺", "Dock · Split · Tile", "ドック・分割・整列"));
        Callout(canvas, 570, 184, "2", L(language, "保存默认与槽位布局", "Save default and slot layouts", "既定・スロット保存"));
    }

    private static void HelpSystem(Canvas canvas, string language)
    {
        Node(canvas, 285, 22, 190, 56, L(language, "帮助", "Help", "ヘルプ"), 1, true);
        Arrow(canvas, 380, 82, 380, 108);
        var labels = new[]
        {
            L(language, "用户指南\n工作流与全部命令", "User Guide\nWorkflows & all commands", "ユーザーガイド\n手順と全コマンド"),
            L(language, "操作数帮助\n定义与参数", "Operand Help\nDefinitions & parameters", "オペランドヘルプ\n定義とパラメーター"),
            L(language, "工具提示\n当前上下文", "Tooltips\nCurrent context", "ツールチップ\n現在の状況"),
            L(language, "关于\n版本与运行时", "About\nVersion & runtime", "バージョン情報\n製品とランタイム")
        };
        for (var index = 0; index < labels.Length; index++)
        {
            var x = 18 + index * 185;
            Arrow(canvas, 380, 108, x + 80, 125);
            Node(canvas, x, 125, 160, 70, labels[index], index + 2, index == 0);
        }
    }

    private static void CommandReference(Canvas canvas, string language)
    {
        Node(canvas, 25, 74, 145, 72, L(language, "搜索词或命令 ID", "Keyword or Command ID", "キーワードまたは ID"), 1, false);
        Arrow(canvas, 175, 110, 265, 110);
        Node(canvas, 270, 55, 185, 110, L(language, "完整命令参考\n按功能区分类\n自动读取注册命令", "Complete Command Reference\nGrouped by ribbon area\nReads registered commands", "完全コマンドリファレンス\nリボン別分類\n登録コマンドを取得"), 2, true);
        Arrow(canvas, 460, 110, 550, 110);
        Node(canvas, 555, 74, 175, 72, L(language, "Ctrl+Q 或功能区执行", "Run with Ctrl+Q or Ribbon", "Ctrl+Q またはリボンで実行"), 3, false);
    }

    private static void AnalysisReference(Canvas canvas, string language)
    {
        Node(canvas, 18, 78, 130, 68, L(language, "当前系统", "Current System", "現在のシステム"), 1, false);
        Arrow(canvas, 153, 112, 225, 112);
        Node(canvas, 230, 48, 180, 128, L(language, "分析目录\n光线 · 像差 · 波前\nPSF · MTF · 能量\n非序列追迹与探测器", "Analysis Catalog\nRays · Aberrations · Wavefront\nPSF · MTF · Energy\nNSC trace & detectors", "解析カタログ\n光線・収差・波面\nPSF・MTF・エネルギー\nNSC 追跡と検出器"), 2, true);
        Arrow(canvas, 415, 112, 490, 112);
        Node(canvas, 495, 62, 120, 100, L(language, "页面参数", "Page Settings", "ページ設定"), 3, false);
        Arrow(canvas, 620, 112, 665, 112);
        Node(canvas, 670, 62, 72, 100, L(language, "结果", "Result", "結果"), 4, false);
    }

    private static void Node(Canvas canvas, double x, double y, double width, double height, string text, int? number, bool accent)
    {
        var label = new TextBlock
        {
            Text = text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = accent ? FontWeight.SemiBold : FontWeight.Normal,
            Margin = new Thickness(number.HasValue ? 20 : 8, 4, 8, 4)
        };
        var border = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = ThemeChromeResources.StandardOverlayCornerRadius,
            BorderThickness = new Thickness(accent ? 2 : 1),
            Child = label
        };
        border.BindThemeResource(Border.BackgroundProperty, accent ? ThemeResourceBindings.SelectionBackground : ThemeResourceBindings.Surface);
        border.BindThemeResource(Border.BorderBrushProperty, accent ? ThemeResourceBindings.TextAccent : ThemeResourceBindings.Border);
        label.BindThemeResource(TextBlock.ForegroundProperty, accent ? ThemeResourceBindings.SelectionForeground : ThemeResourceBindings.TextPrimary);
        canvas.Children.Add(border); Canvas.SetLeft(border, x); Canvas.SetTop(border, y);
        if (number.HasValue) Badge(canvas, x - 8, y - 8, number.Value.ToString());
    }

    private static void Panel(Canvas canvas, double x, double y, double width, double height, string text, bool accent)
    {
        var label = new TextBlock
        {
            Text = text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(5)
        };
        var border = new Border { Width = width, Height = height, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Child = label };
        border.BindThemeResource(Border.BackgroundProperty, accent ? ThemeResourceBindings.SectionHeaderBackground : ThemeResourceBindings.Surface);
        border.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        label.BindThemeResource(TextBlock.ForegroundProperty, accent ? ThemeResourceBindings.SectionHeaderForeground : ThemeResourceBindings.TextPrimary);
        canvas.Children.Add(border); Canvas.SetLeft(border, x); Canvas.SetTop(border, y);
    }

    private static void Label(Canvas canvas, double x, double y, double width, double height, string text, bool muted)
    {
        var label = new TextBlock { Width = width, Height = height, Text = text, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        label.BindThemeResource(TextBlock.ForegroundProperty, muted ? ThemeResourceBindings.MutedText : ThemeResourceBindings.TextPrimary);
        canvas.Children.Add(label); Canvas.SetLeft(label, x); Canvas.SetTop(label, y);
    }

    private static void Badge(Canvas canvas, double x, double y, string text)
    {
        var label = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Bold };
        label.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextOnAccent);
        var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = label };
        badge.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.TextAccent);
        canvas.Children.Add(badge); Canvas.SetLeft(badge, x); Canvas.SetTop(badge, y);
    }

    private static void Callout(Canvas canvas, double x, double y, string number, string text)
    {
        Badge(canvas, x, y, number);
        Label(canvas, x + 34, y + 3, 230, 28, text, true);
    }

    private static void Arrow(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        var line = new Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), StrokeThickness = 2 };
        line.BindThemeResource(Shape.StrokeProperty, ThemeResourceBindings.TextAccent);
        canvas.Children.Add(line);
        var angle = Math.Atan2(y2 - y1, x2 - x1);
        var head = new Polygon
        {
            Points = new Points
            {
                new(x2, y2),
                new(x2 - 10 * Math.Cos(angle - .45), y2 - 10 * Math.Sin(angle - .45)),
                new(x2 - 10 * Math.Cos(angle + .45), y2 - 10 * Math.Sin(angle + .45))
            }
        };
        head.BindThemeResource(Shape.FillProperty, ThemeResourceBindings.TextAccent);
        canvas.Children.Add(head);
    }

    private static void Axis(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        var axis = new Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double> { 5, 4 } };
        axis.BindThemeResource(Shape.StrokeProperty, ThemeResourceBindings.MutedText);
        canvas.Children.Add(axis);
    }

    private static void Ray(Canvas canvas, params double[] points)
    {
        var polyline = new Polyline { StrokeThickness = 2, Points = new Points() };
        for (var index = 0; index < points.Length; index += 2) polyline.Points.Add(new Point(points[index], points[index + 1]));
        polyline.BindThemeResource(Shape.StrokeProperty, ThemeResourceBindings.TextAccent);
        canvas.Children.Add(polyline);
    }

    private static void Lens(Canvas canvas, double x, double y)
    {
        var lens = new Ellipse { Width = 30, Height = 112, StrokeThickness = 2, Opacity = .8 };
        lens.BindThemeResource(Shape.FillProperty, ThemeResourceBindings.SelectionBackground);
        lens.BindThemeResource(Shape.StrokeProperty, ThemeResourceBindings.TextAccent);
        canvas.Children.Add(lens); Canvas.SetLeft(lens, x); Canvas.SetTop(lens, y);
    }

    private static void Histogram(Canvas canvas, double x, double y, double width, double height)
    {
        Panel(canvas, x, y, width, height, string.Empty, false);
        var bars = new[] { 22d, 50d, 84d, 60d, 28d };
        for (var index = 0; index < bars.Length; index++)
        {
            var bar = new Border { Width = 13, Height = bars[index] };
            bar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.TextAccent);
            canvas.Children.Add(bar);
            Canvas.SetLeft(bar, x + 14 + index * 18);
            Canvas.SetTop(bar, y + height - 12 - bars[index]);
        }
    }

    private static string Caption(UserGuideFigureKind kind, string language) => kind switch
    {
        UserGuideFigureKind.Workflow => L(language, "图：从建立系统到保存结果的推荐顺序", "Figure: recommended sequence from system creation to saved results", "図：システム作成から結果保存までの推奨順序"),
        UserGuideFigureKind.Interface => L(language, "图：主界面的四个主要区域", "Figure: the four main areas of the workbench", "図：ワークベンチの 4 つの主要領域"),
        UserGuideFigureKind.ProjectFlow => L(language, "图：导入、原生工程、恢复与导出的数据边界", "Figure: data boundaries for import, native projects, recovery, and export", "図：インポート、ネイティブプロジェクト、復元、出力の境界"),
        UserGuideFigureKind.SequentialModel => L(language, "图：顺序系统按面传播及关键处方数据", "Figure: ordered propagation and key prescription data in a sequential system", "図：シーケンシャル系の面順伝搬と主要処方データ"),
        UserGuideFigureKind.NonSequentialModel => L(language, "图：非序列系统中的对象相交、路径和探测器", "Figure: object intersections, paths, and detectors in a non-sequential system", "図：ノンシーケンシャル系の交差、経路、検出器"),
        UserGuideFigureKind.AnalysisFlow => L(language, "图：分析从工程快照到带单位结果页的数据流", "Figure: analysis data flow from project snapshot to unit-aware results", "図：プロジェクトスナップショットから単位付き結果までの解析フロー"),
        UserGuideFigureKind.OptimizationLoop => L(language, "图：变量、追迹、评价函数和优化器构成闭环", "Figure: the closed loop of variables, tracing, merit function, and optimizer", "図：変数、追跡、メリット関数、最適化器の閉ループ"),
        UserGuideFigureKind.ToleranceFlow => L(language, "图：从名义处方到 Monte Carlo、良率和制造输出", "Figure: from nominal design through Monte Carlo to yield and manufacturing output", "図：公称設計から Monte Carlo、歩留まり、製造出力まで"),
        UserGuideFigureKind.LibrariesAndModules => L(language, "图：数据库和内置模块与当前系统之间的数据关系", "Figure: data relationships between libraries, built-in modules, and the current system", "図：ライブラリ、内蔵モジュール、現在の系のデータ関係"),
        UserGuideFigureKind.WorkspaceLayout => L(language, "图：停靠、分栏、浮动及布局保存", "Figure: docking, splitting, floating, and layout persistence", "図：ドッキング、分割、フロート、レイアウト保存"),
        UserGuideFigureKind.HelpSystem => L(language, "图：用户指南、操作数帮助和上下文帮助的分工", "Figure: roles of the User Guide, Operand Help, and contextual help", "図：ユーザーガイド、オペランドヘルプ、コンテキストヘルプの役割"),
        UserGuideFigureKind.CommandReference => L(language, "图：通过分类、搜索和稳定命令标识找到全部功能", "Figure: find every feature by category, search, and stable command ID", "図：分類、検索、安定したコマンド ID で全機能を検索"),
        _ => L(language, "图：从分析目录选择类型、设置参数并读取结果", "Figure: choose an analysis, set parameters, and read the result", "図：解析を選択し、パラメーターを設定して結果を読む")
    };

    private static string L(string language, string chinese, string english, string japanese) => language switch
    {
        UiLocalization.English => english,
        UiLocalization.Japanese => japanese,
        _ => chinese
    };
}
