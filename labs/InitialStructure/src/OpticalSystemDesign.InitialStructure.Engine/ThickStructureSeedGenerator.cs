using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Layout first, then a shared-Core inverse marginal-ray construction on actual glass/air segments.</summary>
internal static class ThickStructureSeedGenerator
{
    internal static DesignTopology Create(AutonomousSearchPlan plan, StructureGenerationOptions descriptor,
        ref DeterministicRandom random, Action charge, StructureGenerationWork work, CancellationToken token,
        bool requireFeasibleGeometry = false, double[]? initialShape = null, int[]? powerSigns = null, bool captureCosts = false)
    {
        var envelope = plan.Seeds[0]; var count = descriptor.Elements.Length;
        if (envelope.Elements.Any(e => e.Protected) || envelope.HasTransitionElements
            || count < plan.Constraints.MinimumElementCount || count > plan.Constraints.MaximumElementCount)
            throw new ArgumentException("Thick generation cannot replace protected/transition elements or exceed structural bounds.");
        var context = TopologyCompiler.Compile(envelope, plan.Constraints).CreateOptic();
        var wavelength = envelope.Wavelengths.Single(w => w.IsPrimary).Nanometers;
        var thickness = Enumerable.Range(0, count).Select(_ => descriptor.MinimumThicknessMillimeters).ToArray();
        for (var i = 0; i < count; i++) thickness[i] += random.NextUnitDouble() * (descriptor.MaximumThicknessMillimeters - descriptor.MinimumThicknessMillimeters);
        var weights = new double[count + 1];
        for (var i = 0; i <= count; i++) weights[i] = .2 + random.NextUnitDouble();
        var positions = new double[count]; var margin = Math.Max(.05, plan.Constraints.MinimumAirGapMillimeters);
        var back = Math.Max(descriptor.MinimumBackFocusMillimeters, plan.Constraints.MinimumBackFocusMillimeters);
        Layout(0, descriptor.StopAfterElement, envelope.Reference.ZMillimeters,
            Math.Min(envelope.Stop.ZMillimeters - margin, envelope.Image.ZMillimeters - back));
        Layout(descriptor.StopAfterElement, count, envelope.Stop.ZMillimeters + margin, envelope.Image.ZMillimeters - back);
        var materials = new string[count]; var segments = new ThickDelanoElement[count];
        for (var i = 0; i < count; i++)
        {
            var choices = descriptor.Elements[i].Materials;
            materials[i] = choices[random.NextInt32(choices.Length)];
            segments[i] = new(positions[i], thickness[i], context.Materials.Resolve(materials[i]).RefractiveIndex(wavelength));
        }
        double[]? feasibleShape = null; var feasibleScore = double.PositiveInfinity;
        work.MinimumClearanceMarginMillimeters = null;
        var shape = initialShape?.ToArray() ?? Enumerable.Repeat(1.0, 2 * count - 1).ToArray();
        if (shape.Length != 2 * count - 1 || shape.Any(v => !double.IsFinite(v) || Math.Abs(v) > 4))
            throw new ArgumentException("A finite bounded marginal-path proposal is required.");
        var current = Evaluate(shape); var best = current; var bestShape = shape;
        for (var step = 0; step < descriptor.AnnealingProposals; step++)
        {
            token.ThrowIfCancellationRequested();
            var proposed = shape.ToArray();
            for (var i = 0; i < proposed.Length; i++) proposed[i] = Math.Clamp(proposed[i] + .6 * (2 * random.NextUnitDouble() - 1), -4, 4);
            var sample = Evaluate(proposed);
            var temperature = .2 * (1 - (double)step / Math.Max(1, descriptor.AnnealingProposals));
            if (sample.Score < current.Score || double.IsFinite(sample.Score) && random.NextUnitDouble() < Math.Exp(Math.Min(0, (current.Score - sample.Score) / temperature)))
            { shape = proposed; current = sample; }
            if (sample.Score < best.Score) { best = sample; bestShape = proposed; }
        }
        // Reconstruct/recheck once, just as the legacy diagram path charges its final construction.
        if (requireFeasibleGeometry && feasibleShape is null)
            throw new ArithmeticException("This thick layout has no geometrically feasible realization within its proposal budget.");
        var final = Evaluate(requireFeasibleGeometry ? feasibleShape! : bestShape);
        return final.Topology ?? throw new ArithmeticException("No bounded thick Delano realization was constructible.");

        (DesignTopology? Topology, double Score) Evaluate(double[] candidate)
        {
            charge(); token.ThrowIfCancellationRequested();
            try
            {
                var constructed = FixedStopDelano.Construct(segments, plan.Targets.EffectiveFocalLengthMillimeters / (2 * plan.Targets.FNumber),
                    envelope.Stop.ZMillimeters, envelope.Stop.ClearSemiDiameterMillimeters, envelope.Image.ZMillimeters,
                    plan.Targets.EffectiveFocalLengthMillimeters, candidate);
                if (constructed.FrontCurvaturesPerMillimeter.Concat(constructed.BackCurvaturesPerMillimeter)
                    .Any(v => Math.Abs(v) > plan.Options.CurvatureBoundPerMillimeter)) return (null, double.PositiveInfinity);
                var lenses = ImmutableArray.CreateBuilder<LensElement>();
                for (var i = 0; i < count; i++)
                {
                    var item = descriptor.Elements[i]; var id = "generated-" + i;
                    TopologyFace Face(double curvature, StructureApertureDescriptor? aperture) => new()
                    {
                        ClearSemiDiameterMillimeters = aperture?.ClearSemiDiameterMillimeters ?? descriptor.ClearSemiDiameterMillimeters,
                        MechanicalSemiDiameterMillimeters = aperture is null ? descriptor.ClearSemiDiameterMillimeters : aperture.MechanicalSemiDiameterMillimeters,
                        Profile = new()
                        {
                            Kind = item.Profile,
                            CurvaturePerMillimeter = curvature,
                            MaximumEvenPower = item.Profile == TopologyProfileKind.EvenAsphere ? 4 : 0
                        }
                    };
                    lenses.Add(new()
                    {
                        Id = id,
                        FrontSurfaceId = id + ":front",
                        BackSurfaceId = id + ":back",
                        FrontZMillimeters = positions[i],
                        ThicknessMillimeters = thickness[i],
                        Material = materials[i],
                        Front = Face(constructed.FrontCurvaturesPerMillimeter[i], item.FrontAperture),
                        Back = Face(constructed.BackCurvaturesPerMillimeter[i], item.BackAperture)
                    });
                }
                var topology = envelope with { Name = "Descriptor Thick Delano", Elements = lenses.ToImmutable() };
                var source = TopologyCompiler.Compile(topology, plan.Constraints); var optic = source.CreateOptic();
                var matrixStarted = captureCosts ? Stopwatch.GetTimestamp() : 0;
                work.MatrixRequests++; var focal = optic.Paraxial.EstimateEffectiveFocalLength();
                work.MatrixRequests++; var fNumber = optic.Paraxial.EstimateFNumber();
                work.MatrixRequests++; var matrix = optic.Paraxial.GetTransferMatrix(source.SurfaceIndices[topology.Reference.Id], source.SurfaceIndices[topology.Image.Id]);
                var score = Math.Pow((focal / plan.Targets.EffectiveFocalLengthMillimeters - 1) / plan.Targets.FocalLengthRelativeTolerance, 2)
                    + Math.Pow((fNumber / plan.Targets.FNumber - 1) / plan.Targets.FNumberRelativeTolerance, 2)
                    + Math.Pow(matrix.A / .001, 2);
                var feasible = double.IsFinite(focal) && focal > 0 && double.IsFinite(fNumber) && fNumber > 0
                    && Math.Abs(focal / plan.Targets.EffectiveFocalLengthMillimeters - 1) <= plan.Targets.FocalLengthRelativeTolerance
                    && Math.Abs(fNumber / plan.Targets.FNumber - 1) <= plan.Targets.FNumberRelativeTolerance
                    && Math.Abs(matrix.A) <= 1e-8;
                if (powerSigns is not null)
                {
                    if (powerSigns.Length != count) throw new ArgumentException("Power signs must match the element count.");
                    for (var i = 0; i < count; i++)
                    {
                        var map = source.Elements[i]; work.MatrixRequests++;
                        var power = optic.Paraxial.ElementOpticalPower(map.FrontIndex, map.BackIndex, wavelength);
                        var matches = double.IsFinite(power) && power * powerSigns[i] > 1e-10;
                        feasible &= matches;
                        if (!matches) score += 100 + Math.Pow(Math.Min(0, power * powerSigns[i]) / .001, 2);
                    }
                }
                if (captureCosts) work.MatrixTime += Stopwatch.GetElapsedTime(matrixStarted);
                var geometryStarted = captureCosts ? Stopwatch.GetTimestamp() : 0;
                var clearanceMargin = double.PositiveInfinity;
                foreach (var row in TopologyEvaluationPlan.Geometry(source, plan.Targets, 64))
                {
                    token.ThrowIfCancellationRequested(); work.GeometryProbes++;
                    var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                    if (!double.IsFinite(value)) return (null, double.PositiveInfinity);
                    var violation = row.Upper ? value - row.Limit : row.Limit - value;
                    feasible &= violation <= 0;
                    if (!row.Upper) clearanceMargin = Math.Min(clearanceMargin, -violation);
                    score += Math.Pow(Math.Max(0, violation + 1e-6) / .01, 2);
                }
                if (captureCosts) work.GeometryTime += Stopwatch.GetElapsedTime(geometryStarted);
                if (requireFeasibleGeometry)
                {
                    if (!feasible) work.RejectedGeometryProposals++;
                    else if (-clearanceMargin < feasibleScore)
                    {
                        feasibleScore = -clearanceMargin;
                        feasibleShape = candidate.ToArray();
                        work.MinimumClearanceMarginMillimeters = clearanceMargin;
                    }
                    // Hard feasibility governs return eligibility; the continuous
                    // penalty still guides proposals that have not reached it.
                    if (feasible) score = -clearanceMargin;
                }
                return double.IsFinite(score) ? (topology, score) : (null, double.PositiveInfinity);
            }
            catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { return (null, double.PositiveInfinity); }
        }

        void Layout(int first, int end, double start, double limit)
        {
            if (first == end) return;
            var free = limit - start - thickness.Skip(first).Take(end - first).Sum() - margin * (end - first);
            if (!double.IsFinite(free) || free <= 0) throw new ArgumentException("No physical space for this descriptor around the fixed stop.");
            var sum = weights.Skip(first).Take(end - first + 1).Sum(); var z = start;
            for (var i = first; i < end; i++) { z += margin + free * weights[i] / sum; positions[i] = z; z += thickness[i]; }
        }
    }
}
