using System.Globalization;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.FileIO;

public interface IOpticalFormatImporter
{
    string FormatName { get; }

    string[] Extensions { get; }

    Optic Import(string text);
}

public interface IOpticalFormatExporter
{
    string FormatName { get; }

    string[] Extensions { get; }

    string Export(Optic optic);
}

public sealed record SequentialSurfaceRecord(
    int Number,
    string Label,
    double Radius,
    double Thickness,
    string Material,
    string Coating,
    double SemiDiameter,
    double Conic,
    bool IsStop,
    bool IsReflective);

public sealed record SequentialLensDocument(string Name, IReadOnlyList<SequentialSurfaceRecord> Surfaces)
{
    public static SequentialLensDocument FromOptic(Optic optic)
    {
        return new SequentialLensDocument(
            optic.Name,
            optic.SurfaceGroup.Items.Select(surface => new SequentialSurfaceRecord(
                surface.Number,
                surface.Label,
                surface.Radius,
                surface.Thickness,
                surface.Material,
                surface.Coating,
                surface.SemiDiameter,
                surface.Conic,
                surface.IsStop,
                surface.IsReflective)).ToArray());
    }

    public Optic ToOptic()
    {
        var optic = new Optic(Name);
        optic.SurfaceGroup.Replace(Surfaces.Select(surface => new OpticalSurface
        {
            Number = surface.Number,
            Label = surface.Label,
            Radius = surface.Radius,
            Thickness = surface.Thickness,
            Material = surface.Material,
            Coating = surface.Coating,
            SemiDiameter = surface.SemiDiameter,
            Conic = surface.Conic,
            IsStop = surface.IsStop,
            IsReflective = surface.IsReflective
        }));
        return optic;
    }
}

public static class OpticalFormatCatalog
{
    public static IReadOnlyList<IOpticalFormatImporter> Importers { get; } = new IOpticalFormatImporter[]
    {
        new ZemaxZmxImporter(),
        new CodeVSeqImporter(),
        new OsloLenImporter(),
        new SequentialLensTextImporter()
    };

    public static IReadOnlyList<IOpticalFormatExporter> Exporters { get; } = new IOpticalFormatExporter[]
    {
        new ZemaxZmxExporter(),
        new CodeVSeqExporter(),
        new OsloLenExporter(),
        new SequentialLensTextExporter()
    };

    public static Optic Import(string text, string extension)
    {
        return FindImporter(extension).Import(text);
    }

    public static string Export(Optic optic, string extension)
    {
        return FindExporter(extension).Export(optic);
    }

    public static IOpticalFormatImporter FindImporter(string extension)
    {
        return Importers.FirstOrDefault(importer => MatchesExtension(importer.Extensions, extension))
            ?? Importers[^1];
    }

    public static IOpticalFormatExporter FindExporter(string extension)
    {
        return Exporters.FirstOrDefault(exporter => MatchesExtension(exporter.Extensions, extension))
            ?? Exporters[^1];
    }

    private static bool MatchesExtension(IEnumerable<string> supportedExtensions, string extension)
    {
        var normalized = NormalizeExtension(extension);
        return supportedExtensions.Any(item => NormalizeExtension(item).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }
}

public sealed class SequentialLensTextImporter : IOpticalFormatImporter
{
    public string FormatName => "common-sequential-lens";

    public string[] Extensions { get; } = { ".txt", ".lens", ".dat" };

    public Optic Import(string text)
    {
        return SequentialLensParser.ParseCommon(text, FormatName).ToOptic();
    }
}

public sealed class SequentialLensTextExporter : IOpticalFormatExporter
{
    public string FormatName => "common-sequential-lens";

    public string[] Extensions { get; } = { ".txt", ".lens", ".dat" };

    public string Export(Optic optic)
    {
        return string.Join(Environment.NewLine, SequentialLensDocument.FromOptic(optic).Surfaces.Select(surface =>
            string.Join(" ", new[]
            {
                FormatDouble(surface.Radius),
                FormatDouble(surface.Thickness),
                surface.Material,
                FormatDouble(surface.SemiDiameter),
                FormatDouble(surface.Conic),
                surface.IsStop ? "STOP" : "SURF"
            })));
    }

    private static string FormatDouble(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}

public sealed class ZemaxZmxImporter : IOpticalFormatImporter
{
    public string FormatName => "zemax-zmx-subset";

    public string[] Extensions { get; } = { ".zmx" };

    public Optic Import(string text)
    {
        return SequentialLensParser.ParseZemax(text).ToOptic();
    }
}

public sealed class ZemaxZmxExporter : IOpticalFormatExporter
{
    public string FormatName => "zemax-zmx-subset";

    public string[] Extensions { get; } = { ".zmx" };

    public string Export(Optic optic)
    {
        var lines = new List<string>
        {
            "! OptilandWorkbench Zemax ZMX common sequential subset"
        };

        foreach (var surface in SequentialLensDocument.FromOptic(optic).Surfaces)
        {
            lines.Add($"SURF {surface.Number}");
            lines.Add($"  COMM {surface.Label}");
            lines.Add($"  CURV {FormatDouble(RadiusToCurvature(surface.Radius))}");
            lines.Add($"  DISZ {FormatDouble(surface.Thickness)}");
            lines.Add($"  GLAS {NormalizeAir(surface.Material)}");
            lines.Add($"  DIAM {FormatDouble(surface.SemiDiameter)}");
            lines.Add($"  CONI {FormatDouble(surface.Conic)}");
            if (surface.IsStop)
            {
                lines.Add("  STOP");
            }

            if (surface.IsReflective)
            {
                lines.Add("  MIRR");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static double RadiusToCurvature(double radius) => Math.Abs(radius) < 1e-12 ? 0 : 1.0 / radius;

    private static string NormalizeAir(string material) => material.Equals("Air", StringComparison.OrdinalIgnoreCase) ? "AIR" : material;

    private static string FormatDouble(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
}

public sealed class CodeVSeqImporter : IOpticalFormatImporter
{
    public string FormatName => "codev-seq-subset";

    public string[] Extensions { get; } = { ".seq" };

    public Optic Import(string text)
    {
        return SequentialLensParser.ParseCodeV(text).ToOptic();
    }
}

public sealed class CodeVSeqExporter : IOpticalFormatExporter
{
    public string FormatName => "codev-seq-subset";

    public string[] Extensions { get; } = { ".seq" };

    public string Export(Optic optic)
    {
        var lines = new List<string>
        {
            "! OptilandWorkbench CODE V SEQ common sequential subset"
        };

        foreach (var surface in SequentialLensDocument.FromOptic(optic).Surfaces)
        {
            lines.Add($"S {surface.Number}");
            lines.Add($"  COM {surface.Label}");
            lines.Add($"  RDY {FormatDouble(surface.Radius)}");
            lines.Add($"  THI {FormatDouble(surface.Thickness)}");
            lines.Add($"  GLA {surface.Material}");
            lines.Add($"  SDIA {FormatDouble(surface.SemiDiameter)}");
            lines.Add($"  CON {FormatDouble(surface.Conic)}");
            if (surface.IsStop)
            {
                lines.Add("  STO");
            }

            if (surface.IsReflective)
            {
                lines.Add("  REFL");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDouble(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
}

public sealed class OsloLenImporter : IOpticalFormatImporter
{
    public string FormatName => "oslo-len-subset";

    public string[] Extensions { get; } = { ".len" };

    public Optic Import(string text)
    {
        return SequentialLensParser.ParseOslo(text).ToOptic();
    }
}

public sealed class OsloLenExporter : IOpticalFormatExporter
{
    public string FormatName => "oslo-len-subset";

    public string[] Extensions { get; } = { ".len" };

    public string Export(Optic optic)
    {
        var lines = new List<string>
        {
            "! OptilandWorkbench OSLO LEN common sequential subset"
        };

        foreach (var surface in SequentialLensDocument.FromOptic(optic).Surfaces)
        {
            lines.Add($"SRF {surface.Number}");
            lines.Add($"  NOTE {surface.Label}");
            lines.Add($"  RD {FormatDouble(surface.Radius)}");
            lines.Add($"  TH {FormatDouble(surface.Thickness)}");
            lines.Add($"  GLA {surface.Material}");
            lines.Add($"  AP {FormatDouble(surface.SemiDiameter)}");
            lines.Add($"  CC {FormatDouble(surface.Conic)}");
            if (surface.IsStop)
            {
                lines.Add("  STO");
            }

            if (surface.IsReflective)
            {
                lines.Add("  REFL");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDouble(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
}

internal static class SequentialLensParser
{
    public static SequentialLensDocument ParseCommon(string text, string name)
    {
        var builders = new List<SequentialSurfaceBuilder>();
        foreach (var line in Lines(text))
        {
            var tokens = Tokenize(line);
            if (tokens.Length < 3)
            {
                continue;
            }

            if (!TryParseDouble(tokens[0], out var radius) || !TryParseDouble(tokens[1], out var thickness))
            {
                continue;
            }

            var builder = new SequentialSurfaceBuilder(builders.Count)
            {
                Radius = radius,
                Thickness = thickness,
                Material = NormalizeMaterial(tokens[2])
            };

            if (tokens.Length > 3 && TryParseDouble(tokens[3], out var semiDiameter))
            {
                builder.SemiDiameter = semiDiameter;
            }

            if (tokens.Length > 4 && TryParseDouble(tokens[4], out var conic))
            {
                builder.Conic = conic;
            }

            if (tokens.Any(token => token.Equals("STOP", StringComparison.OrdinalIgnoreCase) || token.Equals("STO", StringComparison.OrdinalIgnoreCase)))
            {
                builder.IsStop = true;
            }

            builders.Add(builder);
        }

        return BuildDocument(name, builders);
    }

    public static SequentialLensDocument ParseZemax(string text)
    {
        return ParseSectioned(
            text,
            "Imported Zemax ZMX",
            beginCommands: new[] { "SURF" },
            radiusCommands: Array.Empty<string>(),
            curvatureCommands: new[] { "CURV" },
            thicknessCommands: new[] { "DISZ", "THIC", "THI" },
            materialCommands: new[] { "GLAS" },
            semiDiameterCommands: new[] { "DIAM", "SDIA", "AP" },
            conicCommands: new[] { "CONI", "CONIC" },
            labelCommands: new[] { "COMM" });
    }

    public static SequentialLensDocument ParseCodeV(string text)
    {
        return ParseSectioned(
            text,
            "Imported CODE V SEQ",
            beginCommands: new[] { "S", "SO", "SRF", "SUR" },
            radiusCommands: new[] { "RDY", "RDX", "RD", "RADIUS" },
            curvatureCommands: new[] { "CV", "CURV" },
            thicknessCommands: new[] { "THI", "TH", "THICKNESS" },
            materialCommands: new[] { "GLA", "GLASS" },
            semiDiameterCommands: new[] { "SDIA", "AP", "APER" },
            conicCommands: new[] { "CON", "CC", "CONIC" },
            labelCommands: new[] { "COM", "COMM", "NOTE" });
    }

    public static SequentialLensDocument ParseOslo(string text)
    {
        return ParseSectioned(
            text,
            "Imported OSLO LEN",
            beginCommands: new[] { "SRF", "SURF", "S" },
            radiusCommands: new[] { "RD", "RADIUS" },
            curvatureCommands: new[] { "CV", "CURV" },
            thicknessCommands: new[] { "TH", "THI", "THICK" },
            materialCommands: new[] { "GLA", "GLASS" },
            semiDiameterCommands: new[] { "AP", "SDIA", "APER" },
            conicCommands: new[] { "CC", "CON", "CONIC" },
            labelCommands: new[] { "NOTE", "COM", "COMM" });
    }

    private static SequentialLensDocument ParseSectioned(
        string text,
        string name,
        IReadOnlyCollection<string> beginCommands,
        IReadOnlyCollection<string> radiusCommands,
        IReadOnlyCollection<string> curvatureCommands,
        IReadOnlyCollection<string> thicknessCommands,
        IReadOnlyCollection<string> materialCommands,
        IReadOnlyCollection<string> semiDiameterCommands,
        IReadOnlyCollection<string> conicCommands,
        IReadOnlyCollection<string> labelCommands)
    {
        var builders = new List<SequentialSurfaceBuilder>();
        SequentialSurfaceBuilder? current = null;

        foreach (var line in Lines(text))
        {
            var tokens = Tokenize(line);
            if (tokens.Length == 0)
            {
                continue;
            }

            var command = NormalizeCommand(tokens[0]);
            if (beginCommands.Contains(command, StringComparer.OrdinalIgnoreCase))
            {
                if (current is not null)
                {
                    builders.Add(current);
                }

                current = new SequentialSurfaceBuilder(ParseSurfaceNumber(tokens, builders.Count));
                ApplyCommandTokens(current, tokens.Skip(2).ToArray(), radiusCommands, curvatureCommands, thicknessCommands, materialCommands, semiDiameterCommands, conicCommands, labelCommands);
                continue;
            }

            current ??= new SequentialSurfaceBuilder(builders.Count);
            ApplyCommandTokens(current, tokens, radiusCommands, curvatureCommands, thicknessCommands, materialCommands, semiDiameterCommands, conicCommands, labelCommands);
        }

        if (current is not null)
        {
            builders.Add(current);
        }

        return BuildDocument(name, builders);
    }

    private static void ApplyCommandTokens(
        SequentialSurfaceBuilder builder,
        IReadOnlyList<string> tokens,
        IReadOnlyCollection<string> radiusCommands,
        IReadOnlyCollection<string> curvatureCommands,
        IReadOnlyCollection<string> thicknessCommands,
        IReadOnlyCollection<string> materialCommands,
        IReadOnlyCollection<string> semiDiameterCommands,
        IReadOnlyCollection<string> conicCommands,
        IReadOnlyCollection<string> labelCommands)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var command = NormalizeCommand(tokens[index]);
            if (Matches(command, radiusCommands) && TryReadDouble(tokens, index + 1, out var radius))
            {
                builder.Radius = radius;
                index++;
            }
            else if (Matches(command, curvatureCommands) && TryReadDouble(tokens, index + 1, out var curvature))
            {
                builder.Radius = Math.Abs(curvature) < 1e-12 ? 0 : 1.0 / curvature;
                index++;
            }
            else if (Matches(command, thicknessCommands) && TryReadDouble(tokens, index + 1, out var thickness))
            {
                builder.Thickness = thickness;
                index++;
            }
            else if (Matches(command, materialCommands) && index + 1 < tokens.Count)
            {
                builder.Material = NormalizeMaterial(tokens[index + 1]);
                index++;
            }
            else if (Matches(command, semiDiameterCommands) && TryReadDouble(tokens, index + 1, out var semiDiameter))
            {
                builder.SemiDiameter = semiDiameter;
                index++;
            }
            else if (Matches(command, conicCommands) && TryReadDouble(tokens, index + 1, out var conic))
            {
                builder.Conic = conic;
                index++;
            }
            else if (Matches(command, labelCommands) && index + 1 < tokens.Count)
            {
                builder.Label = string.Join(" ", tokens.Skip(index + 1));
                break;
            }
            else if (command is "STOP" or "STO")
            {
                builder.IsStop = true;
            }
            else if (command is "MIRR" or "REFL" or "REFLECT")
            {
                builder.IsReflective = true;
            }
        }
    }

    private static SequentialLensDocument BuildDocument(string name, IReadOnlyList<SequentialSurfaceBuilder> builders)
    {
        return new SequentialLensDocument(name, builders.Select((builder, index) => builder.ToRecord(index)).ToArray());
    }

    private static IEnumerable<string> Lines(string text)
    {
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }

    private static string StripComment(string line)
    {
        foreach (var marker in new[] { "!", "#", "//" })
        {
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string[] Tokenize(string line)
    {
        return line.Split(new[] { ' ', '\t', ',', '=' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int ParseSurfaceNumber(IReadOnlyList<string> tokens, int fallback)
    {
        return tokens.Count > 1 && int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;
    }

    private static bool Matches(string command, IReadOnlyCollection<string> commands)
    {
        return commands.Contains(command, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryReadDouble(IReadOnlyList<string> tokens, int index, out double value)
    {
        value = 0;
        return index < tokens.Count && TryParseDouble(tokens[index], out value);
    }

    private static bool TryParseDouble(string token, out double value)
    {
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeCommand(string token)
    {
        return token.Trim().ToUpperInvariant();
    }

    private static string NormalizeMaterial(string material)
    {
        if (string.IsNullOrWhiteSpace(material) || material is "0" or "-" || material.Equals("AIR", StringComparison.OrdinalIgnoreCase))
        {
            return "Air";
        }

        return material.Trim();
    }

    private sealed class SequentialSurfaceBuilder
    {
        public SequentialSurfaceBuilder(int number)
        {
            Number = number;
            Label = $"Surface {number}";
        }

        public int Number { get; }

        public string Label { get; set; }

        public double Radius { get; set; }

        public double Thickness { get; set; }

        public string Material { get; set; } = "Air";

        public string Coating { get; set; } = "None";

        public double SemiDiameter { get; set; } = 10;

        public double Conic { get; set; }

        public bool IsStop { get; set; }

        public bool IsReflective { get; set; }

        public SequentialSurfaceRecord ToRecord(int fallbackNumber)
        {
            var number = Number < 0 ? fallbackNumber : Number;
            return new SequentialSurfaceRecord(
                number,
                string.IsNullOrWhiteSpace(Label) ? $"Surface {number}" : Label,
                Radius,
                Math.Max(0, Thickness),
                NormalizeMaterial(Material),
                string.IsNullOrWhiteSpace(Coating) ? "None" : Coating,
                Math.Max(0.1, SemiDiameter),
                Conic,
                IsStop,
                IsReflective);
        }
    }
}
