using System.Globalization;
using OpticalSystemDesign.Core.Capabilities;

namespace OpticalSystemDesign.Core.FileIO;

internal static class SequentialSubsetTextWriter
{
    internal sealed record Dialect(
        string Caption, string Surface, string Label, string Radius,
        string Thickness, string Aperture, string Conic);

    internal static readonly Dialect CodeV = new("CODE V SEQ", "S", "COM", "RDY", "THI", "SDIA", "CON");
    internal static readonly Dialect Oslo = new("OSLO LEN", "SRF", "NOTE", "RD", "TH", "AP", "CC");

    internal static string Write(Optic optic, string formatName, Dialect dialect)
    {
        if (optic.Polarization != new Rays.SystemPolarization())
            throw new NotSupportedException("此文本格式尚未映射系统偏振设置；请保存 STAROPT，避免丢失 Jones 输入与参考轴。");
        OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Export, formatName);
        var lines = new List<string>
        {
            $"! OpticalSystemDesign {dialect.Caption} common sequential subset",
            $"NAME {optic.Name}"
        };
        foreach (var surface in SequentialLensDocument.FromOptic(optic).Surfaces)
        {
            lines.Add($"{dialect.Surface} {surface.Number}");
            lines.Add($"  {dialect.Label} {surface.Label}");
            lines.Add($"  {dialect.Radius} {FormatDouble(surface.Radius)}");
            lines.Add($"  {dialect.Thickness} {FormatDouble(surface.Thickness)}");
            lines.Add($"  GLA {surface.Material}");
            lines.Add($"  {dialect.Aperture} {FormatDouble(surface.SemiDiameter)}");
            lines.Add($"  {dialect.Conic} {FormatDouble(surface.Conic)}");
            if (surface.IsStop) lines.Add("  STO");
            if (surface.IsReflective) lines.Add("  REFL");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDouble(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
