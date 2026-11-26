using System.Text.Json;
using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.CoatingDesign.Engine;

/// <summary>A single-angle spectrum for coatings supplied as power data without a layer prescription.</summary>
public sealed record CoatingResponseTable(string Format, int Version, string Name, double AngleDegrees, CoatingPowerRow[] Rows)
{
    public bool ZeroPhase { get; init; }

    public const string DocumentFormat = "CoatingLab.PowerSpectrum";

    public static CoatingResponseTable? Read(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("镀膜文件必须是 JSON 对象。");
            if (!root.TryGetProperty("Format", out var tag) || tag.ValueKind != JsonValueKind.String || tag.GetString() != DocumentFormat) return null;
            var table = root.Deserialize<CoatingResponseTable>()!;
            if (table.Version != 1 || string.IsNullOrWhiteSpace(table.Name) || !double.IsFinite(table.AngleDegrees)
                || table.AngleDegrees is < 0 or >= 90 || table.Rows is not { Length: >= 2 and <= 200001 })
                throw new InvalidDataException("镀膜光谱表的版本、入射角或采样数量无效。");
            var last = 0d;
            foreach (var row in table.Rows)
            {
                if (row is null || !double.IsFinite(row.WavelengthNm) || row.WavelengthNm <= last
                    || !Passive(row.Rs, row.Ts) || !Passive(row.Rp, row.Tp))
                    throw new InvalidDataException("镀膜光谱表要求波长严格递增，各偏振的反射率与透射率满足能量守恒。");
                last = row.WavelengthNm;
            }
            return table;
        }
        catch (JsonException error) { throw new InvalidDataException("无法读取镀膜 JSON 文件。", error); }
    }

    private static bool Passive(double r, double t) => r >= 0 && t >= 0 && r + t <= 1 + 1e-12;

    public ThinFilmPower Power(double wavelength, double angle, ThinFilmPolarization polarization)
    {
        if (Math.Abs(angle - AngleDegrees) > 1e-9 || wavelength < Rows[0].WavelengthNm || wavelength > Rows[^1].WavelengthNm)
            throw new ArgumentException($"光谱表的入射角为 {AngleDegrees}°，波长范围为 {Rows[0].WavelengthNm}–{Rows[^1].WavelengthNm} nm。");
        var i = Array.BinarySearch(Rows, new CoatingPowerRow(wavelength, 0, 0, 0, 0), WaveOrder);
        var lo = i >= 0 ? Rows[i] : Rows[~i - 1];
        var hi = i >= 0 ? lo : Rows[~i];
        var f = i >= 0 ? 0 : (wavelength - lo.WavelengthNm) / (hi.WavelengthNm - lo.WavelengthNm);
        double Blend(double s, double p) => polarization == ThinFilmPolarization.S ? s :
            polarization == ThinFilmPolarization.P ? p : (s + p) * .5;
        var r = Blend(lo.Rs + f * (hi.Rs - lo.Rs), lo.Rp + f * (hi.Rp - lo.Rp));
        var t = Blend(lo.Ts + f * (hi.Ts - lo.Ts), lo.Tp + f * (hi.Tp - lo.Tp));
        return new(r, t, 1 - r - t, Math.Log(t));
    }

    private static readonly IComparer<CoatingPowerRow> WaveOrder =
        Comparer<CoatingPowerRow>.Create((a, b) => a.WavelengthNm.CompareTo(b.WavelengthNm));
}

public sealed record CoatingPowerRow(double WavelengthNm, double Rs, double Rp, double Ts, double Tp);
