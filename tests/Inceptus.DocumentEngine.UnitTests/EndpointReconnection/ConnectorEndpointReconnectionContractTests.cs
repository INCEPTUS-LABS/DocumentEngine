using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.EndpointReconnection;

public sealed class ConnectorEndpointReconnectionContractTests
{
    [Fact]
    public void ReconnectionIdentityIsAValidatedValueObject()
    {
        var identity = new ConnectorEndpointReconnectionId("test:connector:reconnect");

        Assert.Equal("test:connector:reconnect", identity.Value);
        Assert.Equal(
            identity,
            new ConnectorEndpointReconnectionId("test:connector:reconnect"));
        Assert.Equal(identity.Value, identity.ToString());
        Assert.Throws<ArgumentNullException>(() =>
            new ConnectorEndpointReconnectionId(null!));
        Assert.Throws<ArgumentException>(() =>
            new ConnectorEndpointReconnectionId(" "));
    }

    [Theory]
    [InlineData(ConnectorEndpointKind.Source)]
    [InlineData(ConnectorEndpointKind.Target)]
    public void StartRequestPreservesImmutableCurrentEndpointInputs(
        ConnectorEndpointKind endpointKind)
    {
        var document = Document(new DocumentRevision(7));
        var request = new ConnectorEndpointReconnectionStartRequest(
            document,
            document.Revision,
            SemanticId("relationship"),
            VisualId("connector"),
            endpointKind,
            SemanticId("current"),
            AnchorId("current"));

        Assert.Same(document, request.Document);
        Assert.Equal(document.Revision, request.ExpectedRevision);
        Assert.Equal(SemanticId("relationship"), request.RelationshipId);
        Assert.Equal(VisualId("connector"), request.ConnectorVisualStateId);
        Assert.Equal(endpointKind, request.EndpointKind);
        Assert.Equal(SemanticId("current"), request.CurrentSemanticElementId);
        Assert.Equal(AnchorId("current"), request.CurrentAnchorId);
    }

    [Fact]
    public void StartRequestRejectsMissingInputsUndefinedEndpointAndMismatchedRevision()
    {
        var document = Document(new DocumentRevision(7));
        var relationshipId = SemanticId("relationship");
        var visualStateId = VisualId("connector");
        var semanticElementId = SemanticId("current");
        var anchorId = AnchorId("current");

        Assert.Throws<ArgumentNullException>(() => StartRequest(
            null!, document.Revision, relationshipId, visualStateId,
            ConnectorEndpointKind.Source, semanticElementId, anchorId));
        Assert.Throws<ArgumentNullException>(() => StartRequest(
            document, document.Revision, null!, visualStateId,
            ConnectorEndpointKind.Source, semanticElementId, anchorId));
        Assert.Throws<ArgumentNullException>(() => StartRequest(
            document, document.Revision, relationshipId, null!,
            ConnectorEndpointKind.Source, semanticElementId, anchorId));
        Assert.Throws<ArgumentNullException>(() => StartRequest(
            document, document.Revision, relationshipId, visualStateId,
            ConnectorEndpointKind.Source, null!, anchorId));
        Assert.Throws<ArgumentNullException>(() => StartRequest(
            document, document.Revision, relationshipId, visualStateId,
            ConnectorEndpointKind.Source, semanticElementId, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => StartRequest(
            document, document.Revision, relationshipId, visualStateId,
            (ConnectorEndpointKind)99, semanticElementId, anchorId));
        Assert.Throws<ArgumentException>(() => StartRequest(
            document, document.Revision.Increment(), relationshipId, visualStateId,
            ConnectorEndpointKind.Source, semanticElementId, anchorId));
    }

    [Fact]
    public void CompletionRequestPreservesCurrentAndCandidateEndpointInputs()
    {
        var document = Document(new DocumentRevision(11));
        var request = Request(
            document,
            document.Revision,
            SemanticId("relationship"),
            VisualId("connector"),
            ConnectorEndpointKind.Target,
            SemanticId("current"),
            AnchorId("current"),
            SemanticId("candidate"),
            VisualId("candidate"),
            AnchorId("candidate"));

        Assert.Same(document, request.Document);
        Assert.Equal(document.Revision, request.ExpectedRevision);
        Assert.Equal(SemanticId("relationship"), request.RelationshipId);
        Assert.Equal(VisualId("connector"), request.ConnectorVisualStateId);
        Assert.Equal(ConnectorEndpointKind.Target, request.EndpointKind);
        Assert.Equal(SemanticId("current"), request.CurrentSemanticElementId);
        Assert.Equal(AnchorId("current"), request.CurrentAnchorId);
        Assert.Equal(SemanticId("candidate"), request.CandidateSemanticElementId);
        Assert.Equal(VisualId("candidate"), request.CandidateVisualStateId);
        Assert.Equal(AnchorId("candidate"), request.CandidateAnchorId);
    }

    [Fact]
    public void CompletionRequestRejectsMissingCandidateInputsAndMismatchedRevision()
    {
        var document = Document(new DocumentRevision(11));
        var relationshipId = SemanticId("relationship");
        var connectorId = VisualId("connector");
        var currentSemanticId = SemanticId("current");
        var currentAnchorId = AnchorId("current");
        var candidateSemanticId = SemanticId("candidate");
        var candidateVisualId = VisualId("candidate");
        var candidateAnchorId = AnchorId("candidate");

        Assert.Throws<ArgumentNullException>(() => Request(
            null!, document.Revision, relationshipId, connectorId,
            ConnectorEndpointKind.Source, currentSemanticId, currentAnchorId,
            candidateSemanticId, candidateVisualId, candidateAnchorId));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, null!, connectorId,
            ConnectorEndpointKind.Source, currentSemanticId, currentAnchorId,
            candidateSemanticId, candidateVisualId, candidateAnchorId));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, relationshipId, null!,
            ConnectorEndpointKind.Source, currentSemanticId, currentAnchorId,
            candidateSemanticId, candidateVisualId, candidateAnchorId));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, relationshipId, connectorId,
            ConnectorEndpointKind.Source, null!, currentAnchorId,
            candidateSemanticId, candidateVisualId, candidateAnchorId));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, relationshipId, connectorId,
            ConnectorEndpointKind.Source, currentSemanticId, null!,
            candidateSemanticId, candidateVisualId, candidateAnchorId));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, relationshipId, connectorId,
            ConnectorEndpointKind.Source, currentSemanticId, currentAnchorId,
            null!, candidateVisualId, candidateAnchorId));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, relationshipId, connectorId,
            ConnectorEndpointKind.Source, currentSemanticId, currentAnchorId,
            candidateSemanticId, null!, candidateAnchorId));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, relationshipId, connectorId,
            ConnectorEndpointKind.Source, currentSemanticId, currentAnchorId,
            candidateSemanticId, candidateVisualId, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(
            document, document.Revision, relationshipId, connectorId,
            (ConnectorEndpointKind)99, currentSemanticId, currentAnchorId,
            candidateSemanticId, candidateVisualId, candidateAnchorId));
        Assert.Throws<ArgumentException>(() => Request(
            document, document.Revision.Increment(), relationshipId, connectorId,
            ConnectorEndpointKind.Source, currentSemanticId, currentAnchorId,
            candidateSemanticId, candidateVisualId, candidateAnchorId));
    }

    [Fact]
    public void PlanExposesExactlyOneCommandAndTheExistingConnectorIdentities()
    {
        var command = new TestCommand();
        var relationshipId = SemanticId("relationship");
        var visualStateId = VisualId("connector");
        var plan = new ConnectorEndpointReconnectionPlan(
            command,
            relationshipId,
            visualStateId);

        Assert.Same(command, plan.Command);
        Assert.Equal(relationshipId, plan.RelationshipId);
        Assert.Equal(visualStateId, plan.ConnectorVisualStateId);
        Assert.Throws<ArgumentNullException>(() =>
            new ConnectorEndpointReconnectionPlan(null!, relationshipId, visualStateId));
        Assert.Throws<ArgumentNullException>(() =>
            new ConnectorEndpointReconnectionPlan(command, null!, visualStateId));
        Assert.Throws<ArgumentNullException>(() =>
            new ConnectorEndpointReconnectionPlan(command, relationshipId, null!));
    }

    [Fact]
    public void ResultDefensivelyCopiesOrdersAndValidatesSuccessDiagnostics()
    {
        var plan = Plan();
        var diagnostics = new List<Diagnostic>
        {
            Warning("TEST_Z", "Zulu"),
            Warning("TEST_A", "Alpha"),
        };
        var result = ConnectorEndpointReconnectionPlanResult.Success(plan, diagnostics);
        diagnostics.Clear();

        Assert.True(result.Succeeded);
        Assert.Same(plan, result.Plan);
        Assert.Equal(
            ["TEST_A", "TEST_Z"],
            result.Diagnostics.Select(static diagnostic => diagnostic.Code));
        AssertReadOnly(result.Diagnostics);
        Assert.Throws<ArgumentNullException>(() =>
            ConnectorEndpointReconnectionPlanResult.Success(null!));
        Assert.Throws<ArgumentException>(() =>
            ConnectorEndpointReconnectionPlanResult.Success(
                plan,
                [Error("TEST_ERROR", "Failure")]));
        Assert.Throws<ArgumentException>(() =>
            ConnectorEndpointReconnectionPlanResult.Success(plan, [null!]));
    }

    [Fact]
    public void FailureRequiresAnErrorAndNeverExposesPersistentWork()
    {
        var diagnostics = new List<Diagnostic>
        {
            Error("TEST_ERROR", "Endpoint reconnection failed"),
        };
        var result = ConnectorEndpointReconnectionPlanResult.Failure(diagnostics);
        diagnostics.Clear();

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Equal("TEST_ERROR", Assert.Single(result.Diagnostics).Code);
        AssertReadOnly(result.Diagnostics);
        Assert.Throws<ArgumentNullException>(() =>
            ConnectorEndpointReconnectionPlanResult.Failure(null!));
        Assert.Throws<ArgumentException>(() =>
            ConnectorEndpointReconnectionPlanResult.Failure([]));
        Assert.Throws<ArgumentException>(() =>
            ConnectorEndpointReconnectionPlanResult.Failure(
                [Warning("TEST_WARNING", "Retry")]));
        Assert.Throws<ArgumentException>(() =>
            ConnectorEndpointReconnectionPlanResult.Failure([null!]));
    }

    [Fact]
    public void FactoryContractSeparatesPureStartMatchingFromPlanCreation()
    {
        var factory = new TestFactory();
        var document = Document(DocumentRevision.Zero);
        var start = StartRequest(
            document,
            document.Revision,
            SemanticId("relationship"),
            VisualId("connector"),
            ConnectorEndpointKind.Source,
            SemanticId("current"),
            AnchorId("current"));
        var completion = Request(
            document,
            document.Revision,
            start.RelationshipId,
            start.ConnectorVisualStateId,
            start.EndpointKind,
            start.CurrentSemanticElementId,
            start.CurrentAnchorId,
            SemanticId("candidate"),
            VisualId("candidate"),
            AnchorId("candidate"));

        Assert.True(factory.CanStart(start));
        var result = factory.CreatePlan(completion);

        Assert.Same(start, factory.LastStartRequest);
        Assert.Same(completion, factory.LastCompletionRequest);
        Assert.IsType<TestCommand>(result.Plan!.Command);
    }

    private static ConnectorEndpointReconnectionStartRequest StartRequest(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        SemanticElementId currentSemanticElementId,
        ConnectorAnchorId currentAnchorId) =>
        new(
            document,
            expectedRevision,
            relationshipId,
            connectorVisualStateId,
            endpointKind,
            currentSemanticElementId,
            currentAnchorId);

    private static ConnectorEndpointReconnectionRequest Request(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        SemanticElementId currentSemanticElementId,
        ConnectorAnchorId currentAnchorId,
        SemanticElementId candidateSemanticElementId,
        VisualStateId candidateVisualStateId,
        ConnectorAnchorId candidateAnchorId) =>
        new(
            document,
            expectedRevision,
            relationshipId,
            connectorVisualStateId,
            endpointKind,
            currentSemanticElementId,
            currentAnchorId,
            candidateSemanticElementId,
            candidateVisualStateId,
            candidateAnchorId);

    private static ConnectorEndpointReconnectionPlan Plan() =>
        new(new TestCommand(), SemanticId("relationship"), VisualId("connector"));

    private static DocumentSnapshot Document(DocumentRevision revision)
    {
        var documentId = new DocumentId("test:document");
        return new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, revision),
            new VisualModelSnapshot(documentId, revision),
            new DocumentMetadataSnapshot(documentId, revision));
    }

    private static SemanticElementId SemanticId(string suffix) =>
        new($"test:semantic:{suffix}");

    private static VisualStateId VisualId(string suffix) =>
        new($"test:visual:{suffix}");

    private static ConnectorAnchorId AnchorId(string suffix) =>
        new($"test:anchor:{suffix}");

    private static Diagnostic Warning(string code, string message) =>
        new(code, DiagnosticSeverity.Warning, message);

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);

    private static void AssertReadOnly<T>(ImmutableArray<T> values)
    {
        var mutableView = (IList<T>)values;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    private sealed class TestFactory : IConnectorEndpointReconnectionCommandFactory
    {
        internal ConnectorEndpointReconnectionStartRequest? LastStartRequest
        { get; private set; }

        internal ConnectorEndpointReconnectionRequest? LastCompletionRequest
        { get; private set; }

        public bool CanStart(ConnectorEndpointReconnectionStartRequest request)
        {
            LastStartRequest = request;
            return true;
        }

        public ConnectorEndpointReconnectionPlanResult CreatePlan(
            ConnectorEndpointReconnectionRequest request)
        {
            LastCompletionRequest = request;
            return ConnectorEndpointReconnectionPlanResult.Success(
                new ConnectorEndpointReconnectionPlan(
                    new TestCommand(request.Document.DocumentId, request.ExpectedRevision),
                    request.RelationshipId,
                    request.ConnectorVisualStateId));
        }
    }

    private sealed class TestCommand : ICommand
    {
        internal TestCommand()
            : this(new DocumentId("test:document"), DocumentRevision.Zero)
        {
        }

        internal TestCommand(DocumentId documentId, DocumentRevision revision)
        {
            TargetDocumentId = documentId;
            ExpectedRevision = revision;
        }

        public CommandTypeId TypeId { get; } = new("test:command/reconnect-endpoint");

        public DocumentId TargetDocumentId { get; }

        public DocumentRevision ExpectedRevision { get; }

        public CommandCategory Category => CommandCategory.Document;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel;
    }
}
