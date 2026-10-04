using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests.TestSupport;

internal static class BpmnModelerTestComposition
{
    internal static IDocumentCanvasCompositionFactory DemoFactory { get; } =
        new FreshDemoTestCompositionFactory();

    internal static ValueTask<DocumentCanvasComposition> CreateDemoAsync(
        CancellationToken cancellationToken = default) =>
        DemoFactory.CreateAsync(cancellationToken);

    internal static IDocumentCanvasCompositionFactory NeutralFactory { get; } =
        CreateNeutralFactory();

    internal static IDocumentCanvasCompositionFactory CreateNeutralFactory(
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null) =>
        new NeutralTestCompositionFactory(connectorAnchorPolicyProvider);

    internal static async Task<Document> PrepareFreshDocumentAsync(Document document,
        EditingSessionConfiguration configuration, Canvas2DRenderer renderer)
    {
        var snapshot = document.CaptureSnapshot();
        if (snapshot.VisualModel.RoutingScopes is not null) return document;
        var prepared = await new ConnectorRoutingStatePreparer(configuration, renderer).PrepareAsync(
            new(snapshot, snapshot, [], [], null, false, ConnectorRoutingPreparationPurpose.InitialConstruction),
            CancellationToken.None);
        Assert.True(prepared.Succeeded, string.Join("; ", prepared.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var reconstructed = DocumentReconstructor.Reconstruct(new DocumentSnapshot(snapshot.SemanticModel,
            new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, snapshot.VisualModel.VisualStates,
                snapshot.VisualModel.ProfileElementPresentations, prepared.RoutingScopes), snapshot.Metadata, snapshot.Publication),
            configuration.ConnectorAnchorPolicyProvider);
        Assert.True(reconstructed.Succeeded);
        return reconstructed.Document!;
    }

    internal static DocumentCanvasComposition WithDocument(DocumentCanvasComposition source, Document document) =>
        new(document, source.Configuration, source.PropertiesSchemaCatalog, source.Counters,
            source.ToolboxPlacementCatalog, source.AnchorConnectionCreationCatalog, source.DocumentCreationIdentityProvider,
            source.EndpointReconnectionCatalog, source.DeletionCatalog, source.ModelValidationCatalog,
            source.ScopeNavigationCatalog, source.BackgroundActionCatalog, source.SemanticSceneViewActionCatalog,
            source.SemanticSceneCommandActionCatalog, source.SpatialEditPlanners);

    internal static ConnectorRoutingRecord SavedRoute(DocumentSnapshot document, VisualStateId id) =>
        document.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors)
            .Single(record => record.VisualStateId == id);

    internal static async Task SetRoutingTypeAsync(EditingSession session, VisualStateId id, ConnectorRoutingType type)
    {
        var state = session.CaptureState();
        var result = await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(state.DocumentId, state.DocumentRevision, id, type));
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        await session.WaitForIdleAsync();
    }

    private sealed class FreshDemoTestCompositionFactory : IDocumentCanvasCompositionFactory
    {
        public async ValueTask<DocumentCanvasComposition> CreateAsync(CancellationToken cancellationToken = default)
        {
            var saved = (await BpmnDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(cancellationToken)
                .ConfigureAwait(false)).CaptureSnapshot();
            // Synthetic renderers use their own text metrics. This is fresh in-memory construction;
            // native v2 tests prepare and reopen bytes with the matching renderer contract.
            var fresh = new DocumentSnapshot(saved.SemanticModel,
                new VisualModelSnapshot(saved.DocumentId, saved.Revision, saved.VisualModel.VisualStates,
                    saved.VisualModel.ProfileElementPresentations), saved.Metadata, saved.Publication);
            return await new BpmnModelerCompositionFactory(initialDocument: fresh)
                .CreateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class NeutralTestCompositionFactory(
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider) :
        IDocumentCanvasCompositionFactory
    {
        public ValueTask<DocumentCanvasComposition> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neutral = NeutralDemoPipeline.CreateComposition(
                connectorAnchorPolicyProvider);
            return ValueTask.FromResult(new DocumentCanvasComposition(
                neutral.Document,
                neutral.Configuration,
                NeutralDemoPropertiesSchemas.Catalog,
                new NeutralDemoPipelineCountersAdapter(neutral.Counters)));
        }
    }
}

internal sealed class NeutralDemoPipelineCountersAdapter(
    NeutralDemoPipelineCounters counters) : IDocumentCanvasPipelineCounters
{
    internal NeutralDemoPipelineCounters Counters { get; } = counters;

    public int ProjectionRuleInvocationCount => Counters.ProjectionRuleInvocationCount;

    public int LayoutInvocationCount => Counters.LayoutInvocationCount;

    public int RoutingInvocationCount => Counters.RoutingInvocationCount;

    public int SceneContributionInvocationCount => Counters.SceneContributionInvocationCount;
}
