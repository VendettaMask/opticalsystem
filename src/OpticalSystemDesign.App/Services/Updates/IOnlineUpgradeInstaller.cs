namespace OpticalSystemDesign.App.Services.Updates;

/// <summary>Future boundary for verified, user-approved installation and application lifecycle handling.</summary>
internal interface IOnlineUpgradeInstaller
{
    bool IsAvailable { get; }
    Task InstallAsync(AvailableRelease release, CancellationToken cancellationToken);
}

internal sealed class ReservedOnlineUpgradeInstaller : IOnlineUpgradeInstaller
{
    public bool IsAvailable => false;

    public Task InstallAsync(AvailableRelease release, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("Online installation is reserved and is not implemented."));
}
