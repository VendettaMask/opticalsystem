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

internal sealed class PrescriptionService : WorkbenchServiceBase, IPrescriptionService
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
            return Runtime.Surfaces.Select(ToSurfaceDto).ToArray();
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
                field.Weight)).ToArray();
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
            target.Label = surface.Label;
            target.Radius = surface.Radius;
            if (!isImageSurface)
            {
                target.Thickness = surface.Thickness;
            }
            target.Material = surface.Material;
            target.Coating = surface.Coating;
            target.SemiDiameterFixed = surface.SemiDiameterFixed;
            if (target.SemiDiameterFixed)
            {
                target.SemiDiameter = surface.SemiDiameter;
            }
            target.Conic = surface.Conic;
            target.IsStop = surface.IsStop;
            target.RadiusVariable = surface.RadiusVariable;
            target.ThicknessVariable = !isImageSurface && surface.ThicknessVariable;
            Runtime.CommitSurfaceEdit(target, nameof(OpticalSurface.Radius));
            Runtime.CommitSurfaceEdit(target, nameof(OpticalSurface.Conic));
            if (!isImageSurface)
            {
                Runtime.CommitSurfaceEdit(target, nameof(OpticalSurface.Thickness));
            }
            Runtime.CommitSurfaceEdit(target, nameof(OpticalSurface.Material));
            Runtime.CommitSurfaceEdit(target, nameof(OpticalSurface.Coating));
            Runtime.CommitSurfaceEdit(target, nameof(OpticalSurface.IsStop));
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
            update.ThinLensFocalLength));
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
