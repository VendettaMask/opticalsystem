using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;

namespace OptilandWorkbench.Application.Services;

internal sealed class MultiConfigurationService : WorkbenchServiceBase, IMultiConfigurationService
{
    public MultiConfigurationService(WorkspaceCoordinator workspace)
        : base(workspace)
    {
    }

    public IReadOnlyList<MultiConfigurationRowDto> GetRows()
    {
        lock (Gate)
        {
            return Connector.GetMultiConfigurationRows().Select(row => new MultiConfigurationRowDto(
                row.Index,
                row.Name,
                row.Active,
                row.SurfaceCount,
                row.TotalTrack,
                row.EffectiveFocalLength)).ToArray();
        }
    }

    public int Add() => Mutate(WorkspaceChangeCategory.Configuration, Connector.AddMultiConfiguration);

    public void Activate(int configurationIndex)
    {
        Workspace.CancelDocumentTasks();
        Mutate(
            WorkspaceChangeCategory.Configuration,
            () => Connector.ActivateMultiConfiguration(configurationIndex));
    }

    public void SetThickness(int configurationIndex, int surfaceNumber, double thickness) => Mutate(
        WorkspaceChangeCategory.Configuration,
        () => Connector.SetMultiConfigurationThickness(configurationIndex, surfaceNumber, thickness));
}
