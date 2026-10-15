using System;

namespace OpticalSystemDesign.ZemaxComparison.Zemax
{
    // Shared by the .NET tool and the isolated C# 5 native host.
    public static class NativeConfigurationSelector
    {
        public static int Numbered(int configuration)
        {
            if (configuration < 1) throw new ArgumentOutOfRangeException("configuration");
            // These analysis selectors reserve two entries before configuration 1.
            // This differs from the one-based MCE.SetCurrentConfiguration contract.
            return checked(configuration + 1);
        }
    }
}
