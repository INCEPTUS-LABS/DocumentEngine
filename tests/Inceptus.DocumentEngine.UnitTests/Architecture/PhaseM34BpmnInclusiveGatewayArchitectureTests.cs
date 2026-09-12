using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Scene;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM34BpmnInclusiveGatewayArchitectureTests
{
    [Fact]
    public void M34IsAnAdditiveBpmnCompositionOverM33()
    {
        var previous = BpmnPluginRegistration.M33;
        var current = BpmnPluginRegistration.M34;

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
            registration.TypeId == CreateBpmnInclusiveGatewayCommand.KnownTypeId);
        Assert.Contains(current.HistoryPolicies, registration =>
            registration.TypeId == CreateBpmnInclusiveGatewayCommand.KnownTypeId);
        Assert.Single(current.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.Single(current.PropertiesSchemas, schema =>
            schema.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);
    }

    [Fact]
    public void M34KeepsM33HistoricalSnapshotAndVersionsOnlyAnchorsAndToolbox()
    {
        var historical = BpmnPluginRegistration.M33;
        var current = BpmnPluginRegistration.M34;

        Assert.Equal(5, historical.ConnectorAnchorPolicies.Length);
        Assert.Equal(6, current.ConnectorAnchorPolicies.Length);
        Assert.Equal(
            historical.ConnectorAnchorPolicies.AsEnumerable(),
            current.ConnectorAnchorPolicies.Take(5));
        Assert.DoesNotContain(historical.ConnectorAnchorPolicies, registration =>
            registration.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.DoesNotContain(historical.CommandHandlers, registration =>
            registration.TypeId == CreateBpmnInclusiveGatewayCommand.KnownTypeId);
        Assert.DoesNotContain(historical.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.DoesNotContain(historical.PropertiesSchemas, schema =>
            schema.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);

        var policy = Assert.Single(current.ConnectorAnchorPolicies, registration =>
            registration.ElementTypeId == BpmnSemanticTypes.InclusiveGateway).Policy;
        Assert.All(Enum.GetValues<ConnectorAnchorSide>(), side =>
        {
            var edge = policy.ForSide(side);
            Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
            Assert.Equal(ConnectorAnchorRoleCapability.SourceOrTarget, edge.AllowedRoles);
        });

        var historicalToolbox = Assert.Single(historical.ToolboxContributions);
        var currentToolbox = Assert.Single(current.ToolboxContributions);
        Assert.Equal(5, historicalToolbox.Items.Length);
        Assert.Equal(6, currentToolbox.Items.Length);
        Assert.Equal(
            historicalToolbox.Items.Select(static item => item.ElementTypeId),
            currentToolbox.Items
                .Where(item => item.ElementTypeId != BpmnSemanticTypes.InclusiveGateway)
                .Select(static item => item.ElementTypeId));
        var inclusiveItem = Assert.Single(currentToolbox.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.Equal("Inclusive Gateway", inclusiveItem.DisplayName);
        Assert.Equal(4, inclusiveItem.Order);
    }

    [Fact]
    public void InclusiveGatewayReusesCanonicalAlgorithmsScenePropertiesLabelsAndAnchors()
    {
        var bpmnAssembly = typeof(BpmnPluginRegistration).Assembly;
        var bpmnTypes = bpmnAssembly.GetTypes();

        Assert.Equal(
            [typeof(BpmnLayoutAlgorithm)],
            bpmnTypes.Where(type =>
                    typeof(ILayoutAlgorithm).IsAssignableFrom(type) &&
                    type is { IsAbstract: false, IsInterface: false })
                .ToArray());
        Assert.Equal(
            [typeof(BpmnRoutingAlgorithm)],
            bpmnTypes.Where(type =>
                    typeof(IRoutingAlgorithm).IsAssignableFrom(type) &&
                    type is { IsAbstract: false, IsInterface: false })
                .ToArray());
        Assert.Equal(
            [typeof(BpmnCanvas2DSceneContributor)],
            bpmnTypes.Where(type =>
                    typeof(ICanvas2DSceneContributor).IsAssignableFrom(type) &&
                    type is { IsAbstract: false, IsInterface: false })
                .ToArray());

        Assert.Single(BpmnPluginRegistration.M34.LayoutAlgorithms);
        Assert.Single(BpmnPluginRegistration.M34.RoutingAlgorithms);
        Assert.Single(BpmnPluginRegistration.M34.SceneContributors);
        Assert.IsType<BpmnLayoutAlgorithm>(
            BpmnPluginRegistration.M34.LayoutAlgorithms[0].Algorithm);
        Assert.IsType<BpmnRoutingAlgorithm>(
            BpmnPluginRegistration.M34.RoutingAlgorithms[0].Algorithm);
        Assert.IsType<BpmnCanvas2DSceneContributor>(
            BpmnPluginRegistration.M34.SceneContributors[0].Contributor);

        Assert.NotNull(typeof(ElementPropertiesSchema));
        Assert.NotNull(typeof(NodeLabelPlacement));
        Assert.NotNull(typeof(NodeLabelVisualOverride));
        Assert.NotNull(typeof(ConnectorAnchor));

        var markerOwners = Directory
            .GetFiles(
                Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Bpmn"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "inclusive-gateway-o",
                StringComparison.Ordinal))
            .Select(static path => Path.GetFileName(path)!)
            .ToArray();
        Assert.Equal(["BpmnCanvas2DSceneContributor.cs"], markerOwners);
    }

    [Fact]
    public void InclusiveGatewayHasOneBpmnSemanticIdentityAndNoSpecializedMechanisms()
    {
        var types = typeof(BpmnPluginRegistration).Assembly.GetTypes();
        string[] forbiddenTypeNames =
        [
            "BpmnInclusiveSplitGateway",
            "BpmnInclusiveJoinGateway",
            "InclusiveSplitGateway",
            "InclusiveJoinGateway",
            "BpmnInclusiveGatewayLayoutAlgorithm",
            "BpmnInclusiveGatewayRoutingAlgorithm",
            "BpmnInclusiveGatewaySceneContributor",
            "BpmnInclusiveGatewayVisualModel",
            "BpmnInclusiveGatewayAnchor",
            "BpmnInclusiveGatewayPort",
            "BpmnInclusiveGatewayLabelOverride",
        ];
        Assert.All(forbiddenTypeNames, name =>
            Assert.DoesNotContain(types, type =>
                StringComparer.Ordinal.Equals(type.Name, name)));

        Assert.Equal(
            ["InclusiveGateway"],
            typeof(BpmnSemanticTypes)
                .GetProperties()
                .Where(property => property.Name.Contains(
                    "Inclusive",
                    StringComparison.Ordinal))
                .Select(static property => property.Name)
                .ToArray());
        Assert.Null(typeof(CreateBpmnInclusiveGatewayCommand).GetProperty("ElementNumber"));
    }

    [Fact]
    public void GenericFrameworkAndPresentationContainNoInclusiveGatewayKnowledge()
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

        Assert.DoesNotContain("BPMN.InclusiveGateway", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain("InclusiveGateway", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(CreateBpmnInclusiveGatewayCommand),
            genericSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "inclusive-gateway-o",
            genericSource,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GatewayMarker", genericSource, StringComparison.Ordinal);
    }

    [Fact]
    public void M34AddsNoExecutionConditionsLaterGatewaysTransformationOrSimulationScope()
    {
        var source = string.Concat(ReadSources(
            Path.Combine("src", "Inceptus.DocumentEngine.Bpmn"),
            "*.cs"));
        string[] forbiddenTokens =
        [
            "BPMN.InclusiveSplitGateway",
            "BPMN.InclusiveJoinGateway",
            "InclusiveBranchRuntime",
            "ActiveBranchSet",
            "JoinRuntimeState",
            "SynchronizationCounter",
            "SimulationDecision",
            "ConditionExpression",
            "ConditionLanguage",
            "DefaultSequenceFlow",
            "DefaultBranch",
            "BranchProbability",
            "BranchWeight",
            "ComplexGateway",
            "Inceptus.Notation",
        ];

        Assert.All(forbiddenTokens, token =>
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(BpmnPluginRegistration).Assembly.GetTypes(),
            type => type.Name is
                "InclusiveBranchRuntime" or
                "ActiveBranchSet" or
                "JoinRuntimeState" or
                "SynchronizationCounter" or
                "SimulationDecision");
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
