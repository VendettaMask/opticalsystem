namespace OpticalSystemDesign.CoatingDesign.Engine;
public sealed partial class DesignService
{
    // Offline control: same windows, bounds and guides, without nonlinear correction.
    private static double[] CorrectNonlinearTrial(double[] start, double[] trial, double[] lower, double[] upper,
        Func<double[], bool, (double[] Values, double[,]? Jacobian)> read, GeneralCounter work, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return trial; }
}
