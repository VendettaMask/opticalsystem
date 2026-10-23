namespace OpticalSystemDesign.Core.Backend;

internal static class ScalarSign
{
    // ILGPU 1.5.3's CUDA CopySign intrinsic reverses operands. Express IEEE sign
    // transfer through the sign bit so CPU and CUDA execute the same operation,
    // including negative zero. Do not compensate by swapping arguments on CPU.
    internal static double CopySign(double magnitude,double sign) =>
        BitConverter.DoubleToInt64Bits(sign)<0 ? -Math.Abs(magnitude) : Math.Abs(magnitude);
}
