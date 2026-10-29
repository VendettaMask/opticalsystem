using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.Modules;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class AutonomousTopologyWindow
{
    private sealed record StructureSource(DesignTopology Topology, TopologyConstraints Constraints, string Name, string? Status);
    private readonly TopologyStructurePlot _structurePlot = Named(new TopologyStructurePlot(), "AutonomousStructurePlot");
    private readonly TextBlock _structureTitle = Named(Text("镜头结构"), "AutonomousStructureTitle");
    private readonly TextBlock _structureHint = Named(Text("打开结构页或点击刷新结构，显示当前处方。"), "AutonomousStructureHint");
    private Button? _structureReset;
    private readonly WrapPanel _structureLegend = new() { Orientation = Orientation.Horizontal };
    private readonly List<Button> _structureNavigation = [];
    private Button? _structureRefresh;
    private StructureSource? _structureSource;
    private string? _structureKey, _structureFailure;
    private CancellationTokenSource? _structureCancellation;
    private int _structureGeneration;
    private bool _structureLoading;
    private bool _structureSelected;
    private long _structureUsedRays;
    private readonly SemaphoreSlim _structureWorker = new(1, 1);
    internal Task StructurePendingLoad { get; private set; } = Task.CompletedTask;

    private Control BuildStructureView()
    {
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = 6, Margin = new Thickness(8), MinHeight = 380 };
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        _structureRefresh = Command("AutonomousStructureRefresh", "刷新结构", () => StructurePendingLoad = RequestStructureDisplayAsync(true));
        Command("AutonomousStructureFit", "适合窗口", _structurePlot.Fit, navigation: true);
        _structureReset = Command("AutonomousStructureReset", "100%", _structurePlot.Fit, navigation: true);
        Command("AutonomousStructureMinus", "−", () => _structurePlot.ZoomBy(.8), navigation: true);
        Command("AutonomousStructurePlus", "+", () => _structurePlot.ZoomBy(1.25), navigation: true);
        root.Children.Add(bar); Grid.SetRow(_structureTitle, 1); root.Children.Add(_structureTitle);
        var legend = new ScrollViewer { Content = _structureLegend, MaxHeight = 64, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(legend, 2); root.Children.Add(legend);
        Grid.SetRow(_structurePlot, 3); root.Children.Add(_structurePlot);
        Grid.SetRow(_structureHint, 4); root.Children.Add(_structureHint);
        _structurePlot.NavigationChanged += () => { if (_structureReset is { } reset) reset.Content = $"{_structurePlot.Zoom:P0}"; };
        return root;

        Button Command(string id, string label, Action action, bool navigation = false)
        {
            var button = Named(new Button { Content = label, Margin = new Thickness(0, 0, 6, 4) }, id);
            AutomationProperties.SetName(button, UiLocalization.Text(id.EndsWith("Plus", StringComparison.Ordinal) ? "放大"
                : id.EndsWith("Minus", StringComparison.Ordinal) ? "缩小" : label));
            button.Click += (_, _) => action(); bar.Children.Add(button);
            if (navigation) _structureNavigation.Add(button);
            return button;
        }
    }

    private void SetStructureSource(DesignTopology topology, TopologyConstraints constraints, string name, string? status = null, bool force = false)
    {
        var key = ContentFingerprint.Compute(new { topology, constraints });
        _structureSource = new(topology, constraints, name, status);
        if (force || key != _structureKey)
        {
            CancelStructureLoad(); _structureKey = key; _structurePlot.SetScene(null); _structureFailure = null; _structureUsedRays = 0;
        }
        RefreshStructureText();
    }
    private void CancelStructureLoad()
    {
        _structureGeneration++; _structureCancellation?.Cancel(); _structureCancellation = null; _structureLoading = false;
    }
    private Task RequestStructureDisplayAsync(bool refresh = false)
    {
        if (_structureLoading) return StructurePendingLoad;
        if (IsBusy) return Task.CompletedTask;
        try
        {
            if (_session.Checkpoint is null)
            {
                var plan = ReadPlan(); SetStructureSource(plan.Seeds[0], plan.Constraints, "已载入起点");
            }
            if (refresh) { CancelStructureLoad(); _structurePlot.SetScene(null); }
            return DisplayStructureAsync();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
        { _structurePlot.SetScene(null); _structureFailure = error.Message; RefreshStructureText(); return Task.CompletedTask; }
    }
    private async Task DisplayStructureAsync()
    {
        if (IsBusy || _structureLoading || _structureSource is not { } source) return;
        if (_structurePlot.Data is not null) { RefreshStructureText(); return; }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var generation = ++_structureGeneration; _structureCancellation = cancellation;
        _structureLoading = true; _structureFailure = null; _structureUsedRays = 0; long usedRays = 0; RefreshStructureText();
        try
        {
            await _structureWorker.WaitAsync(cancellation.Token);
            TopologyStructureScene result;
            try
            {
                result = await Task.Run(() => InitialStructurePresentation.BuildTopologyScene(source.Topology, source.Constraints, cancellation.Token,
                count => usedRays = count), cancellation.Token);
            }
            finally { _structureWorker.Release(); }
            if (!_closed && generation == _structureGeneration) _structurePlot.SetScene(result);
        }
        catch (OperationCanceledException)
        { if (!_closed && generation == _structureGeneration) _structureFailure = UiLocalization.Text("结构显示已停止"); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or ArithmeticException or KeyNotFoundException or NotSupportedException or AggregateException or IOException)
        { if (!_closed && generation == _structureGeneration) _structureFailure = error.Message; }
        finally
        {
            if (!_closed && generation == _structureGeneration)
            { _structureCancellation = null; _structureLoading = false; _structureUsedRays = usedRays; RefreshStructureText(); }
        }
    }
    private void RefreshStructureText()
    {
        if (_structureSource is not { } source) return;
        var caption = UiLocalization.Text(source.Name) + (source.Status is null ? "" : " · " + UiLocalization.Text(source.Status));
        _structureTitle.Text = caption + $" · {source.Topology.Elements.Length} " + UiLocalization.Text("片")
            + (_structurePlot.Data is { } data ? $" · f {data.FocalMillimeters:G7} mm · F/{data.FNumber:G6}" : "");
        _structureLegend.Children.Clear();
        for (var i = 0; i < source.Topology.Fields.Length; i++)
        {
            var field = source.Topology.Fields[i];
            _structureLegend.Children.Add(new TextBlock
            {
                Text = (field.XAngleDegrees == 0 ? $"{field.YAngleDegrees:G5}°" : $"X {field.XAngleDegrees:G5}° / Y {field.YAngleDegrees:G5}°"),
                Foreground = TopologyStructurePlot.FieldBrushes[i % TopologyStructurePlot.FieldBrushes.Length],
                Margin = new Thickness(0, 0, 16, 4)
            });
        }
        _structureHint.Text = _structureLoading ? UiLocalization.Text("正在生成结构显示")
            : _structureFailure is { } failure ? UiLocalization.Text("结构显示未完成") + " · " + failure
            : _structurePlot.Data is not null ? UiLocalization.Text("拖动平移，滚轮缩放；虚线为失光光线")
            : IsBusy ? UiLocalization.Text("运行期间保留已有结构，停止后可刷新") : UiLocalization.Text("打开结构页或点击刷新结构，显示当前处方。");
        if (_structurePlot.Data is not null || _structureFailure is not null)
            _structureHint.Text += "\n" + UiLocalization.Text("显示追迹独立于搜索预算") + $" · {_structureUsedRays}/50000";
        if (_structureRefresh is { } refresh)
        {
            refresh.IsEnabled = !IsBusy && !_structureLoading;
            ToolTip.SetTip(refresh, refresh.IsEnabled ? UiLocalization.Text("显示已载入起点或所选候选，不改变搜索记录")
                : UiLocalization.Text("运行期间保留已有结构，停止后可刷新"));
        }
        foreach (var button in _structureNavigation)
        { button.IsEnabled = _structurePlot.Data is not null; ToolTip.SetTip(button, button.IsEnabled ? null : UiLocalization.Text("请先显示结构")); }
    }
}
