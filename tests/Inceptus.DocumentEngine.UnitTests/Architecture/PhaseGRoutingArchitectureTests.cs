using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseGRoutingArchitectureTests
{
    private static readonly Assembly ContractsAssembly = typeof(RoutingResult).Assembly;
    private static readonly Assembly RuntimeAssembly = typeof(RoutingEngine).Assembly;
    private static readonly BindingFlags DeclaredPublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void RoutingEngineIsTheSingleFrameworkOwnedOrchestrator()
    {
        Assert.Same(RuntimeAssembly, typeof(RoutingEngine).Assembly);
        Assert.True(typeof(RoutingEngine).IsPublic);
        Assert.True(typeof(RoutingEngine).IsSealed);
        Assert.False(typeof(RoutingEngine).IsAbstract);
        Assert.False(typeof(RoutingEngine).IsInterface);

        var productionTypes = ContractsAssembly.GetTypes().Concat(RuntimeAssembly.GetTypes()).ToArray();
        Assert.DoesNotContain(productionTypes, type =>
            string.Equals(type.Name, "IRoutingEngine", StringComparison.Ordinal));
        Assert.Equal(
            [typeof(RoutingEngine)],
            productionTypes.Where(type =>
                string.Equals(type.Name, nameof(RoutingEngine), StringComparison.Ordinal)));
        Assert.DoesNotContain(
            ContractsAssembly.GetTypes(),
            type => type.Name.Contains("RoutingEngine", StringComparison.Ordinal));
    }

    [Fact]
    public void RoutingEngineExposesOnlyImmutableUpstreamInputs()
    {
        var constructor = Assert.Single(typeof(RoutingEngine).GetConstructors());
        Assert.Equal(
            [typeof(IEnumerable<RoutingAlgorithmRegistration>)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(constructor.GetParameters()[0].IsOptional);

        var route = Assert.Single(typeof(RoutingEngine).GetMethods(DeclaredPublicMembers));
        Assert.Equal(nameof(RoutingEngine.Route), route.Name);
        Assert.Equal(typeof(RoutingExecutionResult), route.ReturnType);
        Assert.Equal(
            [
                typeof(ProjectedGraph),
                typeof(LayoutResult),
                typeof(AlgorithmId),
                typeof(RoutingContext),
                typeof(CancellationToken),
            ],
            route.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(route.GetParameters()[3].IsOptional);
        Assert.True(route.GetParameters()[4].IsOptional);

        var signatureTypes = GetPublicSignatureTypes(typeof(RoutingEngine)).ToArray();
        Assert.DoesNotContain(signatureTypes, type =>
            type == typeof(Document) || type == typeof(DocumentSnapshot));
        Assert.DoesNotContain(
            signatureTypes.Where(type => type != typeof(RoutingEngine)),
            IsForbiddenRoutingDependency);
    }

    [Fact]
    public void RoutingAlgorithmsAreContractsOnlyPoliciesWithoutMutationAccess()
    {
        Type[] pluginBoundary =
        [
            typeof(IRoutingAlgorithm),
            typeof(RoutingAlgorithmRegistration),
            typeof(RoutingAlgorithmResult),
            typeof(RoutingComputation),
            typeof(RoutingContext),
            typeof(RoutedConnectorGeometry),
            typeof(RoutingResult),
        ];

        Assert.All(pluginBoundary, type => Assert.Same(ContractsAssembly, type.Assembly));
        var signatureTypes = pluginBoundary.SelectMany(GetPublicSignatureTypes).ToArray();
        Assert.DoesNotContain(signatureTypes, IsForbiddenRoutingDependency);
        Assert.DoesNotContain(signatureTypes, type =>
            type == typeof(Document) ||
            type == typeof(DocumentSnapshot) ||
            type == typeof(IServiceProvider) ||
            typeof(Delegate).IsAssignableFrom(type));

        var route = Assert.Single(typeof(IRoutingAlgorithm).GetMethods());
        Assert.Equal(nameof(IRoutingAlgorithm.Route), route.Name);
        Assert.Equal(typeof(RoutingAlgorithmResult), route.ReturnType);
        Assert.Equal(
            [typeof(ProjectedGraph), typeof(LayoutResult), typeof(RoutingContext), typeof(CancellationToken)],
            route.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void AlgorithmRegistryIsInternalAndCannotReplaceTheEngine()
    {
        var registry = Assert.Single(RuntimeAssembly.GetTypes(), type =>
            type.FullName == "Inceptus.DocumentEngine.Runtime.Routing.RoutingAlgorithmRegistry");

        Assert.False(registry.IsPublic);
        Assert.True(registry.IsSealed);
        Assert.DoesNotContain(
            RuntimeAssembly.GetExportedTypes(),
            type => type.Name.Contains("Registry", StringComparison.Ordinal));
        Assert.False(typeof(RoutingEngine).IsAssignableFrom(typeof(IRoutingAlgorithm)));
        Assert.Equal(
            typeof(IRoutingAlgorithm),
            typeof(RoutingAlgorithmRegistration)
                .GetProperty(nameof(RoutingAlgorithmRegistration.Algorithm))!
                .PropertyType);
    }

    [Fact]
    public void PublicRoutingContractsAreImmutableAndExposeNoMutableCollections()
    {
        var routingTypes = ContractsAssembly.GetExportedTypes()
            .Where(type => type.Namespace == "Inceptus.DocumentEngine.Contracts.Routing")
            .ToArray();

        Assert.NotEmpty(routingTypes);
        Assert.All(
            routingTypes.SelectMany(type => type.GetProperties(DeclaredPublicMembers)),
            property => Assert.Null(property.SetMethod));

        var returnedTypes = routingTypes
            .SelectMany(type => type.GetProperties(DeclaredPublicMembers))
            .Select(property => property.PropertyType)
            .Concat(routingTypes
                .SelectMany(type => type.GetMethods(DeclaredPublicMembers))
                .Where(method => !method.IsSpecialName)
                .Select(method => method.ReturnType))
            .ToArray();
        Assert.DoesNotContain(returnedTypes, IsMutableCollectionContract);

        string[] forbiddenMutationMembers =
        [
            "Add", "Clear", "Delete", "ExecuteCommand", "Insert", "Mutate",
            "Persist", "Remove", "Replace", "Serialize", "Update",
        ];
        Assert.DoesNotContain(
            routingTypes
                .Where(type => type != typeof(IRoutingAlgorithm))
                .SelectMany(type => type.GetMethods(DeclaredPublicMembers))
                .Where(method => !method.IsSpecialName),
            method => forbiddenMutationMembers.Any(forbidden =>
                method.Name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void RoutingCodeContainsNoAuthoritativeMutationOrDownstreamDependency()
    {
        var routingTypes = ContractsAssembly.GetTypes()
            .Concat(RuntimeAssembly.GetTypes())
            .Where(type => type.Namespace?.Contains(".Routing", StringComparison.Ordinal) == true)
            .ToArray();
        var signatureTypes = routingTypes
            .SelectMany(GetAllDeclaredSignatureTypes)
            .Where(type => !routingTypes.Contains(type))
            .ToArray();

        Assert.DoesNotContain(signatureTypes, type =>
            typeof(ICommand).IsAssignableFrom(type) ||
            type == typeof(DocumentChangedEvent) ||
            type == typeof(EditorStateSnapshot) ||
            type == typeof(HistoryStatus) ||
            type == typeof(Document) ||
            type == typeof(DocumentSnapshot));
        Assert.DoesNotContain(signatureTypes, IsForbiddenRoutingDependency);
        Assert.DoesNotContain(routingTypes, type => ContainsAny(
            type.Name,
            "CommandProcessor",
            "History",
            "EditorState",
            "Canvas2D",
            "Renderer",
            "Serialization"));
    }

    [Fact]
    public void RoutingResultNeverEntersAuthoritativeSnapshotsEventsOrHistory()
    {
        var authoritativeAndSessionTypes = new[]
        {
            typeof(DocumentSnapshot),
            typeof(DocumentChangedEvent),
        }.Concat(ContractsAssembly.GetTypes().Where(type =>
            type.Namespace?.Contains(".History", StringComparison.Ordinal) == true))
        .Concat(RuntimeAssembly.GetTypes().Where(type =>
            type.Namespace?.Contains(".History", StringComparison.Ordinal) == true))
        .ToArray();

        var signatureTypes = authoritativeAndSessionTypes
            .SelectMany(GetAllDeclaredSignatureTypes)
            .ToArray();

        Assert.DoesNotContain(signatureTypes, type =>
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Routing",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RoutingIntroducesNoPersistenceRenderingOrHostAssemblyDependency()
    {
        string[] forbiddenAssemblyPrefixes =
        [
            "Inceptus.DocumentEngine.Blazor",
            "Inceptus.DocumentEngine.Bpmn",
            "Inceptus.DocumentEngine.Canvas2D",
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
        ];

        Assert.DoesNotContain(
            ContractsAssembly.GetReferencedAssemblies()
                .Concat(RuntimeAssembly.GetReferencedAssemblies()),
            reference => forbiddenAssemblyPrefixes.Any(prefix =>
                reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true));

        var productionTypes = ContractsAssembly.GetTypes().Concat(RuntimeAssembly.GetTypes());
        Assert.DoesNotContain(productionTypes, type =>
            type.Namespace?.Contains(".Routing", StringComparison.Ordinal) == true &&
            ContainsAny(type.Name, "Cache", "Persistence", "Repository", "Serializer", "Store"));
    }

    private static bool IsForbiddenRoutingDependency(Type type)
    {
        var namespaceName = type.Namespace ?? string.Empty;
        return namespaceName.StartsWith("Inceptus.DocumentEngine.Contracts.Commands", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Contracts.Documents", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Contracts.EditorState", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Contracts.History", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Runtime", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Canvas2D", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Bpmn", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Blazor", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) ||
            namespaceName.StartsWith("System.Runtime.InteropServices.JavaScript", StringComparison.Ordinal) ||
            namespaceName.StartsWith("System.Text.Json", StringComparison.Ordinal) ||
            ContainsAny(type.Name, "Canvas2DScene", "Renderer", "HTMLCanvas", "Path2D");
    }

    private static bool IsMutableCollectionContract(Type type)
    {
        if (type.IsArray || type == typeof(IList) || type == typeof(IDictionary) ||
            type == typeof(ICollection))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        Type[] forbiddenDefinitions =
        [
            typeof(List<>), typeof(Dictionary<,>), typeof(IList<>),
            typeof(IDictionary<,>), typeof(ICollection<>),
        ];
        return forbiddenDefinitions.Contains(type.GetGenericTypeDefinition());
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        yield return type;
        foreach (var constructor in type.GetConstructors(DeclaredPublicMembers))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var property in type.GetProperties(DeclaredPublicMembers))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }

        foreach (var method in type.GetMethods(DeclaredPublicMembers))
        {
            foreach (var expanded in ExpandType(method.ReturnType))
            {
                yield return expanded;
            }

            foreach (var parameter in method.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }
    }

    private static IEnumerable<Type> GetAllDeclaredSignatureTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        yield return type;

        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var field in type.GetFields(flags))
        {
            foreach (var expanded in ExpandType(field.FieldType))
            {
                yield return expanded;
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }

        foreach (var method in type.GetMethods(flags))
        {
            foreach (var expanded in ExpandType(method.ReturnType))
            {
                yield return expanded;
            }

            foreach (var parameter in method.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        if (type.IsArray || type.IsByRef || type.IsPointer)
        {
            var elementType = type.GetElementType();
            if (elementType is not null)
            {
                foreach (var expanded in ExpandType(elementType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(argument))
            {
                yield return expanded;
            }
        }
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
