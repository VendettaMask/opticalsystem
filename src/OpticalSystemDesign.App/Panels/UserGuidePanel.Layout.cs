using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class UserGuidePanel
{
    internal const double ReadingWidth = 720;
    private readonly ComboBox _kindFilter = new() { Name = "UserGuideKindFilter", HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 1 };
    private readonly TextBlock _chapterKind = new() { Name = "UserGuideChapterKind", FontSize = 11, FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _sectionJump = new() { Name = "UserGuideSectionLinks", Spacing = 2 };
    private readonly ScrollViewer _sectionScroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _guideHeading = new() { FontSize = 17, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _guideSubtitle = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _tocHeading = new() { FontSize = 11, FontWeight = FontWeight.SemiBold };
    private readonly Expander _inlineContents = new() { Name = "UserGuideInlineContents", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _navigationToggle = new() { Name = "UserGuideNavigationToggle" };
    private readonly Button _previousChapter = new() { Name = "UserGuidePrevious" };
    private readonly Button _nextChapter = new() { Name = "UserGuideNext" };
    private Grid _readerRoot = null!;
    private Border _navigationBorder = null!;
    private Border _tocBorder = null!;
    private Border _tocLinksHost = null!;
    private StackPanel _readingPage = null!;
    private bool _narrow;
    private bool _showNavigation = true;
    private bool _wideContents;

    private Control BuildReaderLayout()
    {
        AddReadingStyles();
        _resultCount.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _path.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _summary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        _guideSubtitle.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _tocHeading.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _chapterKind.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextAccent);
        _path.FontSize = 11;
        _path.TextWrapping = TextWrapping.NoWrap;
        _path.TextTrimming = TextTrimming.CharacterEllipsis;
        _path.VerticalAlignment = VerticalAlignment.Center;
        _title.FontSize = 30;
        _title.LineHeight = 40;
        _summary.FontSize = 15;
        _summary.LineHeight = 26;
        _summary.Margin = new Thickness(0, 0, 0, 6);
        _sections.Spacing = 34;
        _resultCount.FontSize = 11;
        _search.FontSize = 12;
        _search.MinHeight = 34;
        _search.Padding = new Thickness(10, 7);
        _search.CornerRadius = new CornerRadius(6);
        _search.BorderThickness = new Thickness(1);
        _search.BindThemeResource(BorderBrushProperty, ThemeResourceBindings.Border);
        _kindFilter.FontSize = 12;
        _kindFilter.MinHeight = 32;
        _kindFilter.BorderThickness = new Thickness(0);
        _kindFilter.Background = Brushes.Transparent;
        _kindFilter.Padding = new Thickness(4, 5);
        var navigation = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = 14, Margin = new Thickness(18, 22, 16, 16) };
        var navigationTitle = new StackPanel { Spacing = 5, Children = { _guideHeading, _guideSubtitle } };
        navigation.Children.Add(navigationTitle); navigation.Children.Add(_search); navigation.Children.Add(_kindFilter); navigation.Children.Add(_contents); navigation.Children.Add(_resultCount);
        Grid.SetRow(_search, 1); Grid.SetRow(_kindFilter, 2); Grid.SetRow(_contents, 3); Grid.SetRow(_resultCount, 4);
        _contents.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.BackgroundProperty, Brushes.Transparent), new Setter(TreeViewItem.BorderThicknessProperty, new Thickness(0)),
                new Setter(TreeViewItem.PaddingProperty, new Thickness(4, 5)), new Setter(TreeViewItem.MinHeightProperty, 30d) }
        });
        _navigationBorder = new Border { Name = "UserGuideNavigation", Child = navigation, BorderThickness = new Thickness(0, 0, 1, 0) };
        _navigationBorder.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        _navigationBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 14, Margin = new Thickness(16, 6) };
        _navigationToggle.Classes.Add("guideQuiet");
        _navigationToggle.FontSize = 12;
        _navigationToggle.Padding = new Thickness(8, 4);
        _navigationToggle.MinHeight = 28;
        header.Children.Add(_navigationToggle); header.Children.Add(_path); Grid.SetColumn(_path, 1);
        var actions = PdfControls(); header.Children.Add(actions); Grid.SetColumn(actions, 2);
        _pdfExport.Classes.Add("guideQuiet");
        _pdfExport.FontSize = 20;
        _pdfExport.Width = 30;
        _pdfExport.MinWidth = 30;
        _pdfExport.Height = 28;
        _pdfExport.Padding = new Thickness(2, 0);
        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var footer = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 16, Margin = new Thickness(0, 10, 0, 0) };
        _previousChapter.Classes.Add("guideQuiet"); _nextChapter.Classes.Add("guideQuiet");
        _nextChapter.HorizontalAlignment = HorizontalAlignment.Right;
        _previousChapter.HorizontalAlignment = HorizontalAlignment.Left;
        footer.Children.Add(_previousChapter); footer.Children.Add(_nextChapter); Grid.SetColumn(_nextChapter, 1);
        var footerFrame = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0) };
        footerFrame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        _inlineContents.BorderThickness = new Thickness(0);
        _inlineContents.Padding = new Thickness(0);
        _inlineContents.FontSize = 12;
        _inlineContents.BindThemeResource(ForegroundProperty, ThemeResourceBindings.TextMuted);
        _sectionScroller.Content = _sectionJump;
        _tocLinksHost = new Border();
        var toc = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12, Margin = new Thickness(12, 34, 18, 20) };
        toc.Children.Add(_tocHeading); toc.Children.Add(_tocLinksHost); Grid.SetRow(_tocLinksHost, 1);
        _tocBorder = new Border { Name = "UserGuidePageContents", Child = toc };
        var rule = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 16) };
        rule.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border);
        _readingPage = new StackPanel
        {
            Name = "UserGuideReadingPage",
            MaxWidth = ReadingWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(38, 32, 38, 48),
            Spacing = 12,
            Children = { _chapterKind, _title, _summary, rule, _inlineContents, _sections, footerFrame }
        };
        _detailScroller.Name = "UserGuideReaderScroll";
        _detailScroller.Content = _readingPage;
        _readerRoot = new Grid { Name = "UserGuideReaderLayout", ColumnDefinitions = new("248,*,176"), RowDefinitions = new("Auto,*") };
        _readerRoot.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Surface);
        _readerRoot.Children.Add(headerBorder); _readerRoot.Children.Add(_navigationBorder); _readerRoot.Children.Add(_detailScroller); _readerRoot.Children.Add(_tocBorder);
        Grid.SetColumnSpan(headerBorder, 3); Grid.SetRow(_navigationBorder, 1); Grid.SetRow(_detailScroller, 1); Grid.SetColumn(_detailScroller, 1);
        Grid.SetRow(_tocBorder, 1); Grid.SetColumn(_tocBorder, 2);
        _kindFilter.SelectionChanged += (_, _) => { if (!_updatingTree) RebuildContents(); };
        _navigationToggle.Click += (_, _) => { _showNavigation = !_showNavigation; UpdateReaderColumns(); };
        _previousChapter.Click += (_, _) => SelectAdjacent(-1);
        _nextChapter.Click += (_, _) => SelectAdjacent(1);
        SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 760;
            if (narrow != _narrow) { _narrow = narrow; _showNavigation = !narrow; }
            _wideContents = args.NewSize.Width >= 1120;
            UpdateReaderColumns();
        };
        UpdateReaderColumns();
        return _readerRoot;
    }

    private void AddReadingStyles()
    {
        Styles.Add(new Style(x => x.OfType<Button>().Class("guideQuiet"))
        {
            Setters = { new Setter(Button.BackgroundProperty, Brushes.Transparent), new Setter(Button.BorderBrushProperty, Brushes.Transparent),
                new Setter(Button.BorderThicknessProperty, new Thickness(0)), new Setter(Button.PaddingProperty, new Thickness(6, 6)),
                new Setter(Button.CornerRadiusProperty, new CornerRadius(4)), new Setter(Button.FontSizeProperty, 12.5d),
                new Setter(Button.ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextSecondary)) }
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("guideQuiet").Class(":pointerover"))
        {
            Setters = { new Setter(Button.ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextAccent)),
                new Setter(Button.BackgroundProperty, new DynamicResourceExtension(ThemeResourceBindings.SubtleSurface)) }
        });
    }

    private void RefreshReaderLanguage()
    {
        var selected = _kindFilter.SelectedIndex;
        _updatingTree = true;
        _kindFilter.ItemsSource = new[] { T("全部内容", "All content", "すべて"), T("操作教程", "Using the software", "操作チュートリアル"),
            T("计算原理", "How calculations work", "計算原理"), T("功能参考", "Feature reference", "機能リファレンス") }.Select(Current).ToArray();
        _kindFilter.SelectedIndex = Math.Max(0, selected);
        _updatingTree = false;
        _guideHeading.Text = Current(T("用户指南", "User Guide", "ユーザーガイド"));
        _guideSubtitle.Text = Current(T("使用方法与计算原理", "Using the software and understanding results", "操作方法と計算原理"));
        _tocHeading.Text = Current(T("本章内容", "ON THIS PAGE", "この章の内容"));
        _inlineContents.Header = Current(T("本章内容", "On this page", "この章の内容"));
        _navigationToggle.Content = Current(T("目录", "Contents", "目次"));
        _pdfExport.Content = "⋯";
        AutomationProperties.SetName(_pdfExport, Current(T("更多操作：导出", "More actions: export", "その他の操作：出力")));
        ToolTip.SetTip(_pdfExport, Current(T("更多操作", "More actions", "その他の操作")));
        _pdfCancel.Content = Current(T("取消", "Cancel", "取消"));
        AutomationProperties.SetName(_kindFilter, Current(T("内容分类", "Content category", "コンテンツ分類")));
        AutomationProperties.SetName(_contents, Current(T("用户指南目录", "User guide contents", "ユーザーガイドの目次")));
    }

    private static GuideText ChapterKind(GuideChapter chapter) => chapter.Id.StartsWith("calculation-", StringComparison.Ordinal)
        ? T("计算原理", "How calculations work", "計算原理")
        : chapter.Id.StartsWith("commands-", StringComparison.Ordinal) || chapter.Id.StartsWith("analyses-", StringComparison.Ordinal)
            ? T("功能参考", "Feature reference", "機能リファレンス") : T("操作教程", "Using the software", "操作チュートリアル");

    private bool MatchesKind(GuideChapter chapter) => _kindFilter.SelectedIndex switch
    {
        1 => !chapter.Id.StartsWith("calculation-", StringComparison.Ordinal) && !chapter.Id.StartsWith("commands-", StringComparison.Ordinal) && !chapter.Id.StartsWith("analyses-", StringComparison.Ordinal),
        2 => chapter.Id.StartsWith("calculation-", StringComparison.Ordinal),
        3 => chapter.Id.StartsWith("commands-", StringComparison.Ordinal) || chapter.Id.StartsWith("analyses-", StringComparison.Ordinal),
        _ => true
    };

    private void UpdateReaderColumns()
    {
        _navigationBorder.IsVisible = _showNavigation;
        var reading = !_narrow || !_showNavigation;
        var sideContents = reading && _wideContents && _sectionJump.Children.Count > 1;
        _readerRoot.ColumnDefinitions[0].Width = _showNavigation ? (_narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(248)) : new GridLength(0);
        _readerRoot.ColumnDefinitions[1].Width = reading ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _readerRoot.ColumnDefinitions[2].Width = sideContents ? new GridLength(176) : new GridLength(0);
        _detailScroller.IsVisible = reading;
        _tocBorder.IsVisible = sideContents;
        _readingPage.Margin = _narrow ? new Thickness(22, 26, 22, 36) : new Thickness(38, 32, 38, 48);
        if (sideContents && _sectionScroller.Parent != _tocLinksHost)
        {
            _inlineContents.Content = null; _tocLinksHost.Child = _sectionScroller;
        }
        else if (!sideContents && _sectionScroller.Parent != _inlineContents)
        {
            _tocLinksHost.Child = null; _inlineContents.Content = _sectionScroller;
        }
        _sectionScroller.MaxHeight = sideContents ? double.PositiveInfinity : 280;
        _inlineContents.IsVisible = !sideContents && _sectionJump.Children.Count > 1;
    }

    private void UpdateReadingNavigation(GuideChapter chapter)
    {
        var chapters = _chapters.Where(c => _chapterItems.ContainsKey(c.Id)).ToArray();
        var index = Array.IndexOf(chapters, chapter);
        _previousChapter.Content = Current(T("← 上一章", "← Previous", "← 前の章"));
        _nextChapter.Content = Current(T("下一章 →", "Next →", "次の章 →"));
        _previousChapter.IsEnabled = index > 0;
        _nextChapter.IsEnabled = index >= 0 && index < chapters.Length - 1;
        ToolTip.SetTip(_previousChapter, _previousChapter.IsEnabled ? null : Current(T("当前筛选中没有上一章", "No previous chapter in this selection", "現在の絞り込みに前の章はありません")));
        ToolTip.SetTip(_nextChapter, _nextChapter.IsEnabled ? null : Current(T("当前筛选中没有下一章", "No next chapter in this selection", "現在の絞り込みに次の章はありません")));
        if (_narrow) _showNavigation = false;
        _inlineContents.IsExpanded = false;
        UpdateReaderColumns();
    }

    private void SelectAdjacent(int delta)
    {
        var chapters = _chapters.Where(c => _chapterItems.ContainsKey(c.Id)).ToArray();
        var index = Array.FindIndex(chapters, c => c.Id == _selectedChapterId) + delta;
        if (index >= 0 && index < chapters.Length) _contents.SelectedItem = _chapterItems[chapters[index].Id];
    }

    private void AddSectionLink(string text, Control target)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20 },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0, 6),
            Classes = { "guideQuiet" }
        };
        button.Click += (_, _) => { _inlineContents.IsExpanded = false; target.BringIntoView(); };
        _sectionJump.Children.Add(button);
    }

    private static Control FormulaBlock(string formula)
    {
        var view = new UserGuideMathView(formula) { HorizontalAlignment = HorizontalAlignment.Center };
        var scroll = new ScrollViewer
        {
            Name = "UserGuideFormulaScroll",
            Content = view,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        return new Border { Padding = new Thickness(12, 18), Child = scroll };
    }
}
