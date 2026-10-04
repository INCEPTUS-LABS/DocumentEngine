using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.TestSupport;

internal static class BpmnModelerTestComposition
{
    internal static IDocumentCanvasCompositionFactory DemoFactory { get; } =
        new FreshDemoTestCompositionFactory();

    internal static IDocumentCanvasCompositionFactory NeutralFactory { get; } =
        CreateNeutralFactory();

    internal static ValueTask<DocumentCanvasComposition> CreateDemoAsync(
        CancellationToken cancellationToken = default) =>
        DemoFactory.CreateAsync(cancellationToken);

    internal static async ValueTask<DocumentSnapshot> CreateFreshDemoSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var saved = (await BpmnDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(cancellationToken)
            .ConfigureAwait(false)).CaptureSnapshot();
        // These runtime tests use artificial text metrics. Native compatibility tests
        // separately retain and validate the geometry prepared for their renderer.
        return new DocumentSnapshot(saved.SemanticModel,
            new VisualModelSnapshot(saved.DocumentId, saved.Revision, saved.VisualModel.VisualStates,
                saved.VisualModel.ProfileElementPresentations), saved.Metadata, saved.Publication);
    }

    internal static async ValueTask<BpmnModelerCompositionFactory> CreateFreshDemoFactoryAsync(
        CancellationToken cancellationToken = default) =>
        new(initialDocument: await CreateFreshDemoSnapshotAsync(cancellationToken).ConfigureAwait(false));

    internal static IDocumentCanvasCompositionFactory CreateNeutralFactory(
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null) =>
        new NeutralTestCompositionFactory(connectorAnchorPolicyProvider);

    private sealed class FreshDemoTestCompositionFactory : IDocumentCanvasCompositionFactory
    {
        public async ValueTask<DocumentCanvasComposition> CreateAsync(CancellationToken cancellationToken = default)
        {
            var factory = await CreateFreshDemoFactoryAsync(cancellationToken).ConfigureAwait(false);
            return await factory.CreateAsync(cancellationToken).ConfigureAwait(false);
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
