using Avalonia.Controls;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class AnalysisPanel
{
    internal static ResponsiveAnalysisPlotHost? CreateReportPlot(AnalysisResultDto result,
        OpticalDocumentSnapshot document, DateTimeOffset generatedAt)
    {
        var view = result.View;
        if (!HasReportPlot(result)) return null;
        var content = BuildResultContent(view, document, generatedAt, result.ReferenceScene);
        if (content is ResponsiveAnalysisPlotHost page) return page;
        if (content is TabControl { ItemsSource: object[] tabs }
            && tabs[0] is TabItem { Content: ResponsiveAnalysisPlotHost plot } tab)
        {
            tab.Content = null;
            return plot;
        }
        return null;
    }

    internal static bool HasReportPlot(AnalysisResultDto result)
    {
        var view = result.View;
        return view.Series.Any(series => series.Points.Count > 0)
            || view.PlotPanes.Any(pane => pane.Series.Any(series => series.Points.Count > 0))
            || view.PresentationKind == AnalysisPresentationKind.SeidelDiagram && view.Table?.Rows.Count > 0
            || view.PresentationKind == AnalysisPresentationKind.CardinalPoints && result.ReferenceScene?.TwoDimensional is not null;
    }
}
