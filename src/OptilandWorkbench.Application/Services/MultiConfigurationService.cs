using OptilandWorkbench.Core.Multiconfig;
using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
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

    public IReadOnlyList<MultiConfigurationOperandRowDto> GetOperandRows()
    {
        lock (Gate)
        {
            var context = Runtime.CreateMeritConfigurationContext();
            var variables = Runtime.GetMultiConfigurationVariables().ToHashSet();
            var pickups = Runtime.GetMultiConfigurationPickups().ToDictionary(p => (p.ConfigurationIndex, p.Operand));
            var rowNumbers = context.OperandRows.Select((row, index) => (row, index)).ToDictionary(p => p.row, p => p.index + 1);
            return context.OperandRows.Select((row, index) => new MultiConfigurationOperandRowDto(index + 1,
                (MultiConfigurationParameterKind)row.Kind, row.SurfaceNumber, context.Configurations.Select(row.Read).ToArray(),
                context.Configurations.Select((_, configuration) => variables.Contains(new(configuration, row))).ToArray(),
                context.Configurations.Select((_, configuration) => pickups.TryGetValue((configuration, row), out var pickup)
                    ? new MultiConfigurationPickupDto(rowNumbers[pickup.SourceOperand], pickup.SourceConfigurationIndex, pickup.Scale, pickup.Offset) : null).ToArray())).ToArray();
        }
    }

    public void ReplaceOperandRows(IReadOnlyList<MultiConfigurationOperandBindingDto> rows, long expectedRevision) =>
        MutateTransactional(WorkspaceChangeCategory.Configuration, () =>
        {
            if (expectedRevision != Workspace.Revision) throw new InvalidOperationException("工程已变化，请刷新多配置行表后重试。");
            Runtime.ReplaceMultiConfigurationOperands(rows.Select(row => new MultiConfigurationOperand(
                (MultiConfigurationOperandKind)row.Kind, row.SurfaceNumber)).ToArray());
        });

    public void SetOperandValue(int oneBasedRow, int configurationIndex, double value, long expectedRevision) =>
        MutateTransactional(WorkspaceChangeCategory.Configuration, () =>
        {
            if (expectedRevision != Workspace.Revision) throw new InvalidOperationException("工程已变化，请刷新多配置行表后重试。");
            Runtime.SetMultiConfigurationOperandValue(oneBasedRow, configurationIndex, value);
        });

    public void SetOperandVariable(int oneBasedRow, int configurationIndex, bool enabled, long expectedRevision) =>
        MutateTransactional(WorkspaceChangeCategory.Configuration, () =>
        {
            if (expectedRevision != Workspace.Revision) throw new InvalidOperationException("工程已变化，请刷新多配置行表后重试。");
            Runtime.SetMultiConfigurationOperandVariable(oneBasedRow, configurationIndex, enabled);
        });

    public void SetOperandPickup(int oneBasedRow, int configurationIndex, MultiConfigurationPickupDto? pickup, long expectedRevision) =>
        MutateTransactional(WorkspaceChangeCategory.Configuration, () =>
        {
            if (expectedRevision != Workspace.Revision) throw new InvalidOperationException("工程已变化，请刷新多配置行表后重试。");
            if (pickup is null) Runtime.ClearMultiConfigurationOperandPickup(oneBasedRow, configurationIndex);
            else Runtime.SetMultiConfigurationOperandPickup(oneBasedRow, configurationIndex, pickup.SourceRow, pickup.SourceConfigurationIndex, pickup.Scale, pickup.Offset);
        });

    public IReadOnlyList<MultiConfigurationRowDto> GetRows()
    {
        lock (Gate)
        {
            return Runtime.GetMultiConfigurationRows().Select(row => new MultiConfigurationRowDto(
                row.Index,
                row.Name,
                row.Active,
                row.SurfaceCount,
                row.TotalTrack,
                row.EffectiveFocalLength)).ToArray();
        }
    }

    public int Add() => MutateTransactional(
        WorkspaceChangeCategory.Configuration,
        Runtime.AddMultiConfiguration);

    public void Activate(int configurationIndex)
    {
        Workspace.CancelDocumentTasks();
        MutateTransactional(
            WorkspaceChangeCategory.Configuration,
            () => Runtime.ActivateMultiConfiguration(configurationIndex));
    }

    public void SetThickness(int configurationIndex, int surfaceNumber, double thickness) => MutateTransactional(
        WorkspaceChangeCategory.Configuration,
        () => Runtime.SetMultiConfigurationThickness(configurationIndex, surfaceNumber, thickness));
}
