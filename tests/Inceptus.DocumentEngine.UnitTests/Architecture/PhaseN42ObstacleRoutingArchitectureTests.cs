using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN42ObstacleRoutingArchitectureTests
{
    [Fact]
    public void ObstacleRoutingRemainsBpmnPolicyOverNeutralFrameworkContracts()
    {
        Assert.True(typeof(IRoutingAlgorithm).IsAssignableFrom(typeof(BpmnRoutingAlgorithm)));
        Assert.NotSame(typeof(BpmnRoutingAlgorithm).Assembly, typeof(RoutingEngine).Assembly);

        var frameworkRouting = string.Concat(
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Routing"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "Routing"));
        Assert.DoesNotContain("Bpmn", frameworkRouting, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SequenceFlow", frameworkRouting, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MessageCatchEvent", frameworkRouting, StringComparison.Ordinal);
        Assert.DoesNotContain("TimerCatchEvent", frameworkRouting, StringComparison.Ordinal);
        Assert.DoesNotContain("EventBasedGateway", frameworkRouting, StringComparison.Ordinal);
    }

    [Fact]
    public void LineJumpsRemainDerivedNeutralSceneGeometry()
    {
        var canvasScene = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene");
        Assert.Contains("Canvas2DConnectorLineJumpGeometry", canvasScene, StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", canvasScene, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SequenceFlow", canvasScene, StringComparison.OrdinalIgnoreCase);

        var persistentModel = string.Concat(
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Documents"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Visuals"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Commands"));
        Assert.DoesNotContain("LineJump", persistentModel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CrossingPoint", persistentModel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BridgePoint", persistentModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RendererAndBrowserBoundaryNeedNoNotationOrCrossingBranch()
    {
        var renderer = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering",
            "Canvas2DRenderer.cs");
        var browserBoundary = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Canvas2D",
            "wwwroot",
            "inceptus.canvas2d.js"));

        foreach (var source in new[] { renderer, browserBoundary })
        {
            Assert.DoesNotContain("Bpmn", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SequenceFlow", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LineJump", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TransientInteractionDoesNotInvokeRoutingEngine()
    {
        var interaction = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction");
        Assert.DoesNotContain("RoutingEngine", interaction, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnRoutingAlgorithm", interaction, StringComparison.Ordinal);
    }

    [Fact]
    public void NoRouteOutcomeStateRemainsTransientAndOutsideAuthoritativeModels()
    {
        const BindingFlags declaredMembers = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var noRoutePropertyName = nameof(RoutingComputation.NoRouteEdgeIds);
        var productionAssemblies = new[]
        {
            typeof(RoutingComputation).Assembly,
            typeof(RoutingEngine).Assembly,
            typeof(BpmnRoutingAlgorithm).Assembly,
            typeof(Canvas2DSceneBuilder).Assembly,
        };
        var propertyOwners = productionAssemblies
            .Distinct()
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(type => type.GetProperties(declaredMembers)
                .Any(property => property.Name == noRoutePropertyName))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { typeof(RoutingComputation), typeof(RoutingResult) }
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray(),
            propertyOwners);
        Assert.Equal(
            typeof(ImmutableArray<ProjectedObjectId>),
            typeof(RoutingComputation).GetProperty(noRoutePropertyName)!.PropertyType);

        var persistentTypes = typeof(RoutingComputation).Assembly.GetTypes()
            .Where(static type => IsAuthoritativeNamespace(type.Namespace))
            .Concat(typeof(RoutingEngine).Assembly.GetTypes().Where(static type =>
                type.Namespace?.StartsWith(
                    "Inceptus.DocumentEngine.Runtime.History",
                    StringComparison.Ordinal) == true))
            .ToArray();
        Assert.DoesNotContain(
            persistentTypes.SelectMany(type => type.GetMembers(declaredMembers)),
            member => member.Name.Contains("NoRoute", StringComparison.OrdinalIgnoreCase));

        var authoritativeSource = string.Concat(
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Documents"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Visuals"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Commands"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "History"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "History"));
        Assert.DoesNotContain(noRoutePropertyName, authoritativeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AdaptiveEndpointAndClearancePolicyRemainInsideBpmnRouting()
    {
        var policyType = Assert.Single(typeof(BpmnRoutingAlgorithm).Assembly.GetTypes(), type =>
            type.FullName == "Inceptus.DocumentEngine.Bpmn.Routing.BpmnRoutingPolicy");
        Assert.False(policyType.IsPublic);
        Assert.Equal(typeof(BpmnRoutingAlgorithm).Assembly, policyType.Assembly);

        string[] adaptivePolicyMembers =
        [
            "PreferredObstacleClearance",
            "PreferredEndpointLeadDistance",
            "ObstacleClearanceAttempts",
            "EndpointLeadDistanceAttempts",
        ];
        const BindingFlags policyFlags = BindingFlags.NonPublic | BindingFlags.Static;
        Assert.All(adaptivePolicyMembers, memberName => Assert.Contains(
            policyType.GetMembers(policyFlags),
            member => member.Name == memberName));

        var bpmnRouter = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Routing",
            "BpmnOrthogonalRouter.cs");
        Assert.Contains(
            "BpmnRoutingPolicy.ObstacleClearanceAttempts",
            bpmnRouter,
            StringComparison.Ordinal);
        Assert.Contains(
            "BpmnRoutingPolicy.EndpointLeadDistanceAttempts",
            bpmnRouter,
            StringComparison.Ordinal);

        var presentationBoundary = string.Concat(
            ReadProductionDirectory("Inceptus.DocumentEngine.Canvas2D", "Scene"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Canvas2D", "Rendering"),
            File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "src",
                "Inceptus.DocumentEngine.Canvas2D",
                "wwwroot",
                "inceptus.canvas2d.js")));
        Assert.DoesNotContain("BpmnRoutingPolicy", presentationBoundary, StringComparison.Ordinal);
        Assert.All(adaptivePolicyMembers, memberName => Assert.DoesNotContain(
            memberName,
            presentationBoundary,
            StringComparison.Ordinal));
    }

    [Fact]
    public void NoRouteOutcomeKeepsRoutingTruthAndProducesDerivedHittableFallback()
    {
        var inputs = Inceptus.DocumentEngine.UnitTests.Canvas2D.Canvas2DSceneTestData.Create();
        var edge = Assert.Single(inputs.Graph.Edges);
        var connectorVisual = Assert.Single(inputs.VisualModel.VisualStates, visual =>
            visual.Id == edge.Source.VisualStateId);
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            new RoutingComputation(noRouteEdgeIds: [edge.Id]));

        var build = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(build.Succeeded, string.Join(
            Environment.NewLine,
            build.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Empty(routing.Routes);
        Assert.Equal(edge.Id, Assert.Single(routing.NoRouteEdgeIds));
        Assert.Empty(connectorVisual.Route);
        var scene = Assert.IsType<Canvas2DScene>(build.Scene);
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var fallback = Assert.Single(scene.Items, item => item.Id == connectorId);
        PointD[] expectedPath = [new PointD(110d, 45d), new PointD(210d, 45d)];
        Assert.Equal(edge.Source.SemanticElementId, fallback.Origin.SemanticElementId);
        Assert.Equal(edge.Source.VisualStateId, fallback.Origin.VisualStateId);
        Assert.Equal(edge.Id, fallback.Origin.ProjectedObjectId);
        Assert.Equal(
            expectedPath,
            Canvas2DConnectorPathMetadata.Resolve(fallback)
                .Select(fallback.Transform.TransformPoint));
        var arrow = Assert.Single(scene.Items, item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector-target-arrow"));
        Assert.Equal(
            expectedPath[^1],
            arrow.Transform.TransformPoint(arrow.Geometry.Points[0]));
        Assert.Equal(
            connectorId,
            new Canvas2DSceneHitTestService().HitTest(
                scene,
                new PointD(160d, 45d))?.SceneObjectId);
        Assert.Equal(connectorVisual, Assert.Single(inputs.VisualModel.VisualStates, visual =>
            visual.Id == edge.Source.VisualStateId));
    }

    private static bool IsAuthoritativeNamespace(string? namespaceName) =>
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.Documents",
            StringComparison.Ordinal) == true ||
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.Visuals",
            StringComparison.Ordinal) == true ||
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.Commands",
            StringComparison.Ordinal) == true ||
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.History",
            StringComparison.Ordinal) == true;

    private static string ReadProductionDirectory(string project, string directory) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project, directory),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                        "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
