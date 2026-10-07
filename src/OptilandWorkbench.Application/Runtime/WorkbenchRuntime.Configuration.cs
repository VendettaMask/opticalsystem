using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    public MeritConfigurationContext CreateMeritConfigurationContext() => new(
        _multiConfiguration.Configurations.Select((optic, index) =>
            index == _activeConfigurationIndex ? CurrentOptic : optic), _activeConfigurationIndex, _multiConfiguration.OperandRows);

    public IReadOnlyList<MultiConfigurationOperand> GetMultiConfigurationOperands() => _multiConfiguration.OperandRows.ToArray();

    public void ReplaceMultiConfigurationOperands(IReadOnlyList<MultiConfigurationOperand> rows) =>
        MutateConfigurationOperands(candidate => candidate.ReplaceOperandRows(rows));

    public void SetMultiConfigurationOperandValue(int row, int configurationIndex, double value) =>
        MutateConfigurationOperands(candidate => candidate.SetOperandValue(row, configurationIndex, value));

    public IReadOnlyList<MultiConfigurationVariable> GetMultiConfigurationVariables() => _multiConfiguration.OperandVariables.ToArray();

    public void SetMultiConfigurationOperandVariable(int row, int configurationIndex, bool enabled) =>
        MutateConfigurationOperands(candidate => candidate.SetOperandVariable(row, configurationIndex, enabled));

    public IReadOnlyList<MultiConfigurationPickup> GetMultiConfigurationPickups() => _multiConfiguration.OperandPickups.ToArray();

    public bool IsMultiConfigurationPickupTarget(int surface, string property) =>
        _multiConfiguration.IsOperandPickupTarget(_activeConfigurationIndex, surface, property);

    public void SetMultiConfigurationOperandPickup(int row, int configuration, int sourceRow, int sourceConfiguration, double scale, double offset) =>
        MutateConfigurationOperands(candidate => candidate.SetOperandPickup(row, configuration, sourceRow, sourceConfiguration, scale, offset));

    public void ClearMultiConfigurationOperandPickup(int row, int configuration) =>
        MutateConfigurationOperands(candidate => candidate.ClearOperandPickup(row, configuration));

    public void SynchronizeConfigurationPickups()
    {
        if (_multiConfiguration.OperandPickups.Count == 0) return;
        // Keep live surface references while resolving; detach storage before any later structural edit.
        var alreadyLive = ReferenceEquals(_multiConfiguration.Configurations[_activeConfigurationIndex], CurrentOptic);
        _multiConfiguration.Configurations[_activeConfigurationIndex] = CurrentOptic;
        try { _multiConfiguration.ApplyOperandPickups(); }
        finally
        {
            if (!alreadyLive)
                _multiConfiguration.Configurations[_activeConfigurationIndex] = CurrentOptic.Clone();
        }
    }

    public void ClearMultiConfigurationVariables() => _multiConfiguration.ClearOperandVariables();

    public void ClearGradientIndexVariables()
    {
        foreach (var optic in _multiConfiguration.Configurations.Append(CurrentOptic).Distinct())
        {
            var surfaces = optic.SurfaceGroup.Items;
            for (var index = 0; index < surfaces.Count; index++)
            {
                if (surfaces[index].MaterialAfter is not GradientIndexMaterial { Variables.Count: > 0 } material) continue;
                var replacement = new GradientIndexMaterial(material.Name, material.Profile, material.MaximumPathLength, material.IntegrationOptions);
                optic.InvalidateRayTraceCache();
                surfaces[index].MaterialAfter = replacement;
                if (index + 1 < surfaces.Count) surfaces[index + 1].MaterialBefore = replacement.Clone();
            }
        }
    }

    private void MutateConfigurationOperands(Action<MultiConfiguration> mutation)
    {
        SyncActiveConfigurationFromCurrent();
        var candidate = new MultiConfiguration(_multiConfiguration.Configurations, _multiConfiguration.BrokenLinks, _multiConfiguration.OperandRows, _multiConfiguration.OperandVariables, _multiConfiguration.OperandPickups);
        mutation(candidate);
        CaptureCurrentState();
        _multiConfiguration = candidate;
        CurrentOptic = candidate.Configurations[_activeConfigurationIndex].Clone();
        SetStatus("多配置操作数已更新。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<MultiConfigurationRow> GetMultiConfigurationRows()
    {
        SyncActiveConfigurationFromCurrent();
        return _multiConfiguration.Configurations
            .Select((optic, index) => new MultiConfigurationRow(
                index,
                $"配置 {index + 1}",
                index == _activeConfigurationIndex,
                optic.SurfaceGroup.Items.Count,
                NumericDisplayFormatter.Format(optic.SurfaceGroup.TotalTrack),
                OpticCapabilityPreflight.Inspect(optic).Count == 0
                    ? NumericDisplayFormatter.Format(optic.Paraxial.EstimateEffectiveFocalLength())
                    : "不可计算"))
            .ToArray();
    }

    public int AddMultiConfiguration()
    {
        CaptureCurrentState();
        var index = _multiConfiguration.AddConfiguration(_activeConfigurationIndex);
        SetStatus($"已添加配置 {index}。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
        return index;
    }

    public void ActivateMultiConfiguration(int configIndex)
    {
        if (configIndex < 0 || configIndex >= _multiConfiguration.Configurations.Count)
        {
            return;
        }

        if (configIndex == _activeConfigurationIndex)
        {
            return;
        }

        CaptureCurrentState();
        _activeConfigurationIndex = configIndex;
        CurrentOptic = _multiConfiguration.Configurations[configIndex].Clone();
        SetStatus($"已激活配置 {configIndex}。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMultiConfigurationThickness(int configIndex, int surfaceNumber, double thickness)
    {
        if (!double.IsFinite(thickness)) throw new ArgumentOutOfRangeException(nameof(thickness), "配置厚度必须是有限数值。");
        if (configIndex < 0 || configIndex >= _multiConfiguration.Configurations.Count)
        {
            return;
        }

        SyncActiveConfigurationFromCurrent();
        var surface = _multiConfiguration.Configurations[configIndex].SurfaceGroup.Items
            .SingleOrDefault(item => item.Number == surfaceNumber);
        if (surface is null)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
        }

        if (surface.Thickness.Equals(thickness))
        {
            return;
        }

        CaptureCurrentState();
        _multiConfiguration.SetThickness(configIndex, surfaceNumber, thickness);
        if (configIndex == 0)
        {
            _multiConfiguration.PropagateBaseLinks();
        }

        if (configIndex == _activeConfigurationIndex)
        {
            CurrentOptic = _multiConfiguration.Configurations[configIndex].Clone();
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        }

        SynchronizeConfigurationPickups();
        SetStatus($"配置 {configIndex} 表面 {surfaceNumber} 厚度已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }
}
