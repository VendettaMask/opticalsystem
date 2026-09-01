using System.Collections.ObjectModel;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.Core;

public sealed class Optic
{
    private OpticState _state;
    private RayTraceCacheBinding? _rayTraceCacheBinding;

    public Optic(string name = "Untitled optic")
    {
        _state = new OpticState(this, name);
        RealRayTracer = new RealRayTracer(this);
        SequentialRayTracer = new SequentialRayTracer(this);
        NonSequentialRayTracer = new NonSequentialRayTracer(this);
        Paraxial = new Paraxial(this);
        Analyses = new AnalysisCatalog(this);
    }

    public string Name
    {
        get => _state.Name;
        set => _state.Name = value;
    }

    public NumericBackendProvider Backend => _state.Backend;

    public SystemAperture Aperture => _state.Aperture;

    public OpticalEnvironment Environment => _state.Environment;

    public IApodizationModel? Apodization
    {
        get => _state.Apodization;
        set
        {
            if (!ReferenceEquals(_state.Apodization, value))
            {
                _state.Apodization = value;
                InvalidateRayTraceCache();
            }
        }
    }

    public MaterialRegistry Materials => _state.Materials;

    public IReadOnlyList<string> GlassCatalogs => Materials.PreferredGlassCatalogs;

    public ObservableCollection<FieldPoint> Fields => _state.Fields;

    public FieldDefinitionKind FieldDefinition
    {
        get => _state.FieldDefinition;
        set
        {
            if (_state.FieldDefinition != value)
            {
                _state.FieldDefinition = value;
                InvalidateRayTraceCache();
            }
        }
    }

    public bool ObjectSpaceTelecentric
    {
        get => _state.ObjectSpaceTelecentric;
        set
        {
            if (_state.ObjectSpaceTelecentric != value)
            {
                _state.ObjectSpaceTelecentric = value;
                InvalidateRayTraceCache();
            }
        }
    }

    public bool FieldGroupTelecentric
    {
        get => _state.FieldGroupTelecentric;
        set
        {
            if (_state.FieldGroupTelecentric != value)
            {
                _state.FieldGroupTelecentric = value;
                InvalidateRayTraceCache();
            }
        }
    }

    public bool RayAimingEnabled
    {
        get => _state.RayAimingEnabled;
        set
        {
            if (_state.RayAimingEnabled != value)
            {
                _state.RayAimingEnabled = value;
                InvalidateRayTraceCache();
            }
        }
    }

    public bool ImageSpaceAfocal
    {
        get => _state.ImageSpaceAfocal;
        set
        {
            if (_state.ImageSpaceAfocal != value)
            {
                _state.ImageSpaceAfocal = value;
                InvalidateRayTraceCache();
            }
        }
    }

    public ObservableCollection<Wavelength> Wavelengths => _state.Wavelengths;

    public SurfaceGroup SurfaceGroup => _state.SurfaceGroup;

    public RealRayTracer RealRayTracer { get; }

    public SequentialRayTracer SequentialRayTracer { get; }

    public NonSequentialRayTracer NonSequentialRayTracer { get; }

    public Paraxial Paraxial { get; }

    public PickupManager Pickups => _state.Pickups;

    public SolveManager Solves => _state.Solves;

    public AnalysisCatalog Analyses { get; }

    public void ConfigureRayTraceCache(RayTraceCache? cache, long opticRevision)
    {
        _rayTraceCacheBinding?.Dispose();
        _rayTraceCacheBinding = null;
        SequentialRayTracer.ConfigureCache(cache, opticRevision);
        if (cache is null)
        {
            return;
        }

        cache.SetCurrentRevision(opticRevision);
        _rayTraceCacheBinding = new RayTraceCacheBinding(this, InvalidateRayTraceCache);
    }

    public void InvalidateRayTraceCache()
    {
        var binding = _rayTraceCacheBinding;
        if (binding is null)
        {
            return;
        }

        _rayTraceCacheBinding = null;
        SequentialRayTracer.ConfigureCache(null, 0);
        binding.Dispose();
    }

    public ObservableCollection<MeritOperandDefinition> MeritFunctionOperands =>
        _state.MeritFunctionOperands;

    public SequentialTrace Trace(
        double normalizedFieldX,
        double normalizedFieldY,
        double wavelengthMicrometers,
        int sampleCount = 100,
        string distribution = "hexapolar")
    {
        return SequentialRayTracer.TraceNormalized(
            normalizedFieldX,
            normalizedFieldY,
            wavelengthMicrometers,
            sampleCount,
            distribution);
    }

    public SequentialTrace TraceGeneric(
        double normalizedFieldX,
        double normalizedFieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthMicrometers)
    {
        return SequentialRayTracer.TraceGeneric(
            normalizedFieldX,
            normalizedFieldY,
            normalizedPupilX,
            normalizedPupilY,
            wavelengthMicrometers);
    }

    public Rays.RayTraceSample? TraceGenericFinalSample(
        double normalizedFieldX,
        double normalizedFieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthMicrometers) =>
        SequentialRayTracer.TraceGenericFinalSample(
            normalizedFieldX,
            normalizedFieldY,
            normalizedPupilX,
            normalizedPupilY,
            wavelengthMicrometers);

    public Rays.RayTraceSample? TraceGenericSurfaceSample(
        double normalizedFieldX,
        double normalizedFieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthMicrometers,
        int surfaceIndex,
        bool aimAtStop = false) =>
        SequentialRayTracer.TraceGenericSurfaceSample(
            normalizedFieldX,
            normalizedFieldY,
            normalizedPupilX,
            normalizedPupilY,
            wavelengthMicrometers,
            surfaceIndex,
            aimAtStop);

    public Optimization.OptimizationProblem CreateOptimizationProblem()
    {
        OpticCapabilityPreflight.EnsureSupported(this, OpticCapabilityOperation.Optimization);
        return new Optimization.OptimizationProblem();
    }

    public Tolerancing.Tolerancing CreateTolerancing()
    {
        return new Tolerancing.Tolerancing();
    }

    public static Optic CreateBlank(string name = "Untitled optic")
    {
        var optic = new Optic(name);
        optic.Fields.Add(new FieldPoint { Label = "On axis", Weight = 1 });
        optic.Wavelengths.Add(new Wavelength
        {
            Label = "d",
            Nanometers = 587.6,
            Weight = 1,
            IsPrimary = true
        });
        optic.SurfaceGroup.ImportLegacySurfaces(new[]
        {
            new OpticalSurface
            {
                Label = "Object",
                Thickness = 100,
                Material = "Air",
                SemiDiameter = 10
            },
            new OpticalSurface
            {
                Label = "Image",
                Material = "Air",
                SemiDiameter = 10
            }
        });
        return optic;
    }

    public static Optic CreateDemo()
    {
        var optic = new Optic("Cooke-style triplet starter");

        optic.Fields.Add(new FieldPoint { Label = "On axis", YAngleDegrees = 0, Weight = 1 });
        optic.Fields.Add(new FieldPoint { Label = "Mid field", YAngleDegrees = 6, Weight = 0.75 });
        optic.Fields.Add(new FieldPoint { Label = "Full field", YAngleDegrees = 12, Weight = 0.5 });

        optic.Wavelengths.Add(new Wavelength { Label = "F", Nanometers = 486.1, Weight = 0.4, IsPrimary = false });
        optic.Wavelengths.Add(new Wavelength { Label = "d", Nanometers = 587.6, Weight = 1.0, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Label = "C", Nanometers = 656.3, Weight = 0.4, IsPrimary = false });

        optic.SurfaceGroup.ImportLegacySurfaces(new[]
        {
            new OpticalSurface
            {
                Label = "Object",
                Radius = 0,
                Thickness = 18,
                Material = "Air",
                SemiDiameter = 14
            },
            new OpticalSurface
            {
                Label = "Aperture stop",
                Radius = 0,
                Thickness = 4,
                Material = "Air",
                SemiDiameter = 7,
                IsStop = true
            },
            new OpticalSurface
            {
                Label = "Front crown",
                Radius = 52,
                Thickness = 5,
                Material = "N-BK7",
                Coating = "MgF2",
                SemiDiameter = 13
            },
            new OpticalSurface
            {
                Label = "Back crown",
                Radius = -38,
                Thickness = 3,
                Material = "Air",
                Coating = "MgF2",
                SemiDiameter = 12
            },
            new OpticalSurface
            {
                Label = "Flint front",
                Radius = -64,
                Thickness = 4,
                Material = "N-F2",
                Coating = "MgF2",
                SemiDiameter = 11
            },
            new OpticalSurface
            {
                Label = "Flint back",
                Radius = -240,
                Thickness = 30,
                Material = "Air",
                Coating = "MgF2",
                SemiDiameter = 11
            },
            new OpticalSurface
            {
                Label = "Image",
                Radius = 0,
                Thickness = 0,
                Material = "Air",
                SemiDiameter = 16
            }
        });

        return optic;
    }

    public static Optic CreateCookeTriplet()
    {
        var optic = new Optic("Optiland Cooke Triplet");
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 10.0;

        optic.Fields.Add(new FieldPoint { Label = "On axis", YAngleDegrees = 0, Weight = 1 });
        optic.Fields.Add(new FieldPoint { Label = "14 deg", YAngleDegrees = 14, Weight = 1 });
        optic.Fields.Add(new FieldPoint { Label = "20 deg", YAngleDegrees = 20, Weight = 1 });

        optic.Wavelengths.Add(new Wavelength { Label = "F", Nanometers = 480, Weight = 1, IsPrimary = false });
        optic.Wavelengths.Add(new Wavelength { Label = "d", Nanometers = 550, Weight = 1, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Label = "C", Nanometers = 650, Weight = 1, IsPrimary = false });

        optic.SurfaceGroup.ImportLegacySurfaces(new[]
        {
            new OpticalSurface
            {
                Label = "Object",
                Radius = 0,
                Thickness = double.PositiveInfinity,
                Material = "Air",
                SemiDiameter = 9.85
            },
            new OpticalSurface
            {
                Label = "Crown front",
                Radius = 22.01359,
                Thickness = 3.25896,
                Material = "SK16",
                SemiDiameter = 9.85
            },
            new OpticalSurface
            {
                Label = "Crown back",
                Radius = -435.76044,
                Thickness = 6.00755,
                Material = "Air",
                SemiDiameter = 9.85
            },
            new OpticalSurface
            {
                Label = "Flint front",
                Radius = -22.21328,
                Thickness = 0.99997,
                Material = "SCHOTT:F2",
                SemiDiameter = 4.6
            },
            new OpticalSurface
            {
                Label = "Flint back / stop",
                Radius = 20.29192,
                Thickness = 4.75041,
                Material = "Air",
                SemiDiameter = 4.6,
                IsStop = true
            },
            new OpticalSurface
            {
                Label = "Rear crown front",
                Radius = 79.68360,
                Thickness = 2.95208,
                Material = "SK16",
                SemiDiameter = 8.4
            },
            new OpticalSurface
            {
                Label = "Rear crown back",
                Radius = -18.39533,
                Thickness = 42.20778,
                Material = "Air",
                SemiDiameter = 8.4
            },
            new OpticalSurface
            {
                Label = "Image",
                Radius = 0,
                Thickness = 0,
                Material = "Air",
                SemiDiameter = 20.9
            }
        });

        return optic;
    }

    public static Optic CreateTessarLens()
    {
        var optic = new Optic("Optiland Tessar Lens f/4.5");
        optic.Aperture.Kind = ApertureKind.FNumber;
        optic.Aperture.Value = 4.5;

        optic.Fields.Add(new FieldPoint { Label = "On axis", YAngleDegrees = 0, Weight = 1 });
        optic.Fields.Add(new FieldPoint { Label = "10 deg", YAngleDegrees = 10, Weight = 1 });
        optic.Fields.Add(new FieldPoint { Label = "20.5 deg", YAngleDegrees = 20.5, Weight = 1 });

        optic.Wavelengths.Add(new Wavelength { Label = "F", Nanometers = 486.1327, Weight = 1, IsPrimary = false });
        optic.Wavelengths.Add(new Wavelength { Label = "d", Nanometers = 587.5618, Weight = 1, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Label = "C", Nanometers = 656.2725, Weight = 1, IsPrimary = false });

        optic.SurfaceGroup.ImportLegacySurfaces(new[]
        {
            new OpticalSurface { Label = "Object", Thickness = double.PositiveInfinity, Material = "Air", SemiDiameter = 0.73 },
            new OpticalSurface { Label = "Front crown", Radius = 1.3329, Thickness = 0.2791, Material = "N-SK15", SemiDiameter = 0.73 },
            new OpticalSurface { Label = "Front crown back", Radius = -9.9754, Thickness = 0.2054, Material = "Air", SemiDiameter = 0.73 },
            new OpticalSurface { Label = "Flint front", Radius = -2.0917, Thickness = 0.09, Material = "SCHOTT:F2", SemiDiameter = 0.48 },
            new OpticalSurface { Label = "Flint back", Radius = 1.2123, Thickness = 0.0709, Material = "Air", SemiDiameter = 0.48 },
            new OpticalSurface { Label = "Aperture stop", Thickness = 0.1534, Material = "Air", SemiDiameter = 0.42, IsStop = true },
            new OpticalSurface { Label = "Rear crown front", Radius = -7.5205, Thickness = 0.09, Material = "K10", SemiDiameter = 0.63 },
            new OpticalSurface { Label = "Cemented interface", Radius = 1.3010, Thickness = 0.3389, Material = "N-SK15", SemiDiameter = 0.63 },
            new OpticalSurface { Label = "Rear crown back", Radius = -1.5218, Thickness = 3.4025, Material = "Air", SemiDiameter = 0.63 },
            new OpticalSurface { Label = "Image", Thickness = 0, Material = "Air", SemiDiameter = 1.72 }
        });

        return optic;
    }

    public OpticSnapshot ToSnapshot()
    {
        return new OpticSnapshot(
            SchemaVersion: OpticSnapshotValidator.CurrentSchemaVersion,
            Name,
            new ApertureSnapshot(
                Aperture.Kind.ToString(),
                Aperture.Value,
                Aperture.ObjectSpaceTelecentric),
            Backend.Current.Name,
            Fields.Select(field => new FieldPointSnapshot(
                field.Label,
                field.XAngleDegrees,
                field.YAngleDegrees,
                field.Weight,
                field.VignetteFactorX,
                field.VignetteFactorY)).ToList(),
            Wavelengths.Select(wavelength => new WavelengthSnapshot(
                wavelength.Label,
                wavelength.Nanometers,
                wavelength.Weight,
                wavelength.IsPrimary)).ToList(),
            SurfaceGroup.Items.Select(surface => SurfaceSnapshotCompatibility.PrepareForSave(
                new SurfaceSnapshot(
                    surface.Number,
                    surface.Label,
                    surface.Radius,
                    surface.Thickness,
                    surface.Material,
                    surface.Coating,
                    surface.SemiDiameter,
                    surface.Conic,
                    surface.IsStop,
                    surface.IsReflective,
                    new SurfaceComponentSnapshot(
                        surface.Geometry.Kind,
                        surface.MaterialBefore.Name,
                        surface.MaterialAfter.Name,
                        surface.CoatingModel.Kind,
                        surface.InteractionModel.Kind,
                        surface.PhysicalAperture?.Kind,
                        surface.ScatteringModel?.Kind,
                        ComponentSnapshotFactory.FromGeometry(surface.Geometry),
                        ComponentSnapshotFactory.FromMaterial(surface.MaterialBefore),
                        ComponentSnapshotFactory.FromMaterial(surface.MaterialAfter),
                        ComponentSnapshotFactory.FromCoating(surface.CoatingModel),
                        ComponentSnapshotFactory.FromInteraction(surface.InteractionModel),
                        ComponentSnapshotFactory.FromAperture(surface.PhysicalAperture),
                        ComponentSnapshotFactory.FromScattering(surface.ScatteringModel)),
                    surface.RadiusVariable,
                    surface.ThicknessVariable,
                    surface.SemiDiameterFixed,
                    new CoordinateSystemSnapshot(
                        surface.CoordinateSystem.Origin.X,
                        surface.CoordinateSystem.Origin.Y,
                        surface.CoordinateSystem.Origin.Z,
                        surface.CoordinateSystem.RotationXDegrees,
                        surface.CoordinateSystem.RotationYDegrees,
                        surface.CoordinateSystem.RotationZDegrees)))).ToList(),
            Apodization: ComponentSnapshotFactory.FromApodization(Apodization),
            FieldDefinition: FieldDefinition.ToString(),
            ObjectSpaceTelecentric: ObjectSpaceTelecentric,
            FieldGroupTelecentric: FieldGroupTelecentric,
            RayAimingEnabled: RayAimingEnabled,
            ImageSpaceAfocal: ImageSpaceAfocal,
            RadiusPickups: Pickups.RadiusPickups.Select(pickup => new RadiusPickupSnapshot(
                pickup.SourceSurface,
                pickup.TargetSurface,
                pickup.Scale,
                pickup.Offset)).ToList(),
            SolveSettings: new SolveSettingsSnapshot(
                Solves.DesiredBackFocus,
                Solves.KeepImageAtBackFocus),
            MeritOperands: MeritFunctionOperands.Select(operand => new MeritOperandSnapshot(
                operand.Enabled,
                operand.Type,
                operand.Surface,
                operand.Field,
                operand.Wavelength,
                operand.Hx,
                operand.Hy,
                operand.Px,
                operand.Py,
                operand.Target,
                operand.Weight,
                operand.Comment,
                operand.PupilRings,
                operand.PupilArms,
                operand.PupilObscuration,
                operand.PupilSampling,
                operand.SpatialFrequency,
                operand.IgnoreLateralColor,
                operand.PolychromaticReference,
                operand.CompatibilityOnly,
                operand.ZemaxIntegerParameters?.ToList() ?? [],
                operand.ZemaxDataParameters?.ToList() ?? [])).ToList(),
            Environment: new EnvironmentSnapshot(
                Environment.MatchRefractiveIndexData,
                Environment.TemperatureCelsius,
                Environment.PressureAtmospheres),
            GlassCatalogs: GlassCatalogs.ToList());
    }

    public void ApplySnapshot(OpticSnapshot snapshot)
    {
        ReplaceState(CreateFromSnapshot(snapshot, this, validate: true));
    }

    internal void RestoreTrustedSnapshot(OpticSnapshot snapshot)
    {
        ReplaceState(CreateFromSnapshot(snapshot, this, validate: false));
    }

    private void ReplaceState(Optic staged)
    {
        staged.Pickups.Rebind(this);
        staged.Solves.Rebind(this);
        _state = staged._state;
    }

    private void ApplySnapshotCore(OpticSnapshot snapshot)
    {
        Name = snapshot.Name;
        if (snapshot.Aperture is not null)
        {
            if (Enum.TryParse<ApertureKind>(snapshot.Aperture.Kind, out var apertureKind))
            {
                Aperture.Kind = apertureKind;
            }

            Aperture.Value = snapshot.Aperture.Value;
            Aperture.ObjectSpaceTelecentric = snapshot.Aperture.ObjectSpaceTelecentric;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.BackendName))
        {
            if (!Backend.Names.Contains(snapshot.BackendName, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Numeric backend '{snapshot.BackendName}' is not registered.");
            }

            Backend.SetBackend(snapshot.BackendName);
        }

        Apodization = ComponentSnapshotFactory.ToApodization(snapshot.Apodization);
        FieldDefinition = Enum.TryParse<FieldDefinitionKind>(snapshot.FieldDefinition, out var fieldDefinition)
            ? fieldDefinition
            : FieldDefinitionKind.Angle;
        ObjectSpaceTelecentric = snapshot.ObjectSpaceTelecentric;
        FieldGroupTelecentric = snapshot.FieldGroupTelecentric;
        RayAimingEnabled = snapshot.RayAimingEnabled;
        ImageSpaceAfocal = snapshot.ImageSpaceAfocal;
        Environment.MatchRefractiveIndexData = snapshot.Environment?.MatchRefractiveIndexData ?? true;
        Environment.TemperatureCelsius = snapshot.Environment?.TemperatureCelsius ?? 20.0;
        Environment.PressureAtmospheres = snapshot.Environment?.PressureAtmospheres ?? 1.0;
        Materials.SetPreferredGlassCatalogs(snapshot.GlassCatalogs);

        Fields.Clear();
        foreach (var field in snapshot.Fields ?? new List<FieldPointSnapshot>())
        {
            Fields.Add(new FieldPoint
            {
                Label = field.Label,
                XAngleDegrees = field.XAngleDegrees,
                YAngleDegrees = field.YAngleDegrees,
                Weight = field.Weight,
                VignetteFactorX = field.VignetteFactorX,
                VignetteFactorY = field.VignetteFactorY
            });
        }

        Wavelengths.Clear();
        foreach (var wavelength in snapshot.Wavelengths ?? new List<WavelengthSnapshot>())
        {
            Wavelengths.Add(new Wavelength
            {
                Label = wavelength.Label,
                Nanometers = wavelength.Nanometers,
                Weight = wavelength.Weight,
                IsPrimary = wavelength.IsPrimary
            });
        }

        var surfaceSnapshots = snapshot.Surfaces ?? new List<SurfaceSnapshot>();
        SurfaceGroup.Replace(surfaceSnapshots.Select(surface =>
        {
            surface = SurfaceSnapshotCompatibility.NormalizeLegacyFromComponents(surface);
            var opticalSurface = new OpticalSurface
            {
                Number = surface.Number,
                Label = surface.Label,
                Radius = surface.Radius,
                Thickness = surface.Thickness,
                Material = surface.Material,
                Coating = surface.Coating,
                SemiDiameter = surface.SemiDiameter,
                Conic = surface.Conic,
                IsStop = surface.IsStop,
                IsReflective = surface.IsReflective,
                RadiusVariable = surface.RadiusVariable,
                ThicknessVariable = surface.ThicknessVariable,
                SemiDiameterFixed = surface.SemiDiameterFixed
            };

            if (surface.Components is not null)
            {
                opticalSurface.Geometry = ComponentSnapshotFactory.ToGeometry(surface.Components.Geometry, surface.Radius, surface.Conic);
                opticalSurface.MaterialBefore = ComponentSnapshotFactory.ToMaterial(surface.Components.MaterialBeforeComponent, surface.Components.MaterialBefore, Materials);
                opticalSurface.MaterialAfter = ComponentSnapshotFactory.ToMaterial(surface.Components.MaterialAfterComponent, surface.Components.MaterialAfter, Materials);
                opticalSurface.CoatingModel = ComponentSnapshotFactory.ToCoating(surface.Components.Coating);
                opticalSurface.InteractionModel = ComponentSnapshotFactory.ToInteraction(surface.Components.Interaction, surface.IsReflective);
                var apertureSnapshot = surface.Components.PhysicalAperture;
                if (apertureSnapshot is null
                    && !string.IsNullOrWhiteSpace(surface.Components.PhysicalApertureKind))
                {
                    apertureSnapshot = ComponentSnapshot.Empty(surface.Components.PhysicalApertureKind);
                }

                opticalSurface.PhysicalAperture = ComponentSnapshotFactory.ToAperture(
                    apertureSnapshot,
                    surface.SemiDiameter);
                opticalSurface.ScatteringModel = ComponentSnapshotFactory.ToScattering(surface.Components.Scattering);
            }

            return opticalSurface;
        }));
        for (var index = 0; index < surfaceSnapshots.Count; index++)
        {
            var coordinate = surfaceSnapshots[index].CoordinateSystem;
            if (coordinate is null)
            {
                continue;
            }

            SurfaceGroup.Items[index].CoordinateSystem = new Coordinates.CoordinateSystem(
                new Backend.Vector3D(coordinate.OriginX, coordinate.OriginY, coordinate.OriginZ),
                coordinate.RotationXDegrees,
                coordinate.RotationYDegrees,
                coordinate.RotationZDegrees);
        }

        Pickups.Clear();
        foreach (var pickup in snapshot.RadiusPickups ?? new List<RadiusPickupSnapshot>())
        {
            Pickups.LinkRadius(
                pickup.SourceSurface,
                pickup.TargetSurface,
                pickup.Scale,
                pickup.Offset);
        }

        Solves.DesiredBackFocus = snapshot.SolveSettings?.DesiredBackFocus ?? 30;
        Solves.KeepImageAtBackFocus = snapshot.SolveSettings?.KeepImageAtBackFocus ?? true;

        MeritFunctionOperands.Clear();
        foreach (var operand in snapshot.MeritOperands ?? new List<MeritOperandSnapshot>())
        {
            MeritFunctionOperands.Add(new MeritOperandDefinition
            {
                Enabled = operand.Enabled,
                Type = MeritFunctionCatalog.CanonicalType(operand.Type),
                Surface = operand.Surface,
                Field = operand.Field,
                Wavelength = operand.Wavelength,
                Hx = operand.Hx,
                Hy = operand.Hy,
                Px = operand.Px,
                Py = operand.Py,
                Target = operand.Target,
                Weight = operand.Weight,
                Comment = operand.Comment,
                PupilRings = operand.PupilRings,
                PupilArms = operand.PupilArms,
                PupilObscuration = operand.PupilObscuration,
                PupilSampling = operand.PupilSampling,
                SpatialFrequency = operand.SpatialFrequency,
                IgnoreLateralColor = operand.IgnoreLateralColor,
                PolychromaticReference = operand.PolychromaticReference,
                CompatibilityOnly = operand.CompatibilityOnly,
                ZemaxIntegerParameters = operand.ZemaxIntegerParameters?.ToArray() ?? [],
                ZemaxDataParameters = operand.ZemaxDataParameters?.ToArray() ?? []
            });
        }
    }

    public static Optic FromSnapshot(OpticSnapshot snapshot)
    {
        return CreateFromSnapshot(snapshot, template: null, validate: true);
    }

    private static Optic CreateFromSnapshot(
        OpticSnapshot snapshot,
        Optic? template,
        bool validate)
    {
        if (validate)
        {
            snapshot = OpticSnapshotMigration.Upgrade(snapshot);
            OpticSnapshotValidator.Validate(snapshot);
        }

        var optic = new Optic(snapshot.Name);
        if (template is not null)
        {
            optic._state.Backend = template.Backend.Clone();
            optic._state.Materials = template.Materials.Clone();
        }

        try
        {
            optic.ApplySnapshotCore(snapshot);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or KeyNotFoundException
            or OverflowException)
        {
            throw new InvalidDataException(
                "The optic snapshot could not be constructed from its validated state.",
                exception);
        }

        return optic;
    }

    private sealed class OpticState
    {
        public OpticState(Optic owner, string name)
        {
            Name = name;
            Pickups = new PickupManager(owner);
            Solves = new SolveManager(owner);
        }

        public string Name { get; set; }

        public NumericBackendProvider Backend { get; set; } = new();

        public SystemAperture Aperture { get; } = new();

        public OpticalEnvironment Environment { get; } = new();

        public IApodizationModel? Apodization { get; set; }

        public MaterialRegistry Materials { get; set; } = new();

        public ObservableCollection<FieldPoint> Fields { get; } = new();

        public FieldDefinitionKind FieldDefinition { get; set; } = FieldDefinitionKind.Angle;

        public bool ObjectSpaceTelecentric { get; set; }

        public bool FieldGroupTelecentric { get; set; }

        public bool RayAimingEnabled { get; set; }

        public bool ImageSpaceAfocal { get; set; }

        public ObservableCollection<Wavelength> Wavelengths { get; } = new();

        public SurfaceGroup SurfaceGroup { get; } = new();

        public PickupManager Pickups { get; }

        public SolveManager Solves { get; }

        public ObservableCollection<MeritOperandDefinition> MeritFunctionOperands { get; } = new();
    }
}
