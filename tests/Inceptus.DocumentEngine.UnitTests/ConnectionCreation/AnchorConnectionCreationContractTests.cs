using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.ConnectionCreation;

public sealed class AnchorConnectionCreationContractTests
{
    [Fact]
    public void CreationIdentityIsAValidatedValueObject()
    {
        var identity = new AnchorConnectionCreationId("test:connection:create");

        Assert.Equal("test:connection:create", identity.Value);
        Assert.Equal(identity, new AnchorConnectionCreationId("test:connection:create"));
        Assert.Equal(identity.Value, identity.ToString());
        Assert.Throws<ArgumentNullException>(() => new AnchorConnectionCreationId(null!));
        Assert.Throws<ArgumentException>(() => new AnchorConnectionCreationId(" "));
    }

    [Fact]
    public void SourceRequestPreservesImmutableSourceAnchorInputs()
    {
        var document = Document(new DocumentRevision(7));
        var request = new AnchorConnectionCreationSourceRequest(
            document,
            document.Revision,
            SemanticId("source"),
            VisualId("source"),
            AnchorId("source"));

        Assert.Same(document, request.Document);
        Assert.Equal(document.Revision, request.ExpectedRevision);
        Assert.Equal(SemanticId("source"), request.SourceSemanticElementId);
        Assert.Equal(VisualId("source"), request.SourceVisualStateId);
        Assert.Equal(AnchorId("source"), request.SourceAnchorId);
    }

    [Fact]
    public void SourceRequestRejectsMissingInputsAndMismatchedRevision()
    {
        var document = Document(new DocumentRevision(7));
        var semanticId = SemanticId("source");
        var visualId = VisualId("source");
        var anchorId = AnchorId("source");

        Assert.Throws<ArgumentNullException>(() => new AnchorConnectionCreationSourceRequest(
            null!, document.Revision, semanticId, visualId, anchorId));
        Assert.Throws<ArgumentNullException>(() => new AnchorConnectionCreationSourceRequest(
            document, document.Revision, null!, visualId, anchorId));
        Assert.Throws<ArgumentNullException>(() => new AnchorConnectionCreationSourceRequest(
            document, document.Revision, semanticId, null!, anchorId));
        Assert.Throws<ArgumentNullException>(() => new AnchorConnectionCreationSourceRequest(
            document, document.Revision, semanticId, visualId, null!));
        Assert.Throws<ArgumentException>(() => new AnchorConnectionCreationSourceRequest(
            document,
            document.Revision.Increment(),
            semanticId,
            visualId,
            anchorId));
    }

    [Fact]
    public void CompletionRequestPreservesImmutableEndpointsAndSharedIdentityProvider()
    {
        var document = Document(new DocumentRevision(11));
        var identityProvider = new TestIdentityProvider();
        var request = new AnchorConnectionCreationRequest(
            document,
            document.Revision,
            SemanticId("source"),
            VisualId("source"),
            AnchorId("source"),
            SemanticId("target"),
            VisualId("target"),
            AnchorId("target"),
            identityProvider);

        Assert.Same(document, request.Document);
        Assert.Equal(document.Revision, request.ExpectedRevision);
        Assert.Equal(SemanticId("source"), request.SourceSemanticElementId);
        Assert.Equal(VisualId("source"), request.SourceVisualStateId);
        Assert.Equal(AnchorId("source"), request.SourceAnchorId);
        Assert.Equal(SemanticId("target"), request.TargetSemanticElementId);
        Assert.Equal(VisualId("target"), request.TargetVisualStateId);
        Assert.Equal(AnchorId("target"), request.TargetAnchorId);
        Assert.Same(identityProvider, request.IdentityProvider);
        Assert.Same(identityProvider.Identity, request.IdentityProvider.CreateIdentity());
    }

    [Fact]
    public void CompletionRequestRejectsMissingInputsAndMismatchedRevision()
    {
        var document = Document(new DocumentRevision(11));
        var sourceSemanticId = SemanticId("source");
        var sourceVisualId = VisualId("source");
        var sourceAnchorId = AnchorId("source");
        var targetSemanticId = SemanticId("target");
        var targetVisualId = VisualId("target");
        var targetAnchorId = AnchorId("target");
        var provider = new TestIdentityProvider();

        Assert.Throws<ArgumentNullException>(() => Request(
            null!, document.Revision, sourceSemanticId, sourceVisualId, sourceAnchorId,
            targetSemanticId, targetVisualId, targetAnchorId, provider));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, null!, sourceVisualId, sourceAnchorId,
            targetSemanticId, targetVisualId, targetAnchorId, provider));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, sourceSemanticId, null!, sourceAnchorId,
            targetSemanticId, targetVisualId, targetAnchorId, provider));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, sourceSemanticId, sourceVisualId, null!,
            targetSemanticId, targetVisualId, targetAnchorId, provider));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, sourceSemanticId, sourceVisualId, sourceAnchorId,
            null!, targetVisualId, targetAnchorId, provider));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, sourceSemanticId, sourceVisualId, sourceAnchorId,
            targetSemanticId, null!, targetAnchorId, provider));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, sourceSemanticId, sourceVisualId, sourceAnchorId,
            targetSemanticId, targetVisualId, null!, provider));
        Assert.Throws<ArgumentNullException>(() => Request(
            document, document.Revision, sourceSemanticId, sourceVisualId, sourceAnchorId,
            targetSemanticId, targetVisualId, targetAnchorId, null!));
        Assert.Throws<ArgumentException>(() => Request(
            document, document.Revision.Increment(), sourceSemanticId, sourceVisualId,
            sourceAnchorId, targetSemanticId, targetVisualId, targetAnchorId, provider));
    }

    [Fact]
    public void PlanExposesExactlyOneCommandAndBothCreatedIdentities()
    {
        var command = new TestCommand();
        var semanticId = SemanticId("relationship");
        var visualId = VisualId("connector");
        var plan = new AnchorConnectionCreationPlan(command, semanticId, visualId);

        Assert.Same(command, plan.Command);
        Assert.Equal(semanticId, plan.CreatedSemanticRelationshipId);
        Assert.Equal(visualId, plan.CreatedConnectorVisualStateId);
        Assert.Throws<ArgumentNullException>(() =>
            new AnchorConnectionCreationPlan(null!, semanticId, visualId));
        Assert.Throws<ArgumentNullException>(() =>
            new AnchorConnectionCreationPlan(command, null!, visualId));
        Assert.Throws<ArgumentNullException>(() =>
            new AnchorConnectionCreationPlan(command, semanticId, null!));
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
        var result = AnchorConnectionCreationPlanResult.Success(plan, diagnostics);
        diagnostics.Clear();

        Assert.True(result.Succeeded);
        Assert.Same(plan, result.Plan);
        Assert.Equal(["TEST_A", "TEST_Z"],
            result.Diagnostics.Select(static diagnostic => diagnostic.Code));
        AssertReadOnly(result.Diagnostics);
        Assert.Throws<ArgumentNullException>(() =>
            AnchorConnectionCreationPlanResult.Success(null!));
        Assert.Throws<ArgumentException>(() =>
            AnchorConnectionCreationPlanResult.Success(
                plan,
                [Error("TEST_ERROR", "Failure")]));
        Assert.Throws<ArgumentException>(() =>
            AnchorConnectionCreationPlanResult.Success(plan, [null!]));
    }

    [Fact]
    public void FailureRequiresAnErrorAndNeverExposesPersistentWork()
    {
        var diagnostics = new List<Diagnostic>
        {
            Error("TEST_ERROR", "Connection creation failed"),
        };
        var result = AnchorConnectionCreationPlanResult.Failure(diagnostics);
        diagnostics.Clear();

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Equal("TEST_ERROR", Assert.Single(result.Diagnostics).Code);
        AssertReadOnly(result.Diagnostics);
        Assert.Throws<ArgumentNullException>(() =>
            AnchorConnectionCreationPlanResult.Failure(null!));
        Assert.Throws<ArgumentException>(() =>
            AnchorConnectionCreationPlanResult.Failure([]));
        Assert.Throws<ArgumentException>(() =>
            AnchorConnectionCreationPlanResult.Failure(
                [Warning("TEST_WARNING", "Retry")]));
        Assert.Throws<ArgumentException>(() =>
            AnchorConnectionCreationPlanResult.Failure([null!]));
    }

    [Fact]
    public void FactoryContractSeparatesPureSourceMatchingFromPlanCreation()
    {
        var factory = new TestFactory();
        var document = Document(DocumentRevision.Zero);
        var source = new AnchorConnectionCreationSourceRequest(
            document,
            document.Revision,
            SemanticId("source"),
            VisualId("source"),
            AnchorId("source"));
        var completion = new AnchorConnectionCreationRequest(
            document,
            document.Revision,
            source.SourceSemanticElementId,
            source.SourceVisualStateId,
            source.SourceAnchorId,
            SemanticId("target"),
            VisualId("target"),
            AnchorId("target"),
            new TestIdentityProvider());

        Assert.True(factory.CanStart(source));
        var result = factory.CreatePlan(completion);

        Assert.Same(source, factory.LastSourceRequest);
        Assert.Same(completion, factory.LastCompletionRequest);
        Assert.IsType<TestCommand>(result.Plan!.Command);
    }

    private static AnchorConnectionCreationRequest Request(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId sourceSemanticElementId,
        VisualStateId sourceVisualStateId,
        ConnectorAnchorId sourceAnchorId,
        SemanticElementId targetSemanticElementId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorId targetAnchorId,
        IDocumentCreationIdentityProvider identityProvider) =>
        new(
            document,
            expectedRevision,
            sourceSemanticElementId,
            sourceVisualStateId,
            sourceAnchorId,
            targetSemanticElementId,
            targetVisualStateId,
            targetAnchorId,
            identityProvider);

    private static AnchorConnectionCreationPlan Plan() =>
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

    private sealed class TestIdentityProvider : IDocumentCreationIdentityProvider
    {
        internal DocumentCreationIdentity Identity { get; } = new(
            SemanticId("allocated"),
            VisualId("allocated"));

        public DocumentCreationIdentity CreateIdentity() => Identity;
    }

    private sealed class TestFactory : IAnchorConnectionCreationCommandFactory
    {
        internal AnchorConnectionCreationSourceRequest? LastSourceRequest { get; private set; }

        internal AnchorConnectionCreationRequest? LastCompletionRequest { get; private set; }

        public bool CanStart(AnchorConnectionCreationSourceRequest request)
        {
            LastSourceRequest = request;
            return true;
        }

        public AnchorConnectionCreationPlanResult CreatePlan(
            AnchorConnectionCreationRequest request)
        {
            LastCompletionRequest = request;
            var identity = request.IdentityProvider.CreateIdentity();
            return AnchorConnectionCreationPlanResult.Success(
                new AnchorConnectionCreationPlan(
                    new TestCommand(request.Document.DocumentId, request.ExpectedRevision),
                    identity.SemanticElementId,
                    identity.VisualStateId));
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

        public CommandTypeId TypeId { get; } = new("test:command/create-connection");

        public DocumentId TargetDocumentId { get; }

        public DocumentRevision ExpectedRevision { get; }

        public CommandCategory Category => CommandCategory.Document;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel;
    }
}
