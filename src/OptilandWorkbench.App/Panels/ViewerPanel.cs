using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public enum ViewerPresentationMode
{
    OpticalLayout,
    SolidModel
}

public sealed class ViewerPanel : UserControl, IDisposable
{
    private static readonly IBrush ToolbarBackground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
    private static readonly IBrush ToolbarBorder = new SolidColorBrush(Color.FromRgb(209, 209, 214));

    private readonly IVisualizationService _visualization;
    private readonly IWorkspaceEventStream _events;
    private readonly SurfaceSelectionService _surfaceSelection;
    private readonly SceneDimension _dimension;
    private readonly ViewerPresentationMode _presentationMode;
    private readonly OpticSceneControl _scene;
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _summaryBar;
    private readonly ComboBox _startSurfacePicker = SettingPicker();
    private readonly ComboBox _endSurfacePicker = SettingPicker();
    private readonly ComboBox _wavelengthPicker = SettingPicker();
    private readonly ComboBox _fieldPicker = SettingPicker();
    private readonly ComboBox _colorModePicker = SettingPicker();
    private readonly ComboBox _scalePicker = SettingPicker();
    private readonly ComboBox _lineWidthPicker = SettingPicker();
    private readonly NumericUpDown _rayCount = SettingNumber(1, 101, 1, 7);
    private readonly NumericUpDown _yStretch = SettingNumber(0.1m, 10, 0.1m, 1);
    private readonly NumericUpDown _upperPupil = SettingNumber(-1, 1, 0.1m, 1);
    private readonly NumericUpDown _lowerPupil = SettingNumber(-1, 1, 0.1m, -1);
    private readonly CheckBox _suppressFrame = new() { Content = "隐藏底部框架" };
    private readonly CheckBox _rayArrows = new() { Content = "光线箭头" };
    private readonly CheckBox _deleteVignetted = new() { Content = "删除渐晕光线" };
    private readonly CheckBox _marginalAndChiefOnly = new() { Content = "仅边缘和主光线" };
    private readonly CheckBox _autoApply = new() { Content = "自动应用", IsChecked = true };
    private CancellationTokenSource? _refreshCancellation;
    private bool _locked;
    private bool _disposed;
    private bool _updatingSettings;

    public ViewerPanel(
        IVisualizationService visualization,
        IWorkspaceEventStream events,
        SurfaceSelectionService surfaceSelection,
        SceneDimension dimension,
        ViewerPresentationMode presentationMode = ViewerPresentationMode.OpticalLayout)
    {
        _visualization = visualization;
        _events = events;
        _surfaceSelection = surfaceSelection;
        _dimension = dimension;
        _presentationMode = presentationMode;
        _scene = new OpticSceneControl
        {
            MinHeight = 320,
            ViewMode = dimension == SceneDimension.TwoDimensional
                ? OpticSceneViewMode.TwoDimensional
                : OpticSceneViewMode.ThreeDimensional,
            VisualStyle = presentationMode == ViewerPresentationMode.SolidModel
                ? OpticSceneVisualStyle.SolidModel
                : OpticSceneVisualStyle.OpticalLayout,
            HighlightedSurfaceNumber = surfaceSelection.SelectedSurfaceNumber
        };

        RefreshSelectorOptions(preserveSelection: false);
        _colorModePicker.ItemsSource = new[] { "视场 #", "波长 #" };
        _colorModePicker.SelectedIndex = 0;
        _scalePicker.ItemsSource = new[] { "启用", "关闭" };
        _scalePicker.SelectedIndex = 0;
        _lineWidthPicker.ItemsSource = new[] { "细", "标准", "粗" };
        _lineWidthPicker.SelectedIndex = 1;

        var root = new DockPanel();
        _summaryBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 5),
            Child = _summary
        };
        DockPanel.SetDock(_summaryBar, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(_summaryBar);
        root.Children.Add(dimension == SceneDimension.TwoDimensional
            ? Build2DWorkspace()
            : Build3DWorkspace());
        Content = root;

        ApplyDisplaySettings();
        _events.Changed += OnWorkspaceChanged;
        _surfaceSelection.Changed += OnSurfaceSelectionChanged;
        QueueRefresh(TimeSpan.Zero);
    }

    public bool IsLocked
    {
        get => _locked;
        set
        {
            if (_locked == value)
            {
                return;
            }

            _locked = value;
            if (!value)
            {
                QueueRefresh(TimeSpan.Zero);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _events.Changed -= OnWorkspaceChanged;
        _surfaceSelection.Changed -= OnSurfaceSelectionChanged;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
    }

    private Control Build2DWorkspace()
    {
        var showRays = new CheckBox
        {
            Content = "显示光线",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        showRays.IsCheckedChanged += (_, _) => _scene.ShowRays = showRays.IsChecked == true;

        var reset = CompactButton("rotate-ccw", "恢复二维视图的缩放与平移");
        reset.Click += (_, _) => _scene.ResetView();
        return SceneWithOverlay(
            _scene,
            Toolbar(new Control[] { showRays, reset }, HorizontalAlignment.Right),
            BuildSettingsOverlay());
    }

    private Control Build3DWorkspace()
    {
        var showRays = new CheckBox
        {
            Content = "光线",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        showRays.IsCheckedChanged += (_, _) => _scene.ShowRays = showRays.IsChecked == true;

        var cutaway = new CheckBox
        {
            Content = "切面",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(cutaway, "移除镜片近侧半部并显示内部剖面");
        cutaway.IsCheckedChanged += (_, _) => _scene.CutawayEnabled = cutaway.IsChecked == true;

        var renderMode = new ComboBox
        {
            ItemsSource = _presentationMode == ViewerPresentationMode.SolidModel
                ? new[] { "实体", "框架" }
                : new[] { "透明", "框架" },
            SelectedIndex = 0,
            MinWidth = 88,
            VerticalAlignment = VerticalAlignment.Center
        };
        renderMode.SelectionChanged += (_, _) =>
        {
            _scene.RenderMode = renderMode.SelectedIndex == 1
                ? OpticSceneRenderMode.Wireframe
                : OpticSceneRenderMode.Solid;
        };

        var topToolbar = Toolbar(
            new Control[]
            {
                new TextBlock
                {
                    Text = _presentationMode == ViewerPresentationMode.SolidModel ? "实体模型" : "三维布局",
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                cutaway,
                showRays,
                renderMode
            },
            HorizontalAlignment.Right);

        var fitView = CompactButton("maximize-2", "适配窗口");
        fitView.Click += (_, _) => _scene.FitView();
        var presetToolbar = Toolbar(
            new Control[]
            {
                fitView,
                PresetButton(ViewCubeFace.Front, "前视图", OpticSceneViewPreset.Front),
                PresetButton(ViewCubeFace.Back, "后视图", OpticSceneViewPreset.Back),
                PresetButton(ViewCubeFace.Left, "左视图", OpticSceneViewPreset.Left),
                PresetButton(ViewCubeFace.Right, "右视图", OpticSceneViewPreset.Right),
                PresetButton(ViewCubeFace.Top, "俯视图", OpticSceneViewPreset.Top),
                PresetButton(ViewCubeFace.Bottom, "仰视图", OpticSceneViewPreset.Bottom),
                PresetButton(ViewCubeFace.Isometric, "等轴测视图", OpticSceneViewPreset.Isometric)
            },
            HorizontalAlignment.Center,
            VerticalAlignment.Bottom);
        return SceneWithOverlay(_scene, topToolbar, presetToolbar, BuildSettingsOverlay());
    }

    private Control BuildSettingsOverlay()
    {
        var settingsContent = BuildSettingsContent();
        settingsContent.IsVisible = false;

        var toggle = new Button
        {
            Content = new LocalIconLabel("settings", "设置"),
            MinWidth = 0,
            Height = 32,
            Padding = new Thickness(8, 3)
        };
        toggle.Click += (_, _) => settingsContent.IsVisible = !settingsContent.IsVisible;

        var synchronize = CompactButton("refresh-cw", "同步并重新生成视图");
        synchronize.Click += (_, _) =>
        {
            ApplyDisplaySettings();
            QueueRefresh(TimeSpan.Zero);
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            Children = { toggle, synchronize }
        };
        var container = new StackPanel { Children = { header, settingsContent } };
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Background = ToolbarBackground,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 5 16 0 #20000000"),
            Child = container
        };
    }

    private Control BuildSettingsContent()
    {
        var settings = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,150,Auto,150"),
            Margin = new Thickness(12, 8, 12, 4)
        };
        AddSettingRow(settings, 0, "起始面", _startSurfacePicker, "波长", _wavelengthPicker);
        AddSettingRow(settings, 1, "终止面", _endSurfacePicker, "视场", _fieldPicker);
        AddSettingRow(settings, 2, "光线数", _rayCount, "颜色显示", _colorModePicker);
        AddSettingRow(settings, 3, "比例尺", _scalePicker, "Y 拉伸", _yStretch);
        AddSettingRow(settings, 4, "上光瞳", _upperPupil, "下光瞳", _lowerPupil);
        AddSettingRow(settings, 5, "线宽", _lineWidthPicker, string.Empty, new Border());

        var checks = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(12, 2, 12, 8)
        };
        AddCheck(checks, _suppressFrame, 0, 0);
        AddCheck(checks, _deleteVignetted, 0, 1);
        AddCheck(checks, _rayArrows, 1, 0);
        AddCheck(checks, _marginalAndChiefOnly, 1, 1);

        var apply = new Button { Content = "应用", MinWidth = 72 };
        apply.Click += (_, _) =>
        {
            ApplyDisplaySettings();
            QueueRefresh(TimeSpan.Zero);
        };
        var reset = new Button { Content = "重置", MinWidth = 72 };
        reset.Click += (_, _) => ResetSettings();
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 8, 12, 10),
            Children = { _autoApply, apply, reset }
        };

        WatchSettingsChanges();
        return new StackPanel
        {
            Children =
            {
                settings,
                new Border
                {
                    Height = 1,
                    Margin = new Thickness(12, 4),
                    Background = new SolidColorBrush(Color.FromRgb(210, 218, 228))
                },
                checks,
                footer
            }
        };
    }

    private void WatchSettingsChanges()
    {
        foreach (var picker in new[]
                 {
                     _startSurfacePicker,
                     _endSurfacePicker,
                     _wavelengthPicker,
                     _fieldPicker,
                     _colorModePicker,
                     _scalePicker,
                     _lineWidthPicker
                 })
        {
            picker.SelectionChanged += (_, _) => OnSettingChanged();
        }

        foreach (var number in new[] { _rayCount, _yStretch, _upperPupil, _lowerPupil })
        {
            number.ValueChanged += (_, _) => OnSettingChanged();
        }

        foreach (var checkBox in new[]
                 {
                     _suppressFrame,
                     _rayArrows,
                     _deleteVignetted,
                     _marginalAndChiefOnly
                 })
        {
            checkBox.IsCheckedChanged += (_, _) => OnSettingChanged();
        }
    }

    private void OnSettingChanged()
    {
        if (_updatingSettings)
        {
            return;
        }

        ApplyDisplaySettings();
        if (_autoApply.IsChecked == true)
        {
            QueueRefresh(TimeSpan.FromMilliseconds(120));
        }
    }

    private void ApplyDisplaySettings()
    {
        _scene.RayColorMode = _colorModePicker.SelectedIndex == 1
            ? OpticSceneRayColorMode.Wavelength
            : OpticSceneRayColorMode.Field;
        _scene.VerticalStretch = (double)(_yStretch.Value ?? 1);
        _scene.ShowRayArrows = _rayArrows.IsChecked == true;
        var lineWidth = _lineWidthPicker.SelectedIndex switch
        {
            0 => 0.85,
            2 => 1.8,
            _ => 1.25
        };
        _scene.RayLineWidth = _presentationMode == ViewerPresentationMode.SolidModel
            ? lineWidth * 3.6
            : lineWidth;
        _summaryBar.IsVisible = _suppressFrame.IsChecked != true;
        _scene.ShowScaleBar = _scalePicker.SelectedIndex != 1 && _suppressFrame.IsChecked != true;
    }

    private VisualizationRequestDto CreateRequest()
    {
        var firstSurface = SelectedValue(_startSurfacePicker);
        var lastSurface = SelectedValue(_endSurfacePicker);
        if (firstSurface.HasValue && lastSurface.HasValue && firstSurface > lastSurface)
        {
            (firstSurface, lastSurface) = (lastSurface, firstSurface);
        }

        var wavelengthIndex = SelectedValue(_wavelengthPicker);
        return new VisualizationRequestDto(
            _dimension,
            firstSurface,
            lastSurface,
            SelectedValue(_fieldPicker),
            wavelengthIndex,
            IncludeAllWavelengths: !wavelengthIndex.HasValue,
            RayCount: (int)(_rayCount.Value ?? 7),
            LowerPupil: (double)(_lowerPupil.Value ?? -1),
            UpperPupil: (double)(_upperPupil.Value ?? 1),
            DeleteVignetted: _deleteVignetted.IsChecked == true,
            MarginalAndChiefOnly: _marginalAndChiefOnly.IsChecked == true);
    }

    private void RefreshSelectorOptions(bool preserveSelection)
    {
        var previousStart = preserveSelection ? SelectedValue(_startSurfacePicker) : null;
        var previousEnd = preserveSelection ? SelectedValue(_endSurfacePicker) : null;
        var previousField = preserveSelection ? SelectedValue(_fieldPicker) : null;
        var previousWavelength = preserveSelection ? SelectedValue(_wavelengthPicker) : null;
        var options = _visualization.GetVisualizationOptions();

        var wasUpdatingSettings = _updatingSettings;
        _updatingSettings = true;
        try
        {
            var surfaces = options.SurfaceNumbers
                .Select(number => new SelectorItem(number, number.ToString()))
                .ToArray();
            _startSurfacePicker.ItemsSource = surfaces;
            _endSurfacePicker.ItemsSource = surfaces;
            SelectValue(
                _startSurfacePicker,
                previousStart ?? surfaces.FirstOrDefault(item => item.Index > 0)?.Index ?? surfaces.FirstOrDefault()?.Index);
            SelectValue(_endSurfacePicker, previousEnd ?? surfaces.LastOrDefault()?.Index);

            var fields = new[] { new SelectorItem(null, "所有") }
                .Concat(options.Fields.Select(item => new SelectorItem(item.Index, item.Label)))
                .ToArray();
            _fieldPicker.ItemsSource = fields;
            SelectValue(_fieldPicker, preserveSelection ? previousField : null);

            var wavelengths = new[] { new SelectorItem(null, "所有") }
                .Concat(options.Wavelengths.Select(item => new SelectorItem(item.Index, item.Label)))
                .ToArray();
            _wavelengthPicker.ItemsSource = wavelengths;
            SelectValue(_wavelengthPicker, preserveSelection ? previousWavelength : null);
        }
        finally
        {
            _updatingSettings = wasUpdatingSettings;
        }
    }

    private void ResetSettings()
    {
        _updatingSettings = true;
        try
        {
            RefreshSelectorOptions(preserveSelection: false);
            _rayCount.Value = 7;
            _yStretch.Value = 1;
            _upperPupil.Value = 1;
            _lowerPupil.Value = -1;
            _colorModePicker.SelectedIndex = 0;
            _scalePicker.SelectedIndex = 0;
            _lineWidthPicker.SelectedIndex = 1;
            _suppressFrame.IsChecked = false;
            _rayArrows.IsChecked = false;
            _deleteVignetted.IsChecked = false;
            _marginalAndChiefOnly.IsChecked = false;
        }
        finally
        {
            _updatingSettings = false;
        }

        ApplyDisplaySettings();
        QueueRefresh(TimeSpan.Zero);
    }

    private Button PresetButton(ViewCubeFace face, string tooltip, OpticSceneViewPreset preset)
    {
        var button = new Button
        {
            Content = new ViewCubeIcon(face)
            {
                Width = 26,
                Height = 24
            },
            Width = 40,
            MinWidth = 0,
            Height = 32,
            Padding = new Thickness(0)
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => _scene.SetViewPreset(preset);
        return button;
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (!_locked)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshSelectorOptions(preserveSelection: true);
                QueueRefresh(TimeSpan.FromMilliseconds(120));
            });
        }
    }

    private void OnSurfaceSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() => _scene.HighlightedSurfaceNumber = args.SurfaceNumber);
    }

    private void QueueRefresh(TimeSpan delay)
    {
        if (_disposed || _locked)
        {
            return;
        }

        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        _ = RefreshAsync(delay, _refreshCancellation.Token);
    }

    private async Task RefreshAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var scene = await _visualization.BuildSceneAsync(CreateRequest(), cancellationToken);
            if (cancellationToken.IsCancellationRequested || scene.SourceRevision != _events.Revision)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _scene.Scene = scene;
                _scene.InvalidateVisual();
                _summary.Text = $"有效焦距 {NumericDisplayFormatter.Format(scene.Summary.EffectiveFocalLength)} mm    " +
                    $"F 数 {NumericDisplayFormatter.Format(scene.Summary.FNumber)}    " +
                    $"系统总长 {NumericDisplayFormatter.Format(scene.Summary.TotalTrack)} mm";
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"Viewer refresh failed: {exception}");
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed)
                {
                    _summary.Text = $"视图更新失败：{exception.Message}";
                }
            });
        }
    }

    private static Control SceneWithOverlay(Control scene, params Control[] overlays)
    {
        var grid = new Grid();
        grid.Children.Add(scene);
        foreach (var overlay in overlays)
        {
            grid.Children.Add(overlay);
        }

        return grid;
    }

    private static void AddSettingRow(
        Grid grid,
        int row,
        string firstLabel,
        Control firstControl,
        string secondLabel,
        Control secondControl)
    {
        while (grid.RowDefinitions.Count <= row)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        AddSettingCell(grid, new TextBlock
        {
            Text = firstLabel,
            Margin = new Thickness(0, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Center
        }, row, 0);
        AddSettingCell(grid, firstControl, row, 1);
        if (!string.IsNullOrEmpty(secondLabel))
        {
            AddSettingCell(grid, new TextBlock
            {
                Text = secondLabel,
                Margin = new Thickness(22, 5, 8, 5),
                VerticalAlignment = VerticalAlignment.Center
            }, row, 2);
            AddSettingCell(grid, secondControl, row, 3);
        }
    }

    private static void AddSettingCell(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        control.Margin = control.Margin + new Thickness(0, 3);
        grid.Children.Add(control);
    }

    private static void AddCheck(Grid grid, CheckBox checkBox, int row, int column)
    {
        Grid.SetRow(checkBox, row);
        Grid.SetColumn(checkBox, column);
        checkBox.Margin = new Thickness(0, 4);
        grid.Children.Add(checkBox);
    }

    private static ComboBox SettingPicker() => new()
    {
        MinWidth = 140,
        Height = 30,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static NumericUpDown SettingNumber(
        decimal minimum,
        decimal maximum,
        decimal increment,
        decimal value) => new()
    {
        MinWidth = 140,
        Height = 30,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Value = value,
        ShowButtonSpinner = false
    };

    private static int? SelectedValue(ComboBox picker) =>
        (picker.SelectedItem as SelectorItem)?.Index;

    private static void SelectValue(ComboBox picker, int? value)
    {
        picker.SelectedItem = (picker.ItemsSource as IEnumerable<SelectorItem>)?
            .FirstOrDefault(item => item.Index == value);
    }

    private static Border Toolbar(
        IEnumerable<Control> controls,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return new Border
        {
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Margin = new Thickness(10),
            Padding = new Thickness(7, 5),
            CornerRadius = new CornerRadius(7),
            Background = ToolbarBackground,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 4 12 0 #1A000000"),
            Child = panel
        };
    }

    private static Button CompactButton(string iconName, string tooltip)
    {
        var button = new Button
        {
            Content = new LocalIcon
            {
                IconName = iconName,
                Width = 18,
                Height = 18,
                Stroke = new SolidColorBrush(Color.FromRgb(48, 48, 52))
            },
            Width = 36,
            MinWidth = 0,
            Height = 30,
            Padding = new Thickness(0)
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private sealed record SelectorItem(int? Index, string Label)
    {
        public override string ToString() => Label;
    }
}
