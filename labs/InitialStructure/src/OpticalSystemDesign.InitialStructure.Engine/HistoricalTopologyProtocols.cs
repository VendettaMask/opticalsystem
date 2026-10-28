namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Exact, evidence-bound aliases for known v1 platform-dependent protocol encodings.</summary>
internal static class HistoricalTopologyProtocols
{
    // Binding is the complete v2 descriptor (targets, constraints, fields, all sampling and reference settings).
    // Neither arbitrary v1 hashes nor approximate settings are accepted. Never rewrites stored assessments.
    // Evidence: validation/rechecks/2026-10-28/initial-structure-acceptance/README.md.
    internal const string ThreeToTwoProtocol = "233c8a3914f4510f55c201c0acb91b521f2cbb02bd28cd6c3f15df95c728feee";
    internal static bool Matches(string portable, string legacy) => portable == ThreeToTwoProtocol
        && legacy is "7fc8e0607c4f59d029dfe5693362e2672c54a39ddddf9b284863852c5a92e964"
            or "8aa5e4e99da9c1ae72ce1609da55d28b05896c2c1c42a35a5f7242cb826245f4";
}
