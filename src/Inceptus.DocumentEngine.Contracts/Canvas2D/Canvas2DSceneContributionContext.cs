using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Provides one contributor with the five immutable scene-model inputs and declared configuration.
/// </summary>
public sealed class Canvas2DSceneContributionContext
{
    public Canvas2DSceneContributionContext(
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        Canvas2DSceneConfiguration configuration,
        Canvas2DSceneContributorDescriptor contributor,
        Canvas2DScenePresentationContext? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(projectedGraph);
        ArgumentNullException.ThrowIfNull(layoutResult);
        ArgumentNullException.ThrowIfNull(routingResult);
        ArgumentNullException.ThrowIfNull(visualModel);
        ArgumentNullException.ThrowIfNull(editorState);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(contributor);

        ProjectedGraph = projectedGraph;
        LayoutResult = layoutResult;
        RoutingResult = routingResult;
        VisualModel = visualModel;
        EditorState = editorState;
        Configuration = configuration;
        Contributor = contributor;
        Presentation = presentation;
    }

    public ProjectedGraph ProjectedGraph { get; }

    public LayoutResult LayoutResult { get; }

    public RoutingResult RoutingResult { get; }

    public VisualModelSnapshot VisualModel { get; }

    public EditorStateSnapshot EditorState { get; }

    public Canvas2DSceneConfiguration Configuration { get; }

    public Canvas2DSceneContributorDescriptor Contributor { get; }

    /// <summary>
    /// Gets optional read-only profile presentation context. Compatibility builds that do not
    /// supply a Document keep this value null.
    /// </summary>
    public Canvas2DScenePresentationContext? Presentation { get; }
}
