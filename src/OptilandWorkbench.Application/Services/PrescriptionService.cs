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
                Connector.BackendNames,
                Connector.ApertureKindNames,
                Connector.FieldDefinitionNames,
                Connector.ApodizationKinds,
                Connector.GeometryKinds,
                Connector.MaterialNames,
                Connector.CoatingKinds,
                Connector.InteractionKinds,
                Connector.PhysicalApertureKinds);
        }
    }

    public IReadOnlyList<SurfaceRowDto> GetSurfaces()
    {
        lock (Gate)
        {
            return Connector.Surfaces.Select(ToSurfaceDto).ToArray();
        }
    }

    public SystemSettingsDto GetSystemSettings()
    {
        lock (Gate)
        {
            var optic = Connector.CurrentOptic;
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
                second);
        }
    }

    public EnvironmentSettingsDto GetEnvironmentSettings()
    {
        lock (Gate)
        {
            var environment = Connector.CurrentOptic.Environment;
            return new EnvironmentSettingsDto(
                environment.MatchRefractiveIndexData,
                environment.TemperatureCelsius,
                environment.PressureAtmospheres);
        }
    }

    public IReadOnlyList<FieldRowDto> GetFields()
    {
        lock (Gate)
        {
            return Connector.Fields.Select((field, index) => new FieldRowDto(
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
            return Connector.Wavelengths.Select((wavelength, index) => new WavelengthRowDto(
                index,
                wavelength.Label,
                wavelength.Nanometers,
                wavelength.Weight,
                wavelength.IsPrimary)).ToArray();
        }
    }

    public void AddSurface() => Mutate(WorkspaceChangeCategory.Surface, Connector.AddSurface);

    public void RemoveSurface(int surfaceNumber) => Mutate(
        WorkspaceChangeCategory.Surface,
        () => Connector.RemoveSurface(FindSurface(surfaceNumber)));

    public void UpdateSurface(SurfaceRowDto surface)
    {
        Mutate(WorkspaceChangeCategory.Surface, () =>
        {
            var target = FindSurface(surface.Number);
            if (target is null)
            {
                return;
            }

            Connector.CaptureCurrentState();
            var isImageSurface = ReferenceEquals(target, Connector.Surfaces[^1]);
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
            Connector.CommitSurfaceEdit(target, nameof(OpticalSurface.Radius));
            if (!isImageSurface)
            {
                Connector.CommitSurfaceEdit(target, nameof(OpticalSurface.Thickness));
            }
            Connector.CommitSurfaceEdit(target, nameof(OpticalSurface.Material));
            Connector.CommitSurfaceEdit(target, nameof(OpticalSurface.Coating));
            Connector.CommitSurfaceEdit(target, nameof(OpticalSurface.IsStop));
        });
    }

    public void UpdateSurfaceComponents(int surfaceNumber, SurfaceComponentUpdateDto update)
    {
        Mutate(WorkspaceChangeCategory.Surface, () => Connector.ApplySurfaceComponents(
            FindSurface(surfaceNumber),
            update.GeometryKind,
            update.ApertureKind,
            update.GratingOrder,
            update.GratingPeriodMicrometers,
            update.GrooveOrientationAngleDegrees,
            update.ThinLensFocalLength));
    }

    public void AddField() => Mutate(WorkspaceChangeCategory.Field, Connector.AddField);

    public void RemoveField(int index) => Mutate(
        WorkspaceChangeCategory.Field,
        () => Connector.RemoveField(ElementAtOrDefault(Connector.Fields, index)));

    public void UpdateField(FieldRowDto field)
    {
        Mutate(WorkspaceChangeCategory.Field, () =>
        {
            var target = ElementAtOrDefault(Connector.Fields, field.Index);
            if (target is null)
            {
                return;
            }

            Connector.CaptureCurrentState();
            target.Label = field.Label;
            target.X = field.X;
            target.Y = field.Y;
            target.VignetteFactorX = field.VignetteFactorX;
            target.VignetteFactorY = field.VignetteFactorY;
            target.Weight = field.Weight;
            Connector.CommitSystemEdit(target);
        });
    }

    public void AddWavelength() => Mutate(WorkspaceChangeCategory.Wavelength, Connector.AddWavelength);

    public void RemoveWavelength(int index) => Mutate(
        WorkspaceChangeCategory.Wavelength,
        () => Connector.RemoveWavelength(ElementAtOrDefault(Connector.Wavelengths, index)));

    public void UpdateWavelength(WavelengthRowDto wavelength)
    {
        Mutate(WorkspaceChangeCategory.Wavelength, () =>
        {
            var target = ElementAtOrDefault(Connector.Wavelengths, wavelength.Index);
            if (target is null)
            {
                return;
            }

            Connector.CaptureCurrentState();
            target.Label = wavelength.Label;
            target.Nanometers = wavelength.Nanometers;
            target.Weight = wavelength.Weight;
            target.IsPrimary = wavelength.IsPrimary;
            Connector.CommitSystemEdit(target);
        });
    }

    public void UpdateSystemSettings(SystemSettingsDto settings)
    {
        Mutate(WorkspaceChangeCategory.SystemSettings, () => Connector.ApplySystemSettings(
            settings.Backend,
            settings.ApertureKind,
            settings.ApertureValue,
            settings.FieldDefinition,
            settings.ObjectSpaceTelecentric,
            settings.ApodizationKind,
            settings.FirstApodizationParameter,
            settings.SecondApodizationParameter));
    }

    public void UpdateEnvironmentSettings(EnvironmentSettingsDto settings)
    {
        Mutate(WorkspaceChangeCategory.SystemSettings, () =>
        {
            var environment = Connector.CurrentOptic.Environment;
            Connector.CaptureCurrentState();
            environment.MatchRefractiveIndexData = settings.MatchRefractiveIndexData;
            environment.TemperatureCelsius = settings.TemperatureCelsius;
            environment.PressureAtmospheres = settings.PressureAtmospheres;
            Connector.CommitSystemEdit();
        });
    }
}
