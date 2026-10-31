// Explicit, disposable native control. Never replaces an installed asset.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZOSAPI;

internal static class NativePolygonControl
{
    internal static string Apply(IOpticalSystem system, string objectsDirectory)
    {
        string mode = (string)Host.Request["diagnosticPolygonTriangles"];
        bool tagged = mode == "02-tagged" || mode == "02-tagged-reverse";
        bool reverse = mode == "02-reverse" || mode == "02-tagged-reverse";
        bool flipWinding = Host.Request.ContainsKey("diagnosticFlipTriangleWinding")
            && Convert.ToBoolean(Host.Request["diagnosticFlipTriangleWinding"]);
        if (mode != "02" && mode != "02-reverse" && !tagged) throw new InvalidOperationException("Unknown independent polygon control");
        system.NCE.SetActiveSurface(6);
        var obj = system.NCE.GetObjectAt(1);
        string originalName = obj.Comment;
        if (originalName != "PORRO_PRISM-LH.POB" && originalName != "PORRO_PRISM-RH.POB")
            throw new InvalidOperationException("Original polygon identity differs");
        string directory = Path.GetFullPath(Path.Combine(objectsDirectory, "Polygon Objects"));
        string name = "codex_afocal_" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".POB";
        string path = Path.GetFullPath(Path.Combine(directory, name));
        if (Path.GetDirectoryName(path) != directory) throw new InvalidOperationException("Owned polygon escaped its directory");
        var lines = new List<string>();
        var triangleMap = new List<object>();
        int triangle = 0;
        Func<string, string> tag = delegate(string record)
        {
            var fields = record.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 6 || fields[0] != "T" || fields[5] != "0")
                throw new InvalidOperationException("Unexpected original triangle face group");
            int id = triangle++;
            if (flipWinding) { string v = fields[2]; fields[2] = fields[3]; fields[3] = v; }
            if (tagged) fields[5] = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string result = string.Join(" ", fields);
            triangleMap.Add(Host.Object("triangle", id, "faceGroup", tagged ? id : 0, "record", result));
            return result;
        };
        foreach (var line in File.ReadAllLines(Path.Combine(directory, originalName)))
        {
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens[0] == "R")
            {
                string suffix = string.Join(" ", tokens.Skip(5).ToArray());
                string a = tag("T " + tokens[1] + " " + tokens[2] + " " + tokens[3] + " " + suffix);
                string b = tag("T " + tokens[1] + " " + tokens[3] + " " + tokens[4] + " " + suffix);
                lines.Add(reverse ? b : a); lines.Add(reverse ? a : b);
            }
            else if (tokens.Length > 0 && tokens[0] == "T") lines.Add(tag(line));
            else lines.Add(line);
        }
        if (tagged)
        {
            if (triangle != 16) throw new InvalidOperationException("Expected sixteen binocular triangles");
            lines.InsertRange(0, Enumerable.Range(0, triangle).Select(i => "C " + i + " \"Diagnostic triangle " + i + "\""));
        }
        var bytes = System.Text.Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) file.Write(bytes, 0, bytes.Length);
        File.WriteAllBytes(Path.Combine(Host.Output, "diagnostic-polygon.POB"), bytes);
        try
        {
            obj.Comment = name;
            system.NCE.ReloadObject(1);
            Host.Write("diagnostic-polygon.json", Host.Object("originalName", originalName, "temporaryName", name,
                "actualComment", obj.Comment, "status", system.GetCurrentStatus(),
                "mode", mode, "flipWinding", flipWinding, "triangleMap", triangleMap, "object", Host.Properties(obj),
                "savedInput", false, "installedOriginalUnchanged", true));
            if (obj.Comment != name) throw new InvalidOperationException("Native polygon filename readback differs");
            return path;
        }
        catch { RemoveOwned(path); throw; }
    }
    internal static void RemoveOwned(string path)
    {
        if (!Path.GetFileName(path).StartsWith("codex_afocal_", StringComparison.Ordinal) || Path.GetExtension(path) != ".POB")
            throw new InvalidOperationException("Not an owned polygon control");
        File.Delete(path);
    }
}
