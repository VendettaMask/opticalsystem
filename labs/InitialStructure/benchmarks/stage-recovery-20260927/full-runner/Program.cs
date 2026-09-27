using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

await SearchVerification.Run(args);
internal sealed record Input(string Id, InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial);
