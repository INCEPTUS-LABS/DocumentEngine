using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN2SequenceFlowConnectionCreationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n2:document");
    private static readonly DocumentRevision Revision = new(7);
    private static readonly SemanticElementId SourceId = new("bpmn:n2:source");
    private static readonly SemanticElementId TargetId = new("bpmn:n2:target");
    private static readonly VisualStateId SourceVisualId = new("bpmn:n2:source:visual");
    private static readonly VisualStateId TargetVisualId = new("bpmn:n2:target:visual");
    private static readonly ConnectorAnchorId SourceAnchorId =
        new("bpmn:n2:source:anchor");
    private static readonly ConnectorAnchorId SourceAlternateAnchorId =
        new("bpmn:n2:source:alternate-anchor");
    private static readonly ConnectorAnchorId SourceTargetAnchorId =
        new("bpmn:n2:source:target-anchor");
    private static readonly ConnectorAnchorId TargetAnchorId =
        new("bpmn:n2:target:anchor");
    private static readonly ConnectorAnchorId TargetAlternateAnchorId =
        new("bpmn:n2:target:alternate-anchor");
    private static readonly ConnectorAnchorId TargetSourceAnchorId =
        new("bpmn:n2:target:source-anchor");

    [Fact]
    public void N2ComposesN1AndAddsOnlyOneSequenceFlowConnectionRegistration()
    {
        var n1 = BpmnPluginRegistration.N1;
        var n2 = BpmnPluginRegistration.N2;

        Assert.Equal(n1.CommandHandlers.AsEnumerable(), n2.CommandHandlers.AsEnumerable());
        Assert.Equal(n1.CommandValidators.AsEnumerable(), n2.CommandValidators.AsEnumerable());
        Assert.Equal(n1.HistoryPolicies.AsEnumerable(), n2.HistoryPolicies.AsEnumerable());
        Assert.Equal(n1.ProjectionRules.AsEnumerable(), n2.ProjectionRules.AsEnumerable());
        Assert.Equal(n1.LayoutAlgorithms.AsEnumerable(), n2.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(n1.RoutingAlgorithms.AsEnumerable(), n2.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(n1.SceneContributors.AsEnumerable(), n2.SceneContributors.AsEnumerable());
        Assert.Equal(n1.ToolboxContributions.AsEnumerable(), n2.ToolboxContributions.AsEnumerable());
        Assert.Equal(
            n1.ToolboxPlacementRegistrations.AsEnumerable(),
            n2.ToolboxPlacementRegistrations.AsEnumerable());
        Assert.Equal(n1.PropertiesSchemas.AsEnumerable(), n2.PropertiesSchemas.AsEnumerable());
        Assert.Equal(
            n1.ConnectorAnchorPolicies.AsEnumerable(),
            n2.ConnectorAnchorPolicies.AsEnumerable());
        Assert.Empty(n1.AnchorConnectionCreationRegistrations);

        var registration = Assert.Single(n2.AnchorConnectionCreationRegistrations);
        Assert.Equal(
            "bpmn:connection-creation/sequence-flow",
            registration.CreationId.Value);
        Assert.Equal(
            "BpmnSequenceFlowConnectionCreationCommandFactory",
            registration.CommandFactory.GetType().Name);
    }

    [Fact]
    public void FactoryMatchesOnlyAnExistingSourceRoleAnchorOwnedByTheSourceNode()
    {
        var document = CreateDocument();
        var factory = Factory();

        Assert.True(factory.CanStart(SourceRequest(document, SourceAnchorId)));
        Assert.False(factory.CanStart(SourceRequest(document, SourceTargetAnchorId)));
        Assert.False(factory.CanStart(new AnchorConnectionCreationSourceRequest(
            document,
            Revision,
            SourceId,
            TargetVisualId,
            SourceAnchorId)));

        var documentWithExistingFlow = CreateDocument(includeExistingFlow: true);
        Assert.False(factory.CanStart(SourceRequest(
            documentWithExistingFlow,
            SourceAnchorId)));
        Assert.True(factory.CanStart(SourceRequest(
            documentWithExistingFlow,
            SourceAlternateAnchorId)));
    }

    [Fact]
    public void FactoryProducesTheExactExistingSequenceFlowCommandWithoutPersistentWork()
    {
        var document = CreateDocument();
        var relationshipId = new SemanticElementId("bpmn:n2:flow");
        var connectorVisualId = new VisualStateId("bpmn:n2:flow:visual");
        var identities = new RecordingIdentityProvider(
            new DocumentCreationIdentity(relationshipId, connectorVisualId));

        var result = Factory().CreatePlan(new AnchorConnectionCreationRequest(
            document,
            Revision,
            SourceId,
            SourceVisualId,
            SourceAnchorId,
            TargetId,
            TargetVisualId,
            TargetAnchorId,
            identities));

        Assert.True(result.Succeeded);
        var plan = Assert.IsType<AnchorConnectionCreationPlan>(result.Plan);
        var command = Assert.IsType<CreateBpmnSequenceFlowCommand>(plan.Command);
        Assert.Equal(DocumentId, command.TargetDocumentId);
        Assert.Equal(Revision, command.ExpectedRevision);
        Assert.Equal(relationshipId, command.RelationshipId);
        Assert.Equal(connectorVisualId, command.VisualStateId);
        Assert.Equal(SourceId, command.SourceId);
        Assert.Equal(TargetId, command.TargetId);
        Assert.Equal(SourceAnchorId, command.SourceAnchorId);
        Assert.Equal(TargetAnchorId, command.TargetAnchorId);
        Assert.Empty(command.Route);
        Assert.Null(command.Name);
        Assert.Null(command.Description);
        Assert.Equal(relationshipId, plan.CreatedSemanticRelationshipId);
        Assert.Equal(connectorVisualId, plan.CreatedConnectorVisualStateId);
        Assert.Equal(1, identities.InvocationCount);
        Assert.Equal(0, document.SemanticModel.RelationshipCount);
        Assert.Equal(2, document.VisualModel.Count);
    }

    [Fact]
    public void FactoryReturnsCommandValidationFailureForTheWrongTargetRole()
    {
        var document = CreateDocument();
        var result = Factory().CreatePlan(new AnchorConnectionCreationRequest(
            document,
            Revision,
            SourceId,
            SourceVisualId,
            SourceAnchorId,
            TargetId,
            TargetVisualId,
            TargetSourceAnchorId,
            new RecordingIdentityProvider(new DocumentCreationIdentity(
                new SemanticElementId("bpmn:n2:invalid-flow"),
                new VisualStateId("bpmn:n2:invalid-flow:visual")))));

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid);
        Assert.Equal(0, document.SemanticModel.RelationshipCount);
    }

    [Fact]
    public void FactoryPreservesExistingDistinctAnchorSelfLoopSemantics()
    {
        var document = CreateDocument();
        var selfLoop = Factory().CreatePlan(new AnchorConnectionCreationRequest(
            document,
            Revision,
            SourceId,
            SourceVisualId,
            SourceAnchorId,
            SourceId,
            SourceVisualId,
            SourceTargetAnchorId,
            new RecordingIdentityProvider(new DocumentCreationIdentity(
                new SemanticElementId("bpmn:n2:self-loop"),
                new VisualStateId("bpmn:n2:self-loop:visual")))));

        Assert.True(selfLoop.Succeeded);
        var selfLoopCommand = Assert.IsType<CreateBpmnSequenceFlowCommand>(
            Assert.IsType<AnchorConnectionCreationPlan>(selfLoop.Plan).Command);
        Assert.Equal(SourceId, selfLoopCommand.SourceId);
        Assert.Equal(SourceId, selfLoopCommand.TargetId);
        Assert.NotEqual(selfLoopCommand.SourceAnchorId, selfLoopCommand.TargetAnchorId);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    public void FactoryRejectsAPlanWhenEitherRequestedEndpointAnchorIsOccupied(
        string occupiedEndpoint)
    {
        var document = CreateDocument(includeExistingFlow: true);
        var sourceAnchorId = occupiedEndpoint == "source"
            ? SourceAnchorId
            : SourceAlternateAnchorId;
        var targetAnchorId = occupiedEndpoint == "target"
            ? TargetAnchorId
            : TargetAlternateAnchorId;
        var result = Factory().CreatePlan(new AnchorConnectionCreationRequest(
            document,
            Revision,
            SourceId,
            SourceVisualId,
            sourceAnchorId,
            TargetId,
            TargetVisualId,
            targetAnchorId,
            new RecordingIdentityProvider(new DocumentCreationIdentity(
                new SemanticElementId($"bpmn:n2:occupied-{occupiedEndpoint}:flow"),
                new VisualStateId($"bpmn:n2:occupied-{occupiedEndpoint}:flow:visual")))));

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ConnectorAnchorInUse);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid);
        Assert.Equal(1, document.SemanticModel.RelationshipCount);
        Assert.Equal(3, document.VisualModel.Count);
    }

    private static IAnchorConnectionCreationCommandFactory Factory() =>
        Assert.Single(BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations)
            .CommandFactory;

    private static AnchorConnectionCreationSourceRequest SourceRequest(
        DocumentSnapshot document,
        ConnectorAnchorId anchorId) =>
        new(document, Revision, SourceId, SourceVisualId, anchorId);

    private static DocumentSnapshot CreateDocument(bool includeExistingFlow = false)
    {
        var source = new SemanticElementSnapshot(SourceId, BpmnSemanticTypes.Task);
        var target = new SemanticElementSnapshot(TargetId, BpmnSemanticTypes.Task);
        var relationships = includeExistingFlow
            ? new[]
            {
                new SemanticRelationshipSnapshot(
                    new SemanticElementId("bpmn:n2:existing-flow"),
                    BpmnSemanticTypes.SequenceFlow,
                    SourceId,
                    TargetId),
            }
            : [];
        var visuals = new List<VisualStateSnapshot>
        {
            new(
                SourceVisualId,
                SourceId,
                new PointD(10d, 20d),
                new SizeD(120d, 80d),
                VisualPlacementMode.Pinned,
                connectorAnchors:
                [
                    new ConnectorAnchor(
                        SourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0),
                    new ConnectorAnchor(
                        SourceAlternateAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        1),
                    new ConnectorAnchor(
                        SourceTargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0),
                ]),
            new(
                TargetVisualId,
                TargetId,
                new PointD(250d, 20d),
                new SizeD(120d, 80d),
                VisualPlacementMode.Pinned,
                connectorAnchors:
                [
                    new ConnectorAnchor(
                        TargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0),
                    new ConnectorAnchor(
                        TargetAlternateAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        1),
                    new ConnectorAnchor(
                        TargetSourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0),
                ]),
        };
        if (includeExistingFlow)
        {
            visuals.Add(new VisualStateSnapshot(
                new VisualStateId("bpmn:n2:existing-flow:visual"),
                new SemanticElementId("bpmn:n2:existing-flow"),
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Manual,
                sourceAnchorId: SourceAnchorId,
                targetAnchorId: TargetAnchorId));
        }

        return new DocumentSnapshot(
            new SemanticModelSnapshot(DocumentId, Revision, [source, target], relationships),
            new VisualModelSnapshot(DocumentId, Revision, visuals),
            new DocumentMetadataSnapshot(DocumentId, Revision));
    }

    private sealed class RecordingIdentityProvider : IDocumentCreationIdentityProvider
    {
        private readonly DocumentCreationIdentity _identity;

        internal RecordingIdentityProvider(DocumentCreationIdentity identity) =>
            _identity = identity;

        internal int InvocationCount { get; private set; }

        public DocumentCreationIdentity CreateIdentity()
        {
            InvocationCount++;
            return _identity;
        }
    }
}
