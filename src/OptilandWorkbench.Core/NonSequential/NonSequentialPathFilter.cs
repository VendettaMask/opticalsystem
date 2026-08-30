using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.NonSequential;

public sealed class NonSequentialPathFilterException : FormatException
{
    public NonSequentialPathFilterException(string message, int position)
        : base($"{message}（位置 {position + 1}）")
    {
        Position = position;
    }

    public int Position { get; }
}

public sealed class NonSequentialPathFilter
{
    public const int MaximumExpressionLength = 4_096;
    public const int MaximumNestingDepth = 64;
    public const int MaximumNodeCount = 256;
    public const int MaximumSequenceLength = 128;
    private readonly FilterNode _root;

    private NonSequentialPathFilter(string expression, FilterNode root)
    {
        Expression = expression;
        _root = root;
    }

    public string Expression { get; }

    public static NonSequentialPathFilter MatchAll { get; } = new(string.Empty, new ConstantNode(true));

    public static NonSequentialPathFilter Parse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return MatchAll;
        }

        if (expression.Length > MaximumExpressionLength)
        {
            throw new NonSequentialPathFilterException(
                $"路径筛选表达式不能超过 {MaximumExpressionLength} 个字符",
                MaximumExpressionLength);
        }

        var parser = new Parser(expression);
        return new NonSequentialPathFilter(expression, parser.Parse());
    }

    public bool IsMatch(NonSequentialDocument document, NonSequentialRayBranch branch)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(branch);
        return _root.Evaluate(new EvaluationContext(document, branch));
    }

    private sealed class EvaluationContext
    {
        private readonly Dictionary<Guid, int> _numbers;

        public EvaluationContext(NonSequentialDocument document, NonSequentialRayBranch branch)
        {
            Document = document;
            Branch = branch;
            _numbers = document.Objects.Select((item, index) => (item.Id, Number: index + 1))
                .ToDictionary(item => item.Id, item => item.Number);
            Events = BuildEvents(document, branch, _numbers);
        }

        public NonSequentialDocument Document { get; }
        public NonSequentialRayBranch Branch { get; }
        public IReadOnlyList<PathEvent> Events { get; }

        public bool Has(Atom atom)
        {
            if (atom.Kind == AtomKind.Miss)
            {
                return Events.All(item => item.ObjectNumber != atom.Number || item.Kind != AtomKind.Hit);
            }
            return Events.Contains(new PathEvent(atom.Kind, atom.Number));
        }

        private static IReadOnlyList<PathEvent> BuildEvents(
            NonSequentialDocument document,
            NonSequentialRayBranch branch,
            IReadOnlyDictionary<Guid, int> numbers)
        {
            var result = new List<PathEvent>();
            if (branch.SourceObjectId is Guid source && numbers.TryGetValue(source, out var sourceNumber))
            {
                result.Add(new PathEvent(AtomKind.Source, sourceNumber));
            }

            var wavelength = branch.WavelengthNanometers > 0
                ? branch.WavelengthNanometers
                : branch.Segments.FirstOrDefault()?.WavelengthNanometers ?? 0;
            if (wavelength > 0)
            {
                var wavelengthNumber = document.Wavelengths.ToList().FindIndex(item =>
                    Math.Abs(item.Nanometers - wavelength) <= 1e-9) + 1;
                if (wavelengthNumber > 0) result.Add(new PathEvent(AtomKind.Wavelength, wavelengthNumber));
            }

            foreach (var segment in branch.Segments)
            {
                if (segment.ObjectId is not Guid objectId || !numbers.TryGetValue(objectId, out var number)) continue;
                result.Add(new PathEvent(AtomKind.Hit, number));
                if (document.Objects.FirstOrDefault(item => item.Id == objectId)?.Kind
                    == NonSequentialObjectKind.DetectorRectangle)
                {
                    result.Add(new PathEvent(AtomKind.Detected, number));
                }
                if (segment.InteractionKind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection)
                {
                    result.Add(new PathEvent(AtomKind.Reflected, number));
                }
                else if (segment.InteractionKind == RayInteractionKind.Transmitted)
                {
                    result.Add(new PathEvent(AtomKind.Transmitted, number));
                }
            }

            result.Add(new PathEvent(branch.TerminationReason switch
            {
                NonSequentialTerminationReason.Absorbed => AtomKind.Absorbed,
                NonSequentialTerminationReason.Escaped => AtomKind.Escaped,
                NonSequentialTerminationReason.DetectorHit => AtomKind.None,
                NonSequentialTerminationReason.Split => AtomKind.None,
                _ => AtomKind.Truncated
            }, 0));
            return result.Where(item => item.Kind != AtomKind.None).ToArray();
        }
    }

    private abstract record FilterNode
    {
        public abstract bool Evaluate(EvaluationContext context);
    }

    private sealed record ConstantNode(bool Value) : FilterNode
    {
        public override bool Evaluate(EvaluationContext context) => Value;
    }

    private sealed record AtomNode(Atom Atom) : FilterNode
    {
        public override bool Evaluate(EvaluationContext context) => context.Has(Atom);
    }

    private sealed record NotNode(FilterNode Value) : FilterNode
    {
        public override bool Evaluate(EvaluationContext context) => !Value.Evaluate(context);
    }

    private sealed record AndNode(FilterNode Left, FilterNode Right) : FilterNode
    {
        public override bool Evaluate(EvaluationContext context) => Left.Evaluate(context) && Right.Evaluate(context);
    }

    private sealed record OrNode(FilterNode Left, FilterNode Right) : FilterNode
    {
        public override bool Evaluate(EvaluationContext context) => Left.Evaluate(context) || Right.Evaluate(context);
    }

    private sealed record SequenceNode(IReadOnlyList<Atom> Atoms) : FilterNode
    {
        public override bool Evaluate(EvaluationContext context)
        {
            var eventIndex = 0;
            foreach (var atom in Atoms)
            {
                if (atom.Kind == AtomKind.Miss)
                {
                    if (!context.Has(atom)) return false;
                    continue;
                }

                while (eventIndex < context.Events.Count
                    && context.Events[eventIndex] != new PathEvent(atom.Kind, atom.Number))
                {
                    eventIndex++;
                }
                if (eventIndex >= context.Events.Count) return false;
                eventIndex++;
            }
            return true;
        }
    }

    private readonly record struct Atom(AtomKind Kind, int Number);
    private readonly record struct PathEvent(AtomKind Kind, int ObjectNumber);
    private enum AtomKind
    {
        None,
        Source,
        Hit,
        Reflected,
        Transmitted,
        Detected,
        Miss,
        Wavelength,
        Absorbed,
        Escaped,
        Truncated
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;
        private int _nodeCount;

        public Parser(string text) => _text = text;

        public FilterNode Parse()
        {
            var result = ParseOr(0);
            SkipWhiteSpace();
            if (_position != _text.Length)
            {
                throw Error($"无法识别的字符“{_text[_position]}”");
            }
            return result;
        }

        private FilterNode ParseOr(int depth)
        {
            var left = ParseAnd(depth);
            while (Consume('|')) left = Node(new OrNode(left, ParseAnd(depth)));
            return left;
        }

        private FilterNode ParseAnd(int depth)
        {
            var left = ParseUnary(depth);
            while (Consume('&')) left = Node(new AndNode(left, ParseUnary(depth)));
            return left;
        }

        private FilterNode ParseUnary(int depth)
        {
            EnsureDepth(depth);
            if (Consume('!')) return Node(new NotNode(ParseUnary(depth + 1)));
            return ParsePrimary(depth);
        }

        private FilterNode ParsePrimary(int depth)
        {
            SkipWhiteSpace();
            if (Consume('('))
            {
                EnsureDepth(depth + 1);
                var value = ParseOr(depth + 1);
                Require(')', "缺少右括号");
                return value;
            }

            if (PeekWord("SEQ"))
            {
                _position += 3;
                Require('(', "SEQ 后必须使用左括号");
                var atoms = new List<Atom>();
                do
                {
                    if (atoms.Count >= MaximumSequenceLength)
                    {
                        throw Error($"SEQ 最多包含 {MaximumSequenceLength} 个路径事件");
                    }
                    atoms.Add(ParseAtom());
                }
                while (Consume(','));
                if (atoms.Count == 0) throw Error("SEQ 至少需要一个路径事件");
                Require(')', "SEQ 缺少右括号");
                return Node(new SequenceNode(atoms));
            }
            return Node(new AtomNode(ParseAtom()));
        }

        private T Node<T>(T node) where T : FilterNode
        {
            _nodeCount++;
            if (_nodeCount > MaximumNodeCount)
            {
                throw Error($"路径筛选表达式最多包含 {MaximumNodeCount} 个节点");
            }

            return node;
        }

        private void EnsureDepth(int depth)
        {
            if (depth > MaximumNestingDepth)
            {
                throw Error($"路径筛选表达式嵌套不能超过 {MaximumNestingDepth} 层");
            }
        }

        private Atom ParseAtom()
        {
            SkipWhiteSpace();
            if (_position >= _text.Length) throw Error("表达式意外结束");
            var code = char.ToUpperInvariant(_text[_position++]);
            var kind = code switch
            {
                'Q' => AtomKind.Source,
                'H' => AtomKind.Hit,
                'R' => AtomKind.Reflected,
                'T' => AtomKind.Transmitted,
                'D' => AtomKind.Detected,
                'M' => AtomKind.Miss,
                'W' => AtomKind.Wavelength,
                'A' => AtomKind.Absorbed,
                'E' => AtomKind.Escaped,
                'X' => AtomKind.Truncated,
                _ => throw Error($"未知路径标记“{code}”", _position - 1)
            };
            if (kind is AtomKind.Absorbed or AtomKind.Escaped or AtomKind.Truncated)
            {
                return new Atom(kind, 0);
            }

            var start = _position;
            while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
            if (start == _position
                || !int.TryParse(_text.AsSpan(start, _position - start), out var number)
                || number <= 0)
            {
                throw Error($"路径标记“{code}”后必须是正整数", start);
            }
            return new Atom(kind, number);
        }

        private bool PeekWord(string value)
        {
            SkipWhiteSpace();
            return _position + value.Length <= _text.Length
                && _text.AsSpan(_position, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        private bool Consume(char value)
        {
            SkipWhiteSpace();
            if (_position >= _text.Length || _text[_position] != value) return false;
            _position++;
            return true;
        }

        private void Require(char value, string message)
        {
            if (!Consume(value)) throw Error(message);
        }

        private void SkipWhiteSpace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
        }

        private NonSequentialPathFilterException Error(string message, int? position = null) =>
            new(message, position ?? _position);
    }
}

public sealed record NonSequentialPathSummary(
    string Path,
    string FilterExpression,
    int RayCount,
    double TotalPowerWatts,
    double PowerFraction,
    double MinimumOpticalPathLength,
    double AverageOpticalPathLength,
    double MaximumOpticalPathLength,
    IReadOnlyDictionary<int, int> WavelengthRayCounts,
    Guid? DetectorId,
    NonSequentialTerminationReason TerminationReason);

public static class NonSequentialPathAnalyzer
{
    public static IReadOnlyList<NonSequentialPathSummary> Analyze(
        NonSequentialDocument document,
        IEnumerable<NonSequentialRayBranch> branches)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(branches);
        var objectInfo = document.Objects.Select((item, index) => (
                item.Id,
                Number: index + 1,
                item.Kind))
            .ToDictionary(item => item.Id);
        var groups = new Dictionary<(string Path, string Filter), PathAccumulator>();
        var totalPower = 0.0;
        foreach (var branch in branches)
        {
            if (!IsTerminal(branch)) continue;
            var key = Key(branch, objectInfo);
            if (!groups.TryGetValue(key, out var accumulator))
            {
                accumulator = new PathAccumulator(
                    branch.TerminationReason,
                    branch.TerminationReason == NonSequentialTerminationReason.DetectorHit
                        ? branch.Segments.LastOrDefault()?.ObjectId
                        : null);
                groups.Add(key, accumulator);
            }
            var power = Math.Max(0, branch.FinalIntensity);
            var opticalPath = branch.Segments.LastOrDefault()?.CumulativeOpticalPathLength ?? 0;
            accumulator.Add(power, opticalPath, WavelengthNumber(document, branch));
            totalPower += power;
        }

        return groups.Select(group => new NonSequentialPathSummary(
                group.Key.Path,
                group.Key.Filter,
                group.Value.RayCount,
                group.Value.TotalPower,
                totalPower > 0 ? group.Value.TotalPower / totalPower : 0,
                group.Value.MinimumOpticalPath,
                group.Value.TotalOpticalPath / group.Value.RayCount,
                group.Value.MaximumOpticalPath,
                group.Value.WavelengthCounts,
                group.Value.DetectorId,
                group.Value.TerminationReason))
            .OrderByDescending(item => item.TotalPowerWatts)
            .ThenByDescending(item => item.RayCount)
            .ToArray();
    }

    private static (string Path, string Filter) Key(
        NonSequentialRayBranch branch,
        IReadOnlyDictionary<Guid, (Guid Id, int Number, NonSequentialObjectKind Kind)> objectInfo)
    {
        var labels = new List<string>();
        var filters = new List<string>();
        if (branch.SourceObjectId is Guid source && objectInfo.TryGetValue(source, out var sourceInfo))
        {
            labels.Add($"Q{sourceInfo.Number}");
            filters.Add($"Q{sourceInfo.Number}");
        }
        foreach (var segment in branch.Segments)
        {
            if (segment.ObjectId is not Guid id || !objectInfo.TryGetValue(id, out var info)) continue;
            var code = info.Kind == NonSequentialObjectKind.DetectorRectangle
                ? "D"
                : segment.InteractionKind switch
                {
                    RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection => "R",
                    RayInteractionKind.Transmitted => "T",
                    _ => "H"
                };
            labels.Add($"{code}{info.Number}:F{segment.FaceNumber}");
            filters.Add($"{code}{info.Number}");
        }
        labels.Add(branch.TerminationReason.ToString());
        var filter = filters.Count == 0 ? TerminationFilter(branch.TerminationReason) : $"SEQ({string.Join(',', filters)})";
        return (string.Join(" → ", labels), filter);
    }

    private static string TerminationFilter(NonSequentialTerminationReason reason) => reason switch
    {
        NonSequentialTerminationReason.Absorbed => "A",
        NonSequentialTerminationReason.Escaped => "E",
        _ => "X"
    };

    private static int WavelengthNumber(NonSequentialDocument document, NonSequentialRayBranch branch)
    {
        var wavelength = branch.WavelengthNanometers > 0
            ? branch.WavelengthNanometers
            : branch.Segments.FirstOrDefault()?.WavelengthNanometers ?? 0;
        var index = document.Wavelengths.ToList().FindIndex(item => Math.Abs(item.Nanometers - wavelength) <= 1e-9);
        return Math.Max(1, index + 1);
    }

    private static bool IsTerminal(NonSequentialRayBranch branch) =>
        branch.TerminationReason != NonSequentialTerminationReason.Split;

    private sealed class PathAccumulator(
        NonSequentialTerminationReason terminationReason,
        Guid? detectorId)
    {
        public int RayCount { get; private set; }
        public double TotalPower { get; private set; }
        public double MinimumOpticalPath { get; private set; } = double.PositiveInfinity;
        public double TotalOpticalPath { get; private set; }
        public double MaximumOpticalPath { get; private set; } = double.NegativeInfinity;
        public Dictionary<int, int> WavelengthCounts { get; } = new();
        public Guid? DetectorId { get; } = detectorId;
        public NonSequentialTerminationReason TerminationReason { get; } = terminationReason;

        public void Add(double power, double opticalPath, int wavelengthNumber)
        {
            RayCount++;
            TotalPower += power;
            MinimumOpticalPath = Math.Min(MinimumOpticalPath, opticalPath);
            TotalOpticalPath += opticalPath;
            MaximumOpticalPath = Math.Max(MaximumOpticalPath, opticalPath);
            WavelengthCounts[wavelengthNumber] = WavelengthCounts.GetValueOrDefault(wavelengthNumber) + 1;
        }
    }
}
