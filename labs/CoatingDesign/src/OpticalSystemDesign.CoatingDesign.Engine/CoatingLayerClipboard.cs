using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record CoatingLayerPacket(string Format, int Version, string LayerOrder, double ReferenceWavelengthNm,
    MaterialSnapshot[] Materials, FilmLayer[] Layers);
public sealed record CoatingLayerPaste(MaterialSnapshot[] Materials, FilmLayer[] Layers);

public static class CoatingLayerClipboard
{
    public const string Format = "OpticalSystemDesign.CoatingLayers";
    public const int MaximumBytes = 4 * 1024 * 1024;
    public static string Encode(Experiment d, FilmLayer[] layers)
    {
        d.Validate();
        var ids = layers.Select(l => l.MaterialId).ToHashSet();
        var packet = new CoatingLayerPacket(Format, 1, "exterior-to-substrate", d.EffectiveReferenceWavelengthNm,
            d.Materials.Where(m => ids.Contains(m.Id)).ToArray(), layers);
        Validate(packet); var text = JsonSerializer.Serialize(packet, Experiment.JsonOptions); Limit(text); return text;
    }
    public static string PlainText(FilmLayer[] layers) => "MaterialId\tThickness_nm\tVariable\n" + string.Join('\n', layers.Select(l =>
        $"{CsvCell.Escape(l.MaterialId)}\t{l.ThicknessNm.ToString("G17", CultureInfo.InvariantCulture)}\t{l.Variable}"));
    public static CoatingLayerPaste Decode(Experiment receiver, string text, bool rich)
    {
        receiver.Validate();
        Limit(text);
        if (!rich) return Plain(receiver, text);
        CoatingLayerPacket packet;
        try { packet = JsonSerializer.Deserialize<CoatingLayerPacket>(text, Experiment.JsonOptions) ?? throw new InvalidDataException("膜层剪贴板为空。"); }
        catch (JsonException error) { throw new InvalidDataException("膜层剪贴板格式损坏。", error); }
        Validate(packet);
        var materials = receiver.Materials.ToList(); var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var imported in packet.Materials)
        {
            var identity = MaterialIdentity(imported);
            var existing = materials.FirstOrDefault(m => m.Id == imported.Id);
            var id = imported.Id;
            if (existing is not null && MaterialIdentity(existing) != identity)
                id = "clipboard-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
            existing = materials.FirstOrDefault(m => m.Id == id);
            if (existing is not null && MaterialIdentity(existing) != identity) throw new InvalidDataException("剪贴板材料身份发生冲突，未修改实验。");
            if (existing is null) materials.Add(imported with { Id = id });
            map.Add(imported.Id, id);
        }
        if (materials.Count > 500) throw new ArgumentException("粘贴后材料快照超过500项，未修改实验。");
        return new(materials.ToArray(), packet.Layers.Select(l => l with { MaterialId = map[l.MaterialId] }).ToArray());
    }
    private static void Limit(string text)
    { if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("膜层剪贴板超过4 MiB上限。"); }
    private static string MaterialIdentity(MaterialSnapshot m) => JsonSerializer.Serialize(new
    {
        m.Name,
        m.Source,
        m.MinimumNm,
        m.MaximumNm,
        m.HoldTabulatedEndpoints,
        m.DisplayColor,
        m.Model.Kind,
        Numbers = m.Model.Numbers.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
        Text = m.Model.Text.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()
    }, Experiment.JsonOptions);
    private static void Validate(CoatingLayerPacket p)
    {
        if (p.Format != Format || p.Version != 1 || p.LayerOrder != "exterior-to-substrate" || !double.IsFinite(p.ReferenceWavelengthNm)
            || p.ReferenceWavelengthNm <= 0 || p.Materials is not { Length: > 0 and <= 200 } || p.Layers is not { Length: > 0 and <= 200 }
            || p.Materials.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id)) || p.Materials.Select(m => m.Id).Distinct().Count() != p.Materials.Length)
            throw new InvalidDataException("剪贴板版本、层序、参考波长或材料/膜层数量无效。");
        var ids = p.Materials.Select(m => m.Id).ToHashSet(); var registry = new MaterialRegistry();
        foreach (var m in p.Materials)
        {
            if (!double.IsFinite(m.MinimumNm) || !double.IsFinite(m.MaximumNm) || m.MinimumNm <= 0 || m.MaximumNm <= m.MinimumNm)
                throw new InvalidDataException("剪贴板材料的有效域无效。");
            m.Resolve(registry);
        }
        if (p.Layers.Any(l => l is null || !ids.Contains(l.MaterialId) || !double.IsFinite(l.ThicknessNm) || l.ThicknessNm <= 0 || l.ThicknessNm > 100000))
            throw new InvalidDataException("剪贴板包含缺失材料或无效膜厚。");
    }
    private static CoatingLayerPaste Plain(Experiment receiver, string text)
    {
        var rows = ReadTsv(text); var layers = new List<FilmLayer>();
        foreach (var row in rows)
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (layers.Count == 0 && row.Length >= 2 && row[0] == "MaterialId" && row[1] == "Thickness_nm") continue;
            if (row.Length is < 2 or > 3 || !double.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness)
                || !double.IsFinite(thickness) || thickness <= 0 || thickness > 100000) throw new ArgumentException("制表符膜层表需要材料ID、正物理膜厚nm及可选变量列。");
            var materialText = row[0];
            if (materialText.StartsWith('\'') && !receiver.Materials.Any(m => m.Id == materialText || m.Name == materialText)) materialText = materialText[1..];
            var material = CoatingStackFormula.Resolve(receiver, materialText);
            var variable = true;
            if (row.Length == 3 && !string.IsNullOrWhiteSpace(row[2]))
            {
                if (row[2] is "0" or "1") variable = row[2] == "1";
                else if (!bool.TryParse(row[2], out variable)) throw new ArgumentException("变量列须为true/false或1/0。");
            }
            layers.Add(new(material.Id, thickness, variable));
            if (layers.Count > 200) throw new ArgumentException("每面最多粘贴200层。");
        }
        if (layers.Count == 0) throw new ArgumentException("剪贴板没有膜层。");
        return new(receiver.Materials, layers.ToArray());
    }
    private static List<string[]> ReadTsv(string text)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var cell = new StringBuilder(); var quoted = false; var closed = false;
        for (var i = 0; i <= text.Length; i++)
        {
            var c = i == text.Length ? '\n' : text[i];
            if (quoted)
            {
                if (i == text.Length) throw new ArgumentException("剪贴板表格引号未闭合。");
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } }
                else cell.Append(c);
                continue;
            }
            if (c == '"' && cell.Length == 0 && !closed) { quoted = true; continue; }
            if (c is '\t' or '\r' or '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); closed = false;
                if (row.Count > 3) throw new ArgumentException("剪贴板膜层表最多三列。");
                if (c != '\t') { rows.Add(row.ToArray()); row.Clear(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
                if (rows.Count > 202) throw new ArgumentException("剪贴板膜层表超过数量上限。");
            }
            else { if (closed) throw new ArgumentException("引号后只能是制表符或换行。"); cell.Append(c); }
        }
        return rows;
    }
}
