using System.Globalization;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Application.Services;

public sealed record ToleranceScriptLine(int Number, string Text, string Status, string Detail, bool BlocksImport = false);

public sealed record ToleranceScriptPreview(
    IReadOnlyList<ToleranceScriptLine> Lines,
    IReadOnlyList<string> References,
    IReadOnlyList<ToleranceStageObjectiveDto> Stages,
    IReadOnlyList<ToleranceCompensatorDto> Compensators,
    IReadOnlyList<ToleranceMceAdjustmentDto> ConfigurationCompensators,
    ToleranceMeritTableDto? FinalMeritTable,
    int Iterations)
{
    public bool CanImport => Stages.Count > 0 && FinalMeritTable is not null && Lines.All(line => !line.BlocksImport);
}

public static class ToleranceScriptImporter
{
    private const int MaximumLines = 2000;
    private const int MaximumIterations = 500;
    private sealed record PendingCompensator(int Surface, int Configuration = 0, int Row = 0);

    public static ToleranceScriptPreview Preview(string script, IReadOnlyDictionary<string, ToleranceMeritTableDto> tables,
        int surfaceCount, IReadOnlyList<MultiConfigurationOperandRowDto> configurationRows,
        ToleranceConfigurationSettingsDto? configurationSettings, double travel)
    {
        var lines = new List<ToleranceScriptLine>();
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stages = new List<ToleranceStageObjectiveDto>();
        var compensators = new List<ToleranceCompensatorDto>();
        var mce = new List<ToleranceMceAdjustmentDto>();
        var pending = new List<PendingCompensator>();
        var completed = new HashSet<PendingCompensator>();
        string? reference = null;
        ToleranceMeritTableDto? table = null;
        var iterations = 0;
        var measured = false;
        using var reader = new StringReader(script);
        for (var number = 1; reader.ReadLine() is { } raw; number++)
        {
            if (number > MaximumLines)
            {
                lines.Add(new(number, "", "超出限制", "脚本最多读取 2000 行。", true));
                break;
            }
            var text = raw.Trim();
            if (text.Length == 0) continue;
            var split = text.IndexOfAny([' ', '\t']);
            var command = (split < 0 ? text : text[..split]).ToUpperInvariant();
            var arguments = split < 0 ? "" : text[(split + 1)..].Trim();
            var tokens = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (text[0] == '!')
            {
                Add("注释", "");
                continue;
            }

            if (measured)
            {
                Add("未支持", "首批支持作为终点的 GETMERIT，后续只允许注释。", true);
                continue;
            }

            switch (command)
            {
                case "CLEARCOMP":
                    if (tokens.Length != 0) { Invalid("CLEARCOMP"); break; }
                    pending.Clear();
                    Add("已映射", "清除当前可调量，保留已完成阶段的调整。");
                    break;
                case "LOADMERIT":
                    if (!TryReference(arguments, out var file, out var identity)) { Invalid("LOADMERIT 文件名.MF 或 文件名.ZMX"); break; }
                    reference = file;
                    if (references.TryGetValue(file, out var previous) && !string.Equals(previous, identity, StringComparison.OrdinalIgnoreCase))
                    { Add("歧义引用", $"多个原始路径使用相同文件名 {file}。请为每张评价表使用不同文件名。", true); break; }
                    references[file] = identity;
                    if (tables.TryGetValue(file, out table))
                        Add("已映射", $"评价表 {table.Name}（{table.Rows.Count} 行）。");
                    else
                        Add("缺少评价表", $"请选择与 {file} 同名的 ZMX 文件。", true);
                    break;
                case "COMP":
                    if (tokens.Length != 2 || !Integer(tokens[0], out var surface) || !Integer(tokens[1], out var code))
                    { Invalid("COMP 表面编号 类型编号"); break; }
                    if (code != 0)
                    { Add("未支持", "首批 COMP 支持类型 0 的空气间隔。", true); break; }
                    if (surface <= 0 || surface >= surfaceCount - 1)
                    { Add("无效引用", "COMP 须引用当前处方的实体表面。", true); break; }
                    AddCompensator(new(surface));
                    break;
                case "CMCO":
                    if (tokens.Length != 2 || !Integer(tokens[0], out var row) || !Integer(tokens[1], out var config))
                    { Invalid("CMCO 操作数行号 配置编号"); break; }
                    var operand = configurationRows.FirstOrDefault(item => item.Number == row);
                    if (operand is not { Kind: MultiConfigurationParameterKind.Thickness }
                        || config < 1 || config > operand.Values.Count
                        || configurationSettings?.Scope is not (ToleranceConfigurationScope.Selected or ToleranceConfigurationScope.All)
                        || (configurationSettings.Scope == ToleranceConfigurationScope.Selected && configurationSettings.Configurations?.Contains(config) != true))
                    { Add("无效引用", "CMCO 须引用已参与公差的配置和有效厚度行。", true); break; }
                    AddCompensator(new(operand.SurfaceNumber, config, row));
                    break;
                case "OPTIMIZE":
                    if (tokens.Length != 1 || !Integer(tokens[0], out var cycles) || cycles is < 1 or > MaximumIterations)
                    { Add("未支持", "请输入正的固定循环数。自动收敛模式待接入。", true); break; }
                    if (iterations != 0 && cycles != iterations)
                    { Add("未支持", "当前各阶段须使用相同的循环数。", true); break; }
                    if (stages.Count == 8 || (stages.Count + 1) * cycles > MaximumIterations)
                    { Add("超出限制", "最多 8 阶段，总优化循环数不得超过 500。", true); break; }
                    if (pending.Count == 0 || reference is null)
                    { Add("缺少设置", "OPTIMIZE 前须有可调量和 LOADMERIT。", true); break; }
                    if (pending.Any(completed.Contains))
                    { Add("未支持", "已完成阶段的同一可调量保持锁定。请以 CLEARCOMP 分隔并使用不同可调量。", true); break; }
                    iterations = cycles;
                    var stage = stages.Count + 1;
                    stages.Add(new(stage, ToleranceCriterion.SystemMerit, MeritTable: table));
                    foreach (var actuator in pending)
                    {
                        completed.Add(actuator);
                        if (actuator.Row == 0)
                            compensators.Add(new(actuator.Surface, ToleranceCompensatorKind.Thickness, -travel, travel, Stage: stage));
                        else
                            mce.Add(new(actuator.Configuration, actuator.Row, actuator.Surface, -travel, travel, true, stage));
                    }
                    Add("已映射", $"阶段 {stage}，DLS {cycles} 次循环，评价表 {reference}。");
                    break;
                case "GETMERIT":
                    if (tokens.Length != 0) { Invalid("GETMERIT"); break; }
                    if (reference is null)
                    { Add("缺少设置", "GETMERIT 前须有 LOADMERIT。", true); break; }
                    measured = true;
                    Add("已映射", $"最终验收使用 {reference}。");
                    break;
                default:
                    Add("未支持", $"{command} 暂无等效装调阶段。此行会阻止应用。", true);
                    break;
            }

            void Add(string status, string detail, bool error = false) => lines.Add(new(number, raw, status, detail, error));
            void Invalid(string syntax) => Add("语法无效", $"应使用 {syntax}。", true);
            void AddCompensator(PendingCompensator actuator)
            {
                if (pending.Contains(actuator)) { Add("重复可调量", "同一阶段重复定义。", true); return; }
                pending.Add(actuator);
                Add("已映射", actuator.Row == 0 ? $"面 {actuator.Surface} 的空气间隔。" : $"配置 {actuator.Configuration} 厚度行 {actuator.Row}。");
            }
        }

        if (!double.IsFinite(travel) || travel <= 0)
            lines.Add(new(0, "", "缺少行程", "请明确输入 Workbench 对称补偿行程，单位为 mm。", true));
        if (stages.Count == 0)
            lines.Add(new(0, "", "缺少阶段", "脚本至少需要一个可映射的 OPTIMIZE 阶段。", true));

        return new(lines, references.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(), stages, compensators, mce, table, iterations);
    }

    private static bool Integer(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryReference(string text, out string file, out string identity)
    {
        file = "";
        identity = "";
        if (text.Length == 0) return false;
        if (text[0] == '"')
        {
            if (text.Length < 3 || text[^1] != '"' || text[1..^1].Contains('"')) return false;
            text = text[1..^1];
        }
        else if (text.Any(char.IsWhiteSpace) || text.Contains('"')) return false;

        // The script identifies a companion by name. Only files explicitly selected by the user are read.
        identity = text.Replace('\\', '/');
        var leaf = identity.Split('/').Last();
        var dot = leaf.LastIndexOf('.');
        if (dot <= 0 || !leaf[(dot + 1)..].Equals("mf", StringComparison.OrdinalIgnoreCase)
            && !leaf[(dot + 1)..].Equals("zmx", StringComparison.OrdinalIgnoreCase)) return false;
        file = leaf[..dot];
        return true;
    }
}
