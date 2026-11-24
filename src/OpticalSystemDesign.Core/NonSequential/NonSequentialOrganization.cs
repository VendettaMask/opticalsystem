namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>Presentation organization only; never changes coordinates or containing media.</summary>
public sealed record NonSequentialFolder(Guid Id, string Name, Guid? ParentId, IReadOnlyList<Guid> ObjectIds);
public sealed record NonSequentialSelectionSet(Guid Id, string Name, IReadOnlyList<Guid> ObjectIds);
public sealed record NonSequentialOrganization(
    IReadOnlyList<NonSequentialFolder> Folders,
    IReadOnlyList<NonSequentialSelectionSet> SelectionSets)
{
    public static NonSequentialOrganization Empty { get; } = new([], []);

    public NonSequentialOrganization Freeze() => new(
        Array.AsReadOnly(Folders.Select(f => f with { ObjectIds = Array.AsReadOnly(f.ObjectIds.ToArray()) }).ToArray()),
        Array.AsReadOnly(SelectionSets.Select(s => s with { ObjectIds = Array.AsReadOnly(s.ObjectIds.ToArray()) }).ToArray()));

    public NonSequentialOrganization Without(Guid id) => new(
        Folders.Select(f => f with { ObjectIds = f.ObjectIds.Where(n => n != id).ToArray() }).ToArray(),
        SelectionSets.Select(s => s with { ObjectIds = s.ObjectIds.Where(n => n != id).ToArray() }).ToArray());

    public void Validate(IReadOnlySet<Guid> objectIds)
    {
        if (Folders.Count > 4096 || SelectionSets.Count > 4096)
            throw new InvalidDataException("Too many scene folders or selection sets.");
        var folders = Folders.ToDictionary(f => f.Id);
        var assigned = new HashSet<Guid>();
        foreach (var folder in Folders)
        {
            if (folder.Id == Guid.Empty || string.IsNullOrWhiteSpace(folder.Name) || folder.Name.Length > 256)
                throw new InvalidDataException("Invalid scene folder identity or name.");
            var ancestors = new HashSet<Guid> { folder.Id };
            var parent = folder.ParentId;
            while (parent is { } id)
            {
                if (!folders.TryGetValue(id, out var item) || !ancestors.Add(id))
                    throw new InvalidDataException("Scene folder parent is missing or cyclic.");
                parent = item.ParentId;
            }
            foreach (var id in folder.ObjectIds)
                if (!objectIds.Contains(id) || !assigned.Add(id))
                    throw new InvalidDataException("Scene folder contains an unknown or duplicate object.");
        }
        var sets = new HashSet<Guid>();
        foreach (var set in SelectionSets)
            if (set.Id == Guid.Empty || !sets.Add(set.Id) || string.IsNullOrWhiteSpace(set.Name) || set.Name.Length > 256
                || set.ObjectIds.Any(id => !objectIds.Contains(id)) || set.ObjectIds.Distinct().Count() != set.ObjectIds.Count)
                throw new InvalidDataException("Invalid scene selection set.");
    }
}
