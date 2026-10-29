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
        if (mode != "02" && mode != "02-reverse") throw new InvalidOperationException("Unknown independent polygon control");
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
        foreach (var line in File.ReadAllLines(Path.Combine(directory, originalName)))
        {
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens[0] == "R")
            {
                string suffix = string.Join(" ", tokens.Skip(5).ToArray());
                string a = "T " + tokens[1] + " " + tokens[2] + " " + tokens[3] + " " + suffix;
                string b = "T " + tokens[1] + " " + tokens[3] + " " + tokens[4] + " " + suffix;
                lines.Add(mode == "02" ? a : b); lines.Add(mode == "02" ? b : a);
            }
            else lines.Add(line);
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
                "mode", mode, "savedInput", false, "installedOriginalUnchanged", true));
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
