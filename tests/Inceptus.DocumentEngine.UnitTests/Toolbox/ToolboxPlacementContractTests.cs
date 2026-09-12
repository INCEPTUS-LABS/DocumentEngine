using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Toolbox;

public sealed class ToolboxPlacementContractTests
{
    [Fact]
    public void IdentityRetainsDistinctSemanticAndVisualTechnicalIds()
    {
        var semanticId = new SemanticElementId("test:semantic:new");
        var visualId = new VisualStateId("test:visual:new");
        var identity = new DocumentCreationIdentity(semanticId, visualId);

        Assert.Equal(semanticId, identity.SemanticElementId);
        Assert.Equal(visualId, identity.VisualStateId);
        Assert.Equal(identity, new DocumentCreationIdentity(semanticId, visualId));
        Assert.Equal(
            identity.GetHashCode(),
            new DocumentCreationIdentity(semanticId, visualId).GetHashCode());
        Assert.NotEqual(
            identity,
            new DocumentCreationIdentity(
                new SemanticElementId("test:semantic:other"),
                visualId));
        Assert.Throws<ArgumentNullException>(() =>
            new DocumentCreationIdentity(null!, visualId));
        Assert.Throws<ArgumentNullException>(() =>
            new DocumentCreationIdentity(semanticId, null!));
    }

    [Fact]
    public void RequestPreservesGenericImmutablePlacementInputs()
    {
        var revision = new DocumentRevision(7);
        var document = Document(revision);
        var itemId = new ToolboxItemId("test:toolbox:item");
        var point = new PointD(500d, 300d);
        var identityProvider = new TestIdentityProvider();
        var request = new ToolboxPlacementRequest(
            itemId,
            document,
            revision,
            point,
            identityProvider);

        Assert.Equal(itemId, request.ToolboxItemId);
        Assert.Same(document, request.Document);
        Assert.Equal(revision, request.ExpectedRevision);
        Assert.Equal(point, request.DocumentPoint);
        Assert.Same(identityProvider, request.IdentityProvider);
        Assert.Equal(document.SemanticModel.RootScopeId, request.TargetScopeId);
        Assert.Empty(request.VisibleTargets);
        Assert.Null(request.Candidate);
        Assert.Equal(identityProvider.Identity, request.IdentityProvider.CreateIdentity());
    }

    [Fact]
    public void RequestDefensivelyOrdersTargetsAndCarriesOnlyAVisibleCandidate()
    {
        var document = Document(new DocumentRevision(7));
        var zulu = Target("zulu", new RectD(40d, 50d, 60d, 70d));
        var alpha = Target("alpha", new RectD(10d, 20d, 30d, 40d));
        var source = new List<ToolboxPlacementTarget> { zulu, alpha };
        var candidate = new ToolboxPlacementCandidate(
            zulu,
            "test:placement-preview",
            new RectD(95d, 75d, 20d, 20d),
            [new("test:side", PropertyValue.FromText("right"))]);

        var request = new ToolboxPlacementRequest(
            new ToolboxItemId("test:toolbox:item"),
            document,
            document.Revision,
            new PointD(100d, 80d),
            new TestIdentityProvider(),
            visibleTargets: source,
            candidate: candidate);
        source.Clear();

        Assert.Equal(
            [alpha.VisualStateId, zulu.VisualStateId],
            request.VisibleTargets.Select(static target => target.VisualStateId));
        AssertReadOnly(request.VisibleTargets);
        Assert.Same(candidate, request.Candidate);
        Assert.Same(zulu, candidate.Target);
        Assert.Equal("test:placement-preview", candidate.FeedbackKind);
        Assert.Equal(new RectD(95d, 75d, 20d, 20d), candidate.PreviewBounds);
        Assert.Equal("right", candidate.Properties["test:side"].TextValue);
        Assert.Equal(
            EditorFeedbackPresentationMode.Default,
            candidate.FeedbackPresentationMode);
    }

    [Fact]
    public void CandidateCarriesValidatedContributorOnlyFeedbackPresentation()
    {
        var target = Target("alpha", new RectD(10d, 20d, 30d, 40d));
        var candidate = new ToolboxPlacementCandidate(
            target,
            "test:placement-preview",
            target.Bounds,
            feedbackPresentationMode: EditorFeedbackPresentationMode.ContributorOnly);

        Assert.Equal(
            EditorFeedbackPresentationMode.ContributorOnly,
            candidate.FeedbackPresentationMode);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolboxPlacementCandidate(
            target,
            "test:placement-preview",
            target.Bounds,
            feedbackPresentationMode: (EditorFeedbackPresentationMode)int.MaxValue));
    }

    [Fact]
    public void RequestRejectsNullDuplicateAndNonVisibleCandidateTargets()
    {
        var document = Document(new DocumentRevision(7));
        var alpha = Target("alpha", new RectD(10d, 20d, 30d, 40d));
        var duplicateVisual = new ToolboxPlacementTarget(
            new SemanticElementId("test:semantic:duplicate"),
            new SemanticTypeId("test:type:duplicate"),
            alpha.VisualStateId,
            new ProjectedObjectId("test:projected:duplicate"),
            new RectD(50d, 60d, 30d, 40d));
        var missing = Target("missing", new RectD(50d, 60d, 30d, 40d));
        var itemId = new ToolboxItemId("test:toolbox:item");
        var identityProvider = new TestIdentityProvider();

        Assert.Throws<ArgumentException>(() => new ToolboxPlacementRequest(
            itemId,
            document,
            document.Revision,
            default,
            identityProvider,
            visibleTargets: [null!]));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementRequest(
            itemId,
            document,
            document.Revision,
            default,
            identityProvider,
            visibleTargets: [alpha, duplicateVisual]));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementRequest(
            itemId,
            document,
            document.Revision,
            default,
            identityProvider,
            visibleTargets: [alpha],
            candidate: new ToolboxPlacementCandidate(
                missing,
                "test:placement-preview",
                missing.Bounds)));
    }

    [Fact]
    public void PlacementTargetAndCandidateRejectMissingOrEmptyGeometryInputs()
    {
        var target = Target("alpha", new RectD(10d, 20d, 30d, 40d));

        Assert.Throws<ArgumentNullException>(() => new ToolboxPlacementTarget(
            null!,
            target.SemanticTypeId,
            target.VisualStateId,
            target.ProjectedObjectId,
            target.Bounds));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementTarget(
            target.SemanticElementId,
            target.SemanticTypeId,
            target.VisualStateId,
            target.ProjectedObjectId,
            default));
        Assert.Throws<ArgumentNullException>(() => new ToolboxPlacementCandidate(
            null!,
            "test:placement-preview",
            target.Bounds));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementCandidate(
            target,
            " ",
            target.Bounds));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementCandidate(
            target,
            "test:placement-preview",
            default));
    }

    [Fact]
    public void RequestRejectsMissingInputsAndMismatchedDocumentRevision()
    {
        var document = Document(new DocumentRevision(7));
        var itemId = new ToolboxItemId("test:toolbox:item");
        var point = new PointD(500d, 300d);
        var identityProvider = new TestIdentityProvider();

        Assert.Throws<ArgumentNullException>(() => new ToolboxPlacementRequest(
            null!, document, document.Revision, point, identityProvider));
        Assert.Throws<ArgumentNullException>(() => new ToolboxPlacementRequest(
            itemId, null!, document.Revision, point, identityProvider));
        Assert.Throws<ArgumentNullException>(() => new ToolboxPlacementRequest(
            itemId, document, document.Revision, point, null!));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementRequest(
            itemId,
            document,
            document.Revision.Increment(),
            point,
            identityProvider));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementRequest(
            itemId,
            document,
            document.Revision,
            point,
            identityProvider,
            new DocumentScopeId("test:scope:missing")));
    }

    [Fact]
    public void PlanExposesExactlyOneCommandAndBothCreatedIdentities()
    {
        var command = new TestCommand();
        var semanticId = new SemanticElementId("test:semantic:new");
        var visualId = new VisualStateId("test:visual:new");
        var plan = new ToolboxPlacementPlan(command, semanticId, visualId);

        Assert.Same(command, plan.Command);
        Assert.Equal(semanticId, plan.CreatedSemanticElementId);
        Assert.Equal(visualId, plan.CreatedVisualStateId);
        Assert.Throws<ArgumentNullException>(() =>
            new ToolboxPlacementPlan(null!, semanticId, visualId));
        Assert.Throws<ArgumentNullException>(() =>
            new ToolboxPlacementPlan(command, null!, visualId));
        Assert.Throws<ArgumentNullException>(() =>
            new ToolboxPlacementPlan(command, semanticId, null!));
    }

    [Fact]
    public void PlanResultDefensivelyCopiesOrdersAndValidatesSuccessDiagnostics()
    {
        var plan = Plan();
        var diagnostics = new List<Diagnostic>
        {
            Warning("TEST_Z", "Zulu"),
            Warning("TEST_A", "Alpha"),
        };
        var result = ToolboxPlacementPlanResult.Success(plan, diagnostics);
        diagnostics.Clear();

        Assert.True(result.Succeeded);
        Assert.Same(plan, result.Plan);
        Assert.Equal(["TEST_A", "TEST_Z"], result.Diagnostics.Select(static item => item.Code));
        AssertReadOnly(result.Diagnostics);
        Assert.Throws<ArgumentNullException>(() =>
            ToolboxPlacementPlanResult.Success(null!));
        Assert.Throws<ArgumentException>(() => ToolboxPlacementPlanResult.Success(
            plan,
            [Error("TEST_ERROR", "Failure")]));
        Assert.Throws<ArgumentException>(() => ToolboxPlacementPlanResult.Success(
            plan,
            [null!]));
    }

    [Fact]
    public void FailedPlanRequiresAnErrorAndNeverExposesPersistentWork()
    {
        var diagnostics = new List<Diagnostic>
        {
            Error("TEST_ERROR", "Placement failed"),
        };
        var result = ToolboxPlacementPlanResult.Failure(diagnostics);
        diagnostics.Clear();

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Equal("TEST_ERROR", Assert.Single(result.Diagnostics).Code);
        AssertReadOnly(result.Diagnostics);
        Assert.Throws<ArgumentNullException>(() =>
            ToolboxPlacementPlanResult.Failure(null!));
        Assert.Throws<ArgumentException>(() =>
            ToolboxPlacementPlanResult.Failure([]));
        Assert.Throws<ArgumentException>(() => ToolboxPlacementPlanResult.Failure(
            [Warning("TEST_WARNING", "Retry")]));
        Assert.Throws<ArgumentException>(() => ToolboxPlacementPlanResult.Failure(
            [null!]));
    }

    [Fact]
    public void FactoryContractReturnsAPlanWithoutExecutionBehavior()
    {
        var factory = new TestFactory();
        var document = Document(DocumentRevision.Zero);
        var request = new ToolboxPlacementRequest(
            new ToolboxItemId("test:toolbox:item"),
            document,
            document.Revision,
            new PointD(10d, 20d),
            new TestIdentityProvider());

        var result = factory.CreatePlan(request);

        Assert.True(result.Succeeded);
        Assert.Same(request, factory.LastRequest);
        Assert.IsType<TestCommand>(result.Plan!.Command);
    }

    private static ToolboxPlacementPlan Plan() =>
        new(
            new TestCommand(),
            new SemanticElementId("test:semantic:new"),
            new VisualStateId("test:visual:new"));

    private static ToolboxPlacementTarget Target(string suffix, RectD bounds) =>
        new(
            new SemanticElementId($"test:semantic:{suffix}"),
            new SemanticTypeId($"test:type:{suffix}"),
            new VisualStateId($"test:visual:{suffix}"),
            new ProjectedObjectId($"test:projected:{suffix}"),
            bounds);

    private static DocumentSnapshot Document(DocumentRevision revision)
    {
        var documentId = new DocumentId("test:document");
        return new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, revision),
            new VisualModelSnapshot(documentId, revision),
            new DocumentMetadataSnapshot(documentId, revision));
    }

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
            new SemanticElementId("test:semantic:allocated"),
            new VisualStateId("test:visual:allocated"));

        public DocumentCreationIdentity CreateIdentity() => Identity;
    }

    private sealed class TestFactory : IToolboxPlacementCommandFactory
    {
        internal ToolboxPlacementRequest? LastRequest { get; private set; }

        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            LastRequest = request;
            var identity = request.IdentityProvider.CreateIdentity();
            return ToolboxPlacementPlanResult.Success(new ToolboxPlacementPlan(
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

        public CommandTypeId TypeId { get; } = new("test:command/create");

        public DocumentId TargetDocumentId { get; }

        public DocumentRevision ExpectedRevision { get; }

        public CommandCategory Category => CommandCategory.Document;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel;
    }
}
