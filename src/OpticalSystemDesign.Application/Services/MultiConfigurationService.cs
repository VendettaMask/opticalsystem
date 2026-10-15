using OpticalSystemDesign.Core.Multiconfig;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Visualization;
using ContractAnalysisColorMap = OpticalSystemDesign.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OpticalSystemDesign.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OpticalSystemDesign.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OpticalSystemDesign.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OpticalSystemDesign.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OpticalSystemDesign.Application.Contracts.AnalysisSeriesKind;

namespace OpticalSystemDesign.Application.Services;

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
