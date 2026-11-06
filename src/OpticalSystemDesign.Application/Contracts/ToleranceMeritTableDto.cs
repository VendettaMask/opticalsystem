using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Application.Contracts;

public sealed record ToleranceMeritTableDto(string Name, IReadOnlyList<MeritOperandSnapshot> Rows);
