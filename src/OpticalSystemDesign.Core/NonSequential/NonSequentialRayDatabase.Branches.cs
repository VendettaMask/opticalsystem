using System.Security.Cryptography;

namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>Compact chunk ranges for branch and parent lookup in one database.</summary>
public sealed class NonSequentialBranchIndex
{
    internal NonSequentialBranchIndex(string identity, long branchCount, NonSequentialBranchRange[] ranges)
    {
        Identity = identity;
        BranchCount = branchCount;
        Ranges = ranges;
    }

    internal string Identity { get; }
    internal long BranchCount { get; }
    internal NonSequentialBranchRange[] Ranges { get; }
}

internal readonly record struct NonSequentialBranchRange(
    long MinimumId, long MaximumId, long MinimumParentId, long MaximumParentId);

public sealed partial class NonSequentialRayDatabaseReader
{
    public NonSequentialBranchIndex BuildBranchIndex(CancellationToken cancellationToken = default)
    {
        var ranges = new NonSequentialBranchRange[_index.Count];
        for (var chunk = 0; chunk < _index.Count; chunk++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var branches = ReadChunk(_index[chunk]);
            long minimum = long.MaxValue, maximum = 0, minimumParent = long.MaxValue, maximumParent = 0;
            foreach (var branch in branches)
            {
                minimum = Math.Min(minimum, branch.Id);
                maximum = Math.Max(maximum, branch.Id);
                if (branch.ParentId is { } parent)
                {
                    minimumParent = Math.Min(minimumParent, parent);
                    maximumParent = Math.Max(maximumParent, parent);
                }
            }
            ranges[chunk] = new(minimum, maximum, minimumParent, maximumParent);
        }
        return new(_branchIndexIdentity.Value, BranchCount, ranges);
    }

    public NonSequentialRayBranch? ReadBranchById(
        long branchId, NonSequentialBranchIndex index, CancellationToken cancellationToken = default)
    {
        if (branchId <= 0) throw new ArgumentOutOfRangeException(nameof(branchId));
        ValidateBranchIndex(index);
        NonSequentialRayBranch? result = null;
        for (var chunk = 0; chunk < _index.Count; chunk++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = index.Ranges[chunk];
            if (branchId < range.MinimumId || branchId > range.MaximumId) continue;
            foreach (var branch in ReadChunk(_index[chunk]))
            {
                if (branch.Id != branchId) continue;
                if (result is not null) throw new InvalidDataException("光线数据库包含重复分支 ID。");
                result = branch;
            }
        }
        return result;
    }

    public (IReadOnlyList<NonSequentialRayBranch> Branches, long Total) ReadChildBranches(
        long parentId, NonSequentialBranchIndex index, int offset = 0, int count = 100,
        CancellationToken cancellationToken = default)
    {
        if (parentId <= 0) throw new ArgumentOutOfRangeException(nameof(parentId));
        if (offset < 0 || count is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(count));
        ValidateBranchIndex(index);
        var result = new List<NonSequentialRayBranch>();
        long total = 0;
        for (var chunk = 0; chunk < _index.Count; chunk++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = index.Ranges[chunk];
            if (parentId < range.MinimumParentId || parentId > range.MaximumParentId) continue;
            foreach (var branch in ReadChunk(_index[chunk]))
            {
                if (branch.ParentId != parentId) continue;
                if (total >= offset && result.Count < count) result.Add(branch);
                total++;
            }
        }
        return (result, total);
    }

    private void ValidateBranchIndex(NonSequentialBranchIndex index)
    {
        if (!IsBranchIndexCompatible(index))
            throw new ArgumentException("分支索引与当前光线数据库不匹配。", nameof(index));
    }

    public bool IsBranchIndexCompatible(NonSequentialBranchIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.Identity == _branchIndexIdentity.Value && index.BranchCount == BranchCount
            && index.Ranges.Length == _index.Count;
    }

    private string ComputeBranchIndexIdentity()
    {
        lock (_streamGate)
        {
            using var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> fileHeader = stackalloc byte[FileHeaderLength];
            _stream.Position = 0;
            ReadExactly(fileHeader);
            identity.AppendData(fileHeader);
            Span<byte> chunkHeader = stackalloc byte[ChunkHeaderLength];
            foreach (var entry in _index)
            {
                _stream.Position = entry.Offset;
                ReadExactly(chunkHeader);
                identity.AppendData(chunkHeader);
            }
            return Convert.ToHexString(identity.GetHashAndReset());
        }
    }
}
