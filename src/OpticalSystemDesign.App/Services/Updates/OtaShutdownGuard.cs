namespace OpticalSystemDesign.App.Services.Updates;

// Shared close preparation; the update helper may be launched only AFTER the real Closed event.
internal static class OtaShutdownGuard
{
    internal static async Task<bool> PrepareAsync(Func<Task<bool>> confirmDocuments,
        Func<Task<bool>> closeOwnedWorkspaces, Func<Task> persistSession)
    {
        if (!await confirmDocuments()) return false;
        if (!await closeOwnedWorkspaces()) return false;
        await persistSession();
        return true;
    }
}
