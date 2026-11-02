using OpticalSystemDesign.CoatingDesign.Engine;

if (args.Length != 2) return 2;
if (args[0] == "write")
{
    using var session = CoatingRecoverySession.Create(args[1]);
    await session.WriteAsync("crash probe", "{\"physicalText\":\"unfinished.\",\"reference\":625,\"back\":[73.123456789]}", 1);
    Console.WriteLine("READY"); Console.Out.Flush();
    await Task.Delay(Timeout.Infinite);
    return 0;
}
if (args[0] == "read")
{
    var sessions = CoatingRecoverySession.AcquireAvailable(args[1]);
    try
    {
        if (sessions.Count != 1) return 3;
        Console.WriteLine(sessions[0].Read().EditorJson);
        await sessions[0].CompleteAsync();
        return 0;
    }
    finally { foreach (var session in sessions) session.Dispose(); }
}
return 4;
