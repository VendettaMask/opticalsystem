using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed class NonSequentialObjectEditorPanel : UserControl, IDisposable, IDisplaySettingsAware
{
    private readonly INonSequentialDocumentService _service;
    private readonly IWorkspaceEventStream _events;
    private readonly DataGrid _grid;
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    private readonly ComboBox _addKind = new() { MinWidth = 145 };
    private readonly ComboBox _kind = new() { MinWidth = 150 };
    private readonly ComboBox _reference = new() { MinWidth = 165 };
    private readonly ComboBox _container = new() { MinWidth = 165 };
    private readonly CheckBox _visible = new() { Content = "可见" };
    private readonly StackPanel _parameters = new() { Spacing = 6 };
    private readonly Dictionary<string, Control> _fields = new(StringComparer.Ordinal);
    private NonSequentialObjectUpdateDto? _clipboard;
    private BsdfImportSides _bsdfImportSide = BsdfImportSides.Front;
    private BsdfImportOptions _bsdfImportOptions = new();
    private readonly Dictionary<string, string> _bsdfImportText = new()
    { ["bsdf-zemax-wave"] = "550", ["bsdf-zemax-ni"] = "1", ["bsdf-zemax-nt"] = "1", ["bsdf-zemax-xyz"] = "",
      ["bsdf-speos-angle"] = "10", ["bsdf-speos-height"] = "0", ["bsdf-speos-base"] = "550" };
    private bool _loading;
    private bool _disposed;

    public NonSequentialObjectEditorPanel(
        INonSequentialDocumentService service,
        IWorkspaceEventStream events)
    {
        _service = service;
        _events = events;
        _addKind.ItemsSource = service.GetObjectKinds();
        _addKind.SelectedIndex = 0;
        _kind.ItemsSource = service.GetObjectKinds();
        _grid = CreateGrid();

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,8,2*") };
        body.Children.Add(_grid);
        var splitter = new GridSplitter { Width = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);
        var properties = BuildProperties();
        Grid.SetColumn(properties, 2);
        body.Children.Add(properties);

        var root = new DockPanel();
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        var commands = BuildCommands();
        DockPanel.SetDock(commands, Avalonia.Controls.Dock.Top);
        root.Children.Add(commands);
        root.Children.Add(body);
        Content = root;

        _grid.SelectionChanged += (_, _) => LoadSelection();
        _events.Changed += OnWorkspaceChanged;
        Refresh(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _events.Changed -= OnWorkspaceChanged;
    }

    public void RefreshDisplaySettings() => Refresh();

    private Control BuildCommands()
    {
        var import = CommandButton("导入 STL");
        import.Click += async (_, _) => await ImportStlAsync();
        var add = CommandButton("添加");
        add.Click += (_, _) =>
        {
            if (_addKind.SelectedItem is NonSequentialObjectKind kind)
            {
                var selected = _grid.SelectedItem as ObjectRow;
                SelectAfter(() => _service.AddObject(kind, selected?.Number));
            }
        };
        var remove = CommandButton("删除");
        remove.Click += (_, _) => WithSelection(row => _service.DeleteObject(row.Id));
        var copy = CommandButton("复制");
        copy.Click += (_, _) => WithSelection(row => _clipboard = row.ToUpdate());
        var paste = CommandButton("粘贴");
        paste.Click += (_, _) =>
        {
            if (_clipboard is null) return;
            var index = (_grid.SelectedItem as ObjectRow)?.Number ?? _service.GetDocument().Objects.Count;
            SelectAfter(() => _service.PasteObject(_clipboard, index));
        };
        var up = CommandButton("上移");
        up.Click += (_, _) => WithSelection(row => _service.MoveObject(row.Id, row.Number - 2));
        var down = CommandButton("下移");
        down.Click += (_, _) => WithSelection(row => _service.MoveObject(row.Id, row.Number));
        var convert = CommandButton("从顺序配置转换");
        convert.Click += (_, _) =>
        {
            var result = _service.ConvertFromSequential();
            _summary.Text = $"已转换 {result.ObjectCount} 个对象。{string.Join(" ", result.Warnings)}";
        };
        var content = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(10, 6),
            Children = { _addKind, add, import, remove, copy, paste, up, down, convert, _summary }
        };
        var border = new Border { BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = content };
        border.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        border.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return border;
    }

    private DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            FrozenColumnCount = 3,
            RowHeight = UiDensity.CompactTableRowHeight,
            ColumnHeaderHeight = UiDensity.TableHeaderHeight
        };
        grid.BindThemeResource(DataGrid.RowBackgroundProperty, ThemeResourceBindings.Surface);
        grid.BindThemeResource(DataGrid.BorderBrushProperty, ThemeResourceBindings.Border);
        grid.Columns.Add(TextColumn("#", nameof(ObjectRow.Number), 44, true));
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "启用",
            Binding = TwoWay(nameof(ObjectRow.Enabled)),
            Width = new DataGridLength(56)
        });
        grid.Columns.Add(TextColumn("类型", nameof(ObjectRow.Kind), 128, true));
        grid.Columns.Add(TextColumn("名称", nameof(ObjectRow.Name), 145));
        grid.Columns.Add(TextColumn("角色", nameof(ObjectRow.Role), 72, true));
        grid.Columns.Add(TextColumn("X (mm)", nameof(ObjectRow.X), 82));
        grid.Columns.Add(TextColumn("Y (mm)", nameof(ObjectRow.Y), 82));
        grid.Columns.Add(TextColumn("Z (mm)", nameof(ObjectRow.Z), 82));
        grid.Columns.Add(TextColumn("倾斜 X", nameof(ObjectRow.TiltX), 82));
        grid.Columns.Add(TextColumn("倾斜 Y", nameof(ObjectRow.TiltY), 82));
        grid.Columns.Add(TextColumn("倾斜 Z", nameof(ObjectRow.TiltZ), 82));
        grid.Columns.Add(TextColumn("材料", nameof(ObjectRow.Material), 110, true));
        grid.Columns.Add(TextColumn("类型参数", nameof(ObjectRow.Summary), 250, true));
        grid.CellEditEnded += (_, args) =>
        {
            if (!_loading && args.EditAction == DataGridEditAction.Commit && args.Row.DataContext is ObjectRow row)
            {
                _service.UpdateObject(row.ToUpdate());
            }
        };
        return grid;
    }

    private Control BuildProperties()
    {
        var common = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Label("类型"), _kind,
                Label("参考"), _reference,
                Label("包含于"), _container,
                _visible
            }
        };
        var apply = CommandButton("应用对象属性");
        apply.Name = "nsc-apply";
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Click += (_, _) => ApplyProperties();
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 9,
            Children =
            {
                new TextBlock { Text = "对象属性", FontWeight = FontWeight.SemiBold, FontSize = DisplayTypography.CardTitle },
                common,
                new ScrollViewer
                {
                    Content = _parameters,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                },
                apply
            }
        };
        var border = new Border { BorderThickness = new Avalonia.Thickness(1, 0, 0, 0), Child = panel };
        border.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        border.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return border;
    }

    private void LoadSelection()
    {
        _loading = true;
        try
        {
            _fields.Clear();
            _parameters.Children.Clear();
            if (_grid.SelectedItem is not ObjectRow row) return;
            var rows = (_grid.ItemsSource as IEnumerable<ObjectRow> ?? Array.Empty<ObjectRow>()).ToArray();
            var choices = new[] { new ObjectChoice(null, "全局") }
                .Concat(rows.Where(item => item.Id != row.Id).Select(item => new ObjectChoice(item.Id, $"{item.Number}: {item.Name}")))
                .ToArray();
            _reference.ItemsSource = choices;
            _container.ItemsSource = choices;
            _reference.SelectedItem = choices.FirstOrDefault(item => item.Id == row.ReferenceId) ?? choices[0];
            _container.SelectedItem = choices.FirstOrDefault(item => item.Id == row.ContainerId) ?? choices[0];
            _kind.SelectedItem = row.Kind;
            _kind.IsEnabled = row.Kind != NonSequentialObjectKind.Mesh;
            _visible.IsChecked = row.Visible;
            AddParameterFields(row.Parameters);
            if (row.Kind is NonSequentialObjectKind.PlaneRectangle or NonSequentialObjectKind.Sphere
                or NonSequentialObjectKind.Cylinder or NonSequentialObjectKind.Box or NonSequentialObjectKind.Mesh)
            {
                Field("lambertian", "Lambertian 方向散射", row.SurfaceBsdf?.Kind == NonSequentialBsdfKind.LambertianReflection);
                Field("diffuseReflectance", "漫反射率 ρ (0–1)", row.SurfaceBsdf?.Kind == NonSequentialBsdfKind.LambertianReflection ? row.SurfaceBsdf.Reflectance : .5);
                var lambertian = (CheckBox)_fields["lambertian"]; var reflectance = _fields["diffuseReflectance"];
                void RefreshReflectanceAvailability() => ControlAvailability.Set(reflectance, lambertian.IsChecked == true,
                    "实测模型直接使用文件中的 BRDF/BTDF；此反射率只用于 Lambertian。");
                lambertian.PropertyChanged += (_, args) => { if (args.Property == CheckBox.IsCheckedProperty) RefreshReflectanceAvailability(); };
                RefreshReflectanceAvailability();
                _parameters.Children.Add(new TextBlock { Text = "不透明表面：ρ 反射，其余吸收；替代整个表面反射，不与 Fresnel 叠加。", TextWrapping = TextWrapping.Wrap });
                var sideChoices = new[] { new BsdfSideChoice("仅正面", BsdfImportSides.Front), new BsdfSideChoice("仅背面", BsdfImportSides.Back), new BsdfSideChoice("正反相同（显式声明）", BsdfImportSides.BothDeclaredIdentical) };
                var sideChoice = new ComboBox { Name = "nsc-bsdf-import-side", ItemsSource = sideChoices, SelectedItem = sideChoices.Single(c => c.Side == _bsdfImportSide), MinWidth = 200 };
                sideChoice.SelectionChanged += (_, _) => { if (sideChoice.SelectedItem is BsdfSideChoice choice) _bsdfImportSide = choice.Side; };
                _fields["bsdfImportSide"] = sideChoice;
                _parameters.Children.Add(new StackPanel { Children = { new TextBlock { Text = "商业文件入射侧（CSV 使用文件自身定义）" }, sideChoice } });
                Field("bsdf-zemax-wave", "Zemax 测量波长 (nm)", _bsdfImportText["bsdf-zemax-wave"]);
                Field("bsdf-zemax-radiometric", "Zemax 原数据为辐射度（不是光度 Y）", _bsdfImportOptions.ZemaxRadiometric);
                Field("bsdf-zemax-ni", "正面入射折射率（Zemax / 镜面增强）", _bsdfImportText["bsdf-zemax-ni"]);
                Field("bsdf-zemax-nt", "正面出射折射率（Zemax / 镜面增强）", _bsdfImportText["bsdf-zemax-nt"]);
                Field("bsdf-zemax-xyz", "XYZ 辐射度校准 JSON 路径", _bsdfImportText["bsdf-zemax-xyz"]);
                var calibration = new Button { Content = "选择 XYZ 校准文件", HorizontalAlignment = HorizontalAlignment.Left };
                calibration.Click += async (_, _) =>
                {
                    if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
                    var selected = await storage.OpenFilePickerAsync(new() { Title = "XYZ 辐射度校准", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
                    if (selected.Count > 0 && _fields.GetValueOrDefault("bsdf-zemax-xyz") is TextBox text) text.Text = _bsdfImportText["bsdf-zemax-xyz"] = selected[0].Path.LocalPath;
                };
                _parameters.Children.Add(calibration);
                Field("bsdf-speos-enhance", "Speos 显式镜面锥（覆盖文件设置）", _bsdfImportOptions.SpeosPeak is not null);
                Field("bsdf-speos-angle", "镜面锥半角 (°)", _bsdfImportText["bsdf-speos-angle"]);
                Field("bsdf-speos-height", "锥内背景高度 / 本切片峰值", _bsdfImportText["bsdf-speos-height"]);
                Field("bsdf-speos-white", "White Specular（需镜面锥）", _bsdfImportOptions.SpeosPeak?.WhiteReferenceNanometers is not null);
                Field("bsdf-speos-base", "White Specular 基准波长 (nm)", _bsdfImportText["bsdf-speos-base"]);
                _parameters.Children.Add(new TextBlock { Text = "XYZ 须用实测辐射度校准；镜面峰采用守恒的斜率配准，背景固定。校准与散射数据随工程和相机实验冻结。", TextWrapping = TextWrapping.Wrap });
                if (row.SurfaceBsdf?.MeasuredData?.Coordinates == 1)
                {
                    var axis = row.SurfaceBsdf.MaterialAxis ?? new();
                    Field("bsdf-axis-x", "局部材料轴 X", axis.X); Field("bsdf-axis-y", "局部材料轴 Y", axis.Y); Field("bsdf-axis-z", "局部材料轴 Z", axis.Z);
                }
                var import = CommandButton("导入 BSDF 文件"); import.Name = "nsc-bsdf-import";
                import.Click += async (_, _) => await PickMeasuredBsdfAsync();
                var clear = CommandButton("清除 BSDF"); clear.Name = "nsc-bsdf-clear";
                clear.Click += (_, _) => { _service.UpdateObject(row.ToUpdate() with { SurfaceBsdf = null }); Refresh(select: row.Id); };
                _parameters.Children.Add(new WrapPanel { Children = { import, clear } });
                _parameters.Children.Add(new TextBlock { Text = "BTDF 使用正反两侧数据与折射交互；测量分布包含全部反射/透射，不再叠加 Fresnel。", TextWrapping = TextWrapping.Wrap });
                if (row.SurfaceBsdf?.MeasuredData is { } data)
                {
                    _parameters.Children.Add(new TextBlock { Name = "nsc-bsdf-info", Text = $"{data.Name} · {data.Points.Count:N0}\n{data.SourceFileName} · {data.SourceFormat ?? "CSV v1"}\nSHA-256 {data.SourceSha256}", TextWrapping = TextWrapping.Wrap });
                    if (data.ImportNotes is { } notes) _parameters.Children.Add(new Expander { Header = UiLocalization.Text("文件导入说明"), Content = new TextBlock { Text = notes, TextWrapping = TextWrapping.Wrap }, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                }
            }
            _summary.Text = $"对象 {row.Number} · {row.Role} · {row.Summary}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void AddParameterFields(NonSequentialObjectParameters value)
    {
        switch (value)
        {
            case SourceRayParameters p:
                Source(p); Vector("origin", "局部起点", p.Origin); Vector("direction", "局部方向", p.Direction); break;
            case SourcePointParameters p:
                Source(p); Field("cone", "圆锥半角 (°)", p.ConeHalfAngleDegrees); break;
            case SourceRectangleParameters p:
                Source(p); Field("width", "宽度 (mm)", p.WidthMillimeters); Field("height", "高度 (mm)", p.HeightMillimeters);
                Field("cone", "兼容圆锥半角 (°)", p.AngularHalfAngleDegrees); SurfaceSourceDistribution(p); break;
            case SourceGaussianParameters p:
                Source(p); Field("waistX", "X 束腰 (mm)", p.WaistXMillimeters); Field("waistY", "Y 束腰 (mm)", p.WaistYMillimeters);
                Field("cone", "发散半角 (°)", p.DivergenceHalfAngleDegrees); break;
            case SourceEllipseParameters p:
                Source(p); Field("width", "宽度 (mm)", p.WidthMillimeters); Field("height", "高度 (mm)", p.HeightMillimeters);
                Field("cone", "兼容圆锥半角 (°)", p.AngularHalfAngleDegrees); SurfaceSourceDistribution(p); break;
            case SourceTwoAngleParameters p:
                Source(p); Field("width", "宽度 (mm)", p.WidthMillimeters); Field("height", "高度 (mm)", p.HeightMillimeters);
                Field("shape", "发光面形状", p.Shape); Field("angleX", "X 发散半角 (°)", p.AngularHalfAngleXDegrees);
                Field("angleY", "Y 发散半角 (°)", p.AngularHalfAngleYDegrees); break;
            case SourceRadialParameters p:
                Source(p); Field("samples", "角度:相对强度", string.Join("; ", p.Samples.Select(sample =>
                    $"{sample.AngleDegrees.ToString("G17", CultureInfo.InvariantCulture)}:{sample.RelativeIntensity.ToString("G17", CultureInfo.InvariantCulture)}"))); break;
            case SourceVolumeRectangleParameters p:
                Source(p); Field("width", "宽度 (mm)", p.WidthMillimeters); Field("height", "高度 (mm)", p.HeightMillimeters);
                Field("depth", "深度 (mm)", p.DepthMillimeters); Field("cone", "兼容圆锥半角 (°)", p.AngularHalfAngleDegrees);
                VolumeSourceDistribution(p.AngularDistribution); break;
            case SourceVolumeEllipseParameters p:
                Source(p); Field("semiX", "X 半轴 (mm)", p.SemiAxisXMillimeters); Field("semiY", "Y 半轴 (mm)", p.SemiAxisYMillimeters);
                Field("semiZ", "Z 半轴 (mm)", p.SemiAxisZMillimeters); Field("cone", "兼容圆锥半角 (°)", p.AngularHalfAngleDegrees);
                VolumeSourceDistribution(p.AngularDistribution); break;
            case SourceVolumeCylinderParameters p:
                Source(p); Field("radiusX", "X 半径 (mm)", p.RadiusXMillimeters); Field("radiusY", "Y 半径 (mm)", p.RadiusYMillimeters);
                Field("length", "轴向长度 (mm)", p.LengthMillimeters); Field("cone", "兼容圆锥半角 (°)", p.AngularHalfAngleDegrees);
                VolumeSourceDistribution(p.AngularDistribution); break;
            case PlaneRectangleParameters p:
                Field("width", "宽度 (mm)", p.WidthMillimeters); Field("height", "高度 (mm)", p.HeightMillimeters);
                Field("behavior", "交互", p.Behavior); Field("before", "前侧材料", p.MaterialBefore); Field("after", "后侧材料", p.MaterialAfter); break;
            case SphereParameters p:
                Field("radius", "半径 (mm)", p.RadiusMillimeters); Field("material", "材料", p.Material); Field("behavior", "交互", p.Behavior); break;
            case CylinderParameters p:
                Field("radius", "半径 (mm)", p.RadiusMillimeters); Field("length", "长度 (mm)", p.LengthMillimeters);
                Field("material", "材料", p.Material); Field("behavior", "交互", p.Behavior); break;
            case BoxParameters p:
                Field("width", "宽度 (mm)", p.WidthMillimeters); Field("height", "高度 (mm)", p.HeightMillimeters); Field("length", "长度 (mm)", p.LengthMillimeters);
                Field("material", "材料", p.Material); Field("behavior", "交互", p.Behavior); break;
            case StandardLensParameters p:
                Field("frontRadius", "前半径 (mm)", p.FrontRadiusMillimeters); Field("backRadius", "后半径 (mm)", p.BackRadiusMillimeters);
                Field("frontConic", "前圆锥系数", p.FrontConic); Field("backConic", "后圆锥系数", p.BackConic);
                Field("thickness", "中心厚度 (mm)", p.CenterThicknessMillimeters); Field("semiDiameter", "半口径 (mm)", p.SemiDiameterMillimeters);
                Field("material", "材料", p.Material); break;
            case MeshObjectParameters p:
                Field("behavior", "交互", p.Behavior); Field("material", "材料", p.Material); Field("twoSided", "双面相交", p.TwoSided);
                _parameters.Children.Add(new TextBlock
                {
                    Text = $"{p.OriginalFileName}\n{p.VertexCount} 顶点 · {p.TriangleCount} 三角形 · {(p.IsClosed ? "闭合" : "开放")}\nSHA-256 {p.Sha256}",
                    TextWrapping = TextWrapping.Wrap
                });
                break;
            case DetectorRectangleParameters p:
                Field("width", "宽度 (mm)", p.WidthMillimeters); Field("height", "高度 (mm)", p.HeightMillimeters);
                Field("pixelsX", "X 像素", p.PixelsX); Field("pixelsY", "Y 像素", p.PixelsY);
                Field("frontOnly", "仅接收正面", p.FrontOnly); Field("absorb", "吸收终止", p.Absorb); break;
        }
    }

    private void Source(SourceParameters p)
    {
        Field("power", "功率 (W)", p.PowerWatts);
        Field("wavelength", "波长编号", p.WavelengthNumber);
        Field("layout", "布局射线数", p.LayoutRayCount);
        Field("analysis", "分析射线数", p.AnalysisRayCount);
    }

    private void SurfaceSourceDistribution(SourceRectangleParameters p) => SurfaceSourceDistribution(
        p.AngularDistribution, p.SourceDistanceMillimeters, p.CosineExponent, p.GaussianX, p.GaussianY,
        p.SourceX, p.SourceY, p.MinimumXHalfWidthMillimeters, p.MinimumYHalfWidthMillimeters);

    private void SurfaceSourceDistribution(SourceEllipseParameters p) => SurfaceSourceDistribution(
        p.AngularDistribution, p.SourceDistanceMillimeters, p.CosineExponent, p.GaussianX, p.GaussianY,
        p.SourceX, p.SourceY, p.MinimumXHalfWidthMillimeters, p.MinimumYHalfWidthMillimeters);

    private void SurfaceSourceDistribution(
        NonSequentialSurfaceSourceAngularDistribution distribution,
        double sourceDistance,
        double cosineExponent,
        double gaussianX,
        double gaussianY,
        double sourceX,
        double sourceY,
        double minimumXHalfWidth,
        double minimumYHalfWidth)
    {
        Field("angularDistribution", "Zemax 方向分布", distribution);
        Field("sourceDistance", "虚拟点距离 (mm)", sourceDistance);
        Field("cosineExponent", "Cosine 指数", cosineExponent);
        Field("gaussianX", "Gaussian X 系数", gaussianX);
        Field("gaussianY", "Gaussian Y 系数", gaussianY);
        Field("sourceX", "Source X", sourceX);
        Field("sourceY", "Source Y", sourceY);
        Field("minimumXHalfWidth", "最小 X 半宽 (mm)", minimumXHalfWidth);
        Field("minimumYHalfWidth", "最小 Y 半宽 (mm)", minimumYHalfWidth);
    }

    private void VolumeSourceDistribution(NonSequentialVolumeSourceAngularDistribution distribution)
    {
        Field("angularDistribution", "Zemax 方向分布", distribution);
    }

    private void ApplyProperties()
    {
        if (_grid.SelectedItem is not ObjectRow row) return;
        try
        {
            var kind = _kind.SelectedItem is NonSequentialObjectKind selected ? selected : row.Kind;
            var parameters = kind == row.Kind ? ReadParameters(row.Parameters) : _service.GetDefaultParameters(kind);
            _service.UpdateObject(row.ToUpdate() with
            {
                Kind = kind,
                Visible = _visible.IsChecked == true,
                ReferenceObjectId = (_reference.SelectedItem as ObjectChoice)?.Id,
                ContainingObjectId = (_container.SelectedItem as ObjectChoice)?.Id,
                Parameters = parameters,
                SurfaceBsdf = _fields.ContainsKey("lambertian") && B("lambertian")
                    ? new(NonSequentialBsdfKind.LambertianReflection, D("diffuseReflectance"))
                    : row.SurfaceBsdf?.Kind == NonSequentialBsdfKind.Measured ? row.SurfaceBsdf with
                    { MaterialAxis = row.SurfaceBsdf.MeasuredData?.Coordinates == 1 ? new(D("bsdf-axis-x"), D("bsdf-axis-y"), D("bsdf-axis-z")) : null } : null
            });
            _summary.Text = $"对象 {row.Number} 的属性已更新。";
        }
        catch (Exception exception) when (
            exception is FormatException
            or InvalidDataException
            or ArgumentException
            or KeyNotFoundException)
        {
            _summary.Text = $"对象属性无效：{exception.Message}";
        }
    }

    private NonSequentialObjectParameters ReadParameters(NonSequentialObjectParameters value) => value switch
    {
        SourceRayParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), Origin = V("origin"), Direction = V("direction") },
        SourcePointParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), ConeHalfAngleDegrees = D("cone") },
        SourceRectangleParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), WidthMillimeters = D("width"), HeightMillimeters = D("height"), AngularHalfAngleDegrees = D("cone"), AngularDistribution = E<NonSequentialSurfaceSourceAngularDistribution>("angularDistribution"), SourceDistanceMillimeters = D("sourceDistance"), CosineExponent = D("cosineExponent"), GaussianX = D("gaussianX"), GaussianY = D("gaussianY"), SourceX = D("sourceX"), SourceY = D("sourceY"), MinimumXHalfWidthMillimeters = D("minimumXHalfWidth"), MinimumYHalfWidthMillimeters = D("minimumYHalfWidth") },
        SourceGaussianParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), WaistXMillimeters = D("waistX"), WaistYMillimeters = D("waistY"), DivergenceHalfAngleDegrees = D("cone") },
        SourceEllipseParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), WidthMillimeters = D("width"), HeightMillimeters = D("height"), AngularHalfAngleDegrees = D("cone"), AngularDistribution = E<NonSequentialSurfaceSourceAngularDistribution>("angularDistribution"), SourceDistanceMillimeters = D("sourceDistance"), CosineExponent = D("cosineExponent"), GaussianX = D("gaussianX"), GaussianY = D("gaussianY"), SourceX = D("sourceX"), SourceY = D("sourceY"), MinimumXHalfWidthMillimeters = D("minimumXHalfWidth"), MinimumYHalfWidthMillimeters = D("minimumYHalfWidth") },
        SourceTwoAngleParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), WidthMillimeters = D("width"), HeightMillimeters = D("height"), Shape = E<NonSequentialSourceApertureShape>("shape"), AngularHalfAngleXDegrees = D("angleX"), AngularHalfAngleYDegrees = D("angleY") },
        SourceRadialParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), Samples = RadialSamples("samples") },
        SourceVolumeRectangleParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), WidthMillimeters = D("width"), HeightMillimeters = D("height"), DepthMillimeters = D("depth"), AngularHalfAngleDegrees = D("cone"), AngularDistribution = E<NonSequentialVolumeSourceAngularDistribution>("angularDistribution") },
        SourceVolumeEllipseParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), SemiAxisXMillimeters = D("semiX"), SemiAxisYMillimeters = D("semiY"), SemiAxisZMillimeters = D("semiZ"), AngularHalfAngleDegrees = D("cone"), AngularDistribution = E<NonSequentialVolumeSourceAngularDistribution>("angularDistribution") },
        SourceVolumeCylinderParameters p => p with { PowerWatts = D("power"), WavelengthNumber = I("wavelength"), LayoutRayCount = I("layout"), AnalysisRayCount = I("analysis"), RadiusXMillimeters = D("radiusX"), RadiusYMillimeters = D("radiusY"), LengthMillimeters = D("length"), AngularHalfAngleDegrees = D("cone"), AngularDistribution = E<NonSequentialVolumeSourceAngularDistribution>("angularDistribution") },
        PlaneRectangleParameters p => p with { WidthMillimeters = D("width"), HeightMillimeters = D("height"), Behavior = E<NonSequentialSurfaceBehavior>("behavior"), MaterialBefore = S("before"), MaterialAfter = S("after") },
        SphereParameters p => p with { RadiusMillimeters = D("radius"), Material = S("material"), Behavior = E<NonSequentialSurfaceBehavior>("behavior") },
        CylinderParameters p => p with { RadiusMillimeters = D("radius"), LengthMillimeters = D("length"), Material = S("material"), Behavior = E<NonSequentialSurfaceBehavior>("behavior") },
        BoxParameters p => p with { WidthMillimeters = D("width"), HeightMillimeters = D("height"), LengthMillimeters = D("length"), Material = S("material"), Behavior = E<NonSequentialSurfaceBehavior>("behavior") },
        StandardLensParameters p => p with { FrontRadiusMillimeters = D("frontRadius"), BackRadiusMillimeters = D("backRadius"), FrontConic = D("frontConic"), BackConic = D("backConic"), CenterThicknessMillimeters = D("thickness"), SemiDiameterMillimeters = D("semiDiameter"), Material = S("material") },
        MeshObjectParameters p => p with { Behavior = E<NonSequentialSurfaceBehavior>("behavior"), Material = S("material"), TwoSided = B("twoSided") },
        DetectorRectangleParameters p => p with { WidthMillimeters = D("width"), HeightMillimeters = D("height"), PixelsX = I("pixelsX"), PixelsY = I("pixelsY"), FrontOnly = B("frontOnly"), Absorb = B("absorb") },
        _ => throw new InvalidOperationException("未知非序列对象参数。")
    };

    private void Refresh(bool preserveSelection = true, Guid? select = null)
    {
        if (_disposed) return;
        _loading = true;
        try
        {
            var selected = select ?? (preserveSelection ? (_grid.SelectedItem as ObjectRow)?.Id : null);
            var document = _service.GetDocument();
            var rows = document.Objects.Select(item => new ObjectRow(item)).ToArray();
            _grid.ItemsSource = rows;
            _grid.SelectedItem = rows.FirstOrDefault(item => item.Id == selected) ?? rows.FirstOrDefault();
            _summary.Text = $"{document.Name} · {rows.Length} 个对象 · {document.Wavelengths.Count} 个波长";
        }
        finally { _loading = false; }
        LoadSelection();
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() => Refresh(!args.FileSwitched));

    private async Task ImportStlAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入非序列 STL 网格",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("STL 网格") { Patterns = new[] { "*.stl" } } }
        });
        if (files.Count == 0) return;
        var options = await new NonSequentialStlImportWindow(_service.GetMaterialNames())
            .ShowDialog<NonSequentialMeshImportOptionsDto?>(owner);
        if (options is null) return;
        try
        {
            _summary.Text = "正在解析并验证 STL 网格…";
            var result = await _service.ImportStlAsync(files[0].Path.LocalPath, options);
            Refresh(select: result.ObjectId);
            _summary.Text = $"已导入 {result.Name}：{result.VertexCount} 顶点，{result.TriangleCount} 三角形。{string.Join(" ", result.Warnings)}";
        }
        catch (Exception exception)
        {
            _summary.Text = $"STL 导入失败：{exception.Message}";
        }
    }

    private void SelectAfter(Func<Guid> operation) { var id = operation(); Refresh(select: id); }
    private void WithSelection(Action<ObjectRow> action) { if (_grid.SelectedItem is ObjectRow row) action(row); }
    private void Vector(string key, string label, NonSequentialVector3 value) { Field(key + "X", label + " X", value.X); Field(key + "Y", label + " Y", value.Y); Field(key + "Z", label + " Z", value.Z); }

    private void Field(string key, string label, object value)
    {
        Control editor;
        if (value is bool boolean)
        {
            editor = new CheckBox { IsChecked = boolean };
        }
        else if (value.GetType().IsEnum)
        {
            var values = Enum.GetValues(value.GetType()).Cast<object>().ToArray();
            editor = new ComboBox { ItemsSource = values, SelectedItem = value, MinWidth = 160 };
        }
        else if (key is "material" or "before" or "after")
        {
            var selected = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            var materials = _service.GetMaterialNames()
                .Append(selected)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            editor = new ComboBox
            {
                ItemsSource = materials,
                SelectedItem = materials.FirstOrDefault(item => item.Equals(selected, StringComparison.OrdinalIgnoreCase)),
                MinWidth = 160
            };
        }
        else
        {
            editor = new TextBox { Text = Convert.ToString(value, CultureInfo.InvariantCulture), MinWidth = 120 };
        }
        if (key == "lambertian") editor.Name = "nsc-lambertian";
        if (key == "diffuseReflectance") editor.Name = "nsc-diffuse-reflectance";
        if (key.StartsWith("bsdf-", StringComparison.Ordinal)) editor.Name = "nsc-" + key;
        _fields[key] = editor;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("145,*") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = key.StartsWith("bsdf-", StringComparison.Ordinal) ? TextWrapping.Wrap : TextWrapping.NoWrap });
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        _parameters.Children.Add(row);
    }

    private async Task PickMeasuredBsdfAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入 BSDF 文件", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("BSDF") { Patterns = ["*.csv", "*.brdf", "*.anisotropicbsdf", "*.bsdf", "*.BSDF", "*.txt"] }]
        });
        if (files.Count > 0) await ImportMeasuredBsdfFromPathAsync(files[0].Path.LocalPath);
    }

    internal async Task<bool> ImportMeasuredBsdfFromPathAsync(string path)
    {
        if (_grid.SelectedItem is not ObjectRow row) return false;
        try
        {
            var side = (_fields.GetValueOrDefault("bsdfImportSide") as ComboBox)?.SelectedItem as BsdfSideChoice;
            foreach (var key in _bsdfImportText.Keys.ToArray()) _bsdfImportText[key] = S(key);
            double Option(string key) => double.TryParse(_bsdfImportText[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN;
            if (B("bsdf-speos-white") && !B("bsdf-speos-enhance")) throw new InvalidDataException("显式 White Specular 需要启用镜面锥；文件自带设置会自动读取。");
            _bsdfImportOptions = new(side?.Side ?? BsdfImportSides.Front, Option("bsdf-zemax-wave"), B("bsdf-zemax-radiometric"), Option("bsdf-zemax-ni"), Option("bsdf-zemax-nt"),
                string.IsNullOrWhiteSpace(_bsdfImportText["bsdf-zemax-xyz"]) ? null : _bsdfImportText["bsdf-zemax-xyz"],
                B("bsdf-speos-enhance") ? new(Option("bsdf-speos-angle"), Option("bsdf-speos-height"), B("bsdf-speos-white") ? Option("bsdf-speos-base") : null) : null);
            var data = await _service.ImportMeasuredBsdfAsync(row.Id, path, options: _bsdfImportOptions);
            if (_disposed) return false;
            Refresh(select: row.Id); _summary.Text = $"BSDF：{data.MeasuredData!.Name} · {data.MeasuredData.Points.Count:N0}"; return true;
        }
        catch (Exception exception) { if (!_disposed) _summary.Text = $"BSDF：{exception.Message}"; return false; }
    }

    private sealed record BsdfSideChoice(string Label, BsdfImportSides Side) { public override string ToString() => UiLocalization.Text(Label); }

    private string S(string key)
    {
        if (!_fields.TryGetValue(key, out var value)) throw new KeyNotFoundException(key);
        return value switch
        {
            TextBox text => text.Text?.Trim() ?? string.Empty,
            CheckBox check => (check.IsChecked == true).ToString(CultureInfo.InvariantCulture),
            ComboBox choice => Convert.ToString(choice.SelectedItem, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
            _ => throw new InvalidOperationException($"参数“{key}”没有可读取的编辑器。")
        };
    }
    private double D(string key) => double.TryParse(S(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException($"参数“{key}”不是有效数值。");
    private int I(string key) => int.TryParse(S(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException($"参数“{key}”不是有效整数。");
    private bool B(string key) => bool.TryParse(S(key), out var value) ? value : throw new FormatException($"参数“{key}”不是有效布尔值。");
    private T E<T>(string key) where T : struct, Enum => Enum.TryParse<T>(S(key), true, out var value) ? value : throw new FormatException($"参数“{key}”不是有效类型。");
    private NonSequentialVector3 V(string key) => new(D(key + "X"), D(key + "Y"), D(key + "Z"));
    private IReadOnlyList<SourceRadialSample> RadialSamples(string key) => S(key)
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(entry =>
        {
            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var angle)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var intensity))
            {
                throw new FormatException("径向分布必须使用“角度:相对强度; …”格式。");
            }
            return new SourceRadialSample(angle, intensity);
        }).ToArray();

    private static Button CommandButton(string text) => new() { Content = text, Margin = new Avalonia.Thickness(3, 0), MinHeight = 30 };
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Avalonia.Thickness(5), VerticalAlignment = VerticalAlignment.Center };
    private static Binding TwoWay(string property) => new(property) { Mode = BindingMode.TwoWay };
    private static DataGridTextColumn TextColumn(string header, string property, double width, bool readOnly = false) => new()
    {
        Header = header,
        Binding = new Binding(property) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay },
        IsReadOnly = readOnly,
        Width = new DataGridLength(width)
    };

    private sealed record ObjectChoice(Guid? Id, string Label) { public override string ToString() => Label; }

    private sealed class ObjectRow
    {
        private readonly NonSequentialObjectRowDto _dto;
        public ObjectRow(NonSequentialObjectRowDto dto)
        {
            _dto = dto; Id = dto.Id; Number = dto.ObjectNumber; Enabled = dto.Enabled; Visible = dto.Visible; Kind = dto.Kind;
            Name = dto.Name; Role = dto.Role; ReferenceId = dto.ReferenceObjectId; ContainerId = dto.ContainingObjectId;
            X = dto.X; Y = dto.Y; Z = dto.Z; TiltX = dto.TiltXDegrees; TiltY = dto.TiltYDegrees; TiltZ = dto.TiltZDegrees;
            Material = dto.Material; Parameters = dto.Parameters; Summary = dto.ParameterSummary;
        }
        public Guid Id { get; }
        public int Number { get; }
        public bool Enabled { get; set; }
        public bool Visible { get; set; }
        public NonSequentialObjectKind Kind { get; set; }
        public string Name { get; set; }
        public string Role { get; }
        public Guid? ReferenceId { get; set; }
        public Guid? ContainerId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double TiltX { get; set; }
        public double TiltY { get; set; }
        public double TiltZ { get; set; }
        public string Material { get; }
        public NonSequentialObjectParameters Parameters { get; set; }
        public NonSequentialSurfaceBsdf? SurfaceBsdf => _dto.SurfaceBsdf;
        public string Summary { get; }
        public NonSequentialObjectUpdateDto ToUpdate() => new(Id, Enabled, Visible, Kind, Name, ReferenceId, ContainerId, X, Y, Z, TiltX, TiltY, TiltZ, Parameters, SurfaceBsdf);
    }
}

public sealed class NonSequentialModePanel : UserControl, IDisposable
{
    private readonly INonSequentialDocumentService _service;
    private readonly IWorkspaceEventStream _events;
    private readonly TextBlock _count = new();
    private bool _disposed;

    public NonSequentialModePanel(INonSequentialDocumentService service, IWorkspaceEventStream events)
    {
        _service = service;
        _events = events;
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "非序列模式", FontSize = DisplayTypography.SectionTitle, FontWeight = FontWeight.SemiBold },
                _count,
                new TextBlock { Text = "场景具有独立对象、光源、探测器和波长；切换模式不会修改顺序处方。", TextWrapping = TextWrapping.Wrap }
            }
        };
        _events.Changed += Changed;
        Refresh();
    }

    public void Dispose() { if (_disposed) return; _disposed = true; _events.Changed -= Changed; }
    private void Changed(object? sender, WorkspaceChangedEventArgs args) => Dispatcher.UIThread.Post(Refresh);
    private void Refresh()
    {
        if (_disposed) return;
        var document = _service.GetDocument();
        _count.Text = $"场景对象：{document.Objects.Count}\n独立波长：{document.Wavelengths.Count}";
    }
}
