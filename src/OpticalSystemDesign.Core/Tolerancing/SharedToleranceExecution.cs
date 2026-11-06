using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.Tolerancing;

internal interface ISharedSurfaceForm
{
    int SurfaceNumber { get; }
}

/// <summary>Applies one physical draw to matching perturbations on independent configuration copies.</summary>
public static class SharedToleranceExecution
{
    public static void ApplyTrial(IReadOnlyList<(Optic Optic, Tolerancing Study)> configurations,
        int seed, int trial, CancellationToken cancellationToken, Action<Random>? applyLocal = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (configurations.Count == 0) throw new ArgumentException("At least one configuration is required.", nameof(configurations));
        var groups = configurations.Select(configuration => configuration.Study.TrialPerturbations().ToArray()).ToArray();
        if (groups.Any(group => group.Length != groups[0].Length))
            throw new ArgumentException("Shared tolerances must match across configurations.", nameof(configurations));
        var random = new Random(MonteCarlo.DeriveTrialSeed(seed, trial));
        applyLocal?.Invoke(random);
        for (var row = 0; row < groups[0].Length; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(configurations, groups.Select(group => group[row]).ToArray(), random, false, 1);
        }
    }

    public static void ApplyEndpoint(IReadOnlyList<(Optic Optic, Tolerancing Study)> configurations,
        int row, bool maximum, double scale)
    {
        VariableRangePerturbation.ValidateScale(scale);
        if (configurations.Count == 0) throw new ArgumentException("At least one configuration is required.", nameof(configurations));
        Apply(configurations, configurations.Select(configuration => configuration.Study.Perturbations[row]).ToArray(),
            null, maximum, scale);
    }

    private static void Apply(IReadOnlyList<(Optic Optic, Tolerancing Study)> configurations,
        IPerturbation[] perturbations, Random? random, bool maximum, double scale)
    {
        var reference = configurations[0].Optic;
        double Endpoint(IRangePerturbation perturbation) => (maximum ? perturbation.Maximum : perturbation.Minimum) * scale;

        switch (perturbations[0])
        {
            case VariableRangePerturbation variable:
                var delta = random is null ? Endpoint(variable) : variable.DrawDelta(random);
                foreach (var peer in perturbations.Cast<VariableRangePerturbation>()) peer.ApplyDelta(delta);
                break;
            case SurfaceFringePerturbation fringe:
                var (fringes, angle) = random is null ? (Endpoint(fringe), 0d) : fringe.DrawFringes(random);
                var radius = fringe.ReferenceRadius(reference);
                for (var i = 0; i < perturbations.Length; i++)
                    ((SurfaceFringePerturbation)perturbations[i]).ApplyFringes(configurations[i].Optic, fringes, angle, radius);
                break;
            case ISharedSurfaceForm form:
                var surface = reference.SurfaceGroup.Items[form.SurfaceNumber];
                IGeometry original = surface.Geometry;
                if (random is not null) ((ISampledPerturbation)perturbations[0]).Apply(reference, random);
                else if (maximum) ((IScaledRangePerturbation)perturbations[0]).ApplyScaledMaximum(reference, scale);
                else ((IScaledRangePerturbation)perturbations[0]).ApplyScaledMinimum(reference, scale);
                if (!ReferenceEquals(original, surface.Geometry))
                    for (var i = 1; i < configurations.Count; i++)
                        SurfaceFormPerturbation.CopyDeformation(surface.Geometry,
                            configurations[i].Optic.SurfaceGroup.Items[((ISharedSurfaceForm)perturbations[i]).SurfaceNumber]);
                break;
            default:
                throw new NotSupportedException($"The tolerance {perturbations[0].Name} does not define a shared physical perturbation.");
        }
    }
}
