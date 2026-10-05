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
using static OptilandWorkbench.Application.Services.WorkbenchMapper;

namespace OptilandWorkbench.Application.Services;

internal sealed partial class PrescriptionService : WorkbenchServiceBase, IPrescriptionService
{
    public PrescriptionService(WorkspaceCoordinator workspace)
        : base(workspace)
    {
    }

    public PrescriptionOptionsDto GetOptions()
    {
        lock (Gate)
        {
            return new PrescriptionOptionsDto(
                Runtime.BackendNames,
                Runtime.ApertureKindNames,
                Runtime.FieldDefinitionNames,
                Runtime.ApodizationKinds,
                Runtime.GeometryKinds,
                Runtime.MaterialNames,
                Runtime.CoatingKinds,
                Runtime.InteractionKinds,
                Runtime.PhysicalApertureKinds);
        }
    }

    public IReadOnlyList<SurfaceRowDto> GetSurfaces()
    {
        lock (Gate)
        {
            return Runtime.Surfaces.Select(surface => ToSurfaceDto(surface) with
            {
                RadiusSolve = Runtime.GetRadiusSolve(surface.Number),
                ThicknessSolve = Runtime.GetThicknessSolve(surface.Number),
                SemiDiameterSolve = Runtime.GetSemiDiameterSolve(surface.Number)
            }).ToArray();
        }
    }

    public SystemSettingsDto GetSystemSettings()
    {
        lock (Gate)
        {
            var optic = Runtime.CurrentOptic;
            var (apodizationKind, first, second) = ToApodizationSettings(optic.Apodization);
            return new SystemSettingsDto(
                optic.Backend.Current.Name,
                optic.Aperture.Kind switch
                {
                    ApertureKind.FNumber => "像方 F 数",
                    ApertureKind.NumericalAperture => "物方数值孔径",
                    ApertureKind.FloatByStopSize => "按光阑面尺寸浮动",
                    _ => "入瞳直径"
                },
                optic.Aperture.Kind == ApertureKind.FloatByStopSize
                    ? optic.SurfaceGroup.ApertureRadius()
                    : optic.Aperture.Value,
                optic.FieldDefinition switch
                {
                    FieldDefinitionKind.ObjectHeight => "物高",
                    FieldDefinitionKind.ParaxialImageHeight => "近轴像高",
                    FieldDefinitionKind.RealImageHeight => "实际像高",
                    _ => "角度"
                },
                optic.ObjectSpaceTelecentric,
                apodizationKind,
                first,
                second,
                optic.ImageSpaceAfocal);
        }
    }

    public PolarizationSettingsDto GetPolarizationSettings()
    {
        lock (Gate)
        {
            var p = Runtime.CurrentOptic.Polarization;
            return new(p.Unpolarized, p.Jx, p.Jy, p.XPhaseDegrees, p.YPhaseDegrees, p.ReferenceAxis.ToString());
        }
    }

    public void UpdatePolarizationSettings(PolarizationSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ReferenceAxis is not ("X" or "Y" or "Z"))
            throw new ArgumentException("偏振参考轴须为 X、Y 或 Z。");
        var state = new OptilandWorkbench.Core.Rays.SystemPolarization(settings.Unpolarized, settings.Jx, settings.Jy,
            settings.XPhaseDegrees, settings.YPhaseDegrees,
            Enum.Parse<OptilandWorkbench.Core.Rays.PolarizationReferenceAxis>(settings.ReferenceAxis));
        state.Validate();
        MutateTransactional(WorkspaceChangeCategory.SystemSettings, () =>
        {
            Runtime.CaptureCurrentState();
            Runtime.CurrentOptic.Polarization = state;
            Runtime.CommitSystemEdit();
        });
    }

    public EnvironmentSettingsDto GetEnvironmentSettings()
    {
        lock (Gate)
        {
            var environment = Runtime.CurrentOptic.Environment;
            return new EnvironmentSettingsDto(
                environment.MatchRefractiveIndexData,
                environment.TemperatureCelsius,
                environment.PressureAtmospheres);
        }
    }

    public IReadOnlyList<string> GetGlassCatalogs()
    {
        lock (Gate)
        {
            return Runtime.CurrentOptic.GlassCatalogs.ToArray();
        }
    }

    public IReadOnlyList<FieldRowDto> GetFields()
    {
        lock (Gate)
        {
            return Runtime.Fields.Select((field, index) => new FieldRowDto(
                index,
                field.Label,
                field.X,
                field.Y,
                field.VignetteFactorX,
                field.VignetteFactorY,
                field.Weight,
                field.VignetteDecenterX,
                field.VignetteDecenterY,
                field.VignetteAngleDegrees)).ToArray();
        }
    }

    public IReadOnlyList<WavelengthRowDto> GetWavelengths()
    {
        lock (Gate)
        {
            return Runtime.Wavelengths.Select((wavelength, index) => new WavelengthRowDto(
                index,
                wavelength.Label,
                wavelength.Nanometers,
                wavelength.Weight,
                wavelength.IsPrimary)).ToArray();
        }
    }

    public void AddSurface() => MutateTransactional(WorkspaceChangeCategory.Surface, Runtime.AddSurface);

    public void SetRadiusSolve(int surfaceNumber, RadiusSolveUpdateDto update, long? expectedRevision = null) =>
        MutateTransactional(WorkspaceChangeCategory.Surface, () =>
        {
            if (expectedRevision.HasValue && expectedRevision != Workspace.Revision)
                throw new InvalidOperationException("工程已变化，请重新打开求解设置。");
            Runtime.SetRadiusSolve(surfaceNumber, update);
        });

    public void SetThicknessSolve(int surfaceNumber, ThicknessSolveUpdateDto update, long? expectedRevision = null) =>
        MutateTransactional(WorkspaceChangeCategory.Surface, () =>
        {
            if (expectedRevision.HasValue && expectedRevision != Workspace.Revision)
                throw new InvalidOperationException("工程已变化，请重新打开求解设置。");
            Runtime.SetThicknessSolve(surfaceNumber, update);
        });

    public void SetSemiDiameterSolve(int surfaceNumber, SemiDiameterSolveUpdateDto update, long? expectedRevision = null) =>
        MutateTransactional(WorkspaceChangeCategory.Surface, () =>
        {
            if (expectedRevision.HasValue && expectedRevision != Workspace.Revision)
                throw new InvalidOperationException("工程已变化，请重新打开求解设置。");
            Runtime.SetSemiDiameterSolve(surfaceNumber, update);
        });

    public int InsertSurface(int surfaceNumber, bool after) => MutateTransactional(
        WorkspaceChangeCategory.Surface,
        () => Runtime.InsertSurface(surfaceNumber, after));

    public void RemoveSurface(int surfaceNumber) => MutateTransactional(
        WorkspaceChangeCategory.Surface,
        () => Runtime.RemoveSurface(FindSurface(surfaceNumber)));

    public void UpdateSurface(SurfaceRowDto surface)
    {
        MutateTransactional(WorkspaceChangeCategory.Surface, () =>
        {
            var target = FindSurface(surface.Number);
            if (target is null)
            {
                return;
            }

            Runtime.CaptureCurrentState();
            var isImageSurface = ReferenceEquals(target, Runtime.Surfaces[^1]);
            var hasRadiusPickup = Runtime.CurrentOptic.Pickups.RadiusPickups
                .Any(pickup => pickup.TargetSurface == surface.Number);
            var hasThicknessPickup = Runtime.CurrentOptic.Pickups.ThicknessPickups
                .Any(pickup => pickup.TargetSurface == surface.Number);
            var hasSemiDiameterPickup = Runtime.CurrentOptic.Pickups.SemiDiameterPickups
                .Any(pickup => pickup.TargetSurface == surface.Number);
            var mceRadius = Runtime.IsMultiConfigurationPickupTarget(surface.Number, "radius");
            var mceThickness = Runtime.IsMultiConfigurationPickupTarget(surface.Number, "thickness");
            var mceConic = Runtime.IsMultiConfigurationPickupTarget(surface.Number, "conic");
            var mceSemi = Runtime.IsMultiConfigurationPickupTarget(surface.Number, "semiDiameter");
            if ((mceRadius && (!target.Radius.Equals(surface.Radius) || surface.RadiusVariable))
                || (mceThickness && (!target.Thickness.Equals(surface.Thickness) || surface.ThicknessVariable))
                || (mceConic && !target.Conic.Equals(surface.Conic))
                || (mceSemi && (!target.SemiDiameter.Equals(surface.SemiDiameter) || !surface.SemiDiameterFixed)))
                throw new InvalidOperationException("该参数由多配置拾取控制，请编辑源单元格或移除拾取。");
            hasRadiusPickup |= mceRadius;
            hasThicknessPickup |= mceThickness;
            hasSemiDiameterPickup |= mceSemi;
            target.Label = surface.Label;
            if (!hasRadiusPickup) target.Radius = surface.Radius;
            if (!isImageSurface && !hasThicknessPickup)
            {
                target.Thickness = surface.Thickness;
            }
            target.Material = surface.Material;
            target.Coating = surface.Coating;
            target.SemiDiameterFixed = surface.SemiDiameterFixed;
            if (target.SemiDiameterFixed && !hasSemiDiameterPickup)
            {
                target.SemiDiameter = surface.SemiDiameter;
            }
            if (!mceConic) target.Conic = surface.Conic;
            if (surface.ChipZone is { } chipZone) target.ChipZone = chipZone;
            if (surface.ThermalExpansionPpmPerC is { } thermalExpansion)
                target.ThermalExpansionPpmPerC = thermalExpansion;
            target.IsStop = surface.IsStop;
            target.RadiusVariable = surface.RadiusVariable && !hasRadiusPickup;
            target.ThicknessVariable = !isImageSurface && surface.ThicknessVariable && !hasThicknessPickup;
            var editedProperties = new List<string?> { nameof(OpticalSurface.Material), nameof(OpticalSurface.Coating), nameof(OpticalSurface.IsStop) };
            if (!mceRadius) editedProperties.Add(nameof(OpticalSurface.Radius));
            if (!mceConic) editedProperties.Add(nameof(OpticalSurface.Conic));
            if (!isImageSurface && !mceThickness) editedProperties.Add(nameof(OpticalSurface.Thickness));
            if (!mceSemi) editedProperties.Add(nameof(OpticalSurface.SemiDiameter));
            Runtime.CommitSurfaceEdits(target, editedProperties);
        });
    }

    public IReadOnlyList<CoatingLayerEditDto> GetCoatingLayers(int surfaceNumber)
    {
        lock (Gate)
        {
            var surface = FindSurface(surfaceNumber) ?? throw new ArgumentException("表面不存在。");
            if (surface.CoatingModel is not CoherentMultilayerCoating coating) return [];
            return coating.Layers.Select((layer, index) => new CoatingLayerEditDto(layer.Material.Name,
                layer.ThicknessNanometers, layer.Adjustment.Multiplier, layer.Adjustment.IndexOffset,
                layer.Adjustment.ExtinctionOffset, layer.Adjustment.MultiplierVariable,
                layer.Adjustment.IndexVariable, layer.Adjustment.ExtinctionVariable, index + 1)).ToArray();
        }
    }

    private IMaterial ResolveCoatingMaterial(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("膜层材料名称不能为空。");
        try { return Runtime.CurrentOptic.Materials.Resolve(name); }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidDataException)
        { throw new ArgumentException(exception.Message, exception); }
    }

    public void UpdateCoatingLayers(int surfaceNumber, IReadOnlyList<CoatingLayerEditDto> layers, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(layers);
        MutateTransactional(WorkspaceChangeCategory.Surface, () =>
        {
            if (expectedRevision != Workspace.Revision) throw new InvalidOperationException("工程已变化，请重新打开膜层编辑器。");
            var surface = FindSurface(surfaceNumber) ?? throw new ArgumentException("表面不存在。");
            if (surfaceNumber <= 0 || surface.Geometry is INonComputableGeometry)
                throw new NotSupportedException("此表面不能编辑物理膜层。");
            var existing = (surface.CoatingModel as CoherentMultilayerCoating)?.Layers;
            var films = layers.Select(layer =>
            {
                ArgumentNullException.ThrowIfNull(layer);
                IMaterial material;
                if (layer.SourceLayer is { } source)
                {
                    if (source <= 0 || existing is null || source > existing.Count)
                        throw new ArgumentException("原始膜层引用不存在，请重新打开编辑器。");
                    var original = existing[source - 1];
                    material = layer.Material == original.Material.Name ? original.Material.Clone()
                        : ResolveCoatingMaterial(layer.Material);
                }
                else material = ResolveCoatingMaterial(layer.Material);
                if (material is UnresolvedMaterial) throw new ArgumentException($"未找到膜层材料 {layer.Material}。");
                return new CoherentFilm(material, layer.ThicknessNanometers, new(layer.Multiplier,
                    layer.IndexOffset, layer.ExtinctionOffset, layer.MultiplierVariable, layer.IndexVariable, layer.ExtinctionVariable));
            }).ToArray();
            var replacement = new CoherentMultilayerCoating(films);
            CoatingLayerMetrics.ValidateAtSystemWavelengths(Runtime.CurrentOptic, replacement);
            Runtime.CaptureCurrentState();
            Runtime.CurrentOptic.InvalidateRayTraceCache();
            surface.CoatingModel = films.Length == 0 ? new NoneCoatingModel() : replacement;
            Runtime.CommitSurfaceEdit(surface, nameof(OpticalSurface.CoatingModel));
        });
    }

    public void UpdateSurfaceComponents(int surfaceNumber, SurfaceComponentUpdateDto update)
    {
        MutateTransactional(WorkspaceChangeCategory.Surface, () => Runtime.ApplySurfaceComponents(
            FindSurface(surfaceNumber),
            update.GeometryKind,
            update.ApertureKind,
            update.GratingOrder,
            update.GratingPeriodMicrometers,
            update.GrooveOrientationAngleDegrees,
            update.ThinLensFocalLength,
            update.IsStop,
            update.Coating,
            update.SemiDiameterFixed,
            update.SemiDiameter));
    }

    public void AddField() => MutateTransactional(WorkspaceChangeCategory.Field, Runtime.AddField);

    public void RemoveField(int index) => MutateTransactional(
        WorkspaceChangeCategory.Field,
        () => Runtime.RemoveField(ElementAtOrDefault(Runtime.Fields, index)));

    public void UpdateField(FieldRowDto field)
    {
        MutateTransactional(WorkspaceChangeCategory.Field, () =>
        {
            var target = ElementAtOrDefault(Runtime.Fields, field.Index);
            if (target is null)
            {
                return;
            }

            Runtime.CaptureCurrentState();
            target.Label = field.Label;
            target.X = field.X;
            target.Y = field.Y;
            target.VignetteFactorX = field.VignetteFactorX;
            target.VignetteFactorY = field.VignetteFactorY;
            target.VignetteDecenterX = field.VignetteDecenterX;
            target.VignetteDecenterY = field.VignetteDecenterY;
            target.VignetteAngleDegrees = field.VignetteAngleDegrees;
            target.Weight = field.Weight;
            Runtime.CommitSystemEdit(target);
        });
    }

    public void AddWavelength() => MutateTransactional(WorkspaceChangeCategory.Wavelength, Runtime.AddWavelength);

    public void RemoveWavelength(int index) => MutateTransactional(
        WorkspaceChangeCategory.Wavelength,
        () => Runtime.RemoveWavelength(ElementAtOrDefault(Runtime.Wavelengths, index)));

    public void UpdateWavelength(WavelengthRowDto wavelength)
    {
        MutateTransactional(WorkspaceChangeCategory.Wavelength, () =>
        {
            var target = ElementAtOrDefault(Runtime.Wavelengths, wavelength.Index);
            if (target is null)
            {
                return;
            }

            Runtime.CaptureCurrentState();
            target.Label = wavelength.Label;
            target.Nanometers = wavelength.Nanometers;
            target.Weight = wavelength.Weight;
            target.IsPrimary = wavelength.IsPrimary;
            Runtime.CommitSystemEdit(target);
        });
    }

    public void UpdateSystemSettings(SystemSettingsDto settings)
    {
        MutateTransactional(WorkspaceChangeCategory.SystemSettings, () => Runtime.ApplySystemSettings(
            settings.Backend,
            settings.ApertureKind,
            settings.ApertureValue,
            settings.FieldDefinition,
            settings.ObjectSpaceTelecentric,
            settings.ApodizationKind,
            settings.FirstApodizationParameter,
            settings.SecondApodizationParameter,
            settings.ImageSpaceAfocal));
    }

    public void UpdateEnvironmentSettings(EnvironmentSettingsDto settings)
    {
        MutateTransactional(WorkspaceChangeCategory.SystemSettings, () =>
        {
            var environment = Runtime.CurrentOptic.Environment;
            Runtime.CaptureCurrentState();
            environment.MatchRefractiveIndexData = settings.MatchRefractiveIndexData;
            environment.TemperatureCelsius = settings.TemperatureCelsius;
            environment.PressureAtmospheres = settings.PressureAtmospheres;
            Runtime.CommitSystemEdit();
        });
    }

    public void UpdateGlassCatalogs(IReadOnlyList<string> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        if (catalogs.Count == 0)
        {
            throw new ArgumentException(
                "At least one current glass catalog is required.",
                nameof(catalogs));
        }

        MutateTransactional(WorkspaceChangeCategory.SystemSettings, () =>
        {
            Runtime.CaptureCurrentState();
            Runtime.CurrentOptic.Materials.SetPreferredGlassCatalogs(catalogs);
            Runtime.CommitSystemEdit();
        });
    }
}
