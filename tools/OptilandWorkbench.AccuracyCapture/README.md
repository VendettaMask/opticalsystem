# Workbench accuracy recapture

This existing .NET utility replays the historical Workbench settings manifest against a specified ZMX and writes current analysis views. Its positional CLI and historical output schema remain unchanged.

For a new lens requiring live Zemax capture, explicit settings, a complete analysis status matrix, per-quantity numerical metrics and provenance, use [ZemaxComparison](../OptilandWorkbench.ZemaxComparison/README.md). Both tools now execute through the same `WorkbenchRuntime` factory: `BuildAnalysisView` delegates to the unrounded `BuildAnalysisData` entry. There is no second set of Workbench constructor defaults in the new validation tool.
