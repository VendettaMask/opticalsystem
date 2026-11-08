using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true, WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter() }
};
var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(args[0]), options)!);
var wave = optic.Wavelengths.Single();
var results = new List<object>();
foreach (var token in args.Skip(2))
{
    var count = int.Parse(token.TrimEnd('!'));
    var stopOnFirstExcess = token.EndsWith('!');
    var stopwatch = Stopwatch.StartNew();
    var grid = new PupilGridSpecification(count, true, true, 1);
    var coordinates = Enumerable.Range(0, count).Select(grid.Coordinate).ToArray();
    var prior = Empty();
    var current = Empty();
    var priorFiltered = Empty();
    var filtered = Empty();
    var rowsPerBatch = Math.Max(1, 32768 / count);
    var maximum = 0d;
    var traced = 0L;
    var edges = 0L;
    var exhausted = false;
    for (var first = 0; first < count; first += rowsPerBatch)
    {
        var height = Math.Min(rowsPerBatch, count - first);
        var raw = Enumerable.Range(0, height).Select(_ => Empty()).ToArray();
        var samples = new List<PupilSample>(height * count);
        var ranges = new (int First, int End)[height];
        for (var row = 0; row < height; row++)
        {
            var y = coordinates[first + row];
            var ySquared = y * y;
            if (ySquared > 1) continue;
            var left = 0;
            var end = count;
            while (left < end && coordinates[left] * coordinates[left] + ySquared > 1) left++;
            while (end > left && coordinates[end - 1] * coordinates[end - 1] + ySquared > 1) end--;
            ranges[row] = (left, end);
            for (var column = left; column < end; column++) samples.Add(new(coordinates[column], y, 1));
        }
        if (samples.Count > 0)
        {
            var wavefront = WavefrontEngine.GenerateChiefRayBatch(optic, (0, 0), wave, samples, false);
            traced += samples.Count;
            var index = 0;
            for (var row = 0; row < height; row++)
                for (var column = ranges[row].First; column < ranges[row].End; column++)
                {
                    var sample = wavefront.Samples[index++];
                    if (sample.Intensity > 0) raw[row][column] = sample.OpdWaves;
                }
        }
        for (var row = 0; row < height; row++)
        {
            var previousIndex = first + row - 1;
            if (previousIndex >= 0) Assess(previousIndex, prior, current, raw[row]);
            prior = current;
            current = raw[row];
        }
        if (first + height == count)
        {
            Assess(count - 1, prior, current, Empty());
            exhausted = true;
        }
        if (first > 0 && first % 512 < rowsPerBatch)
            Console.WriteLine($"{count}: row {first + height}/{count}, max {maximum:R}, {stopwatch.Elapsed.TotalSeconds:F1}s");
        if (stopOnFirstExcess && maximum > .5) break;
    }
    var passed = exhausted && maximum <= .5 && edges > 0;
    Console.WriteLine($"{count}: max {maximum:R}, complete {exhausted}, phase gate {passed}, {traced} rays, {stopwatch.Elapsed.TotalSeconds:F1}s");
    results.Add(new { count, traced, edges, maximumStepWaves = maximum, fullPupil = exhausted, phaseGatePass = passed,
        wallSeconds = stopwatch.Elapsed.TotalSeconds });
    File.WriteAllText(args[1], JsonSerializer.Serialize(results, options));

    double[] Empty()
    {
        var result = new double[count];
        Array.Fill(result, double.NaN);
        return result;
    }

    void Assess(int row, double[] above, double[] middle, double[] below)
    {
        Array.Fill(filtered, double.NaN);
        for (var column = 0; column < count; column++)
        {
            var phase = middle[column];
            if (!double.IsFinite(phase)) continue;
            if ((row + column) % 2 == 0 && (row != count / 2 || column != count / 2)
                && column > 0 && column + 1 < count
                && double.IsFinite(middle[column - 1]) && double.IsFinite(middle[column + 1])
                && double.IsFinite(above[column]) && double.IsFinite(below[column]))
                phase = (middle[column - 1] + middle[column + 1] + above[column] + below[column]) / 4;
            filtered[column] = phase;
            if (column > 0) Edge(phase, filtered[column - 1]);
            Edge(phase, priorFiltered[column]);
        }
        (filtered, priorFiltered) = (priorFiltered, filtered);
    }

    void Edge(double currentPhase, double otherPhase)
    {
        if (!double.IsFinite(otherPhase)) return;
        edges++;
        maximum = Math.Max(maximum, Math.Abs(currentPhase - otherPhase));
    }
}
