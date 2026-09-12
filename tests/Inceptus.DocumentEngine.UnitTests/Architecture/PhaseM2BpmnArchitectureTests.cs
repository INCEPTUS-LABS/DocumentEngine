using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM2BpmnArchitectureTests
{
    [Fact]
    public void BpmnOwnsPoliciesWhileFrameworkRetainsLayoutAndRoutingEngines()
    {
        var bpmnAssembly = typeof(BpmnPluginRegistration).Assembly;
        var runtimeAssembly = typeof(LayoutEngine).Assembly;

        Assert.Same(bpmnAssembly, typeof(BpmnLayoutAlgorithm).Assembly);
        Assert.Same(bpmnAssembly, typeof(BpmnRoutingAlgorithm).Assembly);
        Assert.True(typeof(ILayoutAlgorithm).IsAssignableFrom(typeof(BpmnLayoutAlgorithm)));
        Assert.True(typeof(IRoutingAlgorithm).IsAssignableFrom(typeof(BpmnRoutingAlgorithm)));

        Assert.Same(runtimeAssembly, typeof(RoutingEngine).Assembly);
        Assert.NotSame(bpmnAssembly, runtimeAssembly);
        Assert.DoesNotContain(bpmnAssembly.GetTypes(), type =>
            type.Name.Contains("LayoutEngine", StringComparison.Ordinal) ||
            type.Name.Contains("RoutingEngine", StringComparison.Ordinal));
    }

    [Fact]
    public void M2RegistrationUsesOnlyGenericAlgorithmRegistrations()
    {
        var registration = BpmnPluginRegistration.M2;

        var layout = Assert.Single(registration.LayoutAlgorithms);
        Assert.Equal(BpmnAlgorithmIds.DefaultLayout, layout.AlgorithmId);
        Assert.IsType<BpmnLayoutAlgorithm>(layout.Algorithm);

        var routing = Assert.Single(registration.RoutingAlgorithms);
        Assert.Equal(BpmnAlgorithmIds.DefaultRouting, routing.AlgorithmId);
        Assert.IsType<BpmnRoutingAlgorithm>(routing.Algorithm);
        Assert.Empty(registration.PropertiesSchemas);
    }

    [Fact]
    public void GenericEnginesAndContractsContainNoBpmnDependencyOrSemanticBranch()
    {
        var genericAssemblies = new[]
        {
            typeof(ILayoutAlgorithm).Assembly,
            typeof(LayoutEngine).Assembly,
        };
        Assert.All(genericAssemblies, assembly => Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => string.Equals(
                reference.Name,
                "Inceptus.DocumentEngine.Bpmn",
                StringComparison.Ordinal)));

        var engineSources = string.Concat(
            File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "src",
                "Inceptus.DocumentEngine.Runtime",
                "Layout",
                "LayoutEngine.cs")),
            File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "src",
                "Inceptus.DocumentEngine.Runtime",
                "Routing",
                "RoutingEngine.cs")));
        Assert.DoesNotContain("BPMN.", engineSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bpmn", engineSources, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlgorithmsExposeOnlyReadOnlyProjectedLayoutAndRoutingContracts()
    {
        var layoutMethod = Assert.Single(typeof(BpmnLayoutAlgorithm).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(ILayoutAlgorithm.Compute), layoutMethod.Name);
        Assert.Equal(typeof(LayoutAlgorithmResult), layoutMethod.ReturnType);
        Assert.Equal(
            new[] { typeof(ProjectedGraph), typeof(LayoutContext), typeof(CancellationToken) },
            layoutMethod.GetParameters().Select(static parameter => parameter.ParameterType));

        var routingMethod = Assert.Single(typeof(BpmnRoutingAlgorithm).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(IRoutingAlgorithm.Route), routingMethod.Name);
        Assert.Equal(typeof(RoutingAlgorithmResult), routingMethod.ReturnType);
        Assert.Equal(
            new[]
            {
                typeof(ProjectedGraph),
                typeof(LayoutResult),
                typeof(RoutingContext),
                typeof(CancellationToken),
            },
            routingMethod.GetParameters().Select(static parameter => parameter.ParameterType));

        var signatureTypes = new[] { typeof(BpmnLayoutAlgorithm), typeof(BpmnRoutingAlgorithm) }
            .SelectMany(DeclaredSignatureTypes)
            .ToArray();
        Assert.DoesNotContain(signatureTypes, IsMutableSubsystemType);
    }

    [Fact]
    public void M2AlgorithmsContainNoCanvasHostOrSimulationImplementation()
    {
        var sources = string.Concat(
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Bpmn", "Layout")),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Bpmn", "Routing")));
        string[] forbidden =
        [
            "CanvasRenderingContext2D",
            "Canvas2DScene",
            "ScenePath",
            "SceneEllipse",
            "JSInterop",
            "CommandProcessor",
            "HistoryManager",
            "DocumentSnapshot",
            "Simulation",
            "DurationDistribution",
            "QueueCapacity",
            "ResourceCapacity",
            "ArrivalDistribution",
        ];

        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, sources, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> DeclaredSignatureTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetConstructors(flags)
            .SelectMany(static constructor => constructor.GetParameters())
            .Select(static parameter => parameter.ParameterType)
            .Concat(type.GetMethods(flags).Select(static method => method.ReturnType))
            .Concat(type.GetMethods(flags)
                .SelectMany(static method => method.GetParameters())
                .Select(static parameter => parameter.ParameterType))
            .Concat(type.GetFields(flags).Select(static field => field.FieldType))
            .Concat(type.GetProperties(flags).Select(static property => property.PropertyType));
    }

    private static bool IsMutableSubsystemType(Type type)
    {
        var namespaceName = type.Namespace ?? string.Empty;
        return namespaceName.StartsWith(
                   "Inceptus.DocumentEngine.Contracts.Commands",
                   StringComparison.Ordinal) ||
               namespaceName.StartsWith(
                   "Inceptus.DocumentEngine.Contracts.Documents",
                   StringComparison.Ordinal) ||
               namespaceName.StartsWith(
                   "Inceptus.DocumentEngine.Contracts.History",
                   StringComparison.Ordinal) ||
               namespaceName.StartsWith(
                   "Inceptus.DocumentEngine.Contracts.EditorState",
                   StringComparison.Ordinal) ||
               namespaceName.StartsWith(
                   "Inceptus.DocumentEngine.Runtime",
                   StringComparison.Ordinal) ||
               namespaceName.StartsWith(
                   "Inceptus.DocumentEngine.Canvas2D",
                   StringComparison.Ordinal) ||
               namespaceName.StartsWith(
                   "Inceptus.DocumentEngine.Blazor",
                   StringComparison.Ordinal);
    }

    private static string ReadSources(string relativeDirectory) => string.Concat(Directory
        .GetFiles(Path.Combine(RepositoryRoot, relativeDirectory), "*.cs", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(File.ReadAllText));

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
