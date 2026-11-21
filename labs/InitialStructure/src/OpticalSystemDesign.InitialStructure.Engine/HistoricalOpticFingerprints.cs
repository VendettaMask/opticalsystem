using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.InitialStructure.Engine;

// Read-only compatibility for persisted assessments. Every physical component,
// field, wavelength and numerical setting still participates in the exact hash.
internal static class HistoricalOpticFingerprints
{
    private static readonly string[] DefaultCatalogs =
        ["SCHOTT", "OHARA", "HOYA", "HIKARI", "NIKON-HIKARI", "SUMITA", "CDGM", "CHENGDU", "GBJ", "NHG", "555CHINESES"];

    internal static bool Matches(OpticSnapshot snapshot, string fingerprint)
    {
        if (fingerprint is not { Length: 64 }) return false;
        return Variants(snapshot).Any(variant => ContentFingerprint.Compute(variant.Snapshot) == fingerprint);
    }

    internal static IEnumerable<(int Schema, bool RetiredCatalog, OpticSnapshot Snapshot)> Variants(OpticSnapshot snapshot)
    {
        // Schema 6 introduced these stored assessments; later versions added
        // optional fields. Do not drop those fields or load an old glass model.
        for (var schema = 6; schema < snapshot.SchemaVersion; schema++)
        {
            var historical = snapshot with { SchemaVersion = schema };
            yield return (schema, false, historical);
            if (snapshot.GlassCatalogs is not { } catalogs || !catalogs.SequenceEqual(DefaultCatalogs)) continue;
            var historicalCatalogs = catalogs.ToList();
            // This retired selection key is provenance only. The complete
            // current material component bytes remain unchanged in the hash.
            historicalCatalogs.Insert(6, "CDGM-ZEMAX202309");
            yield return (schema, true, historical with { GlassCatalogs = historicalCatalogs });
        }
    }
}
