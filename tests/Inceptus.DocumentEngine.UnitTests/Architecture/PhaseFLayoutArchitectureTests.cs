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
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Layout;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseFLayoutArchitectureTests
{
    private static readonly Assembly ContractsAssembly = typeof(LayoutResult).Assembly;
    private static readonly Assembly RuntimeAssembly = typeof(LayoutEngine).Assembly;
    private static readonly BindingFlags DeclaredPublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void LayoutEngineIsTheSingleFrameworkOwnedOrchestrator()
    {
        Assert.Same(RuntimeAssembly, typeof(LayoutEngine).Assembly);
        Assert.True(typeof(LayoutEngine).IsPublic);
        Assert.True(typeof(LayoutEngine).IsSealed);
        Assert.False(typeof(LayoutEngine).IsAbstract);
        Assert.False(typeof(LayoutEngine).IsInterface);

        var productionTypes = ContractsAssembly.GetTypes().Concat(RuntimeAssembly.GetTypes()).ToArray();
        Assert.DoesNotContain(productionTypes, type =>
            string.Equals(type.Name, "ILayoutEngine", StringComparison.Ordinal));
        Assert.Equal(
            [typeof(LayoutEngine)],
            productionTypes.Where(type =>
                string.Equals(type.Name, nameof(LayoutEngine), StringComparison.Ordinal)));
        Assert.DoesNotContain(
            ContractsAssembly.GetTypes(),
            type => type.Name.Contains("LayoutEngine", StringComparison.Ordinal));
    }

    [Fact]
    public void LayoutEngineExposesOnlyProjectedGraphBasedReadOnlyExecution()
    {
        var constructor = Assert.Single(typeof(LayoutEngine).GetConstructors());
        Assert.Equal(
            [typeof(IEnumerable<LayoutAlgorithmRegistration>)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(constructor.GetParameters()[0].IsOptional);

        var layout = Assert.Single(typeof(LayoutEngine).GetMethods(DeclaredPublicMembers));
        Assert.Equal(nameof(LayoutEngine.Layout), layout.Name);
        Assert.Equal(typeof(LayoutExecutionResult), layout.ReturnType);
        Assert.Equal(
            [typeof(ProjectedGraph), typeof(AlgorithmId), typeof(LayoutContext), typeof(CancellationToken)],
            layout.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(layout.GetParameters()[2].IsOptional);
        Assert.True(layout.GetParameters()[3].IsOptional);

        var signatureTypes = GetPublicSignatureTypes(typeof(LayoutEngine)).ToArray();
        Assert.DoesNotContain(signatureTypes, type =>
            type == typeof(Document) || type == typeof(DocumentSnapshot));
        Assert.DoesNotContain(
            signatureTypes.Where(type => type != typeof(LayoutEngine)),
            IsForbiddenLayoutDependency);
    }

    [Fact]
    public void LayoutAlgorithmsAreContractsOnlyPoliciesWithoutFrameworkMutationAccess()
    {
        Type[] pluginBoundary =
        [
            typeof(ILayoutAlgorithm),
            typeof(LayoutAlgorithmRegistration),
            typeof(LayoutAlgorithmResult),
            typeof(LayoutComputation),
            typeof(LayoutContext),
            typeof(LayoutGroupGeometry),
            typeof(LayoutNodeGeometry),
            typeof(LayoutResult),
        ];

        Assert.All(pluginBoundary, type => Assert.Same(ContractsAssembly, type.Assembly));
        var signatureTypes = pluginBoundary.SelectMany(GetPublicSignatureTypes).ToArray();
        Assert.DoesNotContain(signatureTypes, IsForbiddenLayoutDependency);
        Assert.DoesNotContain(signatureTypes, type =>
            type == typeof(Document) ||
            type == typeof(DocumentSnapshot) ||
            type == typeof(IServiceProvider) ||
            typeof(Delegate).IsAssignableFrom(type));

        var compute = Assert.Single(typeof(ILayoutAlgorithm).GetMethods());
        Assert.Equal(nameof(ILayoutAlgorithm.Compute), compute.Name);
        Assert.Equal(typeof(LayoutAlgorithmResult), compute.ReturnType);
        Assert.Equal(
            [typeof(ProjectedGraph), typeof(LayoutContext), typeof(CancellationToken)],
            compute.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void AlgorithmRegistryIsInternalAndCannotReplaceTheEngine()
    {
        var registry = Assert.Single(RuntimeAssembly.GetTypes(), type =>
            type.FullName == "Inceptus.DocumentEngine.Runtime.Layout.LayoutAlgorithmRegistry");

        Assert.False(registry.IsPublic);
        Assert.True(registry.IsSealed);
        Assert.DoesNotContain(
            RuntimeAssembly.GetExportedTypes(),
            type => type.Name.Contains("Registry", StringComparison.Ordinal));
        Assert.False(typeof(LayoutEngine).IsAssignableFrom(typeof(ILayoutAlgorithm)));
        Assert.Equal(
            typeof(ILayoutAlgorithm),
            typeof(LayoutAlgorithmRegistration)
                .GetProperty(nameof(LayoutAlgorithmRegistration.Algorithm))!
                .PropertyType);
    }

    [Fact]
    public void PublicLayoutContractsAreImmutableAndExposeNoMutableCollections()
    {
        var layoutTypes = ContractsAssembly.GetExportedTypes()
            .Where(type => type.Namespace == "Inceptus.DocumentEngine.Contracts.Layout")
            .ToArray();

        Assert.NotEmpty(layoutTypes);
        Assert.All(
            layoutTypes.SelectMany(type => type.GetProperties(DeclaredPublicMembers)),
            property => Assert.Null(property.SetMethod));

        var returnedTypes = layoutTypes
            .SelectMany(type => type.GetProperties(DeclaredPublicMembers))
            .Select(property => property.PropertyType)
            .Concat(layoutTypes
                .SelectMany(type => type.GetMethods(DeclaredPublicMembers))
                .Where(method => !method.IsSpecialName)
                .Select(method => method.ReturnType))
            .ToArray();
        Assert.DoesNotContain(returnedTypes, IsMutableCollectionContract);

        string[] forbiddenMutationMembers =
        [
            "Add",
            "Clear",
            "Delete",
            "ExecuteCommand",
            "Insert",
            "Mutate",
            "Persist",
            "Remove",
            "Replace",
            "Serialize",
            "Update",
        ];
        Assert.DoesNotContain(
            layoutTypes
                .Where(type => type != typeof(ILayoutAlgorithm))
                .SelectMany(type => type.GetMethods(DeclaredPublicMembers))
                .Where(method => !method.IsSpecialName),
            method => forbiddenMutationMembers.Any(forbidden =>
                method.Name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LayoutCodeContainsNoAuthoritativeMutationOrDownstreamSubsystemDependency()
    {
        var layoutTypes = ContractsAssembly.GetTypes()
            .Concat(RuntimeAssembly.GetTypes())
            .Where(type => type.Namespace?.Contains(".Layout", StringComparison.Ordinal) == true)
            .ToArray();
        var signatureTypes = layoutTypes
            .SelectMany(GetAllDeclaredSignatureTypes)
            .Where(type => !layoutTypes.Contains(type))
            .ToArray();

        Assert.DoesNotContain(signatureTypes, type =>
            typeof(ICommand).IsAssignableFrom(type) ||
            type == typeof(DocumentChangedEvent) ||
            type == typeof(EditorStateSnapshot) ||
            type == typeof(HistoryStatus) ||
            type == typeof(Document) ||
            type == typeof(DocumentSnapshot));
        Assert.DoesNotContain(signatureTypes, IsForbiddenLayoutDependency);
        Assert.DoesNotContain(layoutTypes, type => ContainsAny(
            type.Name,
            "CommandProcessor",
            "History",
            "EditorState",
            "Routing",
            "Canvas2D",
            "Renderer",
            "Serialization"));
    }

    [Fact]
    public void LayoutResultNeverEntersAuthoritativeSnapshotsEventsOrHistory()
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
                "Inceptus.DocumentEngine.Contracts.Layout",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void LayoutIntroducesNoPersistenceRoutingRenderingOrHostAssemblyDependency()
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
            type.Namespace?.Contains(".Layout", StringComparison.Ordinal) == true &&
            ContainsAny(
                type.Name,
                "Cache",
                "Persistence",
                "Repository",
                "Serializer",
                "Store"));
    }

    private static bool IsForbiddenLayoutDependency(Type type)
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
            ContainsAny(
                type.Name,
                "RoutingResult",
                "RoutingEngine",
                "Canvas2DScene",
                "Renderer",
                "HTMLCanvas",
                "Path2D");
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
            typeof(List<>),
            typeof(Dictionary<,>),
            typeof(IList<>),
            typeof(IDictionary<,>),
            typeof(ICollection<>),
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
