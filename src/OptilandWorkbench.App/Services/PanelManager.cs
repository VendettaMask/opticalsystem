using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.App.Services;

public enum WorkspacePanelId
{
    LensEditor,
    SystemProperties,
    Viewer,
    Analysis,
    Optimization,
    Tolerancing,
    MultiConfiguration
}

public sealed record WorkspacePanelDescriptor(
    WorkspacePanelId Id,
    string Title,
    Func<Control> CreateContent);

public sealed class PanelManager
{
    private static readonly Color BorderGray = Color.FromRgb(209, 209, 214);
    private static readonly Color ExplorerBackground = Color.FromRgb(245, 245, 247);

    private readonly AppSettings _settings;
    private readonly IReadOnlyList<WorkspacePanelDescriptor> _panels;
    private readonly IReadOnlyList<Control> _panelContents;
    private ContentControl _workspaceContent = null!;
    private int _selectedPanelIndex;

    public PanelManager(OptilandConnector connector, AppSettings settings)
    {
        _settings = settings;
        _panels = new WorkspacePanelDescriptor[]
        {
            new(WorkspacePanelId.LensEditor, "镜头数据编辑器", () => new LensEditorPanel(connector)),
            new(WorkspacePanelId.Viewer, "系统视图", () => new ViewerPanel(connector)),
            new(WorkspacePanelId.Analysis, "分析工作区", () => new AnalysisPanel(connector, settings)),
            new(WorkspacePanelId.Optimization, "评价函数与优化", () => new OptimizationPanel(connector)),
            new(WorkspacePanelId.Tolerancing, "公差分析", () => new TolerancingPanel(connector)),
            new(WorkspacePanelId.MultiConfiguration, "多配置编辑器", () => new MultiConfigurationPanel(connector))
        };
        _panelContents = _panels.Select(descriptor => descriptor.CreateContent()).ToArray();

        WorkspaceGrid = BuildWorkspace(connector);
    }

    public Grid WorkspaceGrid { get; }

    public void Show(WorkspacePanelId id)
    {
        if (id == WorkspacePanelId.SystemProperties)
        {
            WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(
                Math.Max(286, WorkspaceGrid.ColumnDefinitions[0].ActualWidth));
            return;
        }

        var index = IndexOf(id);
        if (index >= 0)
        {
            SelectPanel(index);
        }
    }

    public void ShowAnalysis(string analysisName)
    {
        var index = IndexOf(WorkspacePanelId.Analysis);
        if (index < 0 || _panelContents[index] is not AnalysisPanel analysisPanel)
        {
            return;
        }

        SelectPanel(index);
        analysisPanel.OpenAnalysis(analysisName);
    }

    public void ShowViewer(OpticSceneViewMode mode)
    {
        var index = IndexOf(WorkspacePanelId.Viewer);
        if (index < 0 || _panelContents[index] is not ViewerPanel viewerPanel)
        {
            return;
        }

        SelectPanel(index);
        viewerPanel.ShowView(mode);
    }

    public void DockAnalysisWindows()
    {
        AnalysisPanel()?.DockAllWindows();
        Show(WorkspacePanelId.Analysis);
    }

    public void FloatAnalysisWindows()
    {
        AnalysisPanel()?.FloatAllWindows();
        Show(WorkspacePanelId.Analysis);
    }

    public void TileAnalysisWindows()
    {
        AnalysisPanel()?.TileAllWindows();
        Show(WorkspacePanelId.Analysis);
    }

    public void CascadeAnalysisWindows()
    {
        AnalysisPanel()?.CascadeAllWindows();
        Show(WorkspacePanelId.Analysis);
    }

    public WorkspaceLayoutState CaptureLayout()
    {
        var width = WorkspaceGrid.ColumnDefinitions.Count == 0
            ? 286
            : Math.Clamp(WorkspaceGrid.ColumnDefinitions[0].ActualWidth, 230, 360);
        return new WorkspaceLayoutState(width, 0, Math.Max(0, _selectedPanelIndex));
    }

    public void ApplyLayout(WorkspaceLayoutState layout)
    {
        WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(layout.LeftPaneWidth, 230, 360));
        SelectPanel(layout.RightTabIndex, persist: false);
    }

    public void ResetLayout()
    {
        ApplyLayout(new WorkspaceLayoutState(286, 0, 0));
    }

    private Grid BuildWorkspace(OptilandConnector connector)
    {
        var initialExplorerWidth = _settings.LeftPaneWidth > 360
            ? 286
            : Math.Clamp(_settings.LeftPaneWidth, 230, 360);
        var grid = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 247)),
            ColumnDefinitions = new ColumnDefinitions($"{initialExplorerWidth},8,*")
        };

        var explorer = BuildSystemExplorer(connector);
        var splitterGuide = new Border
        {
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(209, 209, 214))
        };
        var splitter = new GridSplitter
        {
            Width = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent
        };
        var selectedIndex = _settings.RightTabIndex >= 0 && _settings.RightTabIndex < _panels.Count
            ? _settings.RightTabIndex
            : 0;
        _selectedPanelIndex = selectedIndex;
        _workspaceContent = new ContentControl
        {
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 247)),
            Content = _panelContents[selectedIndex]
        };

        Grid.SetColumn(explorer, 0);
        Grid.SetColumn(splitterGuide, 1);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(_workspaceContent, 2);
        grid.Children.Add(explorer);
        grid.Children.Add(splitterGuide);
        grid.Children.Add(splitter);
        grid.Children.Add(_workspaceContent);
        return grid;
    }

    private Control BuildSystemExplorer(OptilandConnector connector)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("36,*") };
        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = new SolidColorBrush(BorderGray),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0),
            Child = new TextBlock
            {
                Text = "系统选项",
                Foreground = new SolidColorBrush(Color.FromRgb(29, 29, 31)),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var properties = new SystemPropertiesPanel(connector);

        Grid.SetRow(titleBar, 0);
        Grid.SetRow(properties, 1);
        layout.Children.Add(titleBar);
        layout.Children.Add(properties);

        return new Border
        {
            Background = new SolidColorBrush(ExplorerBackground),
            BorderBrush = new SolidColorBrush(BorderGray),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Margin = new Thickness(0, 0, 3, 0),
            Child = layout
        };
    }

    private void SelectPanel(int index, bool persist = true)
    {
        var selectedIndex = Math.Clamp(index, 0, _panelContents.Count - 1);
        _selectedPanelIndex = selectedIndex;
        _workspaceContent.Content = _panelContents[selectedIndex];
        if (persist)
        {
            PersistSelection();
        }
    }

    private void PersistSelection()
    {
        _settings.RightTabIndex = Math.Max(0, _selectedPanelIndex);
        _settings.Save();
    }

    private int IndexOf(WorkspacePanelId id)
    {
        for (var index = 0; index < _panels.Count; index++)
        {
            if (_panels[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private AnalysisPanel? AnalysisPanel()
    {
        var index = IndexOf(WorkspacePanelId.Analysis);
        return index >= 0 ? _panelContents[index] as AnalysisPanel : null;
    }
}
