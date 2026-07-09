using System.Collections.ObjectModel;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core;

public sealed class Optic
{
    public Optic(string name = "Untitled optic")
    {
        Name = name;
        RealRayTracer = new RealRayTracer(this);
        Paraxial = new Paraxial(this);
        Aberrations = new Aberrations(this);
        Pickups = new PickupManager(this);
        Solves = new SolveManager(this);
    }

    public string Name { get; set; }

    public ObservableCollection<FieldPoint> Fields { get; } = new();

    public ObservableCollection<Wavelength> Wavelengths { get; } = new();

    public SurfaceGroup SurfaceGroup { get; } = new();

    public RealRayTracer RealRayTracer { get; }

    public Paraxial Paraxial { get; }

    public Aberrations Aberrations { get; }

    public PickupManager Pickups { get; }

    public SolveManager Solves { get; }

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
            Name,
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
                surface.IsStop)).ToList());
    }

    public void ApplySnapshot(OpticSnapshot snapshot)
    {
        Name = snapshot.Name;

        Fields.Clear();
        foreach (var field in snapshot.Fields)
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
        foreach (var wavelength in snapshot.Wavelengths)
        {
            Wavelengths.Add(new Wavelength
            {
                Label = wavelength.Label,
                Nanometers = wavelength.Nanometers,
                Weight = wavelength.Weight,
                IsPrimary = wavelength.IsPrimary
            });
        }

        SurfaceGroup.Replace(snapshot.Surfaces.Select(surface => new OpticalSurface
        {
            Number = surface.Number,
            Label = surface.Label,
            Radius = surface.Radius,
            Thickness = surface.Thickness,
            Material = surface.Material,
            Coating = surface.Coating,
            SemiDiameter = surface.SemiDiameter,
            Conic = surface.Conic,
            IsStop = surface.IsStop
        }));
    }

    public static Optic FromSnapshot(OpticSnapshot snapshot)
    {
        var optic = new Optic(snapshot.Name);
        optic.ApplySnapshot(snapshot);
        return optic;
    }
}
