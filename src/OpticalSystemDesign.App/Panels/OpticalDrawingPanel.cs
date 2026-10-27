using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed class OpticalDrawingPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly IMaterialCatalogService _materials;
    private readonly IWorkspaceEventStream _events;
    private readonly IVisualizationService _visualization;
    private readonly ComboBox _elementPicker = new() { MinWidth = 160, MaxWidth = 260 };
    private readonly ComboBox _pageSize = new() { MinWidth = 110 };
    private readonly ComboBox _standardPicker = new() { Name = "DrawingStandardPicker", MinWidth = 150 };
    private readonly List<Control> _isoOnlyControls = new();
    private readonly List<Control> _legacyOnlyControls = new();
    private readonly TextBlock _standardNote = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 8)
    };
    private OpticalDrawingStandard _drawingStandard;
    private readonly TextBox _drawingNumber = Text("OPT-001");
    private readonly TextBox _partName = Text("光学元件");
    private readonly TextBox _designer = Text("设计");
    private readonly TextBox _reviewer = Text("审核");
    private readonly TextBox _revision = Text("A");
    private readonly NumericUpDown _diameterUpperDeviation = Number(0.02m, -10, 10, 0.01m);
    private readonly NumericUpDown _diameterLowerDeviation = Number(-0.02m, -10, 10, 0.01m);
    private readonly NumericUpDown _thicknessUpperDeviation = Number(0.02m, -10, 10, 0.01m);
    private readonly NumericUpDown _thicknessLowerDeviation = Number(-0.02m, -10, 10, 0.01m);
    private readonly NumericUpDown _frontRadiusTolerance = Number(0.1m, 0, 10000, 0.01m);
    private readonly NumericUpDown _backRadiusTolerance = Number(0.1m, 0, 10000, 0.01m);
    private readonly NumericUpDown _refractiveIndexTolerance = Number(0.0005m, 0, 1, 0.0001m, "0.000000");
    private readonly NumericUpDown _abbeNumberTolerance = Number(0.5m, 0, 100, 0.1m);
    private readonly NumericUpDown _frontForm = Number(100, 0, 10000, 10);
    private readonly NumericUpDown _backForm = Number(100, 0, 10000, 10);
    private readonly NumericUpDown _centering = Number(1, 0, 120, 0.1m);
    private readonly NumericUpDown _texture = Number(1, 0, 1000, 0.1m);
    private readonly NumericUpDown _opticalRa = OptionalNumber(1000, 0.001m);
    private readonly NumericUpDown _otherRa = OptionalNumber(1000, 0.1m);
    private readonly NumericUpDown _opticalRaSample = OptionalNumber(1000, 0.01m);
    private readonly NumericUpDown _otherRaSample = OptionalNumber(1000, 0.01m);
    private readonly NumericUpDown _fringeWavelength = OptionalNumber(1000000, 0.01m);
    private readonly TextBox _imperfection = Text("2 × 0.16；L 0.4");
    private readonly TextBox _coating = Text("按设计膜系；有效口径内均匀");
    private readonly TextBox _edgeTreatment = Text("倒边 0.2 × 45°；不允许崩边进入净口径");
    private readonly TextBox _stress = Text("10 nm/cm");
    private readonly TextBox _bubbles = Text("2 × 0.1");
    private readonly TextBox _homogeneity = Text("2；2");
    private readonly TextBox _laserDamageThreshold = Text("-");
    private readonly Dictionary<string, TextBox> _legacyGbEditors = new(StringComparer.Ordinal);
    private readonly TextBlock _logoStatus = new()
    {
        Text = "内置 S.T.A.R.Labs",
        MaxWidth = 190,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly CompanyLogoControl _builtInLogoPreview = new()
    {
        Name = "DrawingCompanyLogo",
        Width = 180,
        Height = 26,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private readonly DrawingPreviewControl _preview = new();
    private readonly TextBlock _zoomStatus = new()
    {
        Text = "100%",
        Width = 48,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _status = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };
    private Bitmap? _previewBitmap;
    private byte[]? _companyLogoPng;
    private long _previewGeneration;
    private bool _disposed;
    private bool _updating;

    public OpticalDrawingStandard DrawingStandard => _drawingStandard;
    public event EventHandler? DrawingStandardChanged;

    public OpticalDrawingPanel(
        IPrescriptionService prescription,
        IMaterialCatalogService materials,
        IWorkspaceEventStream events,
        IVisualizationService visualization,
        OpticalDrawingStandard drawingStandard = OpticalDrawingStandard.Iso10110)
    {
        _prescription = prescription;
        _materials = materials;
        _events = events;
        _visualization = visualization;
        _drawingStandard = Enum.IsDefined(drawingStandard) ? drawingStandard : OpticalDrawingStandard.Iso10110;
        var standards = new[]
        {
            new StandardChoice(OpticalDrawingStandard.Iso10110, "ISO 10110"),
            new StandardChoice(OpticalDrawingStandard.GbT13323_1991, "GB/T 13323—1991（老国标）")
        };
        _standardPicker.ItemsSource = standards;
        _standardPicker.ItemTemplate = new FuncDataTemplate<StandardChoice>((choice, _) => new TextBlock
        {
            Text = UiLocalization.Text(choice?.DisplayName ?? string.Empty),
            VerticalAlignment = VerticalAlignment.Center
        });
        _standardPicker.SelectedItem = standards.Single(choice => choice.Standard == _drawingStandard);
        Avalonia.Automation.AutomationProperties.SetName(_standardPicker, "制图标准");
        _logoStatus.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OpticalSystemDesignMutedTextBrush"));
        _zoomStatus.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OpticalSystemDesignMutedTextBrush"));
        _status.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OpticalSystemDesignMutedTextBrush"));
        _pageSize.ItemsSource = new[] { "A4 竖向", "A3 竖向" };
        _pageSize.SelectedIndex = 0;
        _elementPicker.ItemTemplate = new FuncDataTemplate<ElementChoice>((choice, _) => new TextBlock
        {
            Text = choice?.DisplayName ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center
        });
        _elementPicker.SelectionChanged += (_, _) => OnElementChanged();

        var update = CommandButton("refresh-cw", "更新预览", 104);
        update.Click += (_, _) => UpdatePreview();
        var export = CommandButton("file-down", "导出 PDF", 104);
        export.Click += async (_, _) => await ExportPdfAsync();
        var zoomOut = IconButton("zoom-out", "缩小");
        zoomOut.Click += (_, _) => _preview.ZoomOut();
        var resetView = IconButton("maximize-2", "适合窗口");
        resetView.Click += (_, _) => _preview.ResetView();
        var zoomIn = IconButton("zoom-in", "放大");
        zoomIn.Click += (_, _) => _preview.ZoomIn();
        _preview.ViewChanged += (_, _) =>
            _zoomStatus.Text = $"{Math.Round(_preview.Zoom * 100):0}%";

        var toolbar = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 6),
            Child = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 8,
                LineSpacing = 6,
                Children =
                {
                    new TextBlock { Text = "制图标准", VerticalAlignment = VerticalAlignment.Center },
                    _standardPicker,
                    new TextBlock
                    {
                        Text = "图纸",
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.SemiBold
                    },
                    _elementPicker,
                    new TextBlock { Text = "图幅", VerticalAlignment = VerticalAlignment.Center },
                    _pageSize,
                    update,
                    export,
                    zoomOut,
                    resetView,
                    zoomIn,
                    _zoomStatus,
                    _status
                }
            }
        };
        toolbar.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OpticalSystemDesignSurfaceBrush"));
        toolbar.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OpticalSystemDesignBorderBrush"));

        var settings = BuildSettings();
        var settingsPane = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                Content = settings,
                // Reserve the scrollbar's measured width instead of overlaying editors.
                AllowAutoHide = false,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
        };
        settingsPane.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OpticalSystemDesignSurfaceBrush"));
        settingsPane.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OpticalSystemDesignBorderBrush"));
        var previewFrame = new Border
        {
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = _preview
        };
        previewFrame.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OpticalSystemDesignBorderBrush"));
        var previewPane = new Border
        {
            Padding = new Thickness(16),
            Child = previewFrame
        };
        previewPane.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OpticalSystemDesignWorkspaceBrush"));
        var workspace = new ResponsiveTwoPaneGrid(
            settingsPane,
            previewPane,
            "340,12,*",
            "2*,12,3*",
            breakpoint: 900);

        Content = new ScrollableHeaderGrid(toolbar, workspace);

        UpdateStandardControls();
        _standardPicker.SelectionChanged += (_, _) =>
        {
            if (_disposed || _standardPicker.SelectedItem is not StandardChoice choice
                || choice.Standard == _drawingStandard)
                return;
            _drawingStandard = choice.Standard;
            UpdateStandardControls();
            UpdatePreview();
            DrawingStandardChanged?.Invoke(this, EventArgs.Empty);
        };
        _events.Changed += OnWorkspaceChanged;
        RefreshElements(preserveSelection: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _previewGeneration);
        _events.Changed -= OnWorkspaceChanged;
        _preview.Source = null;
        _previewBitmap?.Dispose();
    }

    private Control BuildSettings()
    {
        var grid = new Grid
        {
            Margin = new Thickness(12),
            ColumnDefinitions = new ColumnDefinitions("118,*")
        };
        var row = 0;
        AddSection("图签", ref row);
        AddRow("图号", _drawingNumber, ref row);
        AddRow("零件名称", _partName, ref row);
        AddRow("设计", _designer, ref row);
        AddRow("审核", _reviewer, ref row);
        AddRow("版本", _revision, ref row);
        var importLogo = CommandButton("image-up", "导入 PNG", 92);
        importLogo.Click += async (_, _) => await ImportLogoAsync();
        var resetLogo = CommandButton("rotate-ccw", "恢复默认", 92);
        resetLogo.Click += (_, _) => ResetLogo();
        AddRow("公司 Logo", new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    ItemSpacing = 6,
                    LineSpacing = 4,
                    Children = { importLogo, resetLogo }
                },
                _builtInLogoPreview,
                _logoStatus
            }
        }, ref row);
        AddSection("尺寸公差", ref row);
        AddRow("直径上偏差", _diameterUpperDeviation, ref row, unit: "mm");
        AddRow("直径下偏差", _diameterLowerDeviation, ref row, unit: "mm");
        AddRow("厚度上偏差", _thicknessUpperDeviation, ref row, "中心厚度上偏差 (mm)", "mm");
        AddRow("厚度下偏差", _thicknessLowerDeviation, ref row, "中心厚度下偏差 (mm)", "mm");
        AddRow("S1 半径公差", _frontRadiusTolerance, ref row, "S1 曲率半径公差 (mm)", "mm", OpticalDrawingStandard.Iso10110);
        AddRow("S2 半径公差", _backRadiusTolerance, ref row, "S2 曲率半径公差 (mm)", "mm", OpticalDrawingStandard.Iso10110);
        AddSection("材料公差", ref row);
        AddRow("n[d] 公差", _refractiveIndexTolerance, ref row);
        AddRow("V[d] 公差", _abbeNumberTolerance, ref row);
        AddSection("光学技术要求", ref row);
        AddRow("S1 面形偏差", _frontForm, ref row, unit: "nm");
        AddRow("S2 面形偏差", _backForm, ref row, unit: "nm");
        AddRow("偏心/倾斜", _centering, ref row, unit: "′");
        AddRow("补充 Rq", _texture, ref row, "补充表面纹理 Rq (nm)，不换算为Ra", "nm", OpticalDrawingStandard.GbT13323_1991);

        AddRow("表面缺陷", _imperfection, ref row, standard: OpticalDrawingStandard.Iso10110);
        AddRow("应力双折射", _stress, ref row, standard: OpticalDrawingStandard.Iso10110);
        AddRow("气泡和夹杂", _bubbles, ref row, standard: OpticalDrawingStandard.Iso10110);
        AddRow("均匀性和条纹", _homogeneity, ref row, standard: OpticalDrawingStandard.Iso10110);
        AddRow("6/ 激光损伤阈值", _laserDamageThreshold, ref row, standard: OpticalDrawingStandard.Iso10110);

        AddSection("老国标材料等级", ref row, OpticalDrawingStandard.GbT13323_1991);
        AddLegacyRow("材料等级依据", nameof(LegacyGb1991Requirements.MaterialQualityStandard), ref row);
        AddLegacyRow("Δn[d] 等级", nameof(LegacyGb1991Requirements.RefractiveIndexGrade), ref row,
            "按材料标准填写类别及批差等级，如1B；留空未规定，数值公差保留在技术要求中，不自动换算等级");
        AddLegacyRow("Δν[d] 等级", nameof(LegacyGb1991Requirements.AbbeNumberGrade), ref row,
            "按材料标准填写类别及批差等级；留空未规定，V[d]绝对数值公差不自动换算为百分比或等级");
        AddLegacyRow("光学均匀性", nameof(LegacyGb1991Requirements.HomogeneityGrade), ref row);
        AddLegacyRow("光吸收系数", nameof(LegacyGb1991Requirements.AbsorptionGrade), ref row);
        AddLegacyRow("应力双折射等级", nameof(LegacyGb1991Requirements.StressGrade), ref row);
        AddLegacyRow("条纹度", nameof(LegacyGb1991Requirements.StriaeGrade), ref row);
        AddLegacyRow("气泡度", nameof(LegacyGb1991Requirements.BubbleGrade), ref row);
        AddSection("老国标零件要求", ref row, OpticalDrawingStandard.GbT13323_1991);
        AddLegacyRow("N 光圈数", nameof(LegacyGb1991Requirements.PowerFringes), ref row,
            "光圈数；半径仅标名义值，精度由样板和检验要求表达；分面要求用S1/S2注明，不由波前或nm限值推定");
        AddLegacyRow("ΔN 光圈数", nameof(LegacyGb1991Requirements.IrregularityFringes), ref row,
            "最大像散光圈数和局部光圈数；适用于D0内工作面，分面要求用S1/S2注明，不由nm限值推定");
        AddLegacyRow("ΔR 光学样板", nameof(LegacyGb1991Requirements.TestPlateGrade), ref row,
            "标准样板等级A或B；分面可填S1:A；S2:B；不是以mm表示的半径公差");
        AddRow("检验波长 λ", _fringeWavelength, ref row, unit: "nm", standard: OpticalDrawingStandard.GbT13323_1991);
        AddLegacyRow("B 表面疵病", nameof(LegacyGb1991Requirements.SurfaceImperfection), ref row);
        AddLegacyRow("χ 定心基准", nameof(LegacyGb1991Requirements.CenteringDatum), ref row,
            "χ表示光学表面面倾角，使用共用角分公差；请明确检验基准，不自动换算为偏心距离或边厚差");
        AddLegacyRow("f′ 要求 (mm)", nameof(LegacyGb1991Requirements.FocalLength), ref row);
        AddLegacyRow("Lf 要求 (mm)", nameof(LegacyGb1991Requirements.FrontFocalDistance), ref row);
        AddLegacyRow("L′f 要求 (mm)", nameof(LegacyGb1991Requirements.BackFocalDistance), ref row);
        AddSection("老国标表面粗糙度", ref row, OpticalDrawingStandard.GbT13323_1991);
        AddRow("光学面 Ra", _opticalRa, ref row, "光学工作面轮廓算术平均偏差 Ra (μm)，未规定时留空", "μm", OpticalDrawingStandard.GbT13323_1991);
        AddRow("光学面取样长度", _opticalRaSample, ref row, "光学面 Ra 取样长度 l (mm)，未规定时留空", "mm", OpticalDrawingStandard.GbT13323_1991);
        AddRow("其余表面 Ra", _otherRa, ref row, "其余表面轮廓算术平均偏差 Ra (μm)，未规定时留空", "μm", OpticalDrawingStandard.GbT13323_1991);
        AddRow("其余面取样长度", _otherRaSample, ref row, "其余表面 Ra 取样长度 l (mm)，未规定时留空", "mm", OpticalDrawingStandard.GbT13323_1991);
        AddLegacyRow("面型定义引用", nameof(LegacyGb1991Requirements.AsphericDefinitionReference), ref row,
            "Forbes Q须填写完整面型定义或受控图纸引用；偶次、奇次和圆锥非球面自动列出表达式及处方系数");
        AddLegacyRow("补充技术要求", nameof(LegacyGb1991Requirements.TechnicalRequirements), ref row);

        AddSection("工艺说明", ref row);
        AddRow("膜层", _coating, ref row);
        AddRow("边缘处理", _edgeTreatment, ref row);

        _standardNote.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OpticalSystemDesignMutedTextBrush"));
        AddControl(_standardNote, row++, 0, 2);
        return grid;

        void AddSection(string title, ref int currentRow, OpticalDrawingStandard? standard = null)
        {
            var heading = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, currentRow == 0 ? 0 : 12, 0, 5)
            };
            AddControl(heading, currentRow++, 0, 2);
            if (standard == OpticalDrawingStandard.GbT13323_1991) _legacyOnlyControls.Add(heading);
        }

        void AddLegacyRow(string label, string property, ref int currentRow, string? fullText = null)
        {
            var editor = Text(property switch
            {
                nameof(LegacyGb1991Requirements.MaterialQualityStandard) => "GB 903—1987",
                nameof(LegacyGb1991Requirements.CenteringDatum) => "机械外圆轴",
                _ => ""
            });
            editor.Name = "Gb1991" + property;
            editor.PlaceholderText = "未规定";
            if (property == nameof(LegacyGb1991Requirements.TechnicalRequirements))
            {
                editor.AcceptsReturn = true;
                editor.TextWrapping = TextWrapping.Wrap;
                editor.MaxHeight = 120;
            }
            _legacyGbEditors.Add(property, editor);
            AddRow(label, editor, ref currentRow, fullText, standard: OpticalDrawingStandard.GbT13323_1991);
        }

        void AddRow(string label, Control control, ref int currentRow, string? fullText = null, string? unit = null,
            OpticalDrawingStandard? standard = null)
        {
            fullText ??= unit is null ? label : $"{label} ({unit})";
            var caption = CompactLabel.Parameter(label, fullText, unit);
            AddControl(caption, currentRow, 0);
            ToolTip.SetTip(control, fullText);
            control.Margin = new Thickness(0, 3);
            AddControl(control, currentRow++, 1);
            if (standard is not null)
            {
                var controls = standard == OpticalDrawingStandard.Iso10110 ? _isoOnlyControls : _legacyOnlyControls;
                controls.Add(caption);
                controls.Add(control);
            }
        }

        void AddControl(Control control, int targetRow, int column, int columnSpan = 1)
        {
            while (grid.RowDefinitions.Count <= targetRow)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            Grid.SetRow(control, targetRow);
            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, columnSpan);
            grid.Children.Add(control);
        }
    }

    private void UpdateStandardControls()
    {
        foreach (var control in _isoOnlyControls)
            control.IsVisible = _drawingStandard == OpticalDrawingStandard.Iso10110;
        foreach (var control in _legacyOnlyControls)
            control.IsVisible = _drawingStandard == OpticalDrawingStandard.GbT13323_1991;
        _standardNote.Text = StandardNote(_drawingStandard);
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() => RefreshElements(preserveSelection: !args.FileSwitched));
    }

    private void RefreshElements(bool preserveSelection)
    {
        if (_disposed)
        {
            return;
        }

        var previousChoice = preserveSelection
            ? (_elementPicker.SelectedItem as ElementChoice)?.DisplayName
            : null;
        IReadOnlyList<OpticalDrawingElementDefinition> elements;
        string? limitation = null;
        try
        {
            elements = OpticalManufacturingModel.BuildDrawingElements(_prescription.GetSurfaces());
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            elements = Array.Empty<OpticalDrawingElementDefinition>();
            limitation = exception.Message;
        }
        ToolTip.SetTip(_elementPicker, limitation);
        var choices = new[]
            {
                new ElementChoice(limitation is null ? "镜头总体布局图" : $"镜头总体布局图（单片/胶合图不可用：{limitation}）", null)
            }
            .Concat(elements
                .Select(element => new ElementChoice(element.DisplayName, element)))
            .ToArray();
        _updating = true;
        _elementPicker.ItemsSource = choices;
        _elementPicker.SelectedItem = choices.FirstOrDefault(choice =>
            choice.DisplayName == previousChoice) ?? choices.FirstOrDefault();
        _updating = false;
        OnElementChanged();
    }

    private void OnElementChanged()
    {
        if (_updating || _elementPicker.SelectedItem is not ElementChoice choice)
        {
            if (_elementPicker.SelectedItem is null)
            {
                _status.Text = "没有可制图的玻璃元件";
                _preview.Source = null;
            }

            return;
        }

        var element = choice.Element;
        if (element is null)
        {
            _drawingNumber.Text = "OPT-SYSTEM-LAYOUT";
            _partName.Text = "\u955c\u5934\u603b\u4f53\u5e03\u5c40";
            UpdatePreview();
            return;
        }

        _drawingNumber.Text = element.IsCemented
            ? $"OPT-CEM-{element.ComponentNumbers.Replace("+", "-")}"
            : $"OPT-L{element.ComponentNumbers.PadLeft(2, '0')}";
        _partName.Text = $"{element.Material} \u5149\u5b66\u5143\u4ef6";
        _coating.Text = CoatingText(element);
        UpdatePreview();
    }

    private static string CoatingText(OpticalDrawingElementDefinition element)
    {
        var coatings = element.Surfaces
            .Select((surface, index) => CoatingLabel($"S{index + 1}", surface.Coating))
            .Where(value => value is not null);
        var text = string.Join("；", coatings!);
        return text.Length > 0 ? text : "按设计膜系；有效口径内均匀";

        static string? CoatingLabel(string surface, string? coating) =>
            string.IsNullOrWhiteSpace(coating)
            || coating.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? null
                : $"{surface} {coating}";
    }

    private async void UpdatePreview()
    {
        var generation = Interlocked.Increment(ref _previewGeneration);
        try
        {
            if (_elementPicker.SelectedItem is ElementChoice { Element: null })
            {
                _status.Text = "\u6b63\u5728\u751f\u6210\u603b\u4f53\u5e03\u5c40...";
                var pageSize = _pageSize.SelectedIndex == 1
                    ? OpticalDrawingPageSize.A3
                    : OpticalDrawingPageSize.A4;
                var drawingNumber = Value(_drawingNumber, "OPT-SYSTEM-LAYOUT");
                var partName = Value(_partName, "\u955c\u5934\u603b\u4f53\u5e03\u5c40");
                var designer = Value(_designer, "\u8bbe\u8ba1");
                var reviewer = Value(_reviewer, "\u5ba1\u6838");
                var revision = Value(_revision, "A");
                var companyLogo = _companyLogoPng;
                var standard = _drawingStandard;

                var scene = await _visualization.BuildSceneAsync(new VisualizationRequestDto(
                    SceneDimension.TwoDimensional,
                    IncludeAllWavelengths: false,
                    RayCount: 1));
                if (scene.TwoDimensional is null)
                {
                    return;
                }

                var systemSheet = new OpticalSystemDrawingSheet(
                    scene.TwoDimensional,
                    pageSize,
                    drawingNumber,
                    partName,
                    designer,
                    reviewer,
                    revision,
                    companyLogo,
                    standard);
                var systemBytes = OpticalDrawingRenderer.RenderSystemPreview(systemSheet);
                using var systemStream = new MemoryStream(systemBytes);
                var systemBitmap = new Bitmap(systemStream);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed || generation != Interlocked.Read(ref _previewGeneration))
                    {
                        systemBitmap.Dispose();
                        return;
                    }

                    _preview.Source = systemBitmap;
                    _previewBitmap?.Dispose();
                    _previewBitmap = systemBitmap;
                    _status.Text = "\u603b\u4f53\u5e03\u5c40\u9884\u89c8\u5df2\u66f4\u65b0";
                });
                return;
            }

            var sheet = CreateSheet();
            if (sheet is null)
            {
                return;
            }

            var bytes = OpticalDrawingRenderer.RenderPreview(sheet);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            if (_disposed || generation != Interlocked.Read(ref _previewGeneration))
            {
                bitmap.Dispose();
                return;
            }

            _preview.Source = bitmap;
            _previewBitmap?.Dispose();
            _previewBitmap = bitmap;
            _status.Text = "预览已更新";
        }
        catch (Exception exception)
        {
            void ShowFailure()
            {
                if (_disposed || generation != Interlocked.Read(ref _previewGeneration))
                {
                    return;
                }

                _status.Text = $"预览失败：{exception.Message}";
            }

            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ShowFailure);
                return;
            }

            ShowFailure();
        }
    }

    private async Task ExportPdfAsync()
    {
        try
        {
            if (_elementPicker.SelectedItem is ElementChoice { Element: null })
            {
                await ExportSystemPdfAsync();
                return;
            }

            var sheet = CreateSheet();
            var topLevel = TopLevel.GetTopLevel(this);
            if (sheet is null || topLevel is null)
            {
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"导出 {OpticalDrawingRenderer.StandardDesignation(_drawingStandard)} PDF",
                SuggestedFileName = $"{SafeFileName(sheet.DrawingNumber)}-{SafeFileName(sheet.PartName)}.pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF 图纸")
                    {
                        Patterns = new[] { "*.pdf" },
                        MimeTypes = new[] { "application/pdf" }
                    }
                }
            });
            if (file is null)
            {
                return;
            }

            OpticalDrawingRenderer.ExportPdf(file.Path.LocalPath, sheet);
            _status.Text = $"PDF 已导出：{file.Name}";
        }
        catch (Exception exception)
        {
            _status.Text = $"导出失败：{exception.Message}";
        }
    }

    private async Task ExportSystemPdfAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var scene = await _visualization.BuildSceneAsync(new VisualizationRequestDto(
            SceneDimension.TwoDimensional,
            IncludeAllWavelengths: false,
            RayCount: 1));
        if (scene.TwoDimensional is null)
        {
            return;
        }

        var sheet = CreateSystemSheet(scene.TwoDimensional);
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "\u5bfc\u51fa\u955c\u5934\u603b\u4f53\u5e03\u5c40\u56fe",
            SuggestedFileName = $"{SafeFileName(sheet.DrawingNumber)}-{SafeFileName(sheet.PartName)}.pdf",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PDF")
                {
                    Patterns = new[] { "*.pdf" },
                    MimeTypes = new[] { "application/pdf" }
                }
            }
        });
        if (file is null)
        {
            return;
        }

        OpticalDrawingRenderer.ExportSystemPdf(file.Path.LocalPath, sheet);
        _status.Text = $"PDF \u5df2\u5bfc\u51fa: {file.Name}";
    }

    private async Task ImportLogoAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入公司 Logo",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PNG 图片")
                {
                    Patterns = new[] { "*.png" },
                    MimeTypes = new[] { "image/png" }
                }
            }
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using var input = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            await input.CopyToAsync(memory);
            if (memory.Length > 10 * 1024 * 1024)
            {
                throw new InvalidDataException("PNG 文件不能超过 10 MB");
            }

            var bytes = memory.ToArray();
            if (bytes.Length < 8
                || bytes[0] != 0x89
                || bytes[1] != 0x50
                || bytes[2] != 0x4e
                || bytes[3] != 0x47
                || bytes[4] != 0x0d
                || bytes[5] != 0x0a
                || bytes[6] != 0x1a
                || bytes[7] != 0x0a)
            {
                throw new InvalidDataException("请选择有效的 PNG 图片");
            }

            using var validationStream = new MemoryStream(bytes);
            using var bitmap = new Bitmap(validationStream);
            if (bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0)
            {
                throw new InvalidDataException("PNG 图片尺寸无效");
            }

            _companyLogoPng = bytes;
            _builtInLogoPreview.IsVisible = false;
            _logoStatus.Text = files[0].Name;
            UpdatePreview();
        }
        catch (Exception exception)
        {
            _status.Text = $"Logo 导入失败：{exception.Message}";
        }
    }

    private void ResetLogo()
    {
        _companyLogoPng = null;
        _builtInLogoPreview.IsVisible = true;
        _logoStatus.Text = "内置 S.T.A.R.Labs";
        UpdatePreview();
    }

    private OpticalSystemDrawingSheet CreateSystemSheet(Scene2Dto scene) => new(
        scene,
        _pageSize.SelectedIndex == 1 ? OpticalDrawingPageSize.A3 : OpticalDrawingPageSize.A4,
        Value(_drawingNumber, "OPT-SYSTEM-LAYOUT"),
        Value(_partName, "\u955c\u5934\u603b\u4f53\u5e03\u5c40"),
        Value(_designer, "\u8bbe\u8ba1"),
        Value(_reviewer, "\u5ba1\u6838"),
        Value(_revision, "A"),
        _companyLogoPng,
        _drawingStandard);

    private OpticalDrawingSheet? CreateSheet()
    {
        if (_elementPicker.SelectedItem is not ElementChoice { Element: { } element })
        {
            return null;
        }

        var componentMaterials = element.Components
            .Select(component => _materials.FindGlass(component.Material))
            .ToArray();
        return new OpticalDrawingSheet(
            element,
            _pageSize.SelectedIndex == 1 ? OpticalDrawingPageSize.A3 : OpticalDrawingPageSize.A4,
            Value(_drawingNumber, "OPT-001"),
            Value(_partName, "光学元件"),
            Value(_designer, "设计"),
            Value(_reviewer, "审核"),
            Value(_revision, "A"),
            (double)(_diameterUpperDeviation.Value ?? 0.02m),
            (double)(_diameterLowerDeviation.Value ?? -0.02m),
            (double)(_thicknessUpperDeviation.Value ?? 0.02m),
            (double)(_thicknessLowerDeviation.Value ?? -0.02m),
            (double)(_frontForm.Value ?? 100),
            (double)(_backForm.Value ?? 100),
            (double)(_centering.Value ?? 1),
            (double)(_texture.Value ?? 1),
            Value(_imperfection, "2 × 0.16"),
            Value(_coating, "按设计膜系"),
            Value(_edgeTreatment, "倒边 0.2 × 45°"),
            Value(_stress, "10 nm/cm"),
            Value(_bubbles, "2 × 0.1"),
            Value(_homogeneity, "2；2"),
            componentMaterials[0],
            _companyLogoPng,
            (double)(_refractiveIndexTolerance.Value ?? 0.0005m),
            (double)(_abbeNumberTolerance.Value ?? 0.5m),
            _drawingStandard,
            (double)(_frontRadiusTolerance.Value ?? 0.1m),
            (double)(_backRadiusTolerance.Value ?? 0.1m),
            componentMaterials,
            Value(_laserDamageThreshold, "-"),
            new LegacyGb1991Requirements
            {
                RefractiveIndexGrade = LegacyValue(nameof(LegacyGb1991Requirements.RefractiveIndexGrade)),
                AbbeNumberGrade = LegacyValue(nameof(LegacyGb1991Requirements.AbbeNumberGrade)),
                HomogeneityGrade = LegacyValue(nameof(LegacyGb1991Requirements.HomogeneityGrade)),
                AbsorptionGrade = LegacyValue(nameof(LegacyGb1991Requirements.AbsorptionGrade)),
                StressGrade = LegacyValue(nameof(LegacyGb1991Requirements.StressGrade)),
                StriaeGrade = LegacyValue(nameof(LegacyGb1991Requirements.StriaeGrade)),
                BubbleGrade = LegacyValue(nameof(LegacyGb1991Requirements.BubbleGrade)),
                PowerFringes = LegacyValue(nameof(LegacyGb1991Requirements.PowerFringes)),
                IrregularityFringes = LegacyValue(nameof(LegacyGb1991Requirements.IrregularityFringes)),
                TestPlateGrade = LegacyValue(nameof(LegacyGb1991Requirements.TestPlateGrade)),
                SurfaceImperfection = LegacyValue(nameof(LegacyGb1991Requirements.SurfaceImperfection)),
                FocalLength = LegacyValue(nameof(LegacyGb1991Requirements.FocalLength)),
                FrontFocalDistance = LegacyValue(nameof(LegacyGb1991Requirements.FrontFocalDistance)),
                BackFocalDistance = LegacyValue(nameof(LegacyGb1991Requirements.BackFocalDistance)),
                OpticalSurfaceRaMicrometers = OptionalValue(_opticalRa),
                OtherSurfaceRaMicrometers = OptionalValue(_otherRa),
                OpticalRaSamplingLengthMillimeters = OptionalValue(_opticalRaSample),
                OtherRaSamplingLengthMillimeters = OptionalValue(_otherRaSample),
                FringeReferenceWavelengthNanometers = OptionalValue(_fringeWavelength),
                MaterialQualityStandard = LegacyValue(nameof(LegacyGb1991Requirements.MaterialQualityStandard)),
                CenteringDatum = LegacyValue(nameof(LegacyGb1991Requirements.CenteringDatum)),
                AsphericDefinitionReference = LegacyValue(nameof(LegacyGb1991Requirements.AsphericDefinitionReference)),
                TechnicalRequirements = LegacyValue(nameof(LegacyGb1991Requirements.TechnicalRequirements))
            });
    }

    private string LegacyValue(string property) => _legacyGbEditors[property].Text?.Trim() ?? "";

    private static string StandardNote(OpticalDrawingStandard standard) => standard switch
    {
        OpticalDrawingStandard.GbT13323_1991 =>
            "当前图样：GB/T 13323—1991；半径标名义值，精度按ΔR及N/ΔN填写。粗糙度Ra使用μm，补充Rq使用nm，互不换算；空项未规定。",
        _ => "当前图样：ISO 10110 系列表格式；6/- 表示未规定激光损伤阈值。"
    };

    private static string Value(TextBox textBox, string fallback) =>
        string.IsNullOrWhiteSpace(textBox.Text) ? fallback : textBox.Text.Trim();

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static TextBox Text(string value) => new()
    {
        Text = value,
        MinHeight = 30
    };

    private static NumericUpDown OptionalNumber(decimal maximum, decimal increment)
    {
        var editor = Number(0, 0, maximum, increment, "0.######");
        editor.Value = null;
        return editor;
    }

    private static double? OptionalValue(NumericUpDown editor) => editor.Value is { } value ? (double)value : null;

    private static NumericUpDown Number(
        decimal value,
        decimal minimum,
        decimal maximum,
        decimal increment,
        string formatString = "0.###") => new()
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            FormatString = formatString,
            MinHeight = 30
        };

    private static Button CommandButton(string iconName, string text, double minWidth) => new()
    {
        Content = new LocalIconLabel(iconName, text),
        MinWidth = minWidth,
        Height = 32,
        Padding = new Thickness(8, 3)
    };

    internal static Button IconButton(string iconName, string tooltip)
    {
        var button = new Button
        {
            Content = new LocalIcon
            {
                IconName = iconName,
                Width = 16,
                Height = 16
            },
            Width = 32,
            Height = 32,
            Padding = new Thickness(7)
        };
        ToolTip.SetTip(button, tooltip);
        Avalonia.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private sealed record StandardChoice(OpticalDrawingStandard Standard, string DisplayName);
    private sealed record ElementChoice(string DisplayName, OpticalDrawingElementDefinition? Element);
}
