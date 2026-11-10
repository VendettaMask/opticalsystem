using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static readonly ConditionalWeakTable<object, List<LanguageBinding>> OwnedLanguageBindings = new();
    private static readonly List<WeakReference<LanguageBinding>> LanguageBindings = [];
    private static readonly object BindingGate = new();
    private static readonly AsyncLocal<string?> OutputLanguage = new();
    private static string _currentLanguage = Chinese;
    private static readonly LanguageSignal BindingLanguage = new();
    private sealed class LanguageSignal : INotifyPropertyChanged
    {
        public string Value => _currentLanguage;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Value)));
    }

    /// <summary>One-way UI fields only. Never use for editable names, comments, materials or numeric values.</summary>
    internal static BindingBase TranslateBinding(BindingBase source, bool analysisMetric = false) => new MultiBinding
    {
        Bindings = { source, new Binding(nameof(LanguageSignal.Value)) { Source = BindingLanguage } },
        Converter = OutputLanguage.Value is { } outputLanguage ? new UiValueConverter(analysisMetric, outputLanguage)
            : analysisMetric ? UiValueConverter.MetricInstance : UiValueConverter.Instance
    };
    private sealed class UiValueConverter(bool metric = false, string? outputLanguage = null) : IMultiValueConverter
    {
        internal static readonly UiValueConverter Instance = new();
        internal static readonly UiValueConverter MetricInstance = new(true);
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 1 && values[0] is string source && values[1] is string language
                ? metric ? AnalysisMetric(source, outputLanguage ?? language) : Text(source, outputLanguage ?? language) : values.FirstOrDefault();
    }

    /// <summary>Bind presentation to language without retaining its owner or replacing its data/controls.</summary>
    internal static IDisposable BindLanguage(object owner, Action refresh)
    {
        var binding = new LanguageBinding(refresh);
        lock (BindingGate)
        {
            OwnedLanguageBindings.GetOrCreateValue(owner).Add(binding);
            LanguageBindings.Add(new(binding));
        }
        binding.Refresh();
        return binding;
    }

    private static void RefreshLanguageBindings()
    {
        BindingLanguage.Refresh();
        LanguageBinding[] bindings;
        lock (BindingGate)
        {
            LanguageBindings.RemoveAll(reference => !reference.TryGetTarget(out var binding) || binding.IsDisposed);
            bindings = LanguageBindings.Select(reference => reference.TryGetTarget(out var binding) ? binding : null)
                .OfType<LanguageBinding>().ToArray();
        }
        foreach (var binding in bindings) binding.Refresh();
    }

    private sealed class LanguageBinding(Action refresh) : IDisposable
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly string? _outputLanguage = OutputLanguage.Value;
        private Action? _refresh = refresh;
        internal bool IsDisposed => _refresh is null;
        internal void Refresh()
        {
            if (_thread != Environment.CurrentManagedThreadId || _refresh is null) return;
            using var scope = new OutputLanguageScope(_outputLanguage);
            _refresh();
        }
        public void Dispose() => _refresh = null;
    }

    /// <summary>Capture once at the start of rendering/export; never change the user's UI language.</summary>
    internal static IDisposable UseLanguage(string language) => new OutputLanguageScope(NormalizeLanguage(language));

    /// <summary>Localize a fixed interpolated template, retaining each argument's original format and data.</summary>
    internal static string Template(FormattableString source)
    {
        var values = new List<object?>();
        var template = new StringBuilder();
        for (var index = 0; index < source.Format.Length; index++)
        {
            var character = source.Format[index];
            if (character is '{' or '}' && index + 1 < source.Format.Length && source.Format[index + 1] == character)
            { template.Append(character, 2); index++; continue; }
            if (character != '{') { template.Append(character); continue; }
            var end = source.Format.IndexOf('}', index + 1);
            if (end < 0) throw new FormatException("Invalid interpolated UI template.");
            var specifier = source.Format[(index + 1)..end];
            var digits = specifier.TakeWhile(char.IsAsciiDigit).Count();
            var argument = int.Parse(specifier[..digits], CultureInfo.InvariantCulture);
            values.Add(string.Format(CultureInfo.CurrentCulture, "{0" + specifier[digits..] + "}", source.GetArgument(argument)));
            template.Append('{').Append(values.Count - 1).Append('}');
            index = end;
        }
        return string.Format(CultureInfo.InvariantCulture, Text(template.ToString()), values.ToArray());
    }

    internal static string AnalysisMetric(string source) => AnalysisMetric(source, CurrentLanguage);
    private static string AnalysisMetric(string source, string language) =>
        source.StartsWith("场曲.", StringComparison.Ordinal) || source.StartsWith("畸变.", StringComparison.Ordinal)
            ? string.Join(".", source.Split('.').Select(part => Text(part, language))) : Text(source, language);

    /// <summary>Localize generated analysis keys and known categorical values; keep raw data opaque.</summary>
    internal static string AnalysisReport(string source) => string.Join(Environment.NewLine,
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line =>
        {
            var separator = line.IndexOf('：');
            if (separator < 0) return Text(line);
            var key = line[..separator];
            var value = line[(separator + 1)..];
            var displayKey = AnalysisMetric(key);
            var field = key.Split('.').Last();
            if (field is "分析" or "参考" or "方法" or "颜色显示" or "显示缩放" or "显示为" or "Type" or "偏振")
                value = Text(value);
            return displayKey + (CurrentLanguage == English ? ": " : "：") + value;
        }));

    private sealed class DefaultTextBinding(TextBox input)
    {
        internal string Source = string.Empty;
        internal string? Last;
        internal void Refresh()
        {
            if (input.Text != Last) return; // An actual edit owns the text from this point.
            input.Text = Last = Text(Source);
        }
    }
    private static readonly ConditionalWeakTable<TextBox, DefaultTextBinding> DefaultTextBindings = new();
    internal static string DisplayedDefault(TextBox input, string displayed) =>
        DefaultTextBindings.TryGetValue(input, out var state) && input.Text == state.Last
            && new[] { Chinese, English, Japanese }.Any(language => displayed == Text(state.Source, language))
                ? state.Last ?? displayed : displayed;
    internal static void SetDefaultText(TextBox input, string source)
    {
        var binding = DefaultTextBindings.GetValue(input, value =>
        {
            var state = new DefaultTextBinding(value);
            BindLanguage(value, state.Refresh);
            return state;
        });
        binding.Source = source;
        input.Text = binding.Last = Text(source);
    }
    private sealed class OutputLanguageScope : IDisposable
    {
        private readonly string? _previous = OutputLanguage.Value;
        internal OutputLanguageScope(string? language) => OutputLanguage.Value = language;
        public void Dispose() => OutputLanguage.Value = _previous;
    }

    /// <summary>Only fixed UI choices opt in. Items, selected values and persistence keys remain canonical.</summary>
    internal static void BindChoices<T>(ComboBox combo, Func<T, string> label)
    {
        combo.ItemTemplate = new FuncDataTemplate<T>((item, _) =>
        {
            var text = Preserve(new TextBlock());
            BindLanguage(text, () => text.Text = item is null ? string.Empty : Text(label(item)));
            return text;
        });
    }

    internal static void BindChoices(ComboBox combo) => BindChoices<string>(combo, value => value);

    private sealed class GridLanguageBinding
    {
        private readonly DataGrid _grid;
        private readonly Dictionary<DataGridColumn, PropertyTranslation> _headers = [];
        private bool _applying;
        private readonly string? _outputLanguage = OutputLanguage.Value;
        internal GridLanguageBinding(DataGrid grid)
        {
            _grid = grid;
            grid.Columns.CollectionChanged += (_, _) => Refresh();
            Refresh();
        }
        internal void Refresh()
        {
            if (_applying || _grid.GetValue(PreserveTextProperty)) return;
            _applying = true;
            var language = _outputLanguage ?? _currentLanguage;
            try
            {
                foreach (var old in _headers.Keys.Except(_grid.Columns).ToArray())
                { old.PropertyChanged -= Changed; _headers.Remove(old); }
                foreach (var column in _grid.Columns)
                {
                    if (column.Header is not string current) continue;
                    if (!_headers.TryGetValue(column, out var state))
                    {
                        _headers[column] = state = new() { Source = current };
                        column.PropertyChanged += Changed;
                    }
                    else if (current != state.LastApplied && current != state.Source) state.Source = current;
                    if (state.Language == language && current == state.LastApplied) continue;
                    state.Language = language;
                    state.LastApplied = Text(state.Source, language);
                    column.SetCurrentValue(DataGridColumn.HeaderProperty, state.LastApplied);
                }
            }
            finally { _applying = false; }
        }
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == DataGridColumn.HeaderProperty) Refresh(); }
    }
    private static readonly ConditionalWeakTable<DataGrid, GridLanguageBinding> GridBindings = new();
}
