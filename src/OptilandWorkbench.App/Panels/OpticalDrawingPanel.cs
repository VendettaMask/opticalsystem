using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private readonly ComboBox _elementPicker = new() { MinWidth = 260 };
    private readonly ComboBox _pageSize = new() { MinWidth = 110 };
    private readonly TextBox _drawingNumber = Text("OPT-001");
    private readonly TextBox _partName = Text("光学元件");
    private readonly TextBox _designer = Text("设计");
    private readonly TextBox _reviewer = Text("审核");
    private readonly TextBox _revision = Text("A");
    private readonly NumericUpDown _diameterUpperDeviation = Number(0.02m, -10, 10, 0.01m);
    private readonly NumericUpDown _diameterLowerDeviation = Number(-0.02m, -10, 10, 0.01m);
    private readonly NumericUpDown _thicknessUpperDeviation = Number(0.02m, -10, 10, 0.01m);
    private readonly NumericUpDown _thicknessLowerDeviation = Number(-0.02m, -10, 10, 0.01m);
    private readonly NumericUpDown _refractiveIndexTolerance = Number(0.0005m, 0, 1, 0.0001m, "0.000000");
    private readonly NumericUpDown _abbeNumberTolerance = Number(0.5m, 0, 100, 0.1m);
    private readonly NumericUpDown _frontForm = Number(100, 0, 10000, 10);
    private readonly NumericUpDown _backForm = Number(100, 0, 10000, 10);
    private readonly NumericUpDown _centering = Number(1, 0, 120, 0.1m);
    private readonly NumericUpDown _texture = Number(1, 0, 1000, 0.1m);
    private readonly TextBox _imperfection = Text("0.16 × 2；划痕 L 0.4");
    private readonly TextBox _coating = Text("按设计膜系；有效口径内均匀");
    private readonly TextBox _edgeTreatment = Text("倒边 0.2 × 45°；不允许崩边进入净口径");
    private readonly TextBox _stress = Text("≤ 10 nm/cm");
    private readonly TextBox _bubbles = Text("0.1 × 2");
    private readonly TextBox _homogeneity = Text("2；2");
    private readonly TextBlock _logoStatus = new()
    {
        Text = "默认 S.T.A.R. LABS",
        MaxWidth = 190,
        Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 102)),
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly DrawingPreviewControl _preview = new();
    private readonly TextBlock _zoomStatus = new()
    {
        Text = "100%",
        Width = 48,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(72, 72, 74))
    };
    private readonly TextBlock _status = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(72, 72, 74))
    };
    private Bitmap? _previewBitmap;
    private byte[]? _companyLogoPng;
    private bool _disposed;
    private bool _updating;

    public OpticalDrawingPanel(
        IPrescriptionService prescription,
        IMaterialCatalogService materials,
        IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _materials = materials;
        _events = events;
        _pageSize.ItemsSource = new[] { "A4 竖向", "A3 竖向" };
        _pageSize.SelectedIndex = 0;
        _elementPicker.ItemTemplate = new FuncDataTemplate<ElementChoice>((choice, _) => new TextBlock
        {
            Text = choice?.Element.DisplayName ?? string.Empty,
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
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
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
                        Text = "元件",
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

        var settings = BuildSettings();
        var settingsPane = new Border
        {
            Width = 340,
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer
            {
                Content = settings,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
        };
        var previewPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(220, 223, 228)),
            Padding = new Thickness(16),
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(174, 174, 178)),
                BorderThickness = new Thickness(1),
                Child = _preview
            }
        };
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
        AddSection("材料公差", ref row);
        AddRow("n[d] 公差", _refractiveIndexTolerance, ref row);
        AddRow("V[d] 公差", _abbeNumberTolerance, ref row);
        AddSection("ISO 10110 要求", ref row);
        AddRow("S1 面形 (nm)", _frontForm, ref row);
        AddRow("S2 面形 (nm)", _backForm, ref row);
        AddRow("偏心/倾斜 (′)", _centering, ref row);
        AddRow("表面纹理 Rq (nm)", _texture, ref row);
        AddRow("表面疵病", _imperfection, ref row);
        AddRow("应力双折射", _stress, ref row);
        AddRow("气泡和夹杂", _bubbles, ref row);
        AddRow("均匀性和条纹", _homogeneity, ref row);
        AddSection("工艺说明", ref row);
        AddRow("膜层", _coating, ref row);
        AddRow("边缘处理", _edgeTreatment, ref row);

        var note = new TextBlock
        {
            Text = "依据：ISO 10110-1:2019、ISO 10110-5:2026、ISO 10110-6:2025、ISO 10110-7:2017、ISO 10110-8:2019。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 102)),
            Margin = new Thickness(0, 12, 0, 8)
        };
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

        var previousSurface = preserveSelection
            ? (_elementPicker.SelectedItem as ElementChoice)?.Element.FrontSurface.Number
            : null;
        var choices = OpticalManufacturingModel.BuildElements(_prescription.GetSurfaces())
            .Select(element => new ElementChoice(element))
            .ToArray();
        _updating = true;
        _elementPicker.ItemsSource = choices;
        _elementPicker.SelectedItem = choices.FirstOrDefault(choice =>
            choice.Element.FrontSurface.Number == previousSurface) ?? choices.FirstOrDefault();
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

        _drawingNumber.Text = $"OPT-S{choice.Element.FrontSurface.Number:00}-S{choice.Element.BackSurface.Number:00}";
        _partName.Text = $"{choice.Element.Material} 光学元件";
        _coating.Text = CoatingText(choice.Element);
        UpdatePreview();
    }

    private static string CoatingText(OpticalElementDefinition element)
    {
        var coatings = new[]
        {
            CoatingLabel("S1", element.FrontSurface.Coating),
            CoatingLabel("S2", element.BackSurface.Coating)
        }.Where(value => value is not null);
        var text = string.Join("；", coatings!);
        return text.Length > 0 ? text : "按设计膜系；有效口径内均匀";

        static string? CoatingLabel(string surface, string? coating) =>
            string.IsNullOrWhiteSpace(coating)
            || coating.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? null
                : $"{surface} {coating}";
    }

    private void UpdatePreview()
    {
        try
        {
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
            _status.Text = $"预览失败：{exception.Message}";
        }
    }

    private async Task ExportPdfAsync()
    {
        try
        {
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
        _logoStatus.Text = "默认 S.T.A.R. LABS";
        UpdatePreview();
    }

    private OpticalDrawingSheet? CreateSheet()
    {
        if (_elementPicker.SelectedItem is not ElementChoice choice)
        {
            return null;
        }

        return new OpticalDrawingSheet(
            choice.Element,
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
            Value(_imperfection, "0.16 × 2"),
            Value(_coating, "按设计膜系"),
            Value(_edgeTreatment, "倒边 0.2 × 45°"),
            Value(_stress, "≤ 10 nm/cm"),
            Value(_bubbles, "0.1 × 2"),
            Value(_homogeneity, "2；2"),
            FindMaterial(choice.Element.Material),
            _companyLogoPng,
            (double)(_refractiveIndexTolerance.Value ?? 0.0005m),
            (double)(_abbeNumberTolerance.Value ?? 0.5m));
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

    private sealed record ElementChoice(OpticalElementDefinition Element);
}
