using Avalonia.Automation;
using Avalonia.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private readonly string _layoutPath = CoatingDockLayoutStore.DefaultPath;
    private Task SaveWorkspaceLayoutAsync()
    {
        CoatingDockLayoutStore.Save(_layoutPath, _workspace.CaptureLayout());
        _status.Text = "镀膜布局已保存。"; return Task.CompletedTask;
    }
    private Task RestoreWorkspaceLayoutAsync()
    {
        _workspace.RestoreLayout(CoatingDockLayoutStore.Load(_layoutPath));
        _status.Text = "镀膜布局已恢复。"; return Task.CompletedTask;
    }
    private DropDownButton BuildWindowMenu()
    {
        var menu = new MenuFlyout();
        void Item(string title, string automation, Func<Task> action)
        {
            var item = new MenuItem { Header = UiLocalization.Text(title), Tag = title };
            AutomationProperties.SetName(item, automation);
            item.Click += (_, _) => TrackOperation(action);
            menu.Items.Add(item);
        }
        Task Layout(CoatingPaneLayout layout, bool reset = false) { _workspace.ApplyLayout(layout, reset); return Task.CompletedTask; }
        Item("膜系编辑器", "显示膜系编辑器", () => { _workspace.Open(CoatingDockWorkspace.EditorId); return Task.CompletedTask; });
        Item("光学评价", "显示光学评价", () => { _workspace.Open(CoatingDockWorkspace.SpectrumId); return Task.CompletedTask; });
        Item("堆栈几何", "显示堆栈几何", ShowStackGeometryAsync);
        Item("镀膜设计结果", "显示镀膜设计结果", ShowDesignResultsAsync);
        menu.Items.Add(new Separator());
        Item("浮动当前面板", "浮动当前镀膜面板", () => { _workspace.FloatActive(); return Task.CompletedTask; });
        Item("停靠全部面板", "停靠全部镀膜面板", () => Layout(CoatingPaneLayout.Columns));
        menu.Items.Add(new Separator());
        Item("左右布局", "镀膜左右布局", () => Layout(CoatingPaneLayout.Columns));
        Item("上下布局", "镀膜上下布局", () => Layout(CoatingPaneLayout.Rows));
        Item("标签布局", "镀膜标签布局", () => Layout(CoatingPaneLayout.Tabs));
        menu.Items.Add(new Separator());
        Item("保存布局", "保存镀膜布局", SaveWorkspaceLayoutAsync);
        Item("恢复布局", "恢复镀膜布局", RestoreWorkspaceLayoutAsync);
        Item("重置布局", "重置镀膜布局", () => Layout(CoatingPaneLayout.Columns, reset: true));
        menu.Opening += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) item.Header = UiLocalization.Text((string)item.Tag!);
        };
        var button = new DropDownButton { Content = ToolContent("panels-top-left", "窗口"), Flyout = menu };
        button.Classes.Add("coating-tool"); AutomationProperties.SetName(button, "镀膜窗口"); ToolTip.SetTip(button, "窗口");
        return button;
    }
}
