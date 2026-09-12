using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseEProjectionArchitectureTests
{
    private static readonly Assembly ContractsAssembly = typeof(ProjectedGraph).Assembly;
    private static readonly Assembly RuntimeAssembly = typeof(ProjectionEngine).Assembly;
    private static readonly BindingFlags DeclaredPublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void ProjectionEngineIsTheSingleFrameworkOwnedOrchestrator()
    {
        Assert.Same(RuntimeAssembly, typeof(ProjectionEngine).Assembly);
        Assert.True(typeof(ProjectionEngine).IsPublic);
        Assert.True(typeof(ProjectionEngine).IsSealed);
        Assert.False(typeof(ProjectionEngine).IsAbstract);

        var productionTypes = ContractsAssembly.GetTypes().Concat(RuntimeAssembly.GetTypes()).ToArray();
        Assert.DoesNotContain(productionTypes, type =>
            string.Equals(type.Name, "IProjectionEngine", StringComparison.Ordinal));
        Assert.Equal(
            [typeof(ProjectionEngine)],
            productionTypes.Where(type =>
                string.Equals(type.Name, nameof(ProjectionEngine), StringComparison.Ordinal)));
        Assert.DoesNotContain(
            ContractsAssembly.GetTypes(),
            type => type.Name.Contains("ProjectionEngine", StringComparison.Ordinal));
        Assert.DoesNotContain(
            RuntimeAssembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Bpmn", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ProjectionEngineExposesOnlySnapshotBasedReadOnlyExecution()
    {
        var constructor = Assert.Single(typeof(ProjectionEngine).GetConstructors());
        Assert.Equal(
            [typeof(IEnumerable<ProjectionRuleRegistration>)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(constructor.GetParameters()[0].IsOptional);

        var projects = typeof(ProjectionEngine).GetMethods(DeclaredPublicMembers);
        Assert.Equal(2, projects.Length);
        Assert.All(projects, project =>
        {
            Assert.Equal(nameof(ProjectionEngine.Project), project.Name);
            Assert.Equal(typeof(ProjectionResult), project.ReturnType);
        });
        var rootProject = Assert.Single(projects, method => method.GetParameters().Length == 3);
        Assert.Equal(
            [typeof(DocumentSnapshot), typeof(ProjectionContext), typeof(CancellationToken)],
            rootProject.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(rootProject.GetParameters()[1].IsOptional);
        Assert.True(rootProject.GetParameters()[2].IsOptional);
        var scopedProject = Assert.Single(projects, method => method.GetParameters().Length == 4);
        Assert.Equal(
            [
                typeof(DocumentSnapshot),
                typeof(DocumentScopeId),
                typeof(ProjectionContext),
                typeof(CancellationToken),
            ],
            scopedProject.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.False(scopedProject.GetParameters()[1].IsOptional);
        Assert.True(scopedProject.GetParameters()[2].IsOptional);
        Assert.True(scopedProject.GetParameters()[3].IsOptional);
        Assert.DoesNotContain(
            GetPublicSignatureTypes(typeof(ProjectionEngine)),
            type => type == typeof(Document));
    }

    [Fact]
    public void ProjectionRulesAreContractsOnlyPoliciesWithoutRuntimeOrMutationAccess()
    {
        Type[] pluginBoundary =
        [
            typeof(IProjectionRule),
            typeof(ProjectionRuleRegistration),
            typeof(ProjectionRuleInput),
            typeof(ElementProjectionRuleInput),
            typeof(RelationshipProjectionRuleInput),
            typeof(ProjectionRuleResult),
            typeof(ProjectionRuleContribution),
            typeof(ProjectionContext),
        ];

        Assert.All(pluginBoundary, type => Assert.Same(ContractsAssembly, type.Assembly));
        var signatureTypes = pluginBoundary.SelectMany(GetPublicSignatureTypes).ToArray();
        string[] forbiddenNamespacePrefixes =
        [
            "Inceptus.DocumentEngine.Runtime",
            "Inceptus.DocumentEngine.Contracts.Commands",
            "Inceptus.DocumentEngine.Contracts.EditorState",
            "Inceptus.DocumentEngine.Contracts.History",
            "Inceptus.DocumentEngine.Bpmn",
            "Inceptus.DocumentEngine.Canvas2D",
            "Inceptus.DocumentEngine.Blazor",
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
        ];

        Assert.DoesNotContain(signatureTypes, type =>
            forbiddenNamespacePrefixes.Any(prefix =>
                type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true));
        Assert.DoesNotContain(signatureTypes, type => type == typeof(IServiceProvider));
        Assert.DoesNotContain(signatureTypes, type => typeof(Delegate).IsAssignableFrom(type));

        var project = Assert.Single(typeof(IProjectionRule).GetMethods());
        Assert.Equal(nameof(IProjectionRule.Project), project.Name);
        Assert.Equal(typeof(ProjectionRuleResult), project.ReturnType);
        Assert.Equal(
            [typeof(ProjectionRuleInput), typeof(CancellationToken)],
            project.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RuleRegistryIsInternalAndCannotReplaceTheEngine()
    {
        var registry = Assert.Single(RuntimeAssembly.GetTypes(), type =>
            type.FullName == "Inceptus.DocumentEngine.Runtime.Projection.ProjectionRuleRegistry");

        Assert.False(registry.IsPublic);
        Assert.DoesNotContain(
            RuntimeAssembly.GetExportedTypes(),
            type => type.Name.Contains("Registry", StringComparison.Ordinal));
        Assert.False(typeof(ProjectionEngine).IsAssignableFrom(typeof(IProjectionRule)));
        Assert.Equal(typeof(IProjectionRule),
            typeof(ProjectionRuleRegistration)
                .GetProperty(nameof(ProjectionRuleRegistration.Rule))!
                .PropertyType);
    }

    [Fact]
    public void ProjectedGraphContractsAreImmutableTransientAndCanvasIndependent()
    {
        Type[] graphBoundary =
        [
            typeof(IProjectedObject),
            typeof(ProjectedGraph),
            typeof(ProjectedNode),
            typeof(ProjectedEdge),
            typeof(ProjectedGroup),
            typeof(ProjectedPort),
            typeof(ProjectedLabel),
            typeof(ProjectionSourceTrace),
            typeof(ProjectedPlacementHint),
        ];

        Assert.All(
            graphBoundary.SelectMany(type => type.GetProperties(DeclaredPublicMembers)),
            property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(
            graphBoundary
                .SelectMany(type => type.GetMethods(DeclaredPublicMembers))
                .Where(method => !method.IsSpecialName),
            method => ContainsAny(
                method.Name,
                "Add",
                "Remove",
                "Update",
                "Mutate",
                "Persist",
                "Serialize"));

        var graphCollections = typeof(ProjectedGraph)
            .GetProperties(DeclaredPublicMembers)
            .Where(property => property.PropertyType.IsGenericType)
            .Select(property => property.PropertyType.GetGenericTypeDefinition())
            .ToArray();
        Assert.All(graphCollections, type => Assert.Equal(typeof(ImmutableArray<>), type));

        var signatureTypes = graphBoundary.SelectMany(GetPublicSignatureTypes).ToArray();
        Assert.DoesNotContain(signatureTypes, type => type == typeof(SceneObjectId));
        Assert.DoesNotContain(signatureTypes, IsForbiddenProjectionDependency);
    }

    [Fact]
    public void ProjectionCodeContainsNoCommandHistoryEditorStateOrDownstreamSubsystemContract()
    {
        var projectionTypes = ContractsAssembly.GetTypes()
            .Concat(RuntimeAssembly.GetTypes())
            .Where(type => type.Namespace?.Contains(".Projection", StringComparison.Ordinal) == true)
            .ToArray();
        var signatureTypes = projectionTypes.SelectMany(GetPublicSignatureTypes).ToArray();

        Assert.DoesNotContain(signatureTypes, type =>
            typeof(ICommand).IsAssignableFrom(type) ||
            type == typeof(DocumentChangedEvent) ||
            type == typeof(EditorStateSnapshot) ||
            type == typeof(HistoryStatus) ||
            type == typeof(Document));
        Assert.DoesNotContain(signatureTypes, IsForbiddenProjectionDependency);
        Assert.DoesNotContain(projectionTypes, type =>
            ContainsAny(type.Name, "DomainAnalysis", "LayoutResult", "RoutingResult", "Scene"));
    }

    [Fact]
    public void NoProjectedGraphPersistenceOrDomainAnalysisApiExists()
    {
        var productionTypes = ContractsAssembly.GetTypes().Concat(RuntimeAssembly.GetTypes()).ToArray();
        var projectedGraphPersistenceTypes = productionTypes.Where(type =>
            type.Name.Contains("ProjectedGraph", StringComparison.OrdinalIgnoreCase) &&
            ContainsAny(
                type.Name,
                "Serializer",
                "Serialization",
                "Persistence",
                "Store",
                "Repository"));
        var domainAnalysisTypes = productionTypes.Where(type =>
            ContainsAny(
                type.Name,
                "DomainAnalysisGraph",
                "SimulationGraph",
                "CriticalPathGraph",
                "ProcessExecutionGraph"));

        Assert.Empty(projectedGraphPersistenceTypes);
        Assert.Empty(domainAnalysisTypes);
    }

    private static bool IsForbiddenProjectionDependency(Type type)
    {
        var namespaceName = type.Namespace ?? string.Empty;
        return namespaceName.StartsWith("Inceptus.DocumentEngine.Contracts.Commands", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Contracts.EditorState", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Contracts.History", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Canvas2D", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Bpmn", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Blazor", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) ||
            namespaceName.StartsWith("System.Runtime.InteropServices.JavaScript", StringComparison.Ordinal) ||
            ContainsAny(type.Name, "LayoutEngine", "RoutingEngine", "Renderer", "Canvas2DScene");
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
