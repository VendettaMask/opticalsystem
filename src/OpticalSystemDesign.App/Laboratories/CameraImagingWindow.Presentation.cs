using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private readonly ComboBox _stageSelector = new() { Name = "camera-stage-selector", MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 5 };
    private readonly ToggleButton _stageOverview = new() { Name = "camera-stage-overview", Content = "阶段总览" };
    private readonly ComboBox _stageZoom = Choice("camera-stage-zoom", "适合窗口", "100%");
    private readonly ComboBox _comparisonSelector = Choice("camera-comparison-selector", "并排对照", "固定结果 A", "当前结果 B", "显示 RGB 绝对差 ×4");
    private readonly ComboBox _comparisonZoom = Choice("camera-comparison-zoom", "适合窗口", "100%");
    private readonly ComboBox _viewSelector = new() { Name = "camera-view-selector", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ProgressBar _activity = new() { Width = 70, Height = 3, IsIndeterminate = true };
    private Control? _fileActionsContent;
    internal Control FileActionsContent => _fileActionsContent!;
    private double _popupFontSize = DisplayTypography.Body;
    private ScrollViewer? _stageScroll, _comparisonScroll;
    private Panel? _resultTabHeaders;
    private TabControl? _resultTabs;
    private bool _syncingView;

    private Control BuildCameraCommandBar(Button import, Control experimentActions)
    {
        _fileActionsContent = new StackPanel { Spacing = 8, MinWidth = 300, MaxWidth = 480, Children = { import, new Separator(), experimentActions } };
        var fileFlyout = new Flyout { Content = _fileActionsContent };
        fileFlyout.Opening += (_, _) =>
        {
            DisplayTypography.ApplyRecursively(_fileActionsContent, _popupFontSize); _popupFontSize = FontSize;
            TextElement.SetFontSize(_fileActionsContent, FontSize); TextElement.SetFontFamily(_fileActionsContent, FontFamily);
        };
        var file = new DropDownButton { Name = "camera-file-menu", Content = "文件", Flyout = fileFlyout, Padding = new(10, 5) };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Children = { file } };
        file.Margin = new(0,0,6,4);
        foreach (var (button, icon) in new[] { (_run,"action-run"),(_cancel,"action-stop"),(_pin,"action-pin"),(_export,"file-export") })
        {
            var label = button.Content?.ToString() ?? "";
            Avalonia.Automation.AutomationProperties.SetName(button, label); ToolTip.SetTip(button,label);
            button.Padding = new(10, 5);
            button.Margin = new(0,0,6,4);
            button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
            { new LocalIcon { IconName = icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = button == _pin ? "固定 A" : button == _export ? "导出" : label, VerticalAlignment = VerticalAlignment.Center } } };
            actions.Children.Add(button);
        }
        _document.TextWrapping = TextWrapping.NoWrap; _document.TextTrimming = TextTrimming.CharacterEllipsis;
        _document.FontSize = DisplayTypography.BodySmall; _document.FontWeight = FontWeight.Normal;
        _document.Margin = new(14, 0, 0, 0); _document.VerticalAlignment = VerticalAlignment.Center;
        _document.Bind(ToolTip.TipProperty,new Binding(nameof(TextBlock.Text)){Source=_document});
        _document.MaxWidth = 260;
        var toolbar = new Grid { Name = "LaboratoryHeader", ColumnDefinitions = new("*,Auto"), Margin = new(10, 6), Children = { actions } };
        Grid.SetColumn(_document,1);toolbar.Children.Add(_document);
        toolbar.SizeChanged += (_,_) => _document.IsVisible = toolbar.Bounds.Width >= 1000;
        return CameraBand(toolbar);
    }

    private Control BuildCameraWorkspace(Control header, ScrollViewer settings, TabControl tabs)
    {
        _resultTabs=tabs;
        tabs.ItemsPanel = new FuncTemplate<Panel?>(() =>
        {
            _resultTabHeaders = new WrapPanel { Orientation = Orientation.Horizontal, IsVisible = !_viewSelector.IsVisible };
            return _resultTabHeaders;
        });
        foreach (var tab in (TabItem[])tabs.ItemsSource!) { tab.FontSize = DisplayTypography.BodySmall; tab.Classes.Add("lab-tab"); }
        _viewSelector.SelectionChanged += (_,_) => { if (!_syncingView && _viewSelector.SelectedIndex >= 0) tabs.SelectedIndex = _viewSelector.SelectedIndex; };
        tabs.SelectionChanged += (_,e) => { if(ReferenceEquals(e.Source,tabs)) RefreshCameraViewNames(); };
        _viewSelector.Margin = new(4,0,4,5);
        var results = new Grid { Name="CameraResultWorkspace", RowDefinitions=new("Auto,*"), Children={_viewSelector,tabs} };Grid.SetRow(tabs,1);
        var body = new Grid { Name="CameraWorkspace", ColumnDefinitions=new("320,5,*"), Margin=new(8) };
        body.ColumnDefinitions[0].MinWidth=270;body.ColumnDefinitions[0].MaxWidth=520;body.ColumnDefinitions[2].MinWidth=300;
        body.Children.Add(CameraPane(settings));
        var splitter=new GridSplitter { Name="camera-parameter-splitter",Width=5,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brushes.Transparent };
        Grid.SetColumn(splitter,1);body.Children.Add(splitter);Grid.SetColumn(results,2);body.Children.Add(results);
        void Navigation()
        {
            var compact=results.Bounds.Width < Math.Max(680,FontSize*43);
            _viewSelector.IsVisible=compact;if(_resultTabHeaders is not null)_resultTabHeaders.IsVisible=!compact;
            UpdateStageLayout();UpdateComparisonLayout();
        }
        results.SizeChanged+=(_,_)=>Navigation();
        PropertyChanged+=(_,e)=>{if(e.Property==FontSizeProperty){body.ColumnDefinitions[0].Width=new(Math.Max(320,FontSize*19));Navigation();}};
        UiLocalization.LanguageChanged+=CameraViewLanguageChanged;
        Closed+=(_,_)=>UiLocalization.LanguageChanged-=CameraViewLanguageChanged;
        RefreshCameraViewNames();
        _status.FontSize=DisplayTypography.BodySmall;_status.TextWrapping=TextWrapping.NoWrap;_status.TextTrimming=TextTrimming.CharacterEllipsis;
        _status.Bind(ToolTip.TipProperty,new Binding(nameof(TextBlock.Text)){Source=_status});
        var status=new Grid{ColumnDefinitions=new("*,Auto"),Margin=new(10,5),ColumnSpacing=12,Children={_status,_activity}};Grid.SetColumn(_activity,1);
        var bar=CameraBand(status);bar.BorderThickness=new(0,1,0,0);bar.BindThemeResource(Border.BackgroundProperty,ThemeResourceBindings.StatusBarBackground);
        var root=new Grid{RowDefinitions=new("Auto,*,Auto"),Children={header,body,bar}};Grid.SetRow(body,1);Grid.SetRow(bar,2);return root;
    }
    private void CameraViewLanguageChanged(object? sender,EventArgs e)=>RefreshCameraViewNames();
    private void RefreshCameraViewNames()
    {
        if(_resultTabs is null)return;
        _syncingView=true;
        try { _viewSelector.ItemsSource=((TabItem[])_resultTabs.ItemsSource!).Select(t=>t.Header?.ToString()??"").ToArray();_viewSelector.SelectedIndex=_resultTabs.SelectedIndex;
            RefreshCameraStageNames(); }
        finally{_syncingView=false;}
    }
    private Control BuildStageWorkspace()
    {
        _stageSelector.ItemsSource=_stageViews.Select(v=>v.Title).ToArray();_stageSelector.SelectedIndex=5;
        var toolbar=new Grid { ColumnDefinitions=new("*,Auto,Auto"),ColumnSpacing=6,Margin=new(4,5) };
        toolbar.Children.Add(_stageSelector);Grid.SetColumn(_stageOverview,1);toolbar.Children.Add(_stageOverview);
        _stageZoom.Width=112;Grid.SetColumn(_stageZoom,2);toolbar.Children.Add(_stageZoom);
        toolbar.SizeChanged+=(_,_)=>
        {
            var narrow=toolbar.Bounds.Width<400;
            toolbar.ColumnDefinitions=new(narrow?"*,Auto":"*,Auto,Auto");toolbar.RowDefinitions=new(narrow?"Auto,Auto":"Auto");
            Grid.SetColumnSpan(_stageSelector,narrow?2:1);Grid.SetRow(_stageOverview,narrow?1:0);Grid.SetColumn(_stageOverview,narrow?0:1);
            _stageOverview.HorizontalAlignment=narrow?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
            Grid.SetRow(_stageZoom,narrow?1:0);Grid.SetColumn(_stageZoom,narrow?1:2);
        };
        _stageScroll=new ScrollViewer { Name="camera-stage-viewport",Content=_stages,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled };
        _stageScroll.SizeChanged+=(_,_)=>UpdateStageLayout();
        _stageSelector.SelectionChanged+=(_,_)=>UpdateStageLayout();_stageOverview.IsCheckedChanged+=(_,_)=>UpdateStageLayout();
        _stageZoom.SelectionChanged+=(_,_)=>{foreach(var view in _stageViews)view.SetFit(_stageZoom.SelectedIndex==0);};
        for(var i=0;i<_stageViews.Length;i++)_stageViews[i].Name="camera-stage-"+i;
        var details=new Expander { Header="结果指标",Content=Scroll(_metrics),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,MaxHeight=190 };
        _metrics.FontSize=DisplayTypography.BodySmall;details.IsVisible=false;
        _metrics.PropertyChanged+=(_,e)=>{if(e.Property==TextBlock.TextProperty)details.IsVisible=!string.IsNullOrWhiteSpace(_metrics.Text);};
        var page=new Grid{RowDefinitions=new("Auto,*,Auto"),Children={toolbar,_stageScroll,details}};Grid.SetRow(_stageScroll,1);Grid.SetRow(details,2);return page;
    }
    private void UpdateStageLayout()
    {
        if(_stageScroll is null)return;var overview=_stageOverview.IsChecked==true;
        var columns=overview?(_stageScroll.Bounds.Width>=900?3:_stageScroll.Bounds.Width>=560?2:1):1;
        _stages.ColumnDefinitions=new(string.Join(",",Enumerable.Repeat("*",columns)));
        _stages.RowDefinitions=new(string.Join(",",Enumerable.Repeat(overview?"Auto":"*",overview?(6+columns-1)/columns:1)));
        _stages.Height=overview?double.NaN:Math.Max(1,_stageScroll.Bounds.Height);
        _stageScroll.VerticalScrollBarVisibility=overview?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
        for(var i=0;i<_stageViews.Length;i++)
        {
            var view=_stageViews[i];view.IsVisible=overview||i==_stageSelector.SelectedIndex;view.MinHeight=overview?Math.Max(220,FontSize*12):0;
            Grid.SetColumn(view,overview?i%columns:0);Grid.SetRow(view,overview?i/columns:0);
        }
    }
    private void RefreshCameraStageNames()
    {
        var names=_stageViews.Select(v=>v.Title).ToArray();
        if(_stageSelector.ItemsSource is IEnumerable<string> current&&current.SequenceEqual(names))return;
        var selected=_stageSelector.SelectedIndex;_stageSelector.ItemsSource=names;_stageSelector.SelectedIndex=selected<0?5:selected;
    }
    private Control BuildComparisonWorkspace()
    {
        var toolbar=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=6,Margin=new(4,5),Children={_comparisonSelector,_comparisonZoom}};
        _comparisonZoom.Width=112;Grid.SetColumn(_comparisonZoom,1);
        _comparisonScroll=new ScrollViewer{Name="camera-comparison-viewport",Content=_comparisons,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        _comparisonScroll.SizeChanged+=(_,_)=>UpdateComparisonLayout();_comparisonSelector.SelectionChanged+=(_,_)=>UpdateComparisonLayout();
        _comparisonZoom.SelectionChanged+=(_,_)=>{foreach(var view in _comparisonViews)view.SetFit(_comparisonZoom.SelectedIndex==0);};
        for(var i=0;i<_comparisonViews.Length;i++)_comparisonViews[i].Name="camera-comparison-"+i;
        var notes=new Expander { Header="对照信息",Content=Scroll(_comparisonInfo),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,MaxHeight=190 };
        var page=new Grid{RowDefinitions=new("Auto,*,Auto"),Children={toolbar,_comparisonScroll,notes}};Grid.SetRow(_comparisonScroll,1);Grid.SetRow(notes,2);return page;
    }
    private void UpdateComparisonLayout()
    {
        if(_comparisonScroll is null)return;var together=_comparisonSelector.SelectedIndex==0;
        var columns=together?(_comparisonScroll.Bounds.Width>=860?3:_comparisonScroll.Bounds.Width>=560?2:1):1;
        var stacked=together&&columns<3;
        _comparisons.ColumnDefinitions=new(string.Join(",",Enumerable.Repeat("*",columns)));
        _comparisons.RowDefinitions=new(string.Join(",",Enumerable.Repeat(stacked?"Auto":"*",together?(3+columns-1)/columns:1)));
        _comparisons.Height=stacked?double.NaN:Math.Max(1,_comparisonScroll.Bounds.Height);
        _comparisonScroll.VerticalScrollBarVisibility=stacked?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
        for(var i=0;i<3;i++){var view=_comparisonViews[i];view.IsVisible=together||i==_comparisonSelector.SelectedIndex-1;view.MinHeight=stacked?220:0;Grid.SetColumn(view,together?i%columns:0);Grid.SetRow(view,together?i/columns:0);}
    }
    private static Control Group(string title,Control[] controls)
    {
        var content=new StackPanel{Spacing=6,Margin=new(10,3,10,10)};
        foreach(var control in controls)
        {
            if(control is TextBlock { Name:null,Text:{Length:>85} })content.Children.Add(new Expander{Header="说明",Content=control,HorizontalContentAlignment=HorizontalAlignment.Stretch});
            else content.Children.Add(control);
        }
        return new Expander { Header=new TextBlock { Text=title,FontWeight=FontWeight.SemiBold,FontSize=DisplayTypography.CardTitle,TextWrapping=TextWrapping.Wrap },Content=content,
            IsExpanded=title is "输入与光学" or "曝光与传感器",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch };
    }
    private static Border CameraPane(Control content)
    {
        var pane=new Border{Child=content,BorderThickness=new(1),ClipToBounds=true};pane.BindThemeResource(Border.BackgroundProperty,ThemeResourceBindings.Surface);pane.BindThemeResource(Border.BorderBrushProperty,ThemeResourceBindings.Border);return pane;
    }
    private static Border CameraBand(Control content)
    {
        var band=new Border{Child=content,BorderThickness=new(0,0,0,1)};band.BindThemeResource(Border.BackgroundProperty,ThemeResourceBindings.SubtleSurface);band.BindThemeResource(Border.BorderBrushProperty,ThemeResourceBindings.Border);return band;
    }
}
