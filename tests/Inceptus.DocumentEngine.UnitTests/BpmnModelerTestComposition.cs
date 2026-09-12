using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.TestSupport;

internal static class BpmnModelerTestComposition
{
    internal static IDocumentCanvasCompositionFactory DemoFactory { get; } =
        new BpmnModelerCompositionFactory([BpmnDemoStartupDocumentProvider.Instance]);

    internal static IDocumentCanvasCompositionFactory NeutralFactory { get; } =
        CreateNeutralFactory();

    internal static ValueTask<DocumentCanvasComposition> CreateDemoAsync(
        CancellationToken cancellationToken = default) =>
        DemoFactory.CreateAsync(cancellationToken);

    internal static IDocumentCanvasCompositionFactory CreateNeutralFactory(
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null) =>
        new NeutralTestCompositionFactory(connectorAnchorPolicyProvider);

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
