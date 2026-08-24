using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Application.Services;

internal abstract class WorkbenchServiceBase
{
    protected WorkbenchServiceBase(WorkspaceCoordinator workspace)
    {
        Workspace = workspace;
    }

    protected WorkspaceCoordinator Workspace { get; }

    protected object Gate => Workspace.Gate;

    protected WorkbenchRuntime Runtime => Workspace.Runtime;

    protected OpticalSurface? FindSurface(int surfaceNumber)
    {
        return Runtime.Surfaces.FirstOrDefault(surface => surface.Number == surfaceNumber);
    }

    protected void Mutate(WorkspaceChangeCategory category, Action action)
    {
        Workspace.Mutate(category, action);
    }

    protected T Mutate<T>(WorkspaceChangeCategory category, Func<T> action)
    {
        return Workspace.Mutate(category, action);
    }

    protected void MutateTransactional(WorkspaceChangeCategory category, Action action)
    {
        Workspace.MutateTransactional(category, action);
    }

    protected T MutateTransactional<T>(WorkspaceChangeCategory category, Func<T> action)
    {
        return Workspace.MutateTransactional(category, action);
    }

    protected void MutateTransactional(
        WorkspaceChangeCategory category,
        Action action,
        CancellationToken automaticSemiDiameterCancellationToken)
    {
        Workspace.MutateTransactional(
            category,
            action,
            automaticSemiDiameterCancellationToken);
    }

    protected T MutateTransactional<T>(
        WorkspaceChangeCategory category,
        Func<T> action,
        CancellationToken automaticSemiDiameterCancellationToken)
    {
        return Workspace.MutateTransactional(
            category,
            action,
            automaticSemiDiameterCancellationToken);
    }
}
