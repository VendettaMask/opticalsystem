using System;
using System.Collections.Generic;
using System.IO;
using ZOSAPI;
using ZOSAPI.Analysis.Data;

// Records unavailable native analyses without inventing numerical references.
internal static class NativeRelativeIlluminationCapture
{
    internal static void Run(IOpticalSystem system, IAR_ results, object applyResult)
    {
        if ((string)Host.Request["analysisType"] != "RelativeIllumination"
            || !Host.Request.ContainsKey("formalAcceptanceRequest") || Convert.ToBoolean(Host.Request["formalAcceptanceRequest"])
            || Convert.ToBoolean(Host.Request["captureScreenshots"]))
            throw new InvalidOperationException("Relative illumination diagnosis requires an owned background non-acceptance capture");
        var text = results != null && results.GetTextFile(Path.Combine(Host.Output, "data.txt"));
        var messages = new List<object>(); var buffers = new List<object>();
        var available = results != null && results.NumberOfDataSeries > 0;
        if (results != null)
        {
            for (var i = 0; i < results.NumberOfMessages; i++)
            {
                var message = results.GetMessageAt(i);
                messages.Add(Host.Object("errorCode", message.ErrorCode.ToString(), "text", message.Text));
            }
            for (var i = 0; i < results.NumberOfDataSeries; i++)
            {
                var series = results.GetDataSeries(i);
                var x = series != null && series.XData != null;
                var y = series != null && series.YData != null;
                available &= x && y;
                buffers.Add(Host.Object("index", i, "seriesObject", series != null, "xBuffer", x, "yBuffer", y));
            }
        }
        Host.Write("result-diagnostics.json", Host.Object("seriesCount", results == null ? 0 : results.NumberOfDataSeries,
            "gridCount", results == null ? 0 : results.NumberOfDataGrids, "messages", messages, "buffers", buffers));
        if (available) Host.Write("data.json", Host.Capture(results));
        Host.Write("relative-illumination-diagnostic.json", Host.Object("nativeNumericalBuffersAvailable", available,
            "afocalImageSpace", system.SystemData.Aperture.AFocalImageSpace, "applyResult", Host.Properties(applyResult),
            "textSaved", text, "savedInput", false, "formalOpticalPass", false,
            "sourcePurpose", "Actual native applicability and buffer diagnostics; no fabricated arrays"));
    }
}
