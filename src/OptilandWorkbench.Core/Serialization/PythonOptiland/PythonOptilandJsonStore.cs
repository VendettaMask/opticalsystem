namespace OptilandWorkbench.Core.Serialization;

public static class PythonOptilandJsonStore
{
    public static bool LooksLike(string json)
    {
        return PythonOptilandJsonReader.LooksLike(json);
    }

    public static Optic Deserialize(string json, string name = "Imported Python Optiland")
    {
        return PythonOptilandJsonReader.Deserialize(json, name);
    }

    public static string Serialize(Optic optic)
    {
        return PythonOptilandJsonWriter.Serialize(optic);
    }

    public static Task SaveAsync(
        Optic optic,
        string path,
        CancellationToken cancellationToken = default)
    {
        return PythonOptilandJsonWriter.SaveAsync(optic, path, cancellationToken);
    }
}
