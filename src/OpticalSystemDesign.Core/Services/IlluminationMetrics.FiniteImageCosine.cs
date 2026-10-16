namespace OpticalSystemDesign.Core.Services;

public static partial class IlluminationMetrics
{
    private sealed partial class Integrator
    {
        // Explicit finite-grid profile, independently observed in the 2026 R1
        // operand controls. The general physical quadratures remain separate.
        private IlluminationMetricResult IntegrateFiniteImageCosine(int requestedDensity)
        {
            var density = Math.Min(requestedDensity, 100);
            var radius = Math.Max(8, density / 2) - 1;
            var envelope = new List<Node>();
            var mappingNodes = new List<Node>();
            for (var row = -radius; row <= radius; row++)
                for (var column = -radius; column <= radius; column++)
                {
                    if (row * row + column * column > radius * radius) continue;
                    var node = Sample(column / (double)radius, row / (double)radius);
                    if (node.Mapped) mappingNodes.Add(node);
                    if (node.Mapped && node.Inside) envelope.Add(node);
                }
            if (envelope.Count == 0) return new(0, 0, _nodes.Count)
            {
                Sampling = IlluminationSamplingKind.FiniteImageCosine,
                IntegrationSamples = checked((2 * density + 1) * (2 * density + 1))
            };
            var minimumL = envelope.Min(node => node.L);
            var maximumL = envelope.Max(node => node.L);
            var minimumM = envelope.Min(node => node.M);
            var maximumM = envelope.Max(node => node.M);
            var centerL = (minimumL + maximumL) / 2;
            var centerM = (minimumM + maximumM) / 2;
            var deltaL = (maximumL - minimumL) * 1.01 / (2 * density);
            var deltaM = (maximumM - minimumM) * 1.01 / (2 * density);
            if (!double.IsFinite(deltaL * deltaM) || deltaL <= 0 || deltaM <= 0)
                throw Unavailable("the finite image-cosine envelope has no area");

            // Retain actual-map fold/overlap checks independently of the finite
            // polynomial map. Node acceptance and power use shared-Core traces.
            var mesh = ImageCosineSeedMesh(density);
            if (mesh.Any(triangle => !triangle.A.Mapped || !triangle.B.Mapped || !triangle.C.Mapped))
                throw Unavailable("the finite image-cosine seed mesh contains an untraceable ray");
            foreach (var triangle in mesh) CheckOrientation(triangle.A, triangle.B, triangle.C);
            if (_orientation == 0) throw Unavailable("the pupil has no invertible image direction-cosine area");
            var pupilMap = new FinitePupilMap(mappingNodes);
            double weight = 0;
            var valid = 0;
            for (var row = -density; row <= density; row++)
                for (var column = -density; column <= density; column++)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    ComputationCancellation.ThrowIfCancellationRequested();
                    var l = centerL + column * deltaL;
                    var m = centerM + row * deltaM;
                    (double X, double Y)? region = null;
                    foreach (var triangle in mesh)
                    {
                        if (!ContainsImageCosine(triangle, l, m, out var candidate)) continue;
                        if (region is { } other && Math.Abs(other.X - candidate.X) + Math.Abs(other.Y - candidate.Y) > 1e-7)
                            throw Unavailable("multiple pupil regions overlap in image direction-cosine space");
                        region = candidate;
                    }
                    var pupil = pupilMap.Evaluate(l, m);
                    if (pupil.X * pupil.X + pupil.Y * pupil.Y >= 1) continue;
                    if (!double.IsFinite(pupil.Determinant) || pupil.Determinant == 0 || Math.Sign(pupil.Determinant) != _orientation)
                        throw Unavailable("the finite pupil-coordinate map folds or is singular");
                    var node = Sample(pupil.X, pupil.Y);
                    if (!node.Inside || node.Weight <= 0) continue;
                    var distanceToEdge = density * (1 - double.Hypot(node.X, node.Y));
                    var edge = distanceToEdge >= 1 ? 1
                        : distanceToEdge <= 0 ? 0 : Math.Pow(Math.Sin(Math.PI * distanceToEdge / 2), 2);
                    weight += node.Weight * edge;
                    if (edge > 0) valid++;
                }
            var area = weight * deltaL * deltaM;
            if (!double.IsFinite(area) || area < 0) throw Unavailable("finite image-cosine quadrature is not finite");
            return new(area, valid, _nodes.Count)
            {
                Sampling = IlluminationSamplingKind.FiniteImageCosine,
                IntegrationSamples = checked((2 * density + 1) * (2 * density + 1))
            };
        }
    }
}
