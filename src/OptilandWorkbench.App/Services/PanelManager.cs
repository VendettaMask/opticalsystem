using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
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

public enum WorkspacePane
{
    Left,
    Right
}

public sealed record WorkspacePanelDescriptor(
    WorkspacePanelId Id,
    string Title,
    WorkspacePane Pane,
    Func<Control> CreateContent);

public sealed class PanelManager
{
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<WorkspacePanelDescriptor> _leftPanels;
    private readonly IReadOnlyList<WorkspacePanelDescriptor> _rightPanels;

    public PanelManager(OptilandConnector connector, AppSettings settings)
    {
        _settings = settings;
        var panels = new WorkspacePanelDescriptor[]
        {
            new(WorkspacePanelId.LensEditor, "镜头编辑器", WorkspacePane.Left, () => new LensEditorPanel(connector)),
            new(WorkspacePanelId.SystemProperties, "系统属性", WorkspacePane.Left, () => new SystemPropertiesPanel(connector)),
            new(WorkspacePanelId.Viewer, "系统视图", WorkspacePane.Right, () => new ViewerPanel(connector)),
            new(WorkspacePanelId.Analysis, "分析", WorkspacePane.Right, () => new AnalysisPanel(connector, settings)),
            new(WorkspacePanelId.Optimization, "优化", WorkspacePane.Right, () => new OptimizationPanel(connector)),
            new(WorkspacePanelId.Tolerancing, "公差", WorkspacePane.Right, () => new TolerancingPanel(connector)),
            new(WorkspacePanelId.MultiConfiguration, "多配置", WorkspacePane.Right, () => new MultiConfigurationPanel(connector))
        };

        _leftPanels = panels.Where(panel => panel.Pane == WorkspacePane.Left).ToArray();
        _rightPanels = panels.Where(panel => panel.Pane == WorkspacePane.Right).ToArray();
        WorkspaceGrid = BuildWorkspace();
    }

    public Grid WorkspaceGrid { get; }

    public TabControl LeftTabs { get; private set; } = null!;

    public TabControl RightTabs { get; private set; } = null!;

    public void Show(WorkspacePanelId id)
    {
        var leftIndex = IndexOf(_leftPanels, id);
        if (leftIndex >= 0)
        {
            LeftTabs.SelectedIndex = leftIndex;
            return;
        }

        var rightIndex = IndexOf(_rightPanels, id);
        if (rightIndex >= 0)
        {
            RightTabs.SelectedIndex = rightIndex;
        }
    }

    public WorkspaceLayoutState CaptureLayout()
    {
        var width = WorkspaceGrid.ColumnDefinitions.Count == 0
            ? 520
            : Math.Clamp(WorkspaceGrid.ColumnDefinitions[0].ActualWidth, 360, 900);
        return new WorkspaceLayoutState(
            width,
            Math.Max(0, LeftTabs.SelectedIndex),
            Math.Max(0, RightTabs.SelectedIndex));
    }

    public void ApplyLayout(WorkspaceLayoutState layout)
    {
        WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(layout.LeftPaneWidth, 360, 900));
        LeftTabs.SelectedIndex = Math.Clamp(layout.LeftTabIndex, 0, _leftPanels.Count - 1);
        RightTabs.SelectedIndex = Math.Clamp(layout.RightTabIndex, 0, _rightPanels.Count - 1);
    }

    public void ResetLayout()
    {
        ApplyLayout(new WorkspaceLayoutState());
    }

    private Grid BuildWorkspace()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Math.Clamp(_settings.LeftPaneWidth, 360, 900)},6,*")
        };

        LeftTabs = BuildTabs(_leftPanels, _settings.LeftTabIndex);
        RightTabs = BuildTabs(_rightPanels, _settings.RightTabIndex);
        LeftTabs.SelectionChanged += (_, _) => PersistSelection();
        RightTabs.SelectionChanged += (_, _) => PersistSelection();

        var splitter = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(210, 218, 228))
        };

        Grid.SetColumn(LeftTabs, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(RightTabs, 2);
        grid.Children.Add(LeftTabs);
        grid.Children.Add(splitter);
        grid.Children.Add(RightTabs);
        return grid;
    }

    private static TabControl BuildTabs(IReadOnlyList<WorkspacePanelDescriptor> descriptors, int selectedIndex)
    {
        return new TabControl
        {
            SelectedIndex = Math.Clamp(selectedIndex, 0, descriptors.Count - 1),
            ItemsSource = descriptors
                .Select(descriptor => new TabItem
                {
                    Header = descriptor.Title,
                    Content = descriptor.CreateContent()
                })
                .ToArray()
        };
    }

    private void PersistSelection()
    {
        _settings.LeftTabIndex = Math.Max(0, LeftTabs.SelectedIndex);
        _settings.RightTabIndex = Math.Max(0, RightTabs.SelectedIndex);
        _settings.Save();
    }

    private static int IndexOf(IReadOnlyList<WorkspacePanelDescriptor> descriptors, WorkspacePanelId id)
    {
        for (var index = 0; index < descriptors.Count; index++)
        {
            if (descriptors[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }
}
