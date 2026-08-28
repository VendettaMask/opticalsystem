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
        CurrentOptic = Optic.FromSnapshot(_multiConfiguration.Configurations[configIndex].ToSnapshot());
        SetStatus($"已激活配置 {configIndex}。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMultiConfigurationThickness(int configIndex, int surfaceNumber, double thickness)
    {
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

        if (surface.Thickness.Equals(Math.Max(0, thickness)))
        {
            return;
        }

        CaptureCurrentState();
        _multiConfiguration.SetThickness(configIndex, surfaceNumber, Math.Max(0, thickness));
        if (configIndex == 0)
        {
            _multiConfiguration.PropagateBaseLinks();
        }

        if (configIndex == _activeConfigurationIndex)
        {
            CurrentOptic = Optic.FromSnapshot(_multiConfiguration.Configurations[configIndex].ToSnapshot());
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        }

        SetStatus($"配置 {configIndex} 表面 {surfaceNumber} 厚度已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }
}
