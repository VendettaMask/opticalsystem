using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace OptilandWorkbench.App.Controls;

public interface IReadOnlyChartAutomationSource
{
    string AutomationValue { get; }
}

internal sealed class ReadOnlyChartAutomationPeer : ControlAutomationPeer, IValueProvider
{
    private readonly IReadOnlyChartAutomationSource _source;

    public ReadOnlyChartAutomationPeer(Control owner) : base(owner)
    {
        _source = owner as IReadOnlyChartAutomationSource
            ?? throw new ArgumentException("Read-only chart must expose an automation source.", nameof(owner));
    }

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.Custom;

    bool IValueProvider.IsReadOnly => true;

    string IValueProvider.Value => _source.AutomationValue;

    void IValueProvider.SetValue(string? value) =>
        throw new InvalidOperationException("Chart automation values are read-only.");
}

internal static class ReadOnlyChartSummary
{
    public static string Series(string title, Application.Contracts.AnalysisSeriesDto? series)
    {
        if (series is null || series.Points.Count == 0)
        {
            return $"{title}；没有可用数据。";
        }

        var values = series.Points
            .Select(point => point.Value)
            .Where(value => value.HasValue && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .ToArray();
        var valueSummary = values.Length == 0
            ? "没有有限数值"
            : $"数值范围 {values.Min():G6} 到 {values.Max():G6}";
        return $"{title}；{series.Points.Count} 个数据点；{valueSummary}；"
            + $"X 轴 {series.XAxisLabel}；Y 轴 {series.YAxisLabel}。";
    }
}
