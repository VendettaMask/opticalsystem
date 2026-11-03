using System;
using System.Collections.Generic;
using ZOSAPI;
using ZOSAPI.Editors.LDE;

// Explicit analysis convention on the owned, unsaved native system. This flag
// affects paraxial rays only; real rays and the original file remain unchanged.
internal static class NativeFirstOrderReference
{
    internal const string Convention = "CenteredVertexOrdinaryIndexV1";
    internal static object Apply(IOpticalSystem system)
    {
        var rows = new List<object>();
        for (int i = 0; i < system.LDE.NumberOfSurfaces; i++)
        {
            var row = system.LDE.GetSurfaceAt(i);
            if (row.Type != SurfaceType.BirefringentIn) continue;
            var flag = row.GetSurfaceCell(SurfaceColumn.Par5);
            var previous = flag.IntegerValue;
            if (!flag.MakeSolveFixed()) throw new InvalidOperationException("Cannot select the declared crystal paraxial reference");
            flag.IntegerValue = 1;
            if (flag.IntegerValue != 1) throw new InvalidOperationException("Crystal paraxial convention rejected");
            rows.Add(Host.Object("surface", i, "originalParaxIgnore", previous, "actualParaxIgnore", 1));
        }
        system.UpdateStatus();
        var record = Host.Object("convention", Convention, "surfaces", rows,
            "realRayModeChanged", false, "savedInput", false);
        Host.Write("first-order-reference.json", record);
        return record;
    }
}
