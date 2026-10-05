namespace OptilandWorkbench.Core.Services;

public static partial class IlluminationMetrics
{
    private sealed partial class Integrator
    {
        // The grid is uniform in the tangent-plane direction cosines at the image,
        // not in entrance-pupil coordinates. The mesh only seeds an inverse solve;
        // every accepted grid sample is retraced through the formal optical engine.
        private IlluminationMetricResult IntegrateUniformImageCosine(int density)
        {
            var mesh = ImageCosineSeedMesh(density);
            var vertices = mesh.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray();
            if (vertices.Any(node => !node.Mapped))
                throw Unavailable("the image-cosine seed mesh contains an untraceable ray");
            foreach (var triangle in mesh) CheckOrientation(triangle.A, triangle.B, triangle.C);
            var minimumL = vertices.Min(node => node.L);
            var minimumM = vertices.Min(node => node.M);
            var width = vertices.Max(node => node.L) - minimumL;
            var height = vertices.Max(node => node.M) - minimumM;
            if (!double.IsFinite(width * height) || width <= 0 || height <= 0 || _orientation == 0)
                throw Unavailable("the pupil has no invertible image direction-cosine area");
            var deltaL = width / density;
            var deltaM = height / density;
            var tolerance = Math.Max(width, height) * 1e-9;
            var weight = 0.0;
            var valid = 0;
            for (var row = 0; row < density; row++)
            {
                for (var column = 0; column < density; column++)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    ComputationCancellation.ThrowIfCancellationRequested();
                    var l = minimumL + (column + 0.5) * deltaL;
                    var m = minimumM + (row + 0.5) * deltaM;
                    (double X, double Y)? seed = null;
                    foreach (var triangle in mesh)
                    {
                        if (!ContainsImageCosine(triangle, l, m, out var candidate)) continue;
                        if (seed is { } other && Math.Abs(other.X - candidate.X) + Math.Abs(other.Y - candidate.Y) > 1e-7)
                            throw Unavailable("multiple pupil regions overlap in image direction-cosine space");
                        seed = candidate;
                    }
                    if (seed is not { } pupil) continue;
                    var node = InvertImageCosine(l, m, pupil.X, pupil.Y, tolerance);
                    if (!node.Inside || node.Weight <= 0) continue;
                    weight += node.Weight;
                    valid++;
                }
            }
            var area = weight * deltaL * deltaM;
            if (!double.IsFinite(area) || area < 0) throw Unavailable("image-cosine quadrature is not finite");
            return new(area, valid, _nodes.Count)
            {
                Sampling = IlluminationSamplingKind.UniformImageCosine,
                IntegrationSamples = checked(density * density)
            };
        }

        private List<CosineTriangle> ImageCosineSeedMesh(int density)
        {
            var mesh = new List<CosineTriangle>();
            var center = Sample(0, 0);
            var previous = new[] { center };
            var rings = Math.Clamp((density + 1) / 2, 2, 8);
            for (var ring = 1; ring <= rings; ring++)
            {
                var count = ring == rings ? Math.Max(64, 8 * density) : Math.Max(12, 6 * ring);
                var radius = ring / (double)rings;
                var current = Enumerable.Range(0, count).Select(index =>
                {
                    var angle = 2 * Math.PI * index / count;
                    return Sample(radius * Math.Cos(angle), radius * Math.Sin(angle));
                }).ToArray();
                if (ring == 1)
                {
                    for (var index = 0; index < count; index++)
                        mesh.Add(new(center, current[index], current[(index + 1) % count]));
                }
                else
                {
                    var inner = 0;
                    var outer = 0;
                    while (inner < previous.Length || outer < current.Length)
                    {
                        var a = previous[inner % previous.Length];
                        var b = current[outer % current.Length];
                        if ((inner + 1) * current.Length < (outer + 1) * previous.Length)
                        {
                            mesh.Add(new(a, b, previous[(inner + 1) % previous.Length]));
                            inner++;
                        }
                        else
                        {
                            mesh.Add(new(a, b, current[(outer + 1) % current.Length]));
                            outer++;
                        }
                    }
                }
                previous = current;
            }
            return mesh;
        }

        private static bool ContainsImageCosine(CosineTriangle triangle, double l, double m,
            out (double X, double Y) pupil)
        {
            var (a, b, c) = triangle;
            var denominator = 2 * SignedArea(a, b, c);
            pupil = default;
            if (denominator == 0) return false;
            var u = ((l - a.L) * (c.M - a.M) - (m - a.M) * (c.L - a.L)) / denominator;
            var v = ((b.L - a.L) * (m - a.M) - (b.M - a.M) * (l - a.L)) / denominator;
            if (u < -1e-12 || v < -1e-12 || u + v > 1 + 1e-12) return false;
            pupil = (a.X + u * (b.X - a.X) + v * (c.X - a.X), a.Y + u * (b.Y - a.Y) + v * (c.Y - a.Y));
            return true;
        }

        private Node InvertImageCosine(double l, double m, double x, double y, double tolerance)
        {
            var node = SampleInUnitPupil(x, y);
            for (var iteration = 0; iteration < 20; iteration++)
            {
                if (!node.Mapped) throw Unavailable("an image-cosine inverse ray cannot reach the image");
                var errorL = l - node.L;
                var errorM = m - node.M;
                var error = Math.Sqrt(errorL * errorL + errorM * errorM);
                if (error <= tolerance) return node;
                const double step = 1e-5;
                var a = SampleInUnitPupil(node.X + (node.X < 0 ? step : -step), node.Y);
                var b = SampleInUnitPupil(node.X, node.Y + (node.Y < 0 ? step : -step));
                if (!a.Mapped || !b.Mapped) throw Unavailable("the image-cosine inverse derivative cannot be traced");
                var al = a.L - node.L;
                var am = a.M - node.M;
                var bl = b.L - node.L;
                var bm = b.M - node.M;
                var determinant = al * bm - am * bl;
                var pupilDeterminant = (a.X - node.X) * (b.Y - node.Y) - (a.Y - node.Y) * (b.X - node.X);
                if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-28 || pupilDeterminant == 0)
                    throw Unavailable("the image-cosine inverse derivative is singular");
                if (Math.Sign(determinant) * Math.Sign(pupilDeterminant) != _orientation)
                    throw Unavailable("the pupil mapping folds in image direction-cosine space");
                var alpha = (errorL * bm - errorM * bl) / determinant;
                var beta = (al * errorM - am * errorL) / determinant;
                var dx = alpha * (a.X - node.X) + beta * (b.X - node.X);
                var dy = alpha * (a.Y - node.Y) + beta * (b.Y - node.Y);
                var accepted = false;
                for (var line = 0; line < 12; line++)
                {
                    var scale = Math.ScaleB(1, -line);
                    var nextX = node.X + scale * dx;
                    var nextY = node.Y + scale * dy;
                    if (!double.IsFinite(nextX + nextY) || nextX * nextX + nextY * nextY > 1 + 1e-12) continue;
                    var next = SampleInUnitPupil(nextX, nextY);
                    if (!next.Mapped) continue;
                    var nextError = Math.Sqrt(Math.Pow(next.L - l, 2) + Math.Pow(next.M - m, 2));
                    if (nextError >= error) continue;
                    node = next;
                    accepted = true;
                    break;
                }
                if (!accepted) throw Unavailable("image-cosine inversion did not converge inside the pupil");
            }
            throw Unavailable("image-cosine inversion exceeded its iteration budget");
        }

        private Node SampleInUnitPupil(double x, double y)
        {
            var radius = Math.Sqrt(x * x + y * y);
            return radius > 1 ? Sample(x / radius, y / radius) : Sample(x, y);
        }

        private readonly record struct CosineTriangle(Node A, Node B, Node C);
    }
}
