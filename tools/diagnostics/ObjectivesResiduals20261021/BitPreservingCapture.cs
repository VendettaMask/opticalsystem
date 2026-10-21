// Offline companion to the normal JSON export. Records the API's binary64
// values before Framework JSON formatting, without changing the capture itself.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

internal static class BitPreservingCapture
{
    internal static void Write(string name, object value)
    {
        var values = new Dictionary<string, string>();
        Visit(value, "", values);
        File.WriteAllText(Path.Combine(Host.Output, name + ".bits.json"),
            new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(values));
    }
    private static void Visit(object value, string path, Dictionary<string, string> values)
    {
        if (value is double)
        {
            values.Add(path, BitConverter.DoubleToInt64Bits((double)value).ToString("x16"));
            return;
        }
        var map = value as IDictionary;
        if (map != null)
        {
            foreach (DictionaryEntry entry in map) Visit(entry.Value, path + "/" + entry.Key, values);
            return;
        }
        var array = value as IEnumerable;
        if (array != null && !(value is string))
        {
            int i = 0;
            foreach (object element in array) Visit(element, path + "/" + i++, values);
        }
    }
}
