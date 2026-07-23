using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;

namespace OptilandWorkbench.App.Panels;

public sealed class OpticalDrawingPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly IMaterialCatalogService _materials;
    private readonly IWorkspaceEventStream _events;
    private readonly IVisualizationService _visualization;
    private readonly ComboBox _elementPicker = new() { MinWidth = 260 };
    private readonly ComboBox _pageSize = new() { MinWidth = 110 };
    private readonly OpticalDrawingStandard _drawingStandard;
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
    private readonly TextBox _imperfection = Text("2 × 0.16；L 0.4");
    private readonly TextBox _coating = Text("按设计膜系；有效口径内均匀");
    private readonly TextBox _edgeTreatment = Text("倒边 0.2 × 45°；不允许崩边进入净口径");
    private readonly TextBox _stress = Text("10 nm/cm");
    private readonly TextBox _bubbles = Text("2 × 0.1");
    private readonly TextBox _homogeneity = Text("2；2");
    private readonly TextBlock _logoStatus = new()
    {
        Text = "内置 S.T.A.R.Labs",
        MaxWidth = 190,
        TextTrimming = TextTrimming.CharacterEllipsis
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
    private bool _disposed;
    private bool _updating;

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
        _drawingStandard = drawingStandard;
        _logoStatus.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OptilandMutedTextBrush"));
        _zoomStatus.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OptilandMutedTextBrush"));
        _status.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OptilandMutedTextBrush"));
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
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
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
        toolbar.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OptilandSurfaceBrush"));
        toolbar.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));

        var settings = BuildSettings();
        var settingsPane = new Border
        {
            Width = 340,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer
            {
                Content = settings,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
        };
        settingsPane.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OptilandSurfaceBrush"));
        settingsPane.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));
        var previewFrame = new Border
        {
            BorderThickness = new Thickness(1),
            Child = _preview
        };
        previewFrame.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));
        var previewPane = new Border
        {
            Padding = new Thickness(16),
            Child = previewFrame
        };
        previewPane.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OptilandWorkspaceBrush"));
        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("340,*"),
            Children = { settingsPane, previewPane }
        };
        Grid.SetColumn(previewPane, 1);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(workspace);
        Content = root;

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
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { importLogo, resetLogo }
                },
                _logoStatus
            }
        }, ref row);
        AddSection("尺寸公差", ref row);
        AddRow("直径上偏差 (mm)", _diameterUpperDeviation, ref row);
        AddRow("直径下偏差 (mm)", _diameterLowerDeviation, ref row);
        AddRow("中心厚度上偏差 (mm)", _thicknessUpperDeviation, ref row);
        AddRow("中心厚度下偏差 (mm)", _thicknessLowerDeviation, ref row);
        AddRow("S1 曲率半径公差 (mm)", _frontRadiusTolerance, ref row);
        AddRow("S2 曲率半径公差 (mm)", _backRadiusTolerance, ref row);
        AddSection("材料公差", ref row);
        AddRow("n[d] 公差", _refractiveIndexTolerance, ref row);
        AddRow("V[d] 公差", _abbeNumberTolerance, ref row);
        AddSection("光学技术要求", ref row);
        AddRow("S1 面形偏差 (nm)", _frontForm, ref row);
        AddRow("S2 面形偏差 (nm)", _backForm, ref row);
        AddRow("偏心/倾斜 (′)", _centering, ref row);
        AddRow("表面纹理 Rq (nm)", _texture, ref row);
        AddRow("表面缺陷", _imperfection, ref row);
        AddRow("应力双折射", _stress, ref row);
        AddRow("气泡和夹杂", _bubbles, ref row);
        AddRow("均匀性和条纹", _homogeneity, ref row);
        AddSection("工艺说明", ref row);
        AddRow("膜层", _coating, ref row);
        AddRow("边缘处理", _edgeTreatment, ref row);

        var note = new TextBlock
        {
            Text = _drawingStandard == OpticalDrawingStandard.GbT13323_2009
                ? "当前图样：GB/T 13323—2009《光学制图》。"
                : "当前图样：ISO 10110 系列表格式。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 8)
        };
        note.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OptilandMutedTextBrush"));
        AddControl(note, row++, 0, 2);
        return grid;

        void AddSection(string title, ref int currentRow)
        {
            var heading = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, currentRow == 0 ? 0 : 12, 0, 5)
            };
            AddControl(heading, currentRow++, 0, 2);
        }

        void AddRow(string label, Control control, ref int currentRow)
        {
            AddControl(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            }, currentRow, 0);
            control.Margin = new Thickness(0, 3);
            AddControl(control, currentRow++, 1);
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
        var choices = new[]
            {
                new ElementChoice("\u955c\u5934\u603b\u4f53\u5e03\u5c40\u56fe", null)
            }
            .Concat(OpticalManufacturingModel.BuildDrawingElements(_prescription.GetSurfaces())
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
                    _drawingStandard);
                var systemBytes = OpticalDrawingRenderer.RenderSystemPreview(systemSheet);
                using var systemStream = new MemoryStream(systemBytes);
                var systemBitmap = new Bitmap(systemStream);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed)
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
            _preview.Source = bitmap;
            _previewBitmap?.Dispose();
            _previewBitmap = bitmap;
            _status.Text = "预览已更新";
        }
        catch (Exception exception)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() =>
                    _status.Text = $"\u9884\u89c8\u5931\u8d25: {exception.Message}");
                return;
            }

            _status.Text = $"预览失败：{exception.Message}";
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
                Title = "导出 ISO 光学图纸",
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
            FindMaterial(element.Components[0].Material),
            _companyLogoPng,
            (double)(_refractiveIndexTolerance.Value ?? 0.0005m),
            (double)(_abbeNumberTolerance.Value ?? 0.5m),
            _drawingStandard,
            (double)(_frontRadiusTolerance.Value ?? 0.1m),
            (double)(_backRadiusTolerance.Value ?? 0.1m),
            element.Components.Select(component => FindMaterial(component.Material)).ToArray());
    }

    private GlassMaterialDto? FindMaterial(string name) =>
        _materials.GetGlasses()
            .Where(glass => glass.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(glass => glass.Manufacturer.Equals("Compatibility", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

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

    private static Button IconButton(string iconName, string tooltip)
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
        return button;
    }

    private sealed record ElementChoice(string DisplayName, OpticalDrawingElementDefinition? Element);
}
