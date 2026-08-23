using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
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
    public void NewBlank()
    {
        ReplaceOptic(Optic.CreateBlank(), "已创建空白光学系统。");
    }

    public void NewDemo()
    {
        ReplaceOptic(Optic.CreateCookeTriplet(), "已创建与 Optiland 官方样例一致的 Cooke 三片式镜头。");
    }

    public void NewTessar()
    {
        ReplaceOptic(Optic.CreateTessarLens(), "已创建 Optiland 官方 Tessar F/4.5 四片式镜头。");
    }

    private void ReplaceOptic(Optic optic, string status)
    {
        CurrentOptic = optic;
        _multiConfiguration = new MultiConfiguration(CurrentOptic);
        _activeConfigurationIndex = 0;
        _undoRedo.Clear();
        SetStatus(status);
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void CaptureCurrentState()
    {
        _undoRedo.Capture(CurrentOptic);
    }

    public void CommitSurfaceEdit(OpticalSurface? surface, string? propertyName)
    {
        if (surface is null || !Surfaces.Contains(surface))
        {
            return;
        }

        switch (propertyName)
        {
            case nameof(OpticalSurface.Thickness):
                CurrentOptic.SurfaceGroup.Renumber();
                break;
            case nameof(OpticalSurface.Material):
                ApplyMaterial(surface, surface.Material);
                break;
            case nameof(OpticalSurface.IsStop) when surface.IsStop:
                foreach (var other in Surfaces.Where(item => !ReferenceEquals(item, surface)))
                {
                    other.IsStop = false;
                }

                break;
        }

        CurrentOptic.Pickups.ApplyAll();
        CurrentOptic.Solves.ApplyAll();
        CurrentOptic.SurfaceGroup.Renumber();
        SetStatus("表面数据已更新。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CommitSystemEdit(object? editedItem = null)
    {
        SetPrimaryWavelengthGuard(editedItem as Wavelength);
        SetStatus("系统属性已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddSurface()
    {
        CaptureCurrentState();
        var insertedSurfaceNumber = Math.Max(0, Surfaces.Count - 1);
        CurrentOptic.Pickups.InsertSurface(insertedSurfaceNumber);
        CurrentOptic.SurfaceGroup.AddDefaultSurface();
        SetStatus("已添加表面。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveSurface(OpticalSurface? surface)
    {
        if (surface is null)
        {
            return;
        }

        var index = Surfaces.IndexOf(surface);
        if (Surfaces.Count <= 2 || index <= 0 || index == Surfaces.Count - 1)
        {
            SetStatus("物面和像面不能删除，系统必须至少保留两个表面。");
            OpticChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        CaptureCurrentState();
        CurrentOptic.Pickups.RemoveSurface(index);
        CurrentOptic.SurfaceGroup.Remove(surface);
        SetStatus("已删除表面。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySurfaceComponents(
        OpticalSurface? surface,
        string geometryKind,
        string physicalApertureKind,
        int gratingOrder = 1,
        double gratingPeriodMicrometers = 1,
        double grooveOrientationAngleDegrees = 0,
        double thinLensFocalLength = 50)
    {
        if (surface is null)
        {
            return;
        }

        CaptureCurrentState();
        ApplyGeometry(surface, geometryKind);
        SyncInteractionForEditedGeometry(surface, thinLensFocalLength);
        ApplyGratingParameters(
            surface,
            gratingOrder,
            gratingPeriodMicrometers,
            grooveOrientationAngleDegrees);
        ApplyPhysicalAperture(surface, physicalApertureKind);
        SetStatus($"表面 {surface.Number} 组件已更新。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySurfaceComponents(
        OpticalSurface? surface,
        string geometryKind,
        string materialName,
        string coatingKind,
        string interactionKind,
        string physicalApertureKind,
        int gratingOrder = 1,
        double gratingPeriodMicrometers = 1,
        double grooveOrientationAngleDegrees = 0,
        double thinLensFocalLength = 50)
    {
        if (surface is null)
        {
            return;
        }

        CaptureCurrentState();
        ApplyGeometry(surface, geometryKind);
        ApplyMaterial(surface, materialName);
        ApplyCoating(surface, coatingKind);
        ApplyInteraction(surface, interactionKind, thinLensFocalLength);
        ApplyGratingParameters(
            surface,
            gratingOrder,
            gratingPeriodMicrometers,
            grooveOrientationAngleDegrees);
        ApplyPhysicalAperture(surface, physicalApertureKind);
        SetStatus($"表面 {surface.Number} 组件已更新。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddField()
    {
        CaptureCurrentState();
        Fields.Add(new FieldPoint
        {
            Label = $"视场 {Fields.Count}",
            Y = Fields.Count * 4,
            Weight = 1
        });
        SetStatus("已添加视场。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveField(FieldPoint? field)
    {
        if (field is null || !Fields.Contains(field))
        {
            return;
        }

        if (Fields.Count <= 1)
        {
            SetStatus("系统必须至少保留一个视场。");
            OpticChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        CaptureCurrentState();
        Fields.Remove(field);
        SetStatus("已删除视场。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddWavelength()
    {
        CaptureCurrentState();
        Wavelengths.Add(new Wavelength
        {
            Label = $"W{Wavelengths.Count + 1}",
            Nanometers = 550,
            Weight = 1,
            IsPrimary = Wavelengths.Count == 0
        });
        SetPrimaryWavelengthGuard();
        SetStatus("已添加波长。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveWavelength(Wavelength? wavelength)
    {
        if (wavelength is null || !Wavelengths.Contains(wavelength))
        {
            return;
        }

        if (Wavelengths.Count <= 1)
        {
            SetStatus("系统必须至少保留一个波长。");
            OpticChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        CaptureCurrentState();
        Wavelengths.Remove(wavelength);
        SetPrimaryWavelengthGuard();
        SetStatus("已删除波长。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySystemSettings(
        string? backendName,
        string apertureKindName,
        double apertureValue,
        string fieldDefinitionName,
        bool objectSpaceTelecentric,
        string apodizationKind,
        double firstApodizationParameter,
        double secondApodizationParameter)
    {
        CaptureCurrentState();
        ApplyBackendValue(backendName);
        ApplySystemApertureValue(apertureKindName, apertureValue);
        ApplyFieldDefinitionValue(fieldDefinitionName, objectSpaceTelecentric);
        ApplyApodizationValue(apodizationKind, firstApodizationParameter, secondApodizationParameter);
        SetStatus("系统设置已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSystemAperture(string apertureKindName, double value)
    {
        if (!TryNormalizeApertureKind(apertureKindName, out var kind))
        {
            return;
        }

        CaptureCurrentState();
        ApplySystemApertureValue(kind, value);
        SetStatus("系统孔径已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFieldDefinition(string fieldDefinitionName, bool objectSpaceTelecentric)
    {
        CaptureCurrentState();
        ApplyFieldDefinitionValue(fieldDefinitionName, objectSpaceTelecentric);
        SetStatus("视场定义已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetApodization(string apodizationKind, double firstParameter, double secondParameter)
    {
        CaptureCurrentState();
        ApplyApodizationValue(apodizationKind, firstParameter, secondParameter);
        SetStatus("光瞳切趾已更新。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetBackend(string backendName)
    {
        if (!IsValidBackend(backendName))
        {
            return;
        }

        CaptureCurrentState();
        ApplyBackendValue(backendName);
        SetStatus($"后端已切换为 {backendName}。");
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }
}
