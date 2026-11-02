using System.Globalization;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record ParsedCoatingFormula(string Formula, double ReferenceWavelengthNm, FilmLayer[] Layers)
{
    public double TotalThicknessNm => Layers.Sum(l => l.ThicknessNm);
}

/// <summary>A bounded symbolic stack grammar, never executable code. Every coefficient is QW at the explicit reference wavelength.</summary>
public static class CoatingStackFormula
{
    public static ParsedCoatingFormula Parse(Experiment experiment, bool back, string formula, IReadOnlyDictionary<string, string>? aliases = null)
    {
        experiment.Validate();
        if (string.IsNullOrWhiteSpace(formula) || formula.Length > 8192) throw new ArgumentException("膜系公式不能为空，且最多8192字符。");
        if (back && (experiment.Duplex is null || experiment.Duplex.Symmetric)) throw new ArgumentException("请先选择可独立编辑的背面。");
        var symbols = new Dictionary<string, string>(StringComparer.Ordinal) { ["H"] = experiment.HighId, ["L"] = experiment.LowId };
        if (aliases?.Count > 64) throw new ArgumentException("公式最多允许64个自定义符号。");
        foreach (var pair in aliases ?? new Dictionary<string, string>())
        {
            if (!Regex.IsMatch(pair.Key, "^[A-Za-z_][A-Za-z0-9_]{0,31}$", RegexOptions.CultureInvariant)) throw new ArgumentException("公式符号仅允许字母、数字和下划线，并以字母或下划线开头。");
            symbols[pair.Key] = Resolve(experiment, pair.Value).Id;
        }
        var pieces = formula.Split('|'); var body = formula;
        if (pieces.Length != 1)
        {
            if (pieces.Length != 3) throw new ArgumentException("边界格式应为 Inc | 膜层公式 | Sub，不会隐式修改介质。");
            var outside = back ? experiment.StackGeometry!.ExitMediumId : experiment.IncidentId;
            var left = pieces[0].Trim(); var right = pieces[2].Trim();
            if ((left != "Inc" && Resolve(experiment, left).Id != outside) || (right != "Sub" && Resolve(experiment, right).Id != experiment.SubstrateId))
                throw new ArgumentException("公式边界与当前面的外侧介质/基板不符；请显式修改介质设置。");
            body = pieces[1];
        }
        var parser = new Parser(body, symbols, experiment);
        var terms = parser.Read(); var wavelength = experiment.EffectiveReferenceWavelengthNm; var registry = new MaterialRegistry();
        var layers = terms.Select(term =>
        {
            var material = Resolve(experiment, term.Id);
            if (!material.HoldTabulatedEndpoints && (wavelength < material.MinimumNm || wavelength > material.MaximumNm))
                throw new ArgumentException($"参考波长超出 {material.Name} 的有效范围，不能构造QW膜厚。");
            var n = material.Resolve(registry).RefractiveIndex(wavelength);
            var thickness = term.Weight * wavelength / (4 * n);
            if (!double.IsFinite(n) || n <= 0 || !double.IsFinite(thickness) || thickness <= 0 || thickness > 100000)
                throw new ArgumentException("公式膜厚无效，要求n为正有限值、物理厚度大于零且不超过100000 nm。");
            return new FilmLayer(material.Id, thickness);
        }).ToArray();
        return new(formula, wavelength, layers);
    }
    internal static MaterialSnapshot Resolve(Experiment d, string text)
    {
        text = text.Trim(); var exact = d.Materials.FirstOrDefault(m => m.Id == text);
        if (exact is not null) return exact;
        var names = d.Materials.Where(m => m.Name == text).ToArray();
        return names.Length == 1 ? names[0] : throw new ArgumentException($"无法唯一识别材料“{text}”，请使用材料ID。");
    }
    private sealed class Parser(string text, Dictionary<string, string> aliases, Experiment experiment)
    {
        private int _position;
        private readonly string[] _symbols = aliases.Keys.OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).ToArray();
        private void Skip() { while (_position < text.Length && (char.IsWhiteSpace(text[_position]) || text[_position] == ',')) _position++; }
        private ArgumentException Error(string detail) => new($"公式第{_position + 1}个字符：{detail}");
        internal List<(string Id, double Weight)> Read()
        {
            var result = Sequence(0); Skip(); if (_position != text.Length) throw Error("多余的括号或无效符号。");
            if (result.Count == 0) throw Error("没有膜层。"); return result;
        }
        private List<(string Id, double Weight)> Sequence(int depth)
        {
            if (depth > 16) throw Error("括号嵌套最多16层。");
            var result = new List<(string, double)>();
            while (true)
            {
                Skip(); if (_position >= text.Length || text[_position] == ')') break;
                var weight = 1.0;
                if (char.IsDigit(text[_position]) || text[_position] == '.')
                {
                    var match = Regex.Match(text[_position..], @"^(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?", RegexOptions.CultureInvariant);
                    if (!match.Success || !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out weight) || !double.IsFinite(weight) || weight <= 0)
                        throw Error("QW系数须为正有限数值。");
                    _position += match.Length; Skip();
                }
                List<(string Id, double Weight)> item;
                if (_position < text.Length && text[_position] == '(')
                {
                    _position++; item = Sequence(depth + 1); Skip();
                    if (_position >= text.Length || text[_position] != ')') throw Error("缺少右括号。");
                    _position++; if (item.Count == 0) throw Error("不允许空括号。");
                }
                else
                {
                    string id;
                    if (_position < text.Length && text[_position] == '{')
                    {
                        var end = text.IndexOf('}', _position + 1); if (end < 0) throw Error("缺少材料ID的右花括号。");
                        id = Resolve(experiment, text[(_position + 1)..end]).Id; _position = end + 1;
                    }
                    else
                    {
                        var symbol = _symbols.FirstOrDefault(s => text.AsSpan(_position).StartsWith(s, StringComparison.Ordinal));
                        if (symbol is null) throw Error("未知材料符号；可使用H/L、自定义符号或{材料ID}。");
                        id = aliases[symbol]; _position += symbol.Length;
                    }
                    item = [(id, 1)];
                }
                Skip(); var repeats = 1;
                if (_position < text.Length && text[_position] == '^')
                {
                    _position++; Skip(); var start = _position; while (_position < text.Length && char.IsDigit(text[_position])) _position++;
                    if (!int.TryParse(text[start.._position], out repeats) || repeats is < 1 or > 200) throw Error("重复次数须为1–200的整数。");
                }
                if ((long)result.Count + (long)item.Count * repeats > 200) throw Error("展开后每面最多200层。");
                for (var i = 0; i < repeats; i++) foreach (var term in item)
                {
                    var value = term.Weight * weight; if (!double.IsFinite(value) || value <= 0) throw Error("组合QW系数无效。");
                    result.Add((term.Id, value));
                }
            }
            return result;
        }
    }
}
