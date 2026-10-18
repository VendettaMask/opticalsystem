using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class ImageSimulationEngine
{
    private static double[,] PadChannel(double[,] source, int padding)
    {
        if (padding == 0) return source;
        var output = new double[source.GetLength(0) + 2 * padding, source.GetLength(1) + 2 * padding];
        for (var row = 0; row < source.GetLength(0); row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < source.GetLength(1); column++)
                output[row + padding, column + padding] = source[row, column];
        }
        return output;
    }
}
