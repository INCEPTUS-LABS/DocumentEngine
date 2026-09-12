using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Scene;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM33BpmnParallelGatewayArchitectureTests
{
    [Fact]
    public void M33IsAnAdditiveBpmnCompositionOverM323()
    {
        var previous = BpmnPluginRegistration.M323;
        var current = BpmnPluginRegistration.M33;

        Assert.Equal(previous.CommandHandlers.Length + 1, current.CommandHandlers.Length);
        Assert.Equal(previous.CommandValidators.Length + 2, current.CommandValidators.Length);
        Assert.Equal(previous.HistoryPolicies.Length + 1, current.HistoryPolicies.Length);
        Assert.Equal(previous.ProjectionRules.Length + 1, current.ProjectionRules.Length);
        Assert.Equal(previous.PropertiesSchemas.Length + 1, current.PropertiesSchemas.Length);
        Assert.Equal(
            previous.ConnectorAnchorPolicies.Length + 1,
            current.ConnectorAnchorPolicies.Length);
        Assert.Equal(
            previous.LayoutAlgorithms.AsEnumerable(),
            current.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(
            previous.RoutingAlgorithms.AsEnumerable(),
            current.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(
            previous.SceneContributors.AsEnumerable(),
            current.SceneContributors.AsEnumerable());

        Assert.Contains(current.CommandHandlers, registration =>
            registration.TypeId == CreateBpmnParallelGatewayCommand.KnownTypeId);
        Assert.Contains(current.HistoryPolicies, registration =>
            registration.TypeId == CreateBpmnParallelGatewayCommand.KnownTypeId);
        Assert.Single(current.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.ParallelGateway);
        Assert.Single(current.PropertiesSchemas, schema =>
            schema.SemanticTypeId == BpmnSemanticTypes.ParallelGateway);
    }

    [Fact]
    public void M33KeepsHistoricalPolicySnapshotAndVersionsOnlyAnchorsAndToolbox()
    {
        var historical = BpmnPluginRegistration.M321;
        var previous = BpmnPluginRegistration.M323;
        var current = BpmnPluginRegistration.M33;

        Assert.Equal(4, historical.ConnectorAnchorPolicies.Length);
        Assert.Equal(
            historical.ConnectorAnchorPolicies.AsEnumerable(),
            previous.ConnectorAnchorPolicies.AsEnumerable());
        Assert.Equal(5, current.ConnectorAnchorPolicies.Length);
        Assert.Equal(
            historical.ConnectorAnchorPolicies.AsEnumerable(),
            current.ConnectorAnchorPolicies.Take(4));

        var parallelPolicy = Assert.Single(current.ConnectorAnchorPolicies, registration =>
            registration.ElementTypeId == BpmnSemanticTypes.ParallelGateway).Policy;
        Assert.All(
            Enum.GetValues<ConnectorAnchorSide>(),
            side =>
            {
                var edge = parallelPolicy.ForSide(side);
                Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
                Assert.Equal(
                    ConnectorAnchorRoleCapability.SourceOrTarget,
                    edge.AllowedRoles);
            });

        var previousToolbox = Assert.Single(previous.ToolboxContributions);
        var currentToolbox = Assert.Single(current.ToolboxContributions);
        Assert.Equal(4, previousToolbox.Items.Length);
        Assert.Equal(5, currentToolbox.Items.Length);
        Assert.Equal(
            previousToolbox.Items.Select(static item => item.ElementTypeId),
            currentToolbox.Items
                .Where(item => item.ElementTypeId != BpmnSemanticTypes.ParallelGateway)
                .Select(static item => item.ElementTypeId));
        var parallelItem = Assert.Single(currentToolbox.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.ParallelGateway);
        Assert.Equal("Parallel Gateway", parallelItem.DisplayName);
        Assert.Equal(3, parallelItem.Order);
    }

    [Fact]
    public void ParallelGatewayReusesCanonicalAlgorithmsSceneAndAnchorModels()
    {
        var assembly = typeof(BpmnPluginRegistration).Assembly;
        var types = assembly.GetTypes();

        Assert.Equal(
            [typeof(BpmnLayoutAlgorithm)],
            types.Where(type =>
                    typeof(ILayoutAlgorithm).IsAssignableFrom(type) &&
                    type is { IsAbstract: false, IsInterface: false })
                .ToArray());
        Assert.Equal(
            [typeof(BpmnRoutingAlgorithm)],
            types.Where(type =>
                    typeof(IRoutingAlgorithm).IsAssignableFrom(type) &&
                    type is { IsAbstract: false, IsInterface: false })
                .ToArray());
        Assert.Equal(
            [typeof(BpmnCanvas2DSceneContributor)],
            types.Where(type =>
                    typeof(ICanvas2DSceneContributor).IsAssignableFrom(type) &&
                    type is { IsAbstract: false, IsInterface: false })
                .ToArray());

        Assert.Single(BpmnPluginRegistration.M33.LayoutAlgorithms);
        Assert.Single(BpmnPluginRegistration.M33.RoutingAlgorithms);
        Assert.Single(BpmnPluginRegistration.M33.SceneContributors);
        Assert.IsType<BpmnLayoutAlgorithm>(
            BpmnPluginRegistration.M33.LayoutAlgorithms[0].Algorithm);
        Assert.IsType<BpmnRoutingAlgorithm>(
            BpmnPluginRegistration.M33.RoutingAlgorithms[0].Algorithm);
        Assert.IsType<BpmnCanvas2DSceneContributor>(
            BpmnPluginRegistration.M33.SceneContributors[0].Contributor);

        var markerOwners = Directory
            .GetFiles(
                Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Bpmn"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "ParallelGatewayMarker",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            ["BpmnCanvas2DSceneContributor.cs"],
            markerOwners.Select(static path => Path.GetFileName(path)!).ToArray());

        string[] forbiddenTypeNames =
        [
            "BpmnParallelSplitGateway",
            "BpmnParallelJoinGateway",
            "BpmnParallelGatewayLayoutAlgorithm",
            "BpmnParallelGatewayRoutingAlgorithm",
            "BpmnParallelGatewaySceneContributor",
            "BpmnParallelGatewayVisualModel",
            "BpmnParallelGatewayAnchor",
            "BpmnParallelGatewayPort",
            "BpmnParallelGatewayLabelOverride",
        ];
        Assert.All(forbiddenTypeNames, name =>
            Assert.DoesNotContain(types, type =>
                StringComparer.Ordinal.Equals(type.Name, name)));

        Assert.Equal(
            ["ParallelGateway"],
            typeof(BpmnSemanticTypes)
                .GetProperties()
                .Where(property => property.Name.Contains(
                    "Parallel",
                    StringComparison.Ordinal))
                .Select(static property => property.Name)
                .ToArray());
        Assert.Null(typeof(CreateBpmnParallelGatewayCommand).GetProperty("ElementNumber"));
    }

    [Fact]
    public void GenericFrameworkAndPresentationContainNoParallelGatewayKnowledge()
    {
        var genericSource = string.Concat(
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Contracts"), "*.cs"),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Runtime"), "*.cs"),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Canvas2D"), "*.cs"),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Canvas2D"), "*.js"),
            ReadSources(
                Path.Combine("src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Components"),
                "*.razor"),
            ReadSources(
                Path.Combine("src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Presentation"),
                "*.cs"),
            ReadSources(
                Path.Combine("src", "Inceptus.DocumentEngine.Bpmn.Blazor", "wwwroot"),
                "*.js"));

        Assert.DoesNotContain("BPMN.ParallelGateway", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ParallelGateway", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(CreateBpmnParallelGatewayCommand),
            genericSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "parallel-gateway-marker",
            genericSource,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M33HasNoSplitJoinSemanticTypesSimulationOrLaterGatewayRegistration()
    {
        var source = string.Concat(ReadSources(
            Path.Combine("src", "Inceptus.DocumentEngine.Bpmn"),
            "*.cs"));
        string[] forbidden =
        [
            "BPMN.ParallelSplitGateway",
            "BPMN.ParallelJoinGateway",
            "ParallelSplitGateway",
            "ParallelJoinGateway",
            "SynchronizationCounter",
            "BranchExecution",
            "ParallelExecution",
            "JoinWait",
            "SimulationState",
            "ConditionExpression",
            "DefaultFlow",
            "Probability",
            "ComplexGateway",
            "Inceptus.Notation",
        ];

        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase));

        var historical = BpmnPluginRegistration.M33;
        Assert.DoesNotContain(historical.CommandHandlers, registration =>
            registration.TypeId == CreateBpmnInclusiveGatewayCommand.KnownTypeId);
        Assert.DoesNotContain(historical.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.DoesNotContain(historical.PropertiesSchemas, schema =>
            schema.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.DoesNotContain(historical.ConnectorAnchorPolicies, registration =>
            registration.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.DoesNotContain(
            Assert.Single(historical.ToolboxContributions).Items,
            item => item.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
    }

    private static IEnumerable<string> ReadSources(string relativeDirectory, string pattern) =>
        Directory.Exists(Path.Combine(RepositoryRoot, relativeDirectory))
            ? Directory
                .GetFiles(
                    Path.Combine(RepositoryRoot, relativeDirectory),
                    pattern,
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText)
            : [];

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
