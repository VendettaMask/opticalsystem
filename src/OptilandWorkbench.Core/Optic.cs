using System.Collections.ObjectModel;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.Core;

public sealed class Optic
{
    public Optic(string name = "Untitled optic")
    {
        Name = name;
        RealRayTracer = new RealRayTracer(this);
        SequentialRayTracer = new SequentialRayTracer(this);
        Paraxial = new Paraxial(this);
        Aberrations = new Aberrations(this);
        Pickups = new PickupManager(this);
        Solves = new SolveManager(this);
        Analyses = new AnalysisCatalog(this);
    }

    public string Name { get; set; }

    public NumericBackendProvider Backend { get; } = new();

    public SystemAperture Aperture { get; } = new();

    public MaterialRegistry Materials { get; } = new();

    public ObservableCollection<FieldPoint> Fields { get; } = new();

    public ObservableCollection<Wavelength> Wavelengths { get; } = new();

    public SurfaceGroup SurfaceGroup { get; } = new();

    public RealRayTracer RealRayTracer { get; }

    public SequentialRayTracer SequentialRayTracer { get; }

    public Paraxial Paraxial { get; }

    public Aberrations Aberrations { get; }

    public PickupManager Pickups { get; }

    public SolveManager Solves { get; }

    public AnalysisCatalog Analyses { get; }

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

    public Optimization.OptimizationProblem CreateOptimizationProblem()
    {
        return new Optimization.OptimizationProblem();
    }

    public Tolerancing.Tolerancing CreateTolerancing()
    {
        return new Tolerancing.Tolerancing();
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

        optic.SurfaceGroup.Replace(new[]
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

    public OpticSnapshot ToSnapshot()
    {
        return new OpticSnapshot(
            SchemaVersion: 2,
            Name,
            new ApertureSnapshot(Aperture.Kind.ToString(), Aperture.Value),
            Backend.Current.Name,
            Fields.Select(field => new FieldPointSnapshot(
                field.Label,
                field.XAngleDegrees,
                field.YAngleDegrees,
                field.Weight)).ToList(),
            Wavelengths.Select(wavelength => new WavelengthSnapshot(
                wavelength.Label,
                wavelength.Nanometers,
                wavelength.Weight,
                wavelength.IsPrimary)).ToList(),
            SurfaceGroup.Items.Select(surface => new SurfaceSnapshot(
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
                    ComponentSnapshotFactory.FromScattering(surface.ScatteringModel)))).ToList());
    }

    public void ApplySnapshot(OpticSnapshot snapshot)
    {
        Name = snapshot.Name;
        if (snapshot.Aperture is not null)
        {
            if (Enum.TryParse<ApertureKind>(snapshot.Aperture.Kind, out var apertureKind))
            {
                Aperture.Kind = apertureKind;
            }

            Aperture.Value = snapshot.Aperture.Value;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.BackendName) && Backend.Names.Contains(snapshot.BackendName))
        {
            Backend.SetBackend(snapshot.BackendName);
        }

        Fields.Clear();
        foreach (var field in snapshot.Fields ?? new List<FieldPointSnapshot>())
        {
            Fields.Add(new FieldPoint
            {
                Label = field.Label,
                XAngleDegrees = field.XAngleDegrees,
                YAngleDegrees = field.YAngleDegrees,
                Weight = field.Weight
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

        SurfaceGroup.Replace((snapshot.Surfaces ?? new List<SurfaceSnapshot>()).Select(surface =>
        {
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
                IsReflective = surface.IsReflective
            };

            if (surface.Components is not null)
            {
                opticalSurface.Geometry = ComponentSnapshotFactory.ToGeometry(surface.Components.Geometry, surface.Radius, surface.Conic);
                opticalSurface.MaterialBefore = ComponentSnapshotFactory.ToMaterial(surface.Components.MaterialBeforeComponent, surface.Components.MaterialBefore, Materials);
                opticalSurface.MaterialAfter = ComponentSnapshotFactory.ToMaterial(surface.Components.MaterialAfterComponent, surface.Components.MaterialAfter, Materials);
                opticalSurface.CoatingModel = ComponentSnapshotFactory.ToCoating(surface.Components.Coating);
                opticalSurface.InteractionModel = ComponentSnapshotFactory.ToInteraction(surface.Components.Interaction, surface.IsReflective);
                opticalSurface.PhysicalAperture = ComponentSnapshotFactory.ToAperture(surface.Components.PhysicalAperture, surface.SemiDiameter);
                opticalSurface.ScatteringModel = ComponentSnapshotFactory.ToScattering(surface.Components.Scattering);
            }

            return opticalSurface;
        }), syncComposition: false);
    }

    public static Optic FromSnapshot(OpticSnapshot snapshot)
    {
        var optic = new Optic(snapshot.Name);
        optic.ApplySnapshot(snapshot);
        return optic;
    }
}
