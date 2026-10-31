using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.ZemaxComparison;

internal static class TaggedFacetDiagnostics
{
    internal static int Run(string repository, string capture, string output)
    {
        if (File.Exists(output)) throw new IOException("Output exists");
        var rows = new List<object>();
        foreach (var configuration in new[] { 1, 2 })
        {
            var vertices = new Dictionary<int, Vector3D>();
            var faces = new List<Vector3D[]>();
            var asset = configuration == 1 ? "PORRO_PRISM-LH.POB" : "PORRO_PRISM-RH.POB";
            foreach (var line in File.ReadLines(Path.Combine(repository, "artifacts/validation/afocal-native-resume-20261029-02", asset)))
            {
                var t = line.Split('!')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                if (t[0] == "V") vertices.Add(int.Parse(t[1]), new(D(t[2]), D(t[3]), D(t[4])));
                if (t[0] is "T" or "R")
                {
                    faces.Add([vertices[int.Parse(t[1])], vertices[int.Parse(t[2])], vertices[int.Parse(t[3])]]);
                    if (t[0] == "R") faces.Add([vertices[int.Parse(t[1])], vertices[int.Parse(t[3])], vertices[int.Parse(t[4])]]);
                }
            }
            var tags = JsonFiles.Read<JsonElement>(Path.Combine(capture, $"binocular-c{configuration}-tags/tagged-paths.json"));
            var bvh = new TriangleBvhDiagnostics(faces);
            var original = JsonFiles.Read<JsonElement>(Path.Combine(repository, $"artifacts/validation/afocal-native-resume-20261029-paths-02/binocular-c{configuration}-nsc-paths-pupil/nsc-paths.json"));
            var batch = JsonFiles.Read<JsonElement>(Path.Combine(capture, $"binocular-c{configuration}-batch/nsc-paths.json"));
            var sameComponents = 0; var differentComponents = 0; var tagSegmentDifferences = 0;
            var tagMaximumPositionDifference = 0d; var tagMaximumDirectionDifference = 0d;
            var probes = new List<object>();
            foreach (var ray in tags.GetProperty("rays").EnumerateArray())
            {
                var probe = ray.GetProperty("probe").GetInt32();
                var segments = ray.GetProperty("segments").EnumerateArray().ToArray();
                var native = original.GetProperty("rays")[probe].GetProperty("segments").EnumerateArray().ToArray();
                var controlled = batch.GetProperty("rays")[probe].GetProperty("segments").EnumerateArray().ToArray();
                if (native.Length != controlled.Length || native.Length != segments.Length) throw new InvalidDataException("Segment count mismatch");
                for (var s = 0; s < native.Length; s++)
                    foreach (var k in new[] { "x", "y", "z", "l", "m", "n" })
                    {
                        if (native[s].GetProperty(k).GetDouble() == controlled[s].GetProperty(k).GetDouble()) sameComponents++;
                        else differentComponents++;
                        // Extracted NSC analysis uses a different final termination
                        // distance. Its eight physical interactions must be identical.
                        if (s is > 0 && s < native.Length - 1)
                        {
                            var delta = Math.Abs(native[s].GetProperty(k).GetDouble() - segments[s].GetProperty(k).GetDouble());
                            if (delta != 0) tagSegmentDifferences++;
                            if (k is "x" or "y" or "z") tagMaximumPositionDifference = Math.Max(tagMaximumPositionDifference, delta);
                            else tagMaximumDirectionDifference = Math.Max(tagMaximumDirectionDifference, delta);
                        }
                    }
                var hits = new List<object>();
                for (var s = 1; s < segments.Length - 1; s++)
                {
                    var chosen = segments[s].GetProperty("triangleTag").GetInt32();
                    var origin = V(segments[s - 1], "x", "y", "z") - new Vector3D(0, 0, 25);
                    var direction = V(segments[s], "l", "m", "n");
                    var candidates = new List<object>();
                    var bvhCandidates = bvh.Candidates(origin, direction);
                    for (var f = 0; f < faces.Count; f++)
                    {
                        var v = faces[f]; var raw = Cross(v[1] - v[0], v[2] - v[0]); var n = raw * (1 / raw.Length);
                        var den = Dot(n, direction);
                        if (Math.Abs(den) < 1e-15) continue;
                        var distance = Dot(n, v[0] - origin) / den;
                        if (distance <= 1e-6) continue;
                        var planeDistance = (Dot(n, v[0]) - Dot(n, origin)) / den;
                        var point = origin + direction * distance;
                        var sides = Enumerable.Range(0, 3).Select(i => Dot(Cross(v[(i + 1) % 3] - v[i], point - v[i]), n) / (v[(i + 1) % 3] - v[i]).Length).ToArray();
                        if (sides.Min() < -1e-5 && f != chosen) continue;
                        var edge1 = v[1] - v[0]; var edge2 = v[2] - v[0];
                        var q = Cross(direction, edge2); var det = Dot(edge1, q);
                        var moller = Dot(edge2, Cross(origin - v[0], edge1)) / det;
                        var rawDistance = Dot(raw, v[0] - origin) / Dot(raw, direction);
                        var rawPlaneDistance = (Dot(raw, v[0]) - Dot(raw, origin)) / Dot(raw, direction);
                        var fmaDistance = FmaDot(n, v[0] - origin) / FmaDot(n, direction);
                        var fmaPlaneDistance = (FmaDot(n, v[0]) - FmaDot(n, origin)) / FmaDot(n, direction);
                        var negativeConstantDistance = -(Dot(n, origin) + -Dot(n, v[0])) / den;
                        var yzFirstDistance = DotYzFirst(n, v[0] - origin) / DotYzFirst(n, direction);
                        candidates.Add(new { f, distance, planeDistance, rawDistance, rawPlaneDistance,
                            moller, fmaDistance, fmaPlaneDistance, negativeConstantDistance, yzFirstDistance,
                            centroid = (v[0] + v[1] + v[2]) / 3,
                            minX = v.Min(a => a.X), maxX = v.Max(a => a.X), point, sides,
                            bvhRank = Array.IndexOf(bvhCandidates, f),
                            minEdgeDistance = sides.Min(), n, chosen = f == chosen });
                    }
                    hits.Add(new { segment = s + 1, chosen, origin, direction, candidates });
                }
                probes.Add(new { probe, input = ray.GetProperty("launch").GetProperty("input"), hits });
            }
            rows.Add(new { configuration, sameComponents, differentComponents, tagSegmentDifferences,
                tagMaximumPositionDifference, tagMaximumDirectionDifference,
                batchMatchesOriginalExactly = differentComponents == 0,
                extractedAnalysisMatchesOriginalExactly = tagSegmentDifferences == 0, probes });
            Console.WriteLine(JsonSerializer.Serialize(new { configuration, sameComponents, differentComponents, tagSegmentDifferences,
                tagMaximumPositionDifference, tagMaximumDirectionDifference }));
            if (differentComponents != 0) throw new InvalidDataException("Triangle tags changed the original batch physical path");
        }
        JsonFiles.Write(output, rows);
        return 0;
    }
    static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    static Vector3D V(JsonElement v, string x, string y, string z) => new(v.GetProperty(x).GetDouble(), v.GetProperty(y).GetDouble(), v.GetProperty(z).GetDouble());
    static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    static double FmaDot(Vector3D a, Vector3D b) => Math.FusedMultiplyAdd(a.X, b.X, Math.FusedMultiplyAdd(a.Y, b.Y, a.Z * b.Z));
    static double DotYzFirst(Vector3D a, Vector3D b) => a.X * b.X + (a.Y * b.Y + a.Z * b.Z);
    static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
