using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Application.Runtime;

internal sealed record JointToleranceSnapshot(OpticSnapshot[] Configurations, int ActiveConfiguration,
    MultiConfigurationLinkOverride[] BrokenLinks, MultiConfigurationOperand[] OperandRows, MultiConfigurationPickup[] Pickups);

public partial class WorkbenchRuntime
{
    internal JointToleranceSnapshot CaptureJointToleranceSnapshot() => new(
        CreateMeritConfigurationContext().Configurations.Select(optic => optic.ToSnapshot()).ToArray(),
        _activeConfigurationIndex + 1, _multiConfiguration.BrokenLinks.ToArray(),
        _multiConfiguration.OperandRows.ToArray(), _multiConfiguration.OperandPickups.ToArray());
}
