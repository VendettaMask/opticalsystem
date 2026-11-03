using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

internal sealed record LayerEditState(FilmLayer Original, string OriginalText, string PhysicalText, string? InvalidUnit, string? InvalidText);

public sealed class LayerRow : INotifyPropertyChanged
{
    private readonly Func<string, string> _displayName;
    private readonly Func<string, IBrush> _materialBrush;
    private string _materialId, _thickness, _originalText;
    private double _originalThickness;
    private bool _variable;
    private double? _index;
    private double _wavelength;
    private string? _invalidUnit, _invalidText;
    private int _number;
    public int Number { get => _number; set { _number = value; Notify(); } }
    public double? OpticalThicknessNm { get; private set; }
    public string OpticalThicknessHelp { get; private set; } = "请设置有效的材料和参考波长。";
    public string OpticalThicknessText { get => UnitText("OT", OpticalThicknessNm); set => EditOptical("OT", value); }
    public string QuarterWaveText { get => UnitText("QW", OpticalThicknessNm is { } ot && _wavelength > 0 ? 4 * ot / _wavelength : null); set => EditOptical("QW", value); }
    public string FullWaveText { get => UnitText("FW", OpticalThicknessNm is { } ot && _wavelength > 0 ? ot / _wavelength : null); set => EditOptical("FW", value); }
    public LayerRow(FilmLayer layer, Func<string, string>? displayName = null, Func<string, IBrush>? materialBrush = null)
    {
        _displayName = displayName ?? (id => id); _materialBrush = materialBrush ?? (_ => Brushes.Transparent); _materialId = layer.MaterialId;
        _thickness = _originalText = NumericDisplayFormatter.Format(layer.ThicknessNm); _originalThickness = layer.ThicknessNm; _variable = layer.Variable;
    }
    public string MaterialName => _displayName(_materialId);
    public string MaterialLabel => SplitMaterialName().Label;
    public string MaterialAnnotation => SplitMaterialName().Annotation;
    public IBrush MaterialBrush => _materialBrush(_materialId);
    public string MaterialId
    {
        get => _materialId;
        set
        {
            if (_materialId == value) return;
            _materialId = value; _index = null;
            Notify(); Notify(nameof(MaterialName)); Notify(nameof(MaterialLabel)); Notify(nameof(MaterialAnnotation)); Notify(nameof(MaterialBrush));
            InputChanged?.Invoke(nameof(MaterialId));
        }
    }
    private (string Label, string Annotation) SplitMaterialName()
    {
        var name = MaterialName;
        var open = name.EndsWith('）') ? name.LastIndexOf('（') : name.EndsWith(')') ? name.LastIndexOf('(') : -1;
        return open > 0 && open < name.Length - 2 ? (name[..open].TrimEnd(), name[(open + 1)..^1]) : (name, string.Empty);
    }
    public string ThicknessText
    {
        get => _thickness;
        set { if (_thickness == value && _invalidUnit is null) return; _thickness = value; _invalidUnit = _invalidText = null; Notify(); InputChanged?.Invoke(nameof(ThicknessText)); }
    }
    public bool Variable { get => _variable; set { if (_variable == value) return; _variable = value; Notify(); Notify(nameof(Locked)); InputChanged?.Invoke(nameof(Variable)); } }
    public bool Locked { get => !Variable; set => Variable = !value; }
    internal void RefreshMaterialIdentity() { Notify(nameof(MaterialId)); Notify(nameof(MaterialName)); Notify(nameof(MaterialBrush)); }
    public FilmLayer ToLayer()
    {
        if (_invalidUnit is not null) throw new ArgumentException($"第 {Number} 层 {_invalidUnit} 输入无效：{_invalidText}。");
        var value = ThicknessText == _originalText ? _originalThickness : ParsePositive(ThicknessText);
        if (value > 100000) throw new ArgumentException($"第 {Number} 层物理厚度不能超过100000 nm。");
        return new(MaterialId, value, Variable);
    }
    private double ParsePositive(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value) || value <= 0)
            throw new ArgumentException($"第 {Number} 层厚度请输入大于零的有限数值。");
        return value;
    }
    private string UnitText(string unit, double? value) => _invalidUnit == unit ? _invalidText ?? "" : value is { } v ? NumericDisplayFormatter.Format(v) : "—";
    private void EditOptical(string unit, string text)
    {
        if (text == (unit == "OT" ? OpticalThicknessText : unit == "QW" ? QuarterWaveText : FullWaveText)) return;
        try
        {
            if (_index is not { } n || !double.IsFinite(n) || n <= 0 || !double.IsFinite(_wavelength) || _wavelength <= 0)
                throw new ArgumentException("参考波长处没有可用折射率。");
            var value = ParsePositive(text);
            var physical = PhysicalFor(value, unit);
            SetPhysical(physical);
        }
        catch (ArgumentException) { _invalidUnit = unit; _invalidText = text; }
        NotifyUnits(); InputChanged?.Invoke(unit);
    }
    internal void SetPhysical(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || value > 100000) throw new ArgumentException("物理厚度必须大于零且不超过100000 nm。");
        _originalThickness = value; _thickness = _originalText = NumericDisplayFormatter.Format(value); _invalidUnit = _invalidText = null;
        Notify(nameof(ThicknessText));
    }
    internal double PhysicalFor(double value, string unit)
    {
        if (unit == "nm") return value;
        if (_index is not { } n || n <= 0 || !double.IsFinite(n) || !double.IsFinite(_wavelength) || _wavelength <= 0)
            throw new ArgumentException($"第 {Number} 层参考波长处没有有效折射率，不能换算{unit}。");
        return value / n * (unit == "OT" ? 1 : unit == "QW" ? _wavelength / 4 : _wavelength);
    }
    internal LayerEditState Capture() => new(new(MaterialId, _originalThickness, Variable), _originalText, _thickness, _invalidUnit, _invalidText);
    internal void Restore(LayerEditState state)
    { _originalText = state.OriginalText; _thickness = state.PhysicalText; _invalidUnit = state.InvalidUnit; _invalidText = state.InvalidText; }
    internal void UpdateOpticalThickness(double? index, double wavelength, string? notice)
    {
        _index = index; _wavelength = wavelength;
        double? optical = null; var help = notice ?? "请设置有效的材料和参考波长。";
        if (index is { } n)
        {
            try
            {
                var value = n * ToLayer().ThicknessNm;
                if (!double.IsFinite(value)) throw new ArgumentException("光学厚度超出有限数值范围。");
                optical = value; help = $"OT = n(λ₀) × d；QW = 4OT/λ₀；FW = OT/λ₀；λ₀ = {wavelength:G9} nm，n = {n:G9}。" + (notice is null ? "" : "\n" + notice);
            }
            catch (ArgumentException error) { help = error.Message; }
        }
        OpticalThicknessNm = optical; OpticalThicknessHelp = help; NotifyUnits();
    }
    private void NotifyUnits()
    { Notify(nameof(OpticalThicknessNm)); Notify(nameof(OpticalThicknessText)); Notify(nameof(QuarterWaveText)); Notify(nameof(FullWaveText)); Notify(nameof(OpticalThicknessHelp)); }
    internal event Action<string>? InputChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
