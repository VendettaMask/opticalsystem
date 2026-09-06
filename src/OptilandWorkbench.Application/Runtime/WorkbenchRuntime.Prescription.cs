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
        ReplaceOptic(Optic.CreateCookeTriplet(), "已创建 Cooke 三片式镜头示例。");
    }

    public void NewTessar()
    {
        ReplaceOptic(Optic.CreateTessarLens(), "已创建 Tessar F/4.5 四片式镜头示例。");
    }

    private void ReplaceOptic(Optic optic, string status)
    {
        CurrentOptic = optic;
        _multiConfiguration = new MultiConfiguration(CurrentOptic);
        _activeConfigurationIndex = 0;
        _nonSequentialDocument = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        _undoRedo.Clear();
        SetStatus(status);
        OpticLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void CaptureCurrentState()
    {
        _undoRedo.Capture(CaptureDocument());
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
        switch (propertyName)
        {
            case nameof(OpticalSurface.Radius):
                SynchronizeMultiConfigurationProperty(surface, "radius");
                break;
            case nameof(OpticalSurface.Thickness):
                SynchronizeMultiConfigurationProperty(surface, "thickness");
                break;
            case nameof(OpticalSurface.Conic):
                SynchronizeMultiConfigurationProperty(surface, "conic");
                break;
            case nameof(OpticalSurface.Material):
                SynchronizeMultiConfigurationProperty(surface, "material");
                break;
        }

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
        SyncActiveConfigurationFromCurrent();
        var insertedSurfaceNumber = _multiConfiguration.AddSurfaceBeforeImage();
        CurrentOptic.Pickups.InsertSurface(insertedSurfaceNumber);
        CurrentOptic.SurfaceGroup.AddDefaultSurface();
        SyncActiveConfigurationFromCurrent();
        SetStatus("已添加表面。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }

    public int InsertSurface(int surfaceNumber, bool after)
    {
        if (surfaceNumber < 0 || surfaceNumber >= Surfaces.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "所选表面不存在。");
        }
        var insertedSurfaceNumber = surfaceNumber + (after ? 1 : 0);
        if (insertedSurfaceNumber <= 0 || insertedSurfaceNumber >= Surfaces.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "不能在物面之前或像面之后插入。");
        }

        CaptureCurrentState();
        SyncActiveConfigurationFromCurrent();
        _multiConfiguration.InsertSurface(insertedSurfaceNumber);
        CurrentOptic.Pickups.InsertSurface(insertedSurfaceNumber);
        CurrentOptic.SurfaceGroup.InsertDefaultSurface(insertedSurfaceNumber);
        SyncActiveConfigurationFromCurrent();
        SetStatus($"已在表面 {surfaceNumber} {(after ? "下方" : "上方")}插入表面。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
        return insertedSurfaceNumber;
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
            return;
        }

        CaptureCurrentState();
        SyncActiveConfigurationFromCurrent();
        _multiConfiguration.RemoveSurface(index);
        CurrentOptic.Pickups.RemoveSurface(index);
        CurrentOptic.SurfaceGroup.Remove(surface);
        SyncActiveConfigurationFromCurrent();
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
        double thinLensFocalLength = 50,
        bool? isStop = null,
        string? coating = null,
        bool? semiDiameterFixed = null,
        double? semiDiameter = null)
    {
        if (surface is null)
        {
            return;
        }

        if (surface.Geometry is INonComputableGeometry)
        {
            throw new NotSupportedException("该导入面型只读，不能编辑表面属性。");
        }
        if (isStop == true && (ReferenceEquals(surface, Surfaces[0]) || ReferenceEquals(surface, Surfaces[^1])))
        {
            throw new ArgumentException("物面和像面不能设为光阑。", nameof(isStop));
        }
        if (isStop == false && surface.IsStop)
        {
            throw new ArgumentException("请选择另一个表面作为光阑，不能直接移除当前光阑。", nameof(isStop));
        }
        if (semiDiameter.HasValue && (!double.IsFinite(semiDiameter.Value) || semiDiameter.Value < 0.1))
        {
            throw new ArgumentOutOfRangeException(nameof(semiDiameter), "净半径必须是至少 0.1 mm 的有限数值。");
        }

        CaptureCurrentState();
        ApplyGeometry(surface, geometryKind);
        SyncInteractionForEditedGeometry(surface, thinLensFocalLength);
        ApplyGratingParameters(
            surface,
            gratingOrder,
            gratingPeriodMicrometers,
            grooveOrientationAngleDegrees);
        if (coating is not null && coating != surface.Coating)
        {
            surface.Coating = string.IsNullOrWhiteSpace(coating) ? "None" : coating.Trim();
        }
        if (isStop == true)
        {
            foreach (var candidate in Surfaces)
            {
                candidate.IsStop = ReferenceEquals(candidate, surface);
            }
        }
        if (semiDiameterFixed.HasValue)
        {
            surface.SemiDiameterFixed = semiDiameterFixed.Value;
        }
        if (surface.SemiDiameterFixed && semiDiameter.HasValue)
        {
            surface.SemiDiameter = semiDiameter.Value;
        }
        ApplyPhysicalAperture(surface, physicalApertureKind);
        SynchronizeMultiConfigurationProperty(surface, "radius");
        SynchronizeMultiConfigurationProperty(surface, "conic");
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
        SynchronizeMultiConfigurationProperty(surface, "radius");
        SynchronizeMultiConfigurationProperty(surface, "conic");
        SynchronizeMultiConfigurationProperty(surface, "material");
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
        double secondApodizationParameter,
        bool imageSpaceAfocal = false)
    {
        CaptureCurrentState();
        ApplyBackendValue(backendName);
        ApplySystemApertureValue(apertureKindName, apertureValue);
        ApplyFieldDefinitionValue(fieldDefinitionName, objectSpaceTelecentric);
        ApplyApodizationValue(apodizationKind, firstApodizationParameter, secondApodizationParameter);
        CurrentOptic.ImageSpaceAfocal = imageSpaceAfocal;
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
