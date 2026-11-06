namespace OpticalSystemDesign.Application.Contracts;

public enum ToleranceConfigurationScope { Current, Selected, All }

public sealed record ToleranceConfigurationLimitDto(int Configuration, double Limit,
    IReadOnlyList<ToleranceFieldLimitDto>? FieldLimits = null);

// Both the displayed row number and its physical operand identity are checked when loading a study.
public sealed record ToleranceMceAdjustmentDto(int Configuration, int Row, int SurfaceNumber,
    double Minimum, double Maximum, bool Compensator = false, int Stage = 1,
    ToleranceDistribution Distribution = ToleranceDistribution.Normal);

public sealed record ToleranceConfigurationSettingsDto(ToleranceConfigurationScope Scope = ToleranceConfigurationScope.Current,
    IReadOnlyList<int>? Configurations = null, IReadOnlyList<ToleranceConfigurationLimitDto>? Limits = null,
    IReadOnlyList<ToleranceMceAdjustmentDto>? MceRows = null);

public sealed record ToleranceConfigurationResultDto(int Configuration, double Criterion,
    IReadOnlyList<ToleranceFieldValueDto>? Fields, IReadOnlyList<ToleranceCompensatorValueDto>? Compensators,
    bool? Passed, double? AcceptanceMargin, int? LimitingField = null, string Error = "");

public sealed record ToleranceConfigurationYieldDto(int Configuration, int Passed, int Trials, double? Yield);
