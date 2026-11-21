namespace OpticalSystemDesign.Core.Domain;

/// <summary>Display-only RGB override. Null uses the viewer's normal palette.</summary>
public static class SurfaceDisplayColor
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var color = value.Trim();
        if (color.Length != 7 || color[0] != '#' || !color.AsSpan(1).ContainsOnlyHexDigits())
            throw new ArgumentException("Surface color must be #RRGGBB.", nameof(value));
        return color.ToUpperInvariant();
    }

    private static bool ContainsOnlyHexDigits(this ReadOnlySpan<char> value)
    {
        foreach (var digit in value)
            if (!Uri.IsHexDigit(digit)) return false;
        return true;
    }
}
