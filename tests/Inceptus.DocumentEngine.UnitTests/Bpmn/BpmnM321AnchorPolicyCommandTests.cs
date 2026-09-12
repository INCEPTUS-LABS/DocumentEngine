using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnM321AnchorPolicyCommandTests
{
    private static readonly DocumentId DocumentId = new("bpmn:m321:policy-document");

    [Fact]
    public async Task StartAndEndPoliciesRejectWrongRolesAndAcceptSemanticRoles()
    {
        var harness = CreateHarness();
        var startId = new SemanticElementId("bpmn:m321:policy:start");
        var endId = new SemanticElementId("bpmn:m321:policy:end");
        var startVisualId = new VisualStateId("bpmn:m321:policy:start:visual");
        var endVisualId = new VisualStateId("bpmn:m321:policy:end:visual");
        await ExecuteRequiredAsync(harness, new CreateBpmnStartEventCommand(
            DocumentId,
            harness.Document.Revision,
            startId,
            startVisualId,
            new PointD(0d, 0d),
            new SizeD(42d, 42d)));
        await ExecuteRequiredAsync(harness, new CreateBpmnEndEventCommand(
            DocumentId,
            harness.Document.Revision,
            endId,
            endVisualId,
            new PointD(300d, 0d),
            new SizeD(42d, 42d)));

        var startWrong = await harness.Processor.ExecuteAsync(
            harness.Document,
            Add(
                harness.Document,
                startVisualId,
                "start-target",
                ConnectorAnchorRole.Target,
                0));
        AssertPolicyFailure(startWrong);
        await ExecuteRequiredAsync(harness, Add(
            harness.Document,
            startVisualId,
            "start-source",
            ConnectorAnchorRole.Source,
            0));

        var endWrong = await harness.Processor.ExecuteAsync(
            harness.Document,
            Add(
                harness.Document,
                endVisualId,
                "end-source",
                ConnectorAnchorRole.Source,
                0));
        AssertPolicyFailure(endWrong);
        await ExecuteRequiredAsync(harness, Add(
            harness.Document,
            endVisualId,
            "end-target",
            ConnectorAnchorRole.Target,
            0));

        Assert.Collection(
            Visual(harness.Document, startVisualId).ConnectorAnchors,
            anchor => Assert.Equal(ConnectorAnchorRole.Source, anchor.Role));
        Assert.Collection(
            Visual(harness.Document, endVisualId).ConnectorAnchors,
            anchor => Assert.Equal(ConnectorAnchorRole.Target, anchor.Role));
    }

    [Fact]
    public async Task TaskPolicyAllowsBothRolesOnOneEdgeWithSharedEvenDistribution()
    {
        var harness = CreateHarness();
        var taskId = new SemanticElementId("bpmn:m321:policy:task");
        var visualId = new VisualStateId("bpmn:m321:policy:task:visual");
        await ExecuteRequiredAsync(harness, new CreateBpmnTaskCommand(
            DocumentId,
            harness.Document.Revision,
            taskId,
            visualId,
            new PointD(20d, 30d),
            new SizeD(120d, 80d),
            "POLICY_TASK",
            "Policy task",
            7));
        await ExecuteRequiredAsync(harness, Add(
            harness.Document,
            visualId,
            "task-source",
            ConnectorAnchorRole.Source,
            0));
        await ExecuteRequiredAsync(harness, Add(
            harness.Document,
            visualId,
            "task-target",
            ConnectorAnchorRole.Target,
            1));

        var anchors = Visual(harness.Document, visualId).ConnectorAnchors;
        Assert.Collection(
            anchors,
            anchor =>
            {
                Assert.Equal(ConnectorAnchorRole.Source, anchor.Role);
                Assert.Equal(0, anchor.Order);
            },
            anchor =>
            {
                Assert.Equal(ConnectorAnchorRole.Target, anchor.Role);
                Assert.Equal(1, anchor.Order);
            });
        Assert.Equal(
            [1d / 3d, 2d / 3d],
            anchors.Select(anchor => ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(
                anchor.Order,
                anchors.Length)));
    }

    [Fact]
    public async Task GatewayPolicyCreatesStableTwoSourceBranchDistribution()
    {
        var harness = CreateHarness();
        var gatewayId = new SemanticElementId("bpmn:m321:policy:gateway");
        var visualId = new VisualStateId("bpmn:m321:policy:gateway:visual");
        await ExecuteRequiredAsync(harness, new CreateBpmnExclusiveGatewayCommand(
            DocumentId,
            harness.Document.Revision,
            gatewayId,
            visualId,
            new PointD(100d, 100d),
            new SizeD(48d, 48d),
            "POLICY_GATEWAY",
            "Policy gateway"));
        var firstId = new ConnectorAnchorId("bpmn:m321:policy:gateway:first");
        var secondId = new ConnectorAnchorId("bpmn:m321:policy:gateway:second");
        await ExecuteRequiredAsync(harness, new AddConnectorAnchorCommand(
            DocumentId,
            harness.Document.Revision,
            visualId,
            firstId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        await ExecuteRequiredAsync(harness, new AddConnectorAnchorCommand(
            DocumentId,
            harness.Document.Revision,
            visualId,
            secondId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            1));

        var anchors = Visual(harness.Document, visualId).ConnectorAnchors;
        Assert.Equal([firstId, secondId], anchors.Select(static anchor => anchor.Id));
        Assert.Equal([0, 1], anchors.Select(static anchor => anchor.Order));
        Assert.All(anchors, anchor => Assert.Equal(ConnectorAnchorRole.Source, anchor.Role));
        Assert.Equal(
            [1d / 3d, 2d / 3d],
            anchors.Select(anchor => ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(
                anchor.Order,
                anchors.Length)));
    }

    private static Harness CreateHarness()
    {
        var registration = BpmnPluginRegistration.M321;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: provider).Document);
        return new Harness(
            document,
            new CommandProcessor(
                registration.CommandHandlers,
                registration.CommandValidators,
                historyPolicies: registration.HistoryPolicies,
                connectorAnchorPolicyProvider: provider));
    }

    private static AddConnectorAnchorCommand Add(
        Document document,
        VisualStateId visualStateId,
        string key,
        ConnectorAnchorRole role,
        int insertionIndex) =>
        new(
            DocumentId,
            document.Revision,
            visualStateId,
            new ConnectorAnchorId($"bpmn:m321:policy:{key}"),
            ConnectorAnchorSide.Right,
            role,
            insertionIndex);

    private static async Task ExecuteRequiredAsync(Harness harness, ICommand command)
    {
        var result = await harness.Processor.ExecuteAsync(harness.Document, command);
        Assert.True(
            result.IsCommitted,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    private static VisualStateSnapshot Visual(Document document, VisualStateId visualStateId)
    {
        Assert.True(document.VisualModel.TryGetVisualState(visualStateId, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static void AssertPolicyFailure(CommandExecutionResult result)
    {
        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ConnectorAnchorPolicyViolation);
    }

    private sealed record Harness(Document Document, CommandProcessor Processor);
}
