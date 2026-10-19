using System.Globalization;
using System.Text.RegularExpressions;

namespace OpticalSystemDesign.App.Services.Updates;

internal sealed record ReleaseVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = null!;
        if (text is null || text.Length > 128) return false;
        var match = Pattern.Match(text);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return false;
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        if (prerelease?.Split('.').Any(part => IsNumeric(part) && part.Length > 1 && part[0] == '0') == true) return false;
        version = new(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0) return comparison;
        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0) return comparison;
        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0) return comparison;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var ln = IsNumeric(left[i]);
            var rn = IsNumeric(right[i]);
            comparison = ln && rn
                ? left[i].Length.CompareTo(right[i].Length)
                : ln != rn ? (ln ? -1 : 1) : 0;
            if (comparison == 0) comparison = string.CompareOrdinal(left[i], right[i]);
            if (comparison != 0) return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }

    private static bool IsNumeric(string part) => part.All(c => c is >= '0' and <= '9');
}
