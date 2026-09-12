using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task StartupProviderDefaultCreatesFreshCanonicalEmptyDocumentPerModeler()
    {
        var factory = new BpmnModelerCompositionFactory();

        var first = await factory.CreateAsync();
        var second = await factory.CreateAsync();

        Assert.NotSame(first.Document, second.Document);
        Assert.NotEqual(first.Document.DocumentId, second.Document.DocumentId);
        Assert.StartsWith("document:", first.Document.DocumentId.Value, StringComparison.Ordinal);
        Assert.StartsWith("document:", second.Document.DocumentId.Value, StringComparison.Ordinal);
        Assert.Equal(DocumentRevision.Zero, first.Document.Revision);
        Assert.Equal(DocumentRevision.Zero, second.Document.Revision);
        Assert.Empty(first.Document.SemanticModel.Elements);
        Assert.Empty(first.Document.SemanticModel.Relationships);
        Assert.Empty(first.Document.VisualModel.VisualStates);
        Assert.Empty(second.Document.SemanticModel.Elements);
        Assert.Empty(second.Document.SemanticModel.Relationships);
        Assert.Empty(second.Document.VisualModel.VisualStates);

        var startEventPolicy = first.Configuration.ConnectorAnchorPolicyProvider.Resolve(
            BpmnSemanticTypes.StartEvent);
        Assert.All(
            new[]
            {
                startEventPolicy.Top,
                startEventPolicy.Right,
                startEventPolicy.Bottom,
                startEventPolicy.Left,
            },
            static edge =>
            {
                Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
                Assert.Equal(ConnectorAnchorRoleCapability.Source, edge.AllowedRoles);
            });
    }

    [Fact]
    public async Task StartupProviderReturnedDocumentBecomesTheExactOwnedDocument()
    {
        var document = BpmnModelerComposition.CreateEmptyDocument();
        var provider = new FixedStartupDocumentProvider(document);
        var factory = new BpmnModelerCompositionFactory([provider]);

        var composition = await factory.CreateAsync();

        Assert.Equal(1, provider.InvocationCount);
        Assert.Same(document, provider.LastDocument);
        Assert.Same(document, composition.Document);
    }

    [Fact]
    public async Task DemoStartupProviderReconstructsIndependentDocumentsWithStableSampleIdentity()
    {
        var first = await BpmnDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync();
        var second = await BpmnDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync();

        Assert.NotSame(first, second);
        Assert.Equal(BpmnDemoPipeline.DemoDocumentId, first.DocumentId);
        Assert.Equal(first.DocumentId, second.DocumentId);
        Assert.Equal(new DocumentRevision(103), first.Revision);
        Assert.Equal(first.CaptureSnapshot(), second.CaptureSnapshot());
        Assert.Equal(26, first.SemanticModel.ElementCount);
        Assert.Equal(25, first.SemanticModel.RelationshipCount);
        Assert.Equal(51, first.VisualModel.VisualStates.Length);
        Assert.Contains(first.SemanticModel.Elements,
            static element => element.Id == BpmnDemoPipeline.ProcessOrderSubProcessId);
        Assert.Contains(first.SemanticModel.NestedScopes,
            static scope => scope.Id == BpmnDemoPipeline.ProcessOrderScopeId);
    }

    [Fact]
    public void StartupProviderRegistrationRejectsNullEntries()
    {
        IBpmnModelerStartupDocumentProvider? missing = null;

        var exception = Assert.Throws<ArgumentException>(() =>
            new BpmnModelerCompositionFactory([missing!]));

        Assert.Equal("startupProviders", exception.ParamName);
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("returns-null")]
    [InlineData("unattachable")]
    [InlineData("multiple")]
    public async Task StartupProviderFailureIsBoundedWithoutPartialSessionOrFallback(
        string failureKind)
    {
        var unattachable = CreateDocumentRejectedByBpmnModelerPolicy();
        var first = failureKind switch
        {
            "throws" => (CountingStartupDocumentProvider)new ThrowingStartupDocumentProvider(),
            "returns-null" => new NullStartupDocumentProvider(),
            "unattachable" => new FixedStartupDocumentProvider(unattachable),
            "multiple" => new FixedStartupDocumentProvider(
                BpmnModelerComposition.CreateEmptyDocument()),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind)),
        };
        var providers = failureKind == "multiple"
            ? new IBpmnModelerStartupDocumentProvider[]
            {
                first,
                new FixedStartupDocumentProvider(BpmnModelerComposition.CreateEmptyDocument()),
            }
            : [first];
        var expectedInvocationCount = failureKind == "multiple" ? 0 : 1;
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: new BpmnModelerCompositionFactory(providers));

        await host.InitializeAsync("startup-provider-canvas", "startup-provider-container");

        var state = host.CaptureState();
        Assert.True(state.InitializationAttempted);
        Assert.False(state.IsInitialized);
        Assert.False(state.LatestPresentationSucceeded);
        Assert.Null(state.Session);
        Assert.Equal(0, state.SuccessfulRenderCount);
        var diagnostic = Assert.Single(state.HostDiagnostics);
        Assert.Equal("CANVAS_HOST_INITIALIZATION_FAILED", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "The browser canvas host could not initialize its Editing Session.",
            diagnostic.Message);
        Assert.DoesNotContain(Environment.NewLine, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(typeof(InvalidOperationException).FullName, diagnostic.SourceIdentity);
        Assert.Empty(diagnostic.Context);
        Assert.Equal(expectedInvocationCount, first.InvocationCount);
        Assert.Equal(1, observer.DisposeCount);
        Assert.Equal(1, execution.DisposeCount);
    }

    [Fact]
    public async Task TwoStartupProviderInvocationsCreateIndependentHostSessionsAndDocuments()
    {
        var provider = new RecordingDemoStartupDocumentProvider();
        var factory = new BpmnModelerCompositionFactory([provider]);
        var firstExecution = new RecordingRenderExecution();
        var secondExecution = new RecordingRenderExecution();
        var firstObserver = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(960d, 640d, 1d));
        var secondObserver = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(960d, 640d, 1d));
        await using var firstHost = CreateHost(
            firstExecution,
            firstObserver,
            compositionFactory: factory);
        await using var secondHost = CreateHost(
            secondExecution,
            secondObserver,
            compositionFactory: factory);

        await Task.WhenAll(
            firstHost.InitializeAsync("first-canvas", "first-container").AsTask(),
            secondHost.InitializeAsync("second-canvas", "second-container").AsTask());

        var firstSession = Session(firstHost);
        var secondSession = Session(secondHost);
        var firstDocument = AttachedDocument(firstSession);
        var secondDocument = AttachedDocument(secondSession);
        var providerDocuments = provider.Documents.ToArray();
        Assert.Equal(2, provider.InvocationCount);
        Assert.Equal(2, providerDocuments.Length);
        Assert.NotSame(providerDocuments[0], providerDocuments[1]);
        Assert.Contains(providerDocuments, document => ReferenceEquals(document, firstDocument));
        Assert.Contains(providerDocuments, document => ReferenceEquals(document, secondDocument));
        Assert.NotSame(firstSession, secondSession);
        Assert.NotSame(firstDocument, secondDocument);
        Assert.Equal(firstDocument.CaptureSnapshot(), secondDocument.CaptureSnapshot());

        var secondStateBefore = secondSession.CaptureState();
        var secondSnapshotBefore = secondDocument.CaptureSnapshot();
        var secondRenderCallsBefore = secondExecution.Calls.Count;
        var firstTransientState = new EditorStateSnapshot(
            selection: [BpmnDemoPipeline.TaskVisualId],
            viewport: new ViewportSnapshot(1.45d, new VectorD(37d, -19d)));
        Assert.True((await firstSession.UpdateEditorStateAsync(firstTransientState)).Succeeded);
        await firstSession.WaitForIdleAsync();

        Assert.Equal(firstTransientState.Selection, firstSession.CaptureState().EditorState.Selection);
        Assert.Equal(firstTransientState.Viewport, firstSession.CaptureState().EditorState.Viewport);
        Assert.Equal(secondStateBefore.EditorState, secondSession.CaptureState().EditorState);
        Assert.Equal(secondRenderCallsBefore, secondExecution.Calls.Count);

        var firstVisual = firstDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BpmnDemoPipeline.TaskVisualId);
        var secondVisualBefore = secondDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BpmnDemoPipeline.TaskVisualId);
        var moved = firstVisual.Position + new VectorD(23d, 17d);
        var command = await firstSession.ExecuteAsync(new MoveVisualStateCommand(
            firstDocument.DocumentId,
            firstDocument.Revision,
            firstVisual.Id,
            moved,
            VisualPlacementMode.Pinned));
        Assert.True(command.IsCommitted);
        await firstSession.WaitForIdleAsync();

        Assert.Equal(new DocumentRevision(104), firstDocument.Revision);
        Assert.Equal(new DocumentRevision(103), secondDocument.Revision);
        Assert.Equal(1, firstSession.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(0, secondSession.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(moved, firstDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BpmnDemoPipeline.TaskVisualId).Position);
        Assert.Equal(secondVisualBefore.Position, secondDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BpmnDemoPipeline.TaskVisualId).Position);
        Assert.Equal(secondSnapshotBefore, secondDocument.CaptureSnapshot());
        Assert.Equal(secondStateBefore.ActiveScopeId, secondSession.CaptureState().ActiveScopeId);
    }

    private static Document CreateDocumentRejectedByBpmnModelerPolicy()
    {
        var documentId = new DocumentId("test:p1.2:unattachable");
        var revision = DocumentRevision.Zero;
        var elementId = new SemanticElementId("test:p1.2:unattachable:start");
        var visualId = new VisualStateId("test:p1.2:unattachable:start:visual");
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                revision,
                [new SemanticElementSnapshot(elementId, BpmnSemanticTypes.StartEvent)]),
            new VisualModelSnapshot(
                documentId,
                revision,
                [
                    new VisualStateSnapshot(
                        visualId,
                        elementId,
                        new PointD(20d, 30d),
                        new SizeD(36d, 36d),
                        VisualPlacementMode.Manual,
                        connectorAnchors:
                        [
                            new ConnectorAnchor(
                                new ConnectorAnchorId("target-on-start"),
                                ConnectorAnchorSide.Right,
                                ConnectorAnchorRole.Target,
                                0),
                        ]),
                ]),
            new DocumentMetadataSnapshot(documentId, revision));
        var generic = DocumentReconstructor.Reconstruct(snapshot);
        Assert.True(generic.Succeeded);
        return Assert.IsType<Document>(generic.Document);
    }

    private abstract class CountingStartupDocumentProvider :
        IBpmnModelerStartupDocumentProvider
    {
        public int InvocationCount { get; protected set; }

        public abstract ValueTask<Document> GetInitialDocumentAsync(
            CancellationToken cancellationToken = default);
    }

    private sealed class FixedStartupDocumentProvider(Document document) :
        CountingStartupDocumentProvider
    {
        internal Document? LastDocument { get; private set; }

        public override ValueTask<Document> GetInitialDocumentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            LastDocument = document;
            return ValueTask.FromResult(document);
        }
    }

    private sealed class ThrowingStartupDocumentProvider : CountingStartupDocumentProvider
    {
        public override ValueTask<Document> GetInitialDocumentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            throw new InvalidOperationException("Sensitive provider failure details.");
        }
    }

    private sealed class NullStartupDocumentProvider : CountingStartupDocumentProvider
    {
        public override ValueTask<Document> GetInitialDocumentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            return ValueTask.FromResult<Document>(null!);
        }
    }

    private sealed class RecordingDemoStartupDocumentProvider :
        IBpmnModelerStartupDocumentProvider
    {
        private int _invocationCount;

        internal int InvocationCount => Volatile.Read(ref _invocationCount);

        internal ConcurrentQueue<Document> Documents { get; } = new();

        public async ValueTask<Document> GetInitialDocumentAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invocationCount);
            var document = await BpmnDemoStartupDocumentProvider.Instance
                .GetInitialDocumentAsync(cancellationToken);
            Documents.Enqueue(document);
            return document;
        }
    }
}
