using System;
using System.Collections.Generic;
using System.Linq;
using ZOSAPI.Analysis.Data;

// Preserve each native result channel. RGB values are data, not screenshots or scalar irradiance.
internal static class ExtendedCapture
{
    private static object Rgb(IAR_Rgb p) { return new object[] { Host.Finite(p.R), Host.Finite(p.G), Host.Finite(p.B) }; }
    internal static void Add(IAR_ r, Dictionary<string, object> raw)
    {
        raw["dataGridsRgb"] = Enumerable.Range(0, r.NumberOfDataGridsRgb).Select(i =>
        {
            var g = r.GetDataGridRgb(i);
            return Host.Object("index", i, "description", g.Description, "nx", g.Nx, "ny", g.Ny,
                "dx", Host.Finite(g.Dx), "dy", Host.Finite(g.Dy), "minX", Host.Finite(g.MinX), "minY", Host.Finite(g.MinY),
                "xLabel", g.XLabel, "yLabel", g.YLabel, "valueLabel", g.ValueLabel,
                "values", Enumerable.Range(0, (int)g.Ny).Select(y => Enumerable.Range(0, (int)g.Nx).Select(x => Rgb(g.Values[y, x])).ToArray()).ToArray());
        }).ToArray();
        raw["dataSeriesRgb"] = Enumerable.Range(0, r.NumberOfDataSeriesRgb).Select(i =>
        {
            var s = r.GetDataSeriesRgb(i);
            return Host.Object("index", i, "description", s.Description, "xLabel", s.XLabel, "seriesLabels", s.SeriesLabels,
                "x", s.XData.Data.Select(Host.Finite).ToArray(),
                "y", Enumerable.Range(0, (int)s.NumberOfRows).Select(row => Enumerable.Range(0, (int)s.NumSeries).Select(col => Rgb(s.YVals[row, col])).ToArray()).ToArray());
        }).ToArray();
        raw["dataScatterPoints"] = Enumerable.Range(0, r.NumberOfDataScatterPoints).Select(i =>
        {
            var s = r.GetDataScatterPoint(i);
            return Host.Object("index", i, "description", s.Description, "xLabel", s.XLabel, "yLabel", s.YLabel, "valueLabel", s.ValueLabel,
                "points", s.Points.Select(p => new object[] { Host.Finite(p.X), Host.Finite(p.Y), Host.Finite(p.Value) }).ToArray());
        }).ToArray();
        raw["dataScatterPointsRgb"] = Enumerable.Range(0, r.NumberOfDataScatterPointsRgb).Select(i =>
        {
            var s = r.GetDataScatterPointRgb(i);
            return Host.Object("index", i, "description", s.Description, "xLabel", s.XLabel, "yLabel", s.YLabel, "valueLabel", s.ValueLabel,
                "points", s.Points.Select(p => new object[] { Host.Finite(p.X), Host.Finite(p.Y), Rgb(p.Value) }).ToArray());
        }).ToArray();
        raw["rayData"] = Enumerable.Range(0, r.NumberOfRayData).Select(i =>
        {
            var rays = r.GetRayData(i);
            return Host.Object("index", i, "description", rays.Description, "rays", rays.Rays.Select(Host.Properties).ToArray());
        }).ToArray();
        var spot = r.SpotData;
        if (spot != null && spot.NumberOfFields > 0)
            raw["spotMetrics"] = Enumerable.Range(1, spot.NumberOfFields).SelectMany(f =>
                Enumerable.Range(0, spot.NumberOfWavelengths + 1).Select(w => Host.Object("field", f, "wavelength", w,
                    "rms", Host.Finite(spot.GetRMSSpotSizeFor(f, w)), "geo", Host.Finite(spot.GetGeoSpotSizeFor(f, w)),
                    "referenceX", Host.Finite(spot.GetReferenceCoordinate_X_For(f, w)), "referenceY", Host.Finite(spot.GetReferenceCoordinate_Y_For(f, w))))).ToArray();
    }
}
