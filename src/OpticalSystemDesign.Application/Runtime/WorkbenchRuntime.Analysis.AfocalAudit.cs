using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Application.Runtime;

public sealed partial class WorkbenchRuntime
{
    private static PsfImageSummaryDto BuildPsfImageSummary(AnalysisData data)
    {
        var angular = data.PlotSeries.First().XUnit == Core.Analysis.AnalysisAxisUnit.Milliradian;
        var suffix = angular ? "Milliradians" : "Micrometers";
        return new PsfImageSummaryDto(
            (string)data.Values["WavelengthRange"],
            (double)data.Values["FieldHy"],
            (double)data.Values["ImageExtent" + suffix],
            (double)data.Values["PeakStrehlRatio"],
            (double)data.Values["CentroidX" + suffix],
            (double)data.Values["CentroidY" + suffix],
            angular ? Contracts.AnalysisAxisUnit.Milliradian : Contracts.AnalysisAxisUnit.Micrometer);
    }
}
