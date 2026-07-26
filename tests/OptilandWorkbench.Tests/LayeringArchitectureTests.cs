using System.Reflection;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

public sealed class LayeringArchitectureTests
{
    [Fact]
    public void AppAssemblyDoesNotReferenceCore()
    {
        var references = typeof(MainWindow).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name == typeof(Optic).Assembly.GetName().Name);
    }

    [Fact]
    public void CoreAndApplicationDoNotReferenceUiFrameworks()
    {
        AssertNoUiReferences(typeof(Optic).Assembly);
        AssertNoUiReferences(typeof(IWorkbenchApplication).Assembly);
    }

    [Fact]
    public void ApplicationContractsDoNotExposeCoreTypes()
    {
        var contractTypes = typeof(IWorkbenchApplication).Assembly.ExportedTypes
            .Where(type => type.Namespace == typeof(IWorkbenchApplication).Namespace)
            .ToArray();

        foreach (var contractType in contractTypes)
        {
            AssertNotCoreType(contractType);
            foreach (var memberType in PublicMemberTypes(contractType))
            {
                AssertNotCoreType(memberType);
            }
        }
    }

    [Fact]
    public void WorkbenchApplicationIsOnlyACompositionRoot()
    {
        var interfaces = typeof(WorkbenchApplication).GetInterfaces();

        Assert.Contains(typeof(IWorkbenchApplication), interfaces);
        Assert.DoesNotContain(typeof(IOpticalDocumentService), interfaces);
        Assert.DoesNotContain(typeof(IPrescriptionService), interfaces);
        Assert.DoesNotContain(typeof(IAnalysisService), interfaces);
        Assert.DoesNotContain(typeof(IVisualizationService), interfaces);
        Assert.DoesNotContain(typeof(IOptimizationService), interfaces);
        Assert.DoesNotContain(typeof(ITolerancingService), interfaces);
        Assert.DoesNotContain(typeof(IMultiConfigurationService), interfaces);
        Assert.DoesNotContain(typeof(IMaterialCatalogService), interfaces);
        Assert.DoesNotContain(typeof(IWorkspaceEventStream), interfaces);
    }

    [Fact]
    public void LegacyConnectorIsAThinCompatibilityFacade()
    {
        Assert.Equal(typeof(OpticalWorkspaceModel), typeof(OptilandConnector).BaseType);

        var declaredMethods = typeof(OptilandConnector).GetMethods(
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly);
        Assert.Empty(declaredMethods);
    }

    private static void AssertNoUiReferences(Assembly assembly)
    {
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true
            || reference.Name?.StartsWith("Dock.", StringComparison.Ordinal) == true);
    }

    private static IEnumerable<Type> PublicMemberTypes(Type contractType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        foreach (var property in contractType.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (var eventInfo in contractType.GetEvents(flags))
        {
            if (eventInfo.EventHandlerType is not null)
            {
                yield return eventInfo.EventHandlerType;
            }
        }

        foreach (var method in contractType.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var constructor in contractType.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static void AssertNotCoreType(Type type)
    {
        foreach (var exposedType in Flatten(type))
        {
            Assert.False(
                exposedType.Namespace?.StartsWith("OptilandWorkbench.Core", StringComparison.Ordinal) == true,
                $"Application contract exposes Core type {exposedType.FullName}.");
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            yield return type.GetElementType()!;
            yield break;
        }

        yield return type;
        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Flatten(argument))
            {
                yield return nested;
            }
        }
    }
}
