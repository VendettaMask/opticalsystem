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

public sealed class WorkbenchApplication :
    IWorkbenchApplication,
    IOpticalDocumentService,
    IPrescriptionService,
    IAnalysisService,
    IVisualizationService,
    IOptimizationService,
    ITolerancingService,
    IMultiConfigurationService,
    IMaterialCatalogService,
    IWorkspaceEventStream
{
    private readonly IOpticContext _context;
    private readonly string? _userCatalogDirectory;
    private readonly LensLibraryService _lensLibrary;
    private WorkspaceChangeCategory _pendingCategory = WorkspaceChangeCategory.Prescription;
    private string? _currentPath;
    private long _documentGeneration;
    private long _revision;
    private int _mutationDepth;
    private bool _deferredEvent;
    private bool _deferredFileSwitch;
    private bool _disposed;

    private WorkbenchApplication(
        Optic optic,
        string? userCatalogDirectory,
        string lensLibraryDirectory)
    {
        _userCatalogDirectory = userCatalogDirectory;
        _lensLibrary = new LensLibraryService(lensLibraryDirectory);
        _context = new OpticContext(optic);
        _connector.OpticLoaded += OnOpticLoaded;
        _connector.OpticChanged += OnOpticChanged;
    }

    private object _gate => _context.SyncRoot;

    private OptilandConnector _connector => _context.Connector;

    public static WorkbenchApplication Create(
        string? sample = null,
        string? userCatalogDirectory = null,
        string? lensLibraryDirectory = null)
    {
        LoadUserCatalogs(userCatalogDirectory);
        var optic = sample?.ToLowerInvariant() switch
        {
            "cooke" => Optic.CreateCookeTriplet(),
            "tessar" => Optic.CreateTessarLens(),
            _ => Optic.CreateBlank()
        };
        lensLibraryDirectory ??= Path.Combine(
            AppContext.BaseDirectory,
            "LensLibrary");
        return new WorkbenchApplication(
            optic,
            userCatalogDirectory,
            lensLibraryDirectory);
    }

    public IOpticalDocumentService Documents => this;

    public IPrescriptionService Prescription => this;

    public IAnalysisService Analyses => this;

    public IVisualizationService Visualization => this;

    public IOptimizationService Optimization => this;

    public ITolerancingService Tolerancing => this;

    public IMultiConfigurationService MultiConfiguration => this;

    public IMaterialCatalogService Materials => this;

    public ILensLibraryService Lenses => _lensLibrary;

    public IWorkspaceEventStream Events => this;

    public IReadOnlyList<MaterialCatalogDto> GetCatalogs()
    {
        return GetGlasses()
            .GroupBy(glass => glass.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MaterialCatalogDto(group.Key, group.Count()))
            .OrderBy(catalog => catalog.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<GlassMaterialDto> GetGlasses()
    {
        lock (_gate)
        {
            var materials = _connector.CurrentOptic.Materials;
            var glasses = new Dictionary<string, GlassMaterialDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in materials.Names)
            {
                CatalogGlassMaterial? glass;
                if (materials.TryResolveExternalGlass(name, preferredManufacturers: null, out var externalGlass))
                {
                    glass = externalGlass;
                }
                else if (materials.TryResolve(name, preferredManufacturers: null, out var resolved) &&
                    resolved is CatalogGlassMaterial catalogGlass)
                {
                    glass = catalogGlass;
                }
                else
                {
                    continue;
                }

                const double wavelengthF = 486.1327;
                const double wavelengthD = 587.5618;
                const double wavelengthC = 656.2725;
                var refractiveIndexD = glass.ZemaxData is { ReferenceIndexD: > 0 } zemaxData
                    ? zemaxData.ReferenceIndexD
                    : glass.RefractiveIndex(wavelengthD);
                var abbeNumber = glass.ZemaxData is { ReferenceAbbeNumber: > 0 } referenceData
                    ? referenceData.ReferenceAbbeNumber
                    : CalculateAbbeNumber(glass, refractiveIndexD, wavelengthF, wavelengthC);
                var key = $"{glass.Manufacturer}:{glass.CatalogName}";
                glasses[key] = new GlassMaterialDto(
                    glass.CatalogName,
                    glass.Manufacturer,
                    glass.Formula,
                    refractiveIndexD,
                    abbeNumber,
                    glass.MinimumWavelengthNanometers / 1000.0,
                    glass.MaximumWavelengthNanometers / 1000.0,
                    glass.Coefficients.ToArray(),
                    glass.RefractiveIndices.Count,
                    glass.ExtinctionCoefficients.Count,
                    glass.ZemaxData?.DispersionFormulaNumber,
                    GlassStatus(glass.ZemaxData?.Status),
                    glass.ZemaxData?.Comment ?? string.Empty,
                    glass.ZemaxData?.ExcludeSubstitution ?? false,
                    glass.ZemaxData?.MeltFrequency ?? 0,
                    glass.ZemaxData?.ThermalExpansionLow,
                    glass.ZemaxData?.ThermalExpansionHigh,
                    glass.ZemaxData?.Density,
                    glass.ZemaxData?.RelativePartialDispersionDeviation,
                    glass.ZemaxData?.ThermalCoefficients.ToArray() ?? Array.Empty<double>(),
                    glass.ZemaxData?.MechanicalData.ToArray() ?? Array.Empty<double>(),
                    glass.ZemaxData?.OtherData.ToArray() ?? Array.Empty<double>(),
                    glass.ZemaxData?.InternalTransmissions.Count ?? 0,
                    glass.ZemaxData?.StressData.Count ?? 0);
            }

            return glasses.Values
                .OrderBy(glass => glass.Manufacturer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(glass => glass.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static double CalculateAbbeNumber(
        CatalogGlassMaterial glass,
        double refractiveIndexD,
        double wavelengthF,
        double wavelengthC)
    {
        var denominator = glass.RefractiveIndex(wavelengthF) - glass.RefractiveIndex(wavelengthC);
        return Math.Abs(denominator) > 1e-12
            ? (refractiveIndexD - 1.0) / denominator
            : double.NaN;
    }

    public async Task<MaterialCatalogImportResultDto> ImportZemaxCatalogAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (_userCatalogDirectory is null)
        {
            throw new InvalidOperationException("No user glass-catalog directory is configured.");
        }

        var document = await ZemaxAgfCatalogReader.ImportFileAsync(path, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_userCatalogDirectory);
        var fileName = string.Concat(document.CatalogName.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var destination = Path.Combine(
            _userCatalogDirectory,
            $"{fileName}{OptilandGlassCatalogStore.Extension}");
        await OptilandGlassCatalogStore.SaveAsync(document, destination, cancellationToken).ConfigureAwait(false);
        ExternalGlassCatalogDatabase.Register(document);
        return new MaterialCatalogImportResultDto(document.CatalogName, document.Glasses.Count, destination);
    }

    private static void LoadUserCatalogs(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
            directory,
            $"*{OptilandGlassCatalogStore.Extension}",
            SearchOption.TopDirectoryOnly))
        {
            try
            {
                ExternalGlassCatalogDatabase.Register(OptilandGlassCatalogStore.Load(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
            }
        }
    }

    private static string GlassStatus(int? status) => status switch
    {
        0 => "标准",
        1 => "首选",
        2 => "废弃",
        3 => "特殊",
        4 => "熔融",
        _ => "内置只读"
    };

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

    public long Revision => Interlocked.Read(ref _revision);

    public string? CurrentPath => _currentPath;

    public IReadOnlyList<string> AnalysisNames => _connector.AnalysisDisplayNames;

    public IReadOnlyList<string> OptimizerNames => _connector.OptimizerNames;

    public IReadOnlyList<MeritOperandTypeDto> GetMeritOperandTypes()
    {
        return MeritFunctionCatalog.Types
            .Select(type => new MeritOperandTypeDto(type.Code, type.DisplayName, type.Description))
            .ToArray();
    }

    public IReadOnlyList<MeritOperandRowDto> GetMeritFunction()
    {
        using var cancellationScope = ComputationCancellation.Push(CancellationToken.None);
        using var evaluationBatch = MeritFunctionCatalog.BeginEvaluationBatch();
        lock (_gate)
        {
            var operands = _connector.CurrentOptic.MeritFunctionOperands.ToArray();
            var weightSum = operands
                .Where(operand => operand.Enabled
                    && MeritFunctionCatalog.CanonicalType(operand.Type) is not ("BLNK" or "DMFS"))
                .Sum(operand => Math.Abs(operand.Weight));
            return operands
                .Select((operand, index) =>
                {
                    var evaluation = MeritFunctionCatalog.Evaluate(_connector.CurrentOptic, operand);
                    return new MeritOperandRowDto(
                        index + 1,
                        operand.Enabled,
                        MeritFunctionCatalog.CanonicalType(operand.Type),
                        operand.Surface,
                        operand.Field,
                        operand.Wavelength,
                        operand.Hx,
                        operand.Hy,
                        operand.Px,
                        operand.Py,
                        operand.Target,
                        operand.Weight,
                        evaluation.Value,
                        weightSum > 0 ? evaluation.Contribution / weightSum : 0,
                        operand.Comment,
                        evaluation.Error,
                        operand.PupilRings,
                        operand.PupilArms,
                        operand.PupilObscuration,
                        operand.PupilSampling,
                        operand.SpatialFrequency,
                        operand.IgnoreLateralColor,
                        operand.PolychromaticReference);
                })
                .ToArray();
        }
    }

    public void SetMeritFunction(IReadOnlyList<MeritOperandRowDto> operands)
    {
        Mutate(WorkspaceChangeCategory.Optimization, () => _connector.ReplaceMeritFunction(
            operands.Select(operand => new MeritOperandDefinition
            {
                Enabled = operand.Enabled,
                Type = MeritFunctionCatalog.CanonicalType(operand.Type),
                Surface = Math.Max(0, operand.Surface),
                Field = Math.Max(0, operand.Field),
                Wavelength = Math.Max(0, operand.Wavelength),
                Hx = Math.Clamp(operand.Hx, -1, 1),
                Hy = Math.Clamp(operand.Hy, -1, 1),
                Px = Math.Clamp(operand.Px, -1, 1),
                Py = Math.Clamp(operand.Py, -1, 1),
                Target = double.IsFinite(operand.Target) ? operand.Target : 0,
                Weight = double.IsFinite(operand.Weight) ? operand.Weight : 0,
                Comment = operand.Comment ?? string.Empty,
                PupilRings = Math.Clamp(operand.PupilRings, 1, 20),
                PupilArms = Math.Clamp(operand.PupilArms, 3, 36),
                PupilObscuration = Math.Clamp(operand.PupilObscuration, 0, 0.95),
                PupilSampling = operand.PupilSampling?.Trim().ToLowerInvariant() switch
                {
                    "uniform" => "uniform",
                    "gaussian_quad" => "gaussian_quad",
                    _ => "hexapolar"
                },
                SpatialFrequency = double.IsFinite(operand.SpatialFrequency)
                    ? Math.Max(0, operand.SpatialFrequency)
                    : 30,
                IgnoreLateralColor = operand.IgnoreLateralColor,
                PolychromaticReference = operand.PolychromaticReference
            })));
    }

    public void GenerateDefaultMeritFunction(MeritFunctionPreset preset)
    {
        Mutate(WorkspaceChangeCategory.Optimization, () => _connector.GenerateDefaultMeritFunction(preset));
    }

    public void GenerateMeritFunction(OptimizationWizardSettingsDto settings)
    {
        var coreSettings = new MeritFunctionWizardSettings(
            settings.ImageQuality switch
            {
                OptimizationImageQuality.RmsWavefront => MeritImageQuality.RmsWavefront,
                OptimizationImageQuality.RmsSpot => MeritImageQuality.RmsSpot,
                OptimizationImageQuality.Contrast => MeritImageQuality.Contrast,
                OptimizationImageQuality.Angular => MeritImageQuality.Angular,
                _ => throw new ArgumentOutOfRangeException(nameof(settings.ImageQuality))
            },
            settings.PupilSampling == OptimizationPupilSampling.RectangularArray
                ? MeritPupilSampling.RectangularArray
                : MeritPupilSampling.GaussianQuadrature,
            settings.PupilRings,
            settings.PupilArms,
            settings.PupilObscuration,
            settings.WeightScale,
            settings.UseAllWavelengths,
            settings.IncludeCommonOperands,
            settings.Reference switch
            {
                OptimizationSpotReference.ChiefRay => MeritSpotReference.ChiefRay,
                OptimizationSpotReference.Unreferenced => MeritSpotReference.Unreferenced,
                _ => MeritSpotReference.Centroid
            },
            settings.SpatialFrequency,
            settings.XWeight,
            settings.YWeight,
            settings.IgnoreLateralColor);
        Mutate(WorkspaceChangeCategory.Optimization, () => _connector.GenerateMeritFunction(
            coreSettings,
            settings.StartRow,
            settings.ReplaceExisting));
    }

    public OpticalDocumentSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var optic = _connector.CurrentOptic;
            return new OpticalDocumentSnapshot(
                optic.Name,
                _currentPath,
                Revision,
                _connector.Status,
                _connector.CanUndo,
                _connector.CanRedo,
                optic.Paraxial.EstimateEffectiveFocalLength(),
                optic.Paraxial.EstimateFNumber(),
                optic.Aperture.Value,
                optic.SurfaceGroup.TotalTrack,
                optic.SurfaceGroup.Items.Count,
                optic.Fields.Count,
                optic.Wavelengths.Count);
        }
    }

    public void NewBlank() => ReplaceDocument(WorkspaceChangeCategory.Document, _connector.NewBlank);

    public void NewCooke() => ReplaceDocument(WorkspaceChangeCategory.Document, _connector.NewDemo);

    public void NewTessar() => ReplaceDocument(WorkspaceChangeCategory.Document, _connector.NewTessar);

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelDocumentTasks();
        using var linked = _context.LinkDocumentToken(cancellationToken);
        var fullPath = Path.GetFullPath(path);
        var document = await OptilandConnector.ReadDocumentAsync(fullPath, linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            linked.Token.ThrowIfCancellationRequested();
            _currentPath = fullPath;
            _pendingCategory = WorkspaceChangeCategory.Document;
            _connector.ApplyLoadedDocument(document, fullPath);
        }
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadedOpticalDocument document;
        long documentGeneration;
        lock (_gate)
        {
            document = _connector.CaptureDocument();
            documentGeneration = Interlocked.Read(ref _documentGeneration);
        }

        var fullPath = Path.GetFullPath(path);
        await OptilandConnector.SaveDocumentAsync(document, fullPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (documentGeneration != Interlocked.Read(ref _documentGeneration))
            {
                return;
            }

            _pendingCategory = WorkspaceChangeCategory.Document;
            _currentPath = fullPath;
            _connector.NotifySaved(fullPath);
        }
    }

    public bool Undo() => Mutate(WorkspaceChangeCategory.Prescription, _connector.Undo);

    public bool Redo() => Mutate(WorkspaceChangeCategory.Prescription, _connector.Redo);

    public PrescriptionOptionsDto GetOptions()
    {
        lock (_gate)
        {
            return new PrescriptionOptionsDto(
                _connector.BackendNames,
                _connector.ApertureKindNames,
                _connector.FieldDefinitionNames,
                _connector.ApodizationKinds,
                _connector.GeometryKinds,
                _connector.MaterialNames,
                _connector.CoatingKinds,
                _connector.InteractionKinds,
                _connector.PhysicalApertureKinds);
        }
    }

    public IReadOnlyList<SurfaceRowDto> GetSurfaces()
    {
        lock (_gate)
        {
            return _connector.Surfaces.Select(ToSurfaceDto).ToArray();
        }
    }

    public SystemSettingsDto GetSystemSettings()
    {
        lock (_gate)
        {
            var optic = _connector.CurrentOptic;
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
        lock (_gate)
        {
            var environment = _connector.CurrentOptic.Environment;
            return new EnvironmentSettingsDto(
                environment.MatchRefractiveIndexData,
                environment.TemperatureCelsius,
                environment.PressureAtmospheres);
        }
    }

    public IReadOnlyList<FieldRowDto> GetFields()
    {
        lock (_gate)
        {
            return _connector.Fields.Select((field, index) => new FieldRowDto(
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
        lock (_gate)
        {
            return _connector.Wavelengths.Select((wavelength, index) => new WavelengthRowDto(
                index,
                wavelength.Label,
                wavelength.Nanometers,
                wavelength.Weight,
                wavelength.IsPrimary)).ToArray();
        }
    }

    public void AddSurface() => Mutate(WorkspaceChangeCategory.Surface, _connector.AddSurface);

    public void RemoveSurface(int surfaceNumber) => Mutate(
        WorkspaceChangeCategory.Surface,
        () => _connector.RemoveSurface(FindSurface(surfaceNumber)));

    public void UpdateSurface(SurfaceRowDto surface)
    {
        Mutate(WorkspaceChangeCategory.Surface, () =>
        {
            var target = FindSurface(surface.Number);
            if (target is null)
            {
                return;
            }

            _connector.CaptureCurrentState();
            var isImageSurface = ReferenceEquals(target, _connector.Surfaces[^1]);
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
            _connector.CommitSurfaceEdit(target, nameof(OpticalSurface.Radius));
            if (!isImageSurface)
            {
                _connector.CommitSurfaceEdit(target, nameof(OpticalSurface.Thickness));
            }
            _connector.CommitSurfaceEdit(target, nameof(OpticalSurface.Material));
            _connector.CommitSurfaceEdit(target, nameof(OpticalSurface.Coating));
            _connector.CommitSurfaceEdit(target, nameof(OpticalSurface.IsStop));
        });
    }

    public void UpdateSurfaceComponents(int surfaceNumber, SurfaceComponentUpdateDto update)
    {
        Mutate(WorkspaceChangeCategory.Surface, () => _connector.ApplySurfaceComponents(
            FindSurface(surfaceNumber),
            update.GeometryKind,
            update.ApertureKind,
            update.GratingOrder,
            update.GratingPeriodMicrometers,
            update.GrooveOrientationAngleDegrees,
            update.ThinLensFocalLength));
    }

    public void AddField() => Mutate(WorkspaceChangeCategory.Field, _connector.AddField);

    public void RemoveField(int index) => Mutate(
        WorkspaceChangeCategory.Field,
        () => _connector.RemoveField(ElementAtOrDefault(_connector.Fields, index)));

    public void UpdateField(FieldRowDto field)
    {
        Mutate(WorkspaceChangeCategory.Field, () =>
        {
            var target = ElementAtOrDefault(_connector.Fields, field.Index);
            if (target is null)
            {
                return;
            }

            _connector.CaptureCurrentState();
            target.Label = field.Label;
            target.X = field.X;
            target.Y = field.Y;
            target.VignetteFactorX = field.VignetteFactorX;
            target.VignetteFactorY = field.VignetteFactorY;
            target.Weight = field.Weight;
            _connector.CommitSystemEdit(target);
        });
    }

    public void AddWavelength() => Mutate(WorkspaceChangeCategory.Wavelength, _connector.AddWavelength);

    public void RemoveWavelength(int index) => Mutate(
        WorkspaceChangeCategory.Wavelength,
        () => _connector.RemoveWavelength(ElementAtOrDefault(_connector.Wavelengths, index)));

    public void UpdateWavelength(WavelengthRowDto wavelength)
    {
        Mutate(WorkspaceChangeCategory.Wavelength, () =>
        {
            var target = ElementAtOrDefault(_connector.Wavelengths, wavelength.Index);
            if (target is null)
            {
                return;
            }

            _connector.CaptureCurrentState();
            target.Label = wavelength.Label;
            target.Nanometers = wavelength.Nanometers;
            target.Weight = wavelength.Weight;
            target.IsPrimary = wavelength.IsPrimary;
            _connector.CommitSystemEdit(target);
        });
    }

    public void UpdateSystemSettings(SystemSettingsDto settings)
    {
        Mutate(WorkspaceChangeCategory.SystemSettings, () => _connector.ApplySystemSettings(
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
            var environment = _connector.CurrentOptic.Environment;
            _connector.CaptureCurrentState();
            environment.MatchRefractiveIndexData = settings.MatchRefractiveIndexData;
            environment.TemperatureCelsius = settings.TemperatureCelsius;
            environment.PressureAtmospheres = settings.PressureAtmospheres;
            _connector.CommitSystemEdit();
        });
    }

    public string CanonicalKey(string analysisName) => _connector.CanonicalAnalysisKey(analysisName);

    public IReadOnlyList<ContractAnalysisParameterDescriptor> GetParameters(string analysisName)
    {
        return _connector.GetAnalysisParameters(analysisName).Select(parameter => new ContractAnalysisParameterDescriptor(
            parameter.Key,
            parameter.DisplayName,
            (ContractAnalysisParameterKind)(int)parameter.Kind,
            parameter.DefaultValue,
            parameter.Minimum,
            parameter.Maximum,
            parameter.Increment,
            parameter.Choices)).ToArray();
    }

    public Dictionary<string, string> MergeSettings(
        string analysisName,
        IReadOnlyDictionary<string, string>? saved)
    {
        return _connector.MergeAnalysisSettings(analysisName, saved);
    }

    public Task<AnalysisResultDto> RunAsync(
        AnalysisRequestDto request,
        CancellationToken cancellationToken = default)
    {
        Optic snapshot;
        long sourceRevision;
        CancellationTokenSource linked;
        lock (_gate)
        {
            sourceRevision = Revision;
            snapshot = Optic.FromSnapshot(_connector.CurrentOptic.ToSnapshot());
            linked = _context.LinkDocumentToken(cancellationToken);
        }

        return RunAnalysisWorkerAsync(snapshot, sourceRevision, request, linked);
    }

    private static async Task<AnalysisResultDto> RunAnalysisWorkerAsync(
        Optic snapshot,
        long sourceRevision,
        AnalysisRequestDto request,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                var worker = new OptilandConnector(snapshot);
                var view = worker.BuildAnalysisView(request.AnalysisKey, request.Settings, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                return new AnalysisResultDto(
                    request.InstanceId,
                    request.Generation,
                    sourceRevision,
                    ToAnalysisViewDto(view));
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public Task<SceneDto> BuildSceneAsync(
        SceneDimension dimension,
        CancellationToken cancellationToken = default)
    {
        return BuildSceneAsync(new VisualizationRequestDto(
            dimension,
            RayCount: dimension == SceneDimension.TwoDimensional ? 3 : 5), cancellationToken);
    }

    public VisualizationOptionsDto GetVisualizationOptions()
    {
        lock (_gate)
        {
            return new VisualizationOptionsDto(
                _connector.CurrentOptic.SurfaceGroup.Items.Select(surface => surface.Number).ToArray(),
                _connector.CurrentOptic.Fields.Select((field, index) =>
                    new VisualizationSelectorOptionDto(index, field.Label)).ToArray(),
                _connector.CurrentOptic.Wavelengths.Select((wavelength, index) =>
                    new VisualizationSelectorOptionDto(
                        index,
                        $"{wavelength.Label}  {wavelength.Nanometers:0.####} nm")).ToArray());
        }
    }

    public Task<SceneDto> BuildSceneAsync(
        VisualizationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        Optic snapshot;
        long sourceRevision;
        OpticalDocumentSnapshot summary;
        CancellationTokenSource linked;
        lock (_gate)
        {
            sourceRevision = Revision;
            summary = GetSnapshot();
            snapshot = Optic.FromSnapshot(_connector.CurrentOptic.ToSnapshot());
            linked = _context.LinkDocumentToken(cancellationToken);
        }

        return BuildSceneWorkerAsync(snapshot, sourceRevision, summary, request, linked);
    }

    private static async Task<SceneDto> BuildSceneWorkerAsync(
        Optic snapshot,
        long sourceRevision,
        OpticalDocumentSnapshot summary,
        VisualizationRequestDto request,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                var builder = new Layout2DBuilder(snapshot);
                var options = new LayoutBuildOptions(
                    request.FirstSurface,
                    request.LastSurface,
                    request.FieldIndex,
                    request.WavelengthIndex,
                    request.IncludeAllWavelengths,
                    request.RayCount,
                    request.LowerPupil,
                    request.UpperPupil,
                    request.DeleteVignetted,
                    request.MarginalAndChiefOnly);
                if (request.Dimension == SceneDimension.TwoDimensional)
                {
                    var scene = builder.Build(options: options);
                    linked.Token.ThrowIfCancellationRequested();
                    return new SceneDto(sourceRevision, request.Dimension, ToScene2Dto(scene), null, summary);
                }

                var scene3 = builder.Build3D(options: options);
                linked.Token.ThrowIfCancellationRequested();
                return new SceneDto(sourceRevision, request.Dimension, null, ToScene3Dto(scene3), summary);
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public Task<OptimizationResultDto> OptimizeSurfaceRadiusAsync(
        int surfaceNumber,
        string optimizerName,
        int maxIterations,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked;
        lock (_gate)
        {
            linked = _context.LinkDocumentToken(cancellationToken);
        }

        return OptimizeSurfaceRadiusWorkerAsync(surfaceNumber, optimizerName, maxIterations, linked);
    }

    private async Task<OptimizationResultDto> OptimizeSurfaceRadiusWorkerAsync(
        int surfaceNumber,
        string optimizerName,
        int maxIterations,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                lock (_gate)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var surface = FindSurface(surfaceNumber)
                        ?? throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
                    var initial = surface.Radius;
                    var result = Mutate(
                        WorkspaceChangeCategory.Optimization,
                        () => _connector.OptimizeSurfaceRadius(surface, optimizerName, maxIterations));
                    RefreshAutomaticSemiDiameters();
                    linked.Token.ThrowIfCancellationRequested();
                    return new OptimizationResultDto(
                        optimizerName,
                        OptilandConnector.DisplayOptimizerMessage(result.Message),
                        initial,
                        surface.Radius,
                        result.FinalMerit,
                        result.Iterations);
                }
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public Task<OptimizationRunResultDto> OptimizeVariablesAsync(
        string optimizerName,
        int maxIterations,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked;
        lock (_gate)
        {
            linked = _context.LinkDocumentToken(cancellationToken);
        }

        return OptimizeVariablesWorkerAsync(optimizerName, maxIterations, linked);
    }

    private async Task<OptimizationRunResultDto> OptimizeVariablesWorkerAsync(
        string optimizerName,
        int maxIterations,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                lock (_gate)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var lastSurfaceNumber = _connector.Surfaces.Count == 0
                        ? -1
                        : _connector.Surfaces[^1].Number;
                    var selected = _connector.Surfaces
                        .Where(surface => surface.Number > 0 && surface.Number < lastSurfaceNumber)
                        .SelectMany(surface => new[]
                        {
                            surface.RadiusVariable
                                ? new OptimizationVariableResultDto(
                                    surface.Number,
                                    OptimizationVariableKind.Radius,
                                    $"表面 {surface.Number} 半径",
                                    surface.Radius,
                                    surface.Radius)
                                : null,
                            surface.ThicknessVariable
                                ? new OptimizationVariableResultDto(
                                    surface.Number,
                                    OptimizationVariableKind.Thickness,
                                    $"表面 {surface.Number} 厚度",
                                    surface.Thickness,
                                    surface.Thickness)
                                : null
                        })
                        .Where(variable => variable is not null)
                        .Cast<OptimizationVariableResultDto>()
                        .ToArray();
                    if (selected.Length == 0)
                    {
                        throw new InvalidOperationException("请先在镜头数据中设置优化变量。");
                    }

                    var result = Mutate(
                        WorkspaceChangeCategory.Optimization,
                        () => _connector.OptimizeMarkedVariables(optimizerName, maxIterations));
                    RefreshAutomaticSemiDiameters();
                    var variables = selected.Select(variable =>
                    {
                        var surface = FindSurface(variable.SurfaceNumber)
                            ?? throw new InvalidOperationException($"优化后找不到表面 {variable.SurfaceNumber}。");
                        var finalValue = variable.Kind == OptimizationVariableKind.Radius
                            ? surface.Radius
                            : surface.Thickness;
                        return variable with { FinalValue = finalValue };
                    }).ToArray();
                    linked.Token.ThrowIfCancellationRequested();
                    return new OptimizationRunResultDto(
                        optimizerName,
                        OptilandConnector.DisplayOptimizerMessage(result.Message),
                        result.InitialMerit,
                        result.FinalMerit,
                        result.Iterations,
                        variables);
                }
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public Task<TolerancingResultDto> RunAsync(
        TolerancingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        Optic snapshot;
        CancellationTokenSource linked;
        lock (_gate)
        {
            snapshot = Optic.FromSnapshot(_connector.CurrentOptic.ToSnapshot());
            linked = _context.LinkDocumentToken(cancellationToken);
        }

        return RunTolerancingWorkerAsync(snapshot, request, linked);
    }

    private static async Task<TolerancingResultDto> RunTolerancingWorkerAsync(
        Optic snapshot,
        TolerancingRequestDto request,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                var worker = new OptilandConnector(snapshot);
                var view = worker.RunTolerancing(
                    worker.Surfaces.FirstOrDefault(surface => surface.Number == request.SurfaceNumber),
                    request.RadiusSigma,
                    request.ThicknessSigma,
                    request.Trials,
                    request.Seed,
                    request.CompensationIterations,
                    linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                return new TolerancingResultDto(
                    view.Summary,
                    view.SensitivityRows.Select(row => new TolerancingSensitivityRowDto(row.Perturbation, row.DeltaMerit)).ToArray(),
                    view.TrialRows.Select(row => new TolerancingTrialRowDto(row.Trial, row.Merit, row.CompensatedMerit)).ToArray(),
                    view.Details);
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<MultiConfigurationRowDto> GetRows()
    {
        lock (_gate)
        {
            return _connector.GetMultiConfigurationRows().Select(row => new MultiConfigurationRowDto(
                row.Index,
                row.Name,
                row.Active,
                row.SurfaceCount,
                row.TotalTrack,
                row.EffectiveFocalLength)).ToArray();
        }
    }

    public int Add() => Mutate(WorkspaceChangeCategory.Configuration, _connector.AddMultiConfiguration);

    public void Activate(int configurationIndex)
    {
        CancelDocumentTasks();
        Mutate(
            WorkspaceChangeCategory.Configuration,
            () => _connector.ActivateMultiConfiguration(configurationIndex));
    }

    public void SetThickness(int configurationIndex, int surfaceNumber, double thickness) => Mutate(
        WorkspaceChangeCategory.Configuration,
        () => _connector.SetMultiConfigurationThickness(configurationIndex, surfaceNumber, thickness));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connector.OpticLoaded -= OnOpticLoaded;
        _connector.OpticChanged -= OnOpticChanged;
        _context.Dispose();
    }

    private void ReplaceDocument(WorkspaceChangeCategory category, Action action)
    {
        CancelDocumentTasks();
        Mutate(category, () =>
        {
            _currentPath = null;
            action();
        });
    }

    private void CancelDocumentTasks()
    {
        _context.CancelDocumentTasks();
    }

    private void OnOpticLoaded(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref _documentGeneration);
        if (_mutationDepth > 0)
        {
            _deferredEvent = true;
            _deferredFileSwitch = true;
            return;
        }

        RefreshAutomaticSemiDiameters();
        Publish(_pendingCategory, fileSwitched: true);
    }

    private void OnOpticChanged(object? sender, EventArgs args)
    {
        if (_mutationDepth > 0)
        {
            _deferredEvent = true;
            return;
        }

        Publish(_pendingCategory, fileSwitched: false);
    }

    private void Publish(WorkspaceChangeCategory category, bool fileSwitched)
    {
        var revision = Interlocked.Increment(ref _revision);
        using var cancellationScope = ComputationCancellation.Push(CancellationToken.None);
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(
            revision,
            category,
            _connector.Status,
            fileSwitched));
    }

    private void Mutate(WorkspaceChangeCategory category, Action action)
    {
        lock (_gate)
        {
            _pendingCategory = category;
            _mutationDepth++;
            try
            {
                action();
            }
            finally
            {
                if (_mutationDepth == 1 && UpdatesAutomaticSemiDiameters(category))
                {
                    RefreshAutomaticSemiDiameters();
                }

                CompleteMutation();
            }
        }
    }

    private T Mutate<T>(WorkspaceChangeCategory category, Func<T> action)
    {
        lock (_gate)
        {
            _pendingCategory = category;
            _mutationDepth++;
            try
            {
                return action();
            }
            finally
            {
                if (_mutationDepth == 1 && UpdatesAutomaticSemiDiameters(category))
                {
                    RefreshAutomaticSemiDiameters();
                }

                CompleteMutation();
            }
        }
    }

    private void RefreshAutomaticSemiDiameters()
    {
        using var cancellationScope = ComputationCancellation.Push(CancellationToken.None);
        AutomaticSemiDiameterSolver.Update(_connector.CurrentOptic);
    }

    private static bool UpdatesAutomaticSemiDiameters(WorkspaceChangeCategory category) => category is
        WorkspaceChangeCategory.Document
        or WorkspaceChangeCategory.Prescription
        or WorkspaceChangeCategory.Surface
        or WorkspaceChangeCategory.Field
        or WorkspaceChangeCategory.Wavelength
        or WorkspaceChangeCategory.SystemSettings
        or WorkspaceChangeCategory.Configuration;

    private void CompleteMutation()
    {
        _mutationDepth--;
        if (_mutationDepth != 0 || !_deferredEvent)
        {
            return;
        }

        var fileSwitched = _deferredFileSwitch;
        _deferredEvent = false;
        _deferredFileSwitch = false;
        Publish(_pendingCategory, fileSwitched);
    }

    private OpticalSurface? FindSurface(int surfaceNumber)
    {
        return _connector.Surfaces.FirstOrDefault(surface => surface.Number == surfaceNumber);
    }

    private static T? ElementAtOrDefault<T>(IList<T> items, int index) where T : class
    {
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    private static SurfaceRowDto ToSurfaceDto(OpticalSurface surface)
    {
        var grating = surface.Geometry as IGratingGeometry;
        var thinLens = surface.InteractionModel as ThinLensInteractionModel;
        return new SurfaceRowDto(
            surface.Number,
            surface.Label,
            surface.Radius,
            surface.Thickness,
            surface.Material,
            surface.Coating,
            surface.SemiDiameter,
            surface.Conic,
            surface.IsStop,
            GeometryKind(surface),
            CoatingKind(surface),
            InteractionKind(surface),
            PhysicalApertureKind(surface),
            grating?.GratingOrder ?? 1,
            grating?.GratingPeriodMicrometers ?? 1,
            (grating?.GrooveOrientationAngleRadians ?? 0) * 180 / Math.PI,
            thinLens?.FocalLength ?? 50,
            surface.RadiusVariable,
            surface.ThicknessVariable,
            surface.SemiDiameterFixed);
    }

    private static string GeometryKind(OpticalSurface surface) => surface.Geometry switch
    {
        PlaneGeometry => "平面",
        PlaneGratingGeometry => "平面光栅",
        StandardGratingGeometry => "标准曲面光栅",
        EvenAsphereGeometry => "偶次非球面",
        OddAsphereGeometry => "奇次非球面",
        BiconicGeometry => "双圆锥",
        ToroidalGeometry => "环形面",
        PolynomialGeometry => "XY 多项式",
        ChebyshevGeometry => "Chebyshev 曲面",
        ZernikeGeometry => "Zernike 曲面",
        ForbesQGeometry => "Forbes Q 曲面",
        _ => "标准球面/圆锥"
    };

    private static string CoatingKind(OpticalSurface surface)
    {
        return surface.CoatingModel is ThinFilmStackCoating stack
            ? stack.Layers.Count > 1 ? "四分之一波堆栈" : "MgF2 单层"
            : "无镀膜";
    }

    private static string InteractionKind(OpticalSurface surface) => surface.InteractionModel switch
    {
        ThinLensInteractionModel model when model.IsReflective => "反射薄透镜",
        ThinLensInteractionModel => "薄透镜",
        DiffractiveInteractionModel model when model.IsReflective => "反射衍射",
        DiffractiveInteractionModel => "衍射",
        PhaseInteractionModel => "相位",
        RefractiveReflectiveInteractionModel model when model.IsReflective => "反射",
        _ => "折射"
    };

    private static string PhysicalApertureKind(OpticalSurface surface) => surface.PhysicalAperture switch
    {
        AnnularAperture => "环形",
        OffsetRadialAperture => "偏心圆",
        RectangularAperture => "矩形",
        EllipticalAperture => "椭圆",
        FileAperture => "多边形",
        PolygonAperture => "多边形",
        BooleanAperture => "组合孔径",
        null => "无",
        _ => "圆形"
    };

    private static (string Kind, double First, double Second) ToApodizationSettings(IApodizationModel? apodization)
    {
        return apodization switch
        {
            UniformApodization => ("均匀", 1, 1),
            GaussianApodization value => ("高斯", value.Sigma, 1),
            CosineSquaredApodization value => ("余弦平方", value.Radius, 1),
            HannApodization value => ("Hann", value.Diameter, 1),
            PolynomialApodization value => ("多项式", value.Radius, value.Power),
            SuperGaussianApodization value => ("超高斯", value.Width, value.Exponent),
            TukeyApodization value => ("Tukey", value.Radius, value.Alpha),
            _ => ("无", 1, 1)
        };
    }

    private static AnalysisViewDto ToAnalysisViewDto(AnalysisView view)
    {
        return new AnalysisViewDto(
            view.Name,
            view.Rows.Select(row => new AnalysisRowDto(row.Metric, row.Value)).ToArray(),
            view.ReportText,
            view.SeriesList.Select(ToSeriesDto).ToArray(),
            ToPlotOptionsDto(view.PlotOptions),
            view.PlotPanes.Select(pane => new AnalysisPlotPaneDto(
                pane.Title,
                pane.Series.Select(ToSeriesDto).ToArray(),
                ToPlotOptionsDto(pane.PlotOptions),
                pane.Metrics?.Select(metric => new AnalysisPlotMetricDto(
                    metric.Label,
                    metric.Value,
                    metric.Unit)).ToArray(),
                pane.Footer)).ToArray(),
            view.PlotPaneColumns);
    }

    private static AnalysisSeriesDto ToSeriesDto(AnalysisSeries series)
    {
        return new AnalysisSeriesDto(
            series.XAxisLabel,
            series.YAxisLabel,
            series.Points.Select(point => new AnalysisPointDto(
                point.X,
                point.Y,
                point.Label,
                point.Value,
                point.Red,
                point.Green,
                point.Blue)).ToArray(),
            (ContractAnalysisSeriesKind)(int)series.Kind,
            series.Name,
            (ContractAnalysisLineStyle)(int)series.LineStyle,
            series.ColorIndex,
            series.ShowMarkers,
            series.LineWidth,
            (ContractAnalysisMarkerStyle)(int)series.MarkerStyle,
            series.MarkerSize,
            series.Opacity,
            series.ValueLabel,
            (ContractAnalysisColorMap)(int)series.ColorMap,
            series.ValueMinimum,
            series.ValueMaximum);
    }

    private static AnalysisPlotOptionsDto ToPlotOptionsDto(AnalysisPlotOptions options)
    {
        return new AnalysisPlotOptionsDto(
            options.Title,
            options.SymmetricX,
            options.EqualAspect,
            options.ShowVerticalZeroLine,
            options.ShowHorizontalZeroLine,
            (ContractAnalysisLineStyle)(int)options.VerticalZeroLineStyle,
            options.VerticalZeroLineWidth,
            options.XMinimum,
            options.XMaximum,
            options.YMinimum,
            options.YMaximum,
            options.ShowLegend,
            options.HideTopAndRightAxes,
            options.DottedGrid,
            options.GridOpacity,
            options.HideAxes);
    }

    private static Scene2Dto ToScene2Dto(Layout2DScene scene)
    {
        ScenePoint2Dto Point(Layout2DPoint point) => new(point.Z, point.Y);
        return new Scene2Dto(
            scene.Surfaces.Select(surface => new SceneSurface2Dto(
                surface.SurfaceNumber,
                surface.Label,
                surface.IsStop,
                surface.IsReferencePlane,
                surface.Points.Select(Point).ToArray())).ToArray(),
            scene.LensElements.Select(element => new SceneLensElement2Dto(
                element.FrontSurfaceNumber,
                element.BackSurfaceNumber,
                element.Material,
                element.Boundary.Select(Point).ToArray())).ToArray(),
            scene.LensEdges.Select(edge => new SceneLensEdge2Dto(
                edge.FrontSurfaceNumber,
                edge.BackSurfaceNumber,
                Point(edge.Start),
                Point(edge.End))).ToArray(),
            scene.Rays.Select(ray => new SceneRay2Dto(
                ray.RayNumber,
                ray.FieldIndex,
                ray.PupilIndex,
                ray.WavelengthIndex,
                ray.Vignetted,
                ray.FinalIntensity,
                ray.Points.Select(Point).ToArray())).ToArray(),
            scene.ZMin,
            scene.ZMax,
            scene.YExtent);
    }

    private static Scene3Dto ToScene3Dto(Layout3DScene scene)
    {
        ScenePoint3Dto Point(Layout3DPoint point) => new(point.X, point.Y, point.Z);
        return new Scene3Dto(
            scene.Surfaces.Select(surface => new SceneSurface3Dto(
                surface.SurfaceNumber,
                surface.Label,
                surface.IsStop,
                surface.IsReferencePlane,
                 surface.Material,
                 surface.Rim.Select(Point).ToArray(),
                 surface.MeridianY.Select(Point).ToArray(),
                 surface.MeridianX.Select(Point).ToArray(),
                 surface.Faces.Select(face => new SceneSurfaceFace3Dto(
                     face.Points.Select(Point).ToArray())).ToArray())).ToArray(),
            scene.LensElements.Select(element => new SceneLensElement3Dto(
                element.FrontSurfaceNumber,
                element.BackSurfaceNumber,
                element.Material,
                element.RefractiveIndex,
                element.FrontRim.Select(Point).ToArray(),
                element.BackRim.Select(Point).ToArray(),
                element.FrontFaces.Select(face => new SceneSurfaceFace3Dto(
                    face.Points.Select(Point).ToArray())).ToArray(),
                element.BackFaces.Select(face => new SceneSurfaceFace3Dto(
                    face.Points.Select(Point).ToArray())).ToArray(),
                element.MeridianBoundary.Select(Point).ToArray())).ToArray(),
            scene.Rays.Select(ray => new SceneRay3Dto(
                ray.RayNumber,
                ray.FieldIndex,
                ray.PupilIndex,
                ray.WavelengthIndex,
                ray.Vignetted,
                ray.FinalIntensity,
                ray.Points.Select(Point).ToArray())).ToArray(),
            scene.XExtent,
            scene.YExtent,
            scene.ZMin,
            scene.ZMax);
    }
}
