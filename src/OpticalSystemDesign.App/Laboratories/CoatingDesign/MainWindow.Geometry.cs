using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private CoatingStackAnalysisView? _stackAnalysisView;
    private readonly CheckBox _includeBackSurface = new() { Content = "计入基板与背面（非相干）" };
    private ComboBox _stackIncident = null!, _stackSubstrate = null!, _stackExit = null!;
    private AutoCompleteBox _exit = null!;

    private static Experiment WorkspaceExample(Experiment d) => d with
    { StackGeometry = new(d.ReferenceExample?.ExitMediumId ?? d.IncidentId, d.ReferenceExample?.SubstrateThicknessMm ?? 1) };

    private Control BuildStackGeometryPanel()
    {
        ComboBox Mirror(AutoCompleteBox source, string name)
        {
            var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(picker, name);
            picker.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(AutoCompleteBox.Text)) { Source = source, Mode = BindingMode.TwoWay });
            return picker;
        }
        _stackIncident = Mirror(_incident, "堆栈入射介质"); _stackSubstrate = Mirror(_substrate, "堆栈基板");
        _exit = MaterialPicker(); Watch(_exit, AutoCompleteBox.TextProperty);
        _stackExit = Mirror(_exit, "出射介质");
        var media = new WrapPanel { Orientation = Orientation.Horizontal };
        void Medium(string title, Control picker)
        {
            media.Children.Add(new StackPanel
            {
                Width = 160,
                Spacing = 4,
                Margin = new Thickness(0, 0, 8, 6),
                Children = { new TextBlock { Text = title }, picker }
            });
        }
        Medium("入射介质", _stackIncident); Medium("基板", _stackSubstrate); Medium("出射介质", _stackExit);
        var reference = BuildReferenceInput();
        var settings = new StackPanel { Spacing = 7, Children = { media, Number("substrateThickness", "基板厚度 (mm)"), Field("参考波长 (nm)", reference), _includeBackSurface } };
        AutomationProperties.SetName(_includeBackSurface, "计入基板与背面");
        _includeBackSurface.IsCheckedChanged += (_, _) => Changed();
        ToolTip.SetTip(_includeBackSurface, "整块基板光谱包含基板吸收、裸背面及内部多次反射；设计指标仍按前表面验收。");
        return new Expander { Header = "介质与基板设置", Name = "CoatingGeometrySettings", Margin = new Thickness(8, 0, 8, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = settings };
    }

    private MaterialSnapshot SelectMaterial(AutoCompleteBox box)
    {
        var text = box.Text?.Trim() ?? ""; var current = _session.Current;
        var exact = current.Materials.FirstOrDefault(m => m.Id == text);
        if (exact is not null) return exact;
        var named = current.Materials.Where(m => m.Name == text).ToArray();
        if (named.Length > 1) throw new ArgumentException("材料名称重复，请选择唯一材料 ID。");
        return named.SingleOrDefault() ?? _materials.Select(text);
    }

    private void RefreshStackGeometry()
    {
        if (_stackAnalysisView is null) return;
        try
        {
            var layers = FrontEditorRows.Select(r => r.ToLayer()).ToArray();
            var backLayers = _duplexEnabled.IsChecked != true ? [] : _symmetricFilms.IsChecked == true ? layers : BackEditorRows.Select(r => r.ToLayer()).ToArray();
            var incident = SelectMaterial(_incident); var substrate = SelectMaterial(_substrate); var exit = SelectMaterial(_exit);
            var thickness = Value("substrateThickness"); var wavelength = Value("reference"); var angle = Value("angle");
            if (thickness <= 0 || wavelength <= 0 || angle is < 0 or >= 90 || layers.Length > 200
                || layers.Any(l => !double.IsFinite(l.ThicknessNm) || l.ThicknessNm <= 0))
                throw new ArgumentException("请填写有效的膜厚、基板厚度、波长和入射角。");
            var films = layers.Select((layer, i) =>
            {
                var material = _session.Current.Materials.FirstOrDefault(m => m.Id == layer.MaterialId) ?? _materials.Select(layer.MaterialId);
                var color = (MaterialBrushFor(layer.MaterialId) as ISolidColorBrush)?.Color ?? Colors.SteelBlue;
                return new CoatingStackLayer(i + 1, layer.MaterialId, material.Name, layer.ThicknessNm, color);
            }).ToArray();
            var rear = backLayers.Select((layer, i) =>
            {
                var material = _session.Current.Materials.FirstOrDefault(m => m.Id == layer.MaterialId) ?? _materials.Select(layer.MaterialId);
                var color = (MaterialBrushFor(layer.MaterialId) as ISolidColorBrush)?.Color ?? Colors.SteelBlue;
                return new CoatingStackLayer(layers.Length + backLayers.Length - i, layer.MaterialId, material.Name, layer.ThicknessNm, color)
                { Side = "背面", FaceNumber = i + 1 };
            }).ToArray();
            _stackAnalysisView.SetSnapshot(new(_name.Text ?? "", incident.Name, substrate.Name, exit.Name,
                thickness, wavelength, angle, Array.AsReadOnly(films), _uiRevision) { BackLayers = Array.AsReadOnly(rear) });
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or KeyNotFoundException)
        { _stackAnalysisView.ShowUnavailable(error.Message); }
    }

    private Task ShowStackGeometryAsync()
    {
        _stackAnalysisView ??= new CoatingStackAnalysisView { FontFamily = FontFamily, FontSize = FontSize };
        RefreshStackGeometry();
        _workspace.Open(CoatingDockWorkspace.StackId, "堆栈几何", _stackAnalysisView);
        return Task.CompletedTask;
    }
}
