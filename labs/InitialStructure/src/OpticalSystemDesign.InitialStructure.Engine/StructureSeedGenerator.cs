using System.Collections.Immutable;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal sealed record StructureGenerationTrace(double[] Marginal, double[] Chief, double Invariant,
    double[] DistancesMillimeters, double[] PowersPerMillimeter, DesignTopology Physical);

internal sealed class StructureGenerationWork
{
    internal int MatrixRequests { get; set; }
    internal long GeometryProbes { get; set; }
}

/// <summary>Descriptor proposals, independent of source lens coordinates/shapes. All optical
/// construction formulas live in Core. Actual thick-lens feasibility is checked by the caller.</summary>
internal static class StructureSeedGenerator
{
    internal static DesignTopology Create(AutonomousSearchPlan plan, StructureGenerationOptions descriptor,
        ref DeterministicRandom random, Action charge, CancellationToken token, Action<StructureGenerationTrace>? diagnostic = null,
        StructureGenerationWork? work = null)
    {
        Validate(descriptor);
        if (descriptor.MaterializationVersion == 2)
            return ThickStructureSeedGenerator.Create(plan, descriptor, ref random, charge, work ?? new(), token);
        var envelope = plan.Seeds[0];
        var n = descriptor.Elements.Length;
        if (envelope.Elements.Any(element => element.Protected) || envelope.HasTransitionElements)
            throw new ArgumentException("Generating a new structure cannot replace protected or transition elements.");
        if (n < plan.Constraints.MinimumElementCount || n > plan.Constraints.MaximumElementCount)
            throw new ArgumentException("Descriptor element count exceeds the original structural bounds.");
        var materialContext = TopologyCompiler.Compile(envelope, plan.Constraints).CreateOptic();
        var primary = envelope.Wavelengths.Single(wavelength => wavelength.IsPrimary).Nanometers;
        var thickness = new double[n]; var weights = new double[n + 1];
        for (var i = 0; i < n; i++) thickness[i] = descriptor.MinimumThicknessMillimeters
            + random.NextUnitDouble() * (descriptor.MaximumThicknessMillimeters - descriptor.MinimumThicknessMillimeters);
        for (var i = 0; i <= n; i++) weights[i] = .2 + random.NextUnitDouble();
        double[]? powers = null;
        double[]? tracedY = null, tracedChief = null, tracedDistances = null; double tracedInvariant = 0;
        if (descriptor.Method == StructureGenerationMethod.Delano)
        {
            var aperture = plan.Targets.EffectiveFocalLengthMillimeters / (2 * plan.Targets.FNumber);
            var angle = Math.Max(.1, envelope.Fields.Max(field => Math.Sqrt(field.XAngleDegrees * field.XAngleDegrees + field.YAngleDegrees * field.YAngleDegrees)));
            var slope = Math.Tan(angle * Math.PI / 180);
            var height = plan.Targets.EffectiveFocalLengthMillimeters * slope;
            var y = Enumerable.Range(0, n + 1).Select(i => aperture * (n - i) / n).ToArray();
            var chief = Enumerable.Range(0, n + 1).Select(i => height * (i - descriptor.StopAfterElement + .5)
                / (n - descriptor.StopAfterElement + .5)).ToArray();
            chief[n] = height;
            var current = Evaluate(y, chief); var best = current; var bestY = y; var bestChief = chief;
            for (var step = 0; step < descriptor.AnnealingProposals; step++)
            {
                token.ThrowIfCancellationRequested();
                var py = y.ToArray(); var pc = chief.ToArray();
                for (var i = 1; i < n; i++) py[i] += aperture * .12 * (2 * random.NextUnitDouble() - 1);
                for (var i = 0; i < n; i++) pc[i] += height * .12 * (2 * random.NextUnitDouble() - 1);
                var score = Evaluate(py, pc);
                var temperature = .2 * (1 - (double)step / Math.Max(1, descriptor.AnnealingProposals));
                if (score < current || double.IsFinite(score) && random.NextUnitDouble() < Math.Exp(Math.Min(0, (current - score) / temperature)))
                { y = py; chief = pc; current = score; }
                if (score < best) { best = score; bestY = py; bestChief = pc; }
            }
            charge();
            var construction = DelanoDiagram.Construct(bestY, bestChief, aperture * slope);
            powers = construction.PowersPerMillimeter;
            tracedY = bestY; tracedChief = bestChief; tracedDistances = construction.DistancesMillimeters; tracedInvariant = aperture * slope;
            for (var i = 1; i <= n; i++) weights[i] = construction.DistancesMillimeters[i - 1];

            double Evaluate(double[] marginal, double[] chiefRay)
            {
                charge(); token.ThrowIfCancellationRequested();
                try
                {
                    var value = DelanoDiagram.Construct(marginal, chiefRay, aperture * slope);
                    var track = value.DistancesMillimeters.Sum();
                    var available = envelope.Image.ZMillimeters - envelope.Reference.ZMillimeters;
                    // Proposal ranking only, not an image-quality/manufacturing acceptance surrogate.
                    var score = value.PowersPerMillimeter.Sum(power => Math.Pow(power * plan.Targets.EffectiveFocalLengthMillimeters, 2)) * .01
                        + Math.Pow(Math.Max(0, track / available - 1), 2)
                        + Math.Pow(Math.Max(0, descriptor.MinimumBackFocusMillimeters - value.DistancesMillimeters[^1]) / available, 2);
                    if (descriptor.StopAfterElement > 0 && chiefRay[descriptor.StopAfterElement - 1] > 0) score += 10;
                    if (descriptor.StopAfterElement < n && chiefRay[descriptor.StopAfterElement] < 0) score += 10;
                    return score;
                }
                catch (ArithmeticException) { return double.PositiveInfinity; }
            }
        }
        var positions = new double[n];
        var margin = Math.Max(plan.Constraints.MinimumAirGapMillimeters, .05);
        var back = Math.Max(descriptor.MinimumBackFocusMillimeters, plan.Constraints.MinimumBackFocusMillimeters);
        Layout(0, descriptor.StopAfterElement, envelope.Reference.ZMillimeters,
            Math.Min(envelope.Stop.ZMillimeters - margin, envelope.Image.ZMillimeters - back));
        Layout(descriptor.StopAfterElement, n, envelope.Stop.ZMillimeters + margin, envelope.Image.ZMillimeters - back);
        var elements = ImmutableArray.CreateBuilder<LensElement>();
        for (var i = 0; i < n; i++)
        {
            var item = descriptor.Elements[i];
            var material = item.Materials[(int)(random.NextUnitDouble() * item.Materials.Length)];
            var index = materialContext.Materials.Resolve(material).RefractiveIndex(primary);
            var curvature = powers is null ? (2 * random.NextUnitDouble() - 1) * plan.Options.CurvatureBoundPerMillimeter * .5
                : DelanoDiagram.SymmetricSingletCurvature(powers[i], index, thickness[i]);
            curvature = Math.Clamp(curvature, -plan.Options.CurvatureBoundPerMillimeter, plan.Options.CurvatureBoundPerMillimeter);
            var bend = powers is null ? (2 * random.NextUnitDouble() - 1) * Math.Abs(curvature) * .5 : 0;
            var id = "generated-" + i;
            TopologyFace Face(double c, StructureApertureDescriptor? aperture) => new()
            {
                ClearSemiDiameterMillimeters = aperture?.ClearSemiDiameterMillimeters ?? descriptor.ClearSemiDiameterMillimeters,
                MechanicalSemiDiameterMillimeters = aperture is null ? descriptor.ClearSemiDiameterMillimeters : aperture.MechanicalSemiDiameterMillimeters,
                Profile = new()
                {
                    Kind = item.Profile,
                    CurvaturePerMillimeter = c,
                    MaximumEvenPower = item.Profile == TopologyProfileKind.EvenAsphere ? 4 : 0
                }
            };
            elements.Add(new()
            {
                Id = id,
                FrontSurfaceId = id + ":front",
                BackSurfaceId = id + ":back",
                FrontZMillimeters = positions[i],
                ThicknessMillimeters = thickness[i],
                Material = material,
                Front = Face(curvature + bend, item.FrontAperture),
                Back = Face(-curvature + bend, item.BackAperture)
            });
        }
        var topology = envelope with { Name = "Descriptor " + descriptor.Method, Elements = elements.ToImmutable() };
        TopologyValidation.Validate(topology, plan.Constraints);
        if (tracedY is not null) diagnostic?.Invoke(new(tracedY, tracedChief!, tracedInvariant, tracedDistances!, powers!, topology));
        return topology;

        void Layout(int first, int end, double start, double limit)
        {
            if (first == end) return;
            var count = end - first;
            var free = limit - start - thickness.Skip(first).Take(count).Sum() - margin * count;
            if (!double.IsFinite(free) || free <= 0) throw new ArgumentException("No room for the descriptor inside the fixed reference/stop/image intervals.");
            var sum = weights.Skip(first).Take(count + 1).Sum();
            var z = start;
            for (var i = first; i < end; i++)
            { z += margin + free * weights[i] / sum; positions[i] = z; z += thickness[i]; }
        }
    }

    internal static void Validate(StructureGenerationOptions options)
    {
        if (options.MaterializationVersion is not (null or 2) || options.MaterializationVersion == 2 && options.Method != StructureGenerationMethod.Delano
            || options.Version != 1 || !Enum.IsDefined(options.Method) || options.Elements.IsDefaultOrEmpty || options.Elements.Length is < 2 or > 16
            || options.StopAfterElement < 0 || options.StopAfterElement > options.Elements.Length
            || !double.IsFinite(options.ClearSemiDiameterMillimeters) || options.ClearSemiDiameterMillimeters is <= 0 or > 1000
            || !double.IsFinite(options.MinimumThicknessMillimeters) || options.MinimumThicknessMillimeters <= 0
            || !double.IsFinite(options.MaximumThicknessMillimeters) || options.MaximumThicknessMillimeters < options.MinimumThicknessMillimeters
            || !double.IsFinite(options.MinimumBackFocusMillimeters) || options.MinimumBackFocusMillimeters <= 0
            || options.AnnealingProposals is < 0 or > 100
            || options.Elements.Any(element => element is null || element.Kind != "S"
                || element.Profile is not (TopologyProfileKind.Standard or TopologyProfileKind.EvenAsphere)
                || !ValidAperture(element.FrontAperture) || !ValidAperture(element.BackAperture)
                || element.Materials.IsDefaultOrEmpty || element.Materials.Length > 32
                || element.Materials.Any(material => string.IsNullOrWhiteSpace(material) || material.Length > 128)))
            throw new ArgumentException("Invalid descriptor: v1 supports 2–16 separate spherical/even-aspheric singlets and a declared stop interval; cemented groups are unsupported.");

        static bool ValidAperture(StructureApertureDescriptor? aperture) => aperture is null
            || double.IsFinite(aperture.ClearSemiDiameterMillimeters) && aperture.ClearSemiDiameterMillimeters is >= .001 and <= 1000
            && (aperture.MechanicalSemiDiameterMillimeters is not { } mechanical
                || double.IsFinite(mechanical) && mechanical >= aperture.ClearSemiDiameterMillimeters && mechanical <= 1000);
    }
}
