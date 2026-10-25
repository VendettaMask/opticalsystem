using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal static class AutoLensReferenceStart
{
    public const double ObjectDistance = 20000;

    public static Optic Create(InitialStructureSpecification spec, int elements, FlatStartFamily? family, IReadOnlyList<string> allowed)
    {
        var settings = spec.FlatStart!.AutoLens!;
        var optic = new Optic(spec.Name + " / AutoLens e58");
        AutoLensReferenceMaterials.Register(optic.Materials);
        optic.Materials.SetPreferredGlassCatalogs(spec.GlassCatalogs);
        var halfField = spec.MaximumFieldAngleDegrees * Math.PI / 180;
        var radius = spec.EffectiveFocalLengthMillimeters / spec.FNumber / 2;
        var random = new AutoLensInitializationRandom(checked((uint)(spec.Budget.RandomSeed + (family?.SeedIndex ?? 0))));
        var surfaces = new List<OpticalSurface>
        {
            new() { Label = "Object", Thickness = ObjectDistance, Material = "Air" },
            new() { Label = "Aperture", Thickness = random.Gap(false), Material = "Air", IsStop = true }
        };
        var initialization = settings.OptimizeMaterials ? AutoLensReferenceMaterials.InitialNames : allowed;
        var curvatures = new List<double> { 0, 0 };
        for (var element = 0; element < elements; element++)
        {
            var name = initialization[random.Below(initialization.Count)];
            surfaces.Add(new() { Label = $"Element {element + 1} front", Material = name, Thickness = random.Gap(true) });
            curvatures.Add(random.Curvature(true));
            surfaces.Add(new() { Label = $"Element {element + 1} back", Material = "Air", Thickness = random.Gap(false) });
            curvatures.Add(random.Curvature(AutoLensManagedTopology.IsCemented(spec, element)));
        }
        var opticalLength = surfaces.Skip(1).Take(surfaces.Count - 2).Sum(surface => surface.Thickness);
        var positions = new double[surfaces.Count];
        var position = 0.0;
        for (var index = 1; index < surfaces.Count; index++)
        {
            positions[index] = AutoLensInitializationRandom.NormalizePosition(position, opticalLength,
                spec.MaximumTrackLengthMillimeters - spec.MinimumBackFocusMillimeters);
            position += surfaces[index].Thickness;
        }
        for (var index = 1; index + 1 < surfaces.Count; index++) surfaces[index].Thickness = positions[index + 1] - positions[index];
        surfaces[^1].Thickness = (float)spec.MaximumTrackLengthMillimeters - positions[^1];
        surfaces.Add(new() { Label = "Image", Material = "Air", Thickness = 0 });
        optic.SurfaceGroup.ImportLegacySurfaces(surfaces, optic.Materials);
        for (var index = 1; index + 1 < surfaces.Count; index++)
        {
            var surface = optic.SurfaceGroup.Items[index];
            surface.Geometry = index == 1 ? new PlaneGeometry() : settings.EvenAsphereOrder == 0
                ? new StandardGeometry(1 / curvatures[index]) : new EvenAsphereGeometry(1 / curvatures[index], 0, new double[settings.EvenAsphereOrder]);
            var envelope = index == 1 ? radius : RotationalDesignGeometry.FieldEnvelopeRadius(radius,
                RotationalDesignGeometry.VertexSeparation(optic, 1, index), halfField);
            RotationalDesignGeometry.SetRadius(optic, index, envelope);
            if (settings.OptimizeMaterials && surface.MaterialAfter is not AirMaterial)
            {
                var coordinates = AutoLensReferenceMaterials.NominalCoordinates(surface.MaterialAfter);
                surface.MaterialAfter = OpticalDesignParameters.CreateRelaxedGlass("AutoLensRelaxed-" + index, (float)coordinates.Index, (float)coordinates.Abbe);
            }
        }
        optic.SurfaceGroup.Items[0].PhysicalAperture = null;
        optic.SurfaceGroup.Items[^1].PhysicalAperture = null;
        if (spec.MaximumFieldAngleDegrees > 0)
            RotationalDesignGeometry.SetRadius(optic, 1, RotationalDesignGeometry.FocalLengthForField(
                RotationalDesignGeometry.ImageRadius(spec.EffectiveFocalLengthMillimeters, halfField), spec.MaximumFieldAngleDegrees / 57.3) / spec.FNumber / 2);
        AutoLensManagedTopology.Apply(optic, spec, elements);
        OpticalDesignParameters.SynchronizeIncidentMedia(optic);
        optic.Aperture.Kind = ApertureKind.FloatByStopSize;
        optic.Aperture.Value = 2 * radius;
        optic.RayAimingEnabled = true;
        optic.Fields.Add(new() { Label = "Axis", Weight = 1 });
        optic.Fields.Add(new() { Label = "Full field", YAngleDegrees = spec.MaximumFieldAngleDegrees, Weight = 1 });
        foreach (var wave in spec.Wavelengths.OrderByDescending(wave => wave.Nanometers))
            optic.Wavelengths.Add(new() { Label = wave.Label, Nanometers = wave.Nanometers, Weight = wave.Weight, IsPrimary = wave.IsPrimary });
        optic.SurfaceGroup.Renumber();
        return optic;
    }

    public static IReadOnlyList<OpticalDesignParameter> Parameters(Optic optic, InitialStructureSpecification spec, bool materials)
    {
        var parameters = new List<OpticalDesignParameter>();
        var elements = (optic.SurfaceGroup.Items.Count - 3) / 2;
        for (var index = 2; index < optic.SurfaceGroup.Items.Count; index++)
        {
            var reference = index % 2 == 0 && index > 2 && AutoLensManagedTopology.IsCemented(spec, (index - 4) / 2);
            if (reference) continue;
            var linked = index % 2 == 1 && index < 2 * elements && AutoLensManagedTopology.IsCemented(spec, (index - 3) / 2) ? index + 1 : (int?)null;
            parameters.Add(new(index, OpticalDesignParameterKind.AxialPosition, LinkedSurfaceIndex: linked) { OriginOffset = ObjectDistance });
            if (index == optic.SurfaceGroup.Items.Count - 1) continue;
            parameters.Add(new(index, OpticalDesignParameterKind.Curvature, LinkedSurfaceIndex: linked));
            if (optic.SurfaceGroup.Items[index].Geometry is EvenAsphereGeometry asphere)
            {
                parameters.Add(new(index, OpticalDesignParameterKind.Conic, LinkedSurfaceIndex: linked));
                for (var coefficient = 1; coefficient < asphere.Coefficients.Count; coefficient++)
                    parameters.Add(new(index, OpticalDesignParameterKind.EvenAsphereCoefficient, CoefficientIndex: coefficient, LinkedSurfaceIndex: linked));
            }
            if (materials && optic.SurfaceGroup.Items[index].MaterialAfter is not AirMaterial)
            {
                parameters.Add(new(index, OpticalDesignParameterKind.MaterialIndex, LinkedSurfaceIndex: linked));
                parameters.Add(new(index, OpticalDesignParameterKind.MaterialAbbe, LinkedSurfaceIndex: linked));
            }
        }
        return parameters;
    }

    public static double LearningRate(Optic optic, OpticalDesignParameter parameter) => parameter.Kind switch
    {
        OpticalDesignParameterKind.AxialPosition => 1e-4,
        OpticalDesignParameterKind.Curvature => 1e-3,
        OpticalDesignParameterKind.Conic => 1e-2,
        OpticalDesignParameterKind.EvenAsphereCoefficient => 1e-4 / Math.Pow(Math.Max(optic.SurfaceGroup.Items[parameter.SurfaceIndex].SemiDiameter, 1e-6), 2 * (parameter.CoefficientIndex + 1)),
        OpticalDesignParameterKind.MaterialIndex => 1e-4,
        OpticalDesignParameterKind.MaterialAbbe => 1e-2,
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };

    public static IReadOnlySet<int> ReferenceRows(InitialStructureSpecification spec, int elements) => Enumerable.Range(0, elements - 1)
        .Where(gap => AutoLensManagedTopology.IsCemented(spec, gap)).Select(gap => 2 * gap + 4).ToHashSet();

    public static string TrainingFingerprint(InitialStructureSpecification spec) => ContentFingerprint.Compute(spec with
    { Budget = spec.Budget with { MaximumEvaluations = 0, MaximumParallelism = 1, TimeLimit = TimeSpan.Zero } });
}
