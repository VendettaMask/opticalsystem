using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App.Panels;

public enum ViewerPresentationMode
{
    OpticalLayout,
    SolidModel
}

public sealed class ViewerPanel : UserControl, IDisposable
{
    private readonly IVisualizationService _visualization;
    private readonly IWorkspaceEventStream _events;
    private readonly IWorkbenchModeService? _modes;
    private readonly INonSequentialAnalysisService? _nonSequentialAnalysis;
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
    private readonly Button _prepareLayoutRays = new()
    {
        Content = "准备布局光线",
        MinWidth = 112,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly CheckBox _showStaleLayoutRays = new()
    {
        Content = "查看过期光线",
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _staleLayoutMessage = new()
    {
        FontWeight = FontWeight.SemiBold
    };
    private readonly Border _staleLayoutBanner;
    private CancellationTokenSource? _refreshCancellation;
    private bool _locked;
    private bool _disposed;
    private bool _updatingSettings;
    private int _preparingLayoutSession;

    public ViewerPanel(
        IVisualizationService visualization,
        IWorkspaceEventStream events,
        SurfaceSelectionService surfaceSelection,
        SceneDimension dimension,
        ViewerPresentationMode presentationMode = ViewerPresentationMode.OpticalLayout,
        IWorkbenchModeService? modes = null,
        INonSequentialAnalysisService? nonSequentialAnalysis = null)
    {
        _visualization = visualization;
        _events = events;
        _modes = modes;
        _nonSequentialAnalysis = nonSequentialAnalysis;
        _surfaceSelection = surfaceSelection;
        _dimension = dimension;
        _presentationMode = presentationMode;
        _scene = new OpticSceneControl
        {
            MinHeight = 240,
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
        _deleteVignetted.IsChecked = true;
        _staleLayoutBanner = new Border
        {
            IsVisible = false,
            Padding = new Thickness(12, 7),
            Margin = new Thickness(12, 54, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _staleLayoutMessage
        };
        ThemeChrome.Apply(
            _staleLayoutBanner,
            ThemeChromeRole.ControlFrame,
            shadow: false,
            borderBrush: false);
        _staleLayoutBanner.BindThemeResource(
            Border.BackgroundProperty,
            ThemeResourceBindings.ErrorSurface);
        _staleLayoutBanner.BindThemeResource(
            Border.BorderBrushProperty,
            ThemeResourceBindings.TextError);
        _staleLayoutMessage.BindThemeResource(
            TextBlock.ForegroundProperty,
            ThemeResourceBindings.TextError);
        _prepareLayoutRays.IsVisible = IsNonSequential3D();
        _prepareLayoutRays.Click += async (_, _) => await PrepareLayoutRaysAsync();
        _showStaleLayoutRays.IsCheckedChanged += (_, _) => QueueRefresh(TimeSpan.Zero);

        var root = new DockPanel();
        _summaryBar = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 5),
            Child = _summary
        };
        _summaryBar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        _summaryBar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        DockPanel.SetDock(_summaryBar, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(_summaryBar);
        root.Children.Add(dimension == SceneDimension.TwoDimensional
            ? Build2DWorkspace()
            : Build3DWorkspace());
        Content = root;

        ApplyDisplaySettings();
        _events.Changed += OnWorkspaceChanged;
        if (_nonSequentialAnalysis is not null)
        {
            _nonSequentialAnalysis.LayoutSessionChanged += OnNonSequentialSessionChanged;
        }
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
        if (_nonSequentialAnalysis is not null)
        {
            _nonSequentialAnalysis.LayoutSessionChanged -= OnNonSequentialSessionChanged;
        }
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
        return ThemeChrome.WrapWithDecoration(
            SceneWithOverlay(
                _scene,
                Toolbar(new Control[] { showRays, reset }, HorizontalAlignment.Right),
                BuildSettingsOverlay()),
            ThemeChromeRole.Viewport);
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
                _prepareLayoutRays,
                _showStaleLayoutRays,
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
        return ThemeChrome.WrapWithDecoration(
            SceneWithOverlay(
                _scene,
                topToolbar,
                presetToolbar,
                BuildSettingsOverlay(),
                _staleLayoutBanner),
            ThemeChromeRole.Viewport);
    }

    private Control BuildSettingsOverlay()
    {
        var settingsContent = BuildSettingsContent();
        settingsContent.IsVisible = false;

        var toggle = SettingsPanelChrome.CreateToggleButton();
        toggle.Click += (_, _) => settingsContent.IsVisible = !settingsContent.IsVisible;

        var synchronize = CompactButton("refresh-cw", "同步并重新生成视图");
        synchronize.BindThemeResource(Button.BackgroundProperty, ThemeResourceBindings.SettingsSurface);
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
        var card = SettingsPanelChrome.CreateCard(
            container,
            new Thickness(10),
            HorizontalAlignment.Left);
        card.MaxWidth = 520;
        return card;
    }

    private Control BuildSettingsContent()
    {
        var settings = new ViewerSettingsForm(
        [
            Setting("起始面", _startSurfacePicker),
            Setting("波长", _wavelengthPicker),
            Setting("终止面", _endSurfacePicker),
            Setting("视场", _fieldPicker),
            Setting("光线数", _rayCount),
            Setting("颜色显示", _colorModePicker),
            Setting("比例尺", _scalePicker),
            Setting("上光瞳", _upperPupil),
            Setting("Y 拉伸", _yStretch),
            Setting("下光瞳", _lowerPupil),
            Setting("线宽", _lineWidthPicker)
        ])
        {
            Margin = new Thickness(12, 8, 12, 4)
        };

        var checks = new ResponsiveSettingsGrid(
            new Control[] { _suppressFrame, _deleteVignetted, _rayArrows, _marginalAndChiefOnly })
        {
            Margin = new Thickness(12, 2, 12, 8)
        };
        foreach (var check in new[] { _suppressFrame, _deleteVignetted, _rayArrows, _marginalAndChiefOnly })
        {
            check.Margin = new Thickness(0, 4);
        }

        var apply = new Button { Content = "应用", MinWidth = 72 };
        apply.Click += (_, _) =>
        {
            ApplyDisplaySettings();
            QueueRefresh(TimeSpan.Zero);
        };
        var reset = new Button { Content = "重置", MinWidth = 72 };
        reset.Click += (_, _) => ResetSettings();
        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 8, 12, 10),
            Children = { _autoApply, apply, reset }
        };
        _autoApply.Margin = new Thickness(0, 4, 8, 4);
        apply.Margin = new Thickness(0, 4, 8, 4);
        reset.Margin = new Thickness(0, 4, 8, 4);

        WatchSettingsChanges();
        return new StackPanel
        {
            Children =
            {
                settings,
                ThemeSeparator(),
                checks,
                footer
            }
        };
    }

    private static Border ThemeSeparator()
    {
        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(12, 4)
        };
        separator.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border);
        return separator;
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
            MarginalAndChiefOnly: _marginalAndChiefOnly.IsChecked == true,
            IncludeStaleNonSequentialRays: _showStaleLayoutRays.IsChecked == true);
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
            _deleteVignetted.IsChecked = true;
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
                if (args.Category == WorkspaceChangeCategory.NonSequential)
                {
                    _showStaleLayoutRays.IsChecked = false;
                }
                RefreshSelectorOptions(preserveSelection: true);
                QueueRefresh(TimeSpan.FromMilliseconds(120));
            });
        }
    }

    private void OnSurfaceSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() => _scene.HighlightedSurfaceNumber = args.SurfaceNumber);
    }

    private void OnNonSequentialSessionChanged(
        object? sender,
        NonSequentialTraceSessionDto? session)
    {
        if (!IsNonSequential3D() || Volatile.Read(ref _preparingLayoutSession) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => QueueRefresh(TimeSpan.Zero));
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

            if (IsNonSequential3D() && _nonSequentialAnalysis is not null)
            {
                await EnsureLayoutRaysAsync(cancellationToken);
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
                var nonSequential = scene.ThreeDimensional is { } threeDimensional
                    && threeDimensional.Surfaces.Any(surface =>
                        surface.RenderRole != SceneSurfaceRenderRole.OpticalSurface);
                _prepareLayoutRays.IsVisible = IsNonSequential3D();
                if (nonSequential)
                {
                    UpdateNonSequentialLayoutState(scene.NonSequentialLayoutResult);
                }
                else
                {
                    _staleLayoutBanner.IsVisible = false;
                    _showStaleLayoutRays.IsVisible = false;
                }

                _summary.Text = nonSequential
                    ? $"非序列对象 {scene.ThreeDimensional!.Surfaces.Count}    "
                        + $"显示光线 {scene.ThreeDimensional.Rays.Count}    "
                        + $"Z 范围 {NumericDisplayFormatter.Format(scene.ThreeDimensional.ZMin)} 至 "
                        + $"{NumericDisplayFormatter.Format(scene.ThreeDimensional.ZMax)} mm"
                        + LayoutStateSummary(scene.NonSequentialLayoutResult)
                    : $"有效焦距 {NumericDisplayFormatter.Format(scene.Summary.EffectiveFocalLength)} mm    "
                        + $"F 数 {NumericDisplayFormatter.Format(scene.Summary.FNumber)}    "
                        + $"系统总长 {NumericDisplayFormatter.Format(scene.Summary.TotalTrack)} mm";
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

    private async Task PrepareLayoutRaysAsync()
    {
        if (!IsNonSequential3D())
        {
            return;
        }

        _prepareLayoutRays.IsEnabled = false;
        _prepareLayoutRays.Content = "正在准备…";
        try
        {
            await EnsureLayoutRaysAsync(CancellationToken.None, forceRefresh: true);
            _showStaleLayoutRays.IsChecked = false;
            QueueRefresh(TimeSpan.Zero);
        }
        catch (Exception exception)
        {
            _summary.Text = $"布局光线准备失败：{exception.Message}";
        }
        finally
        {
            _prepareLayoutRays.IsEnabled = true;
        }
    }

    private async Task EnsureLayoutRaysAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        if (!IsNonSequential3D()
            || _nonSequentialAnalysis is null
            || Interlocked.CompareExchange(ref _preparingLayoutSession, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (forceRefresh)
            {
                await _nonSequentialAnalysis.RefreshLayoutSessionAsync(cancellationToken);
            }
            else
            {
                await _nonSequentialAnalysis.PrepareLayoutSessionAsync(cancellationToken);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _preparingLayoutSession, 0);
        }
    }

    private void UpdateNonSequentialLayoutState(NonSequentialLayoutResultDto? result)
    {
        var stale = result is { HasResult: true, IsStale: true };
        _staleLayoutBanner.IsVisible = stale;
        _showStaleLayoutRays.IsVisible = stale;
        _prepareLayoutRays.Content = result switch
        {
            { HasResult: false } => "准备布局光线",
            { IsStale: true } => "重新准备布局光线",
            _ => "更新布局光线"
        };
        if (stale)
        {
            _staleLayoutMessage.Text = result!.RaysLoaded
                ? "警告：正在把过期光线显示在新场景中，仅供对比。"
                : "布局结果已过期，旧光线已隐藏。请重新准备布局光线。";
        }
    }

    private static string LayoutStateSummary(NonSequentialLayoutResultDto? result) => result switch
    {
        null or { HasResult: false } => "    尚未准备布局光线",
        { IsStale: true, RaysLoaded: true } => "    警告：正在查看过期结果",
        { IsStale: true } => "    布局结果已过期并隐藏",
        _ => "    布局结果与当前场景一致"
    };

    private bool IsNonSequential3D() =>
        _dimension == SceneDimension.ThreeDimensional
        && _modes?.CurrentMode == OpticalWorkbenchMode.NonSequential
        && _nonSequentialAnalysis is not null;

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

    private static (Control Label, Control Editor) Setting(string label, Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.MinWidth = 96;
        AutomationProperties.SetName(control, label);
        control.Margin = new Thickness(0, 2);
        return (new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, control);
    }

    private sealed class ViewerSettingsForm : Grid
    {
        private readonly (Control Label, Control Editor)[] _fields;
        private bool _isNarrow;

        public ViewerSettingsForm(IEnumerable<(Control Label, Control Editor)> fields)
        {
            _fields = fields.ToArray();
            foreach (var (label, editor) in _fields)
            {
                Children.Add(label);
                Children.Add(editor);
            }
            ApplyLayout(false);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var narrow = double.IsFinite(availableSize.Width) && availableSize.Width < 400;
            if (narrow != _isNarrow)
            {
                ApplyLayout(narrow);
            }
            return base.MeasureOverride(availableSize);
        }

        private void ApplyLayout(bool narrow)
        {
            _isNarrow = narrow;
            var pairsPerRow = narrow ? 1 : 2;
            ColumnDefinitions = new ColumnDefinitions(narrow ? "Auto,8,*" : "Auto,8,*,16,Auto,8,*");
            RowDefinitions = new RowDefinitions(string.Join(',',
                Enumerable.Repeat("Auto", (_fields.Length + pairsPerRow - 1) / pairsPerRow)));
            for (var index = 0; index < _fields.Length; index++)
            {
                var (label, editor) = _fields[index];
                var column = (index % pairsPerRow) * 4;
                SetColumn(label, column);
                SetColumn(editor, column + 2);
                SetRow(label, index / pairsPerRow);
                SetRow(editor, index / pairsPerRow);
            }
        }
    }

    private static ComboBox SettingPicker()
    {
        var picker = new ComboBox
        {
            MinWidth = 0,
            Height = UiDensity.StandardControlHeight,
            VerticalAlignment = VerticalAlignment.Center
        };
        return picker;
    }

    private static NumericUpDown SettingNumber(
        decimal minimum,
        decimal maximum,
        decimal increment,
        decimal value)
    {
        var input = new NumericUpDown
        {
            MinWidth = 0,
            Height = UiDensity.StandardControlHeight,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            Value = value,
            ShowButtonSpinner = false
        };
        input.BindThemeResource(NumericUpDown.BackgroundProperty, ThemeResourceBindings.SettingsSurface);
        // Fluent's inner spinner minimum is taller than our compact input. Keep
        // its frame and editor inside the outer control instead of clipping the border.
        input.Styles.Add(new Style(selector => selector.OfType<NumericUpDown>()
            .Template().OfType<ButtonSpinner>().Name("PART_Spinner"))
        {
            Setters = { new Setter(ButtonSpinner.MinHeightProperty, 0d) }
        });
        input.Styles.Add(new Style(selector => selector.OfType<NumericUpDown>()
            .Template().OfType<TextBox>().Name("PART_TextBox"))
        {
            Setters = { new Setter(TextBox.MinHeightProperty, 0d) }
        });
        return input;
    }

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

        var toolbar = new Border
        {
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Margin = new Thickness(10),
            Padding = new Thickness(7, 5),
            Child = panel
        };
        SettingsPanelChrome.ApplySurfaceCardStyle(toolbar);
        return toolbar;
    }

    private static Button CompactButton(string iconName, string tooltip)
    {
        var icon = new LocalIcon
        {
            IconName = iconName,
            Width = 18,
            Height = 18
        };
        icon.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.MutedText);
        var button = new Button
        {
            Content = icon,
            Width = 36,
            MinWidth = 0,
            Height = UiDensity.StandardControlHeight,
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
