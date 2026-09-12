using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

/// <summary>
/// Immutable composition for one Editing Session. The concrete engines remain framework-owned.
/// </summary>
public sealed class EditingSessionConfiguration
{
    public EditingSessionConfiguration(
        ProjectionEngine projectionEngine,
        LayoutEngine layoutEngine,
        AlgorithmId layoutAlgorithmId,
        RoutingEngine routingEngine,
        AlgorithmId routingAlgorithmId,
        Canvas2DSceneBuilder sceneBuilder,
        ProjectionContext? projectionContext = null,
        LayoutContext? layoutContext = null,
        RoutingContext? routingContext = null,
        EditorStateSnapshot? initialEditorState = null,
        IEnumerable<CommandHandlerRegistration>? commandHandlers = null,
        IEnumerable<CommandValidatorRegistration>? commandValidators = null,
        IEnumerable<CommandHistoryPolicyRegistration>? historyPolicies = null,
        IEnumerable<IDocumentChangedSubscriber>? documentChangedSubscribers = null,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null,
        ModelProfileCatalog? modelProfileCatalog = null,
        ModelProfileViewStateSnapshot? initialModelProfileViewState = null)
    {
        ArgumentNullException.ThrowIfNull(projectionEngine);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(layoutAlgorithmId);
        ArgumentNullException.ThrowIfNull(routingEngine);
        ArgumentNullException.ThrowIfNull(routingAlgorithmId);
        ArgumentNullException.ThrowIfNull(sceneBuilder);

        ProjectionEngine = projectionEngine;
        LayoutEngine = layoutEngine;
        LayoutAlgorithmId = layoutAlgorithmId;
        RoutingEngine = routingEngine;
        RoutingAlgorithmId = routingAlgorithmId;
        SceneBuilder = sceneBuilder;
        ProjectionContext = projectionContext ?? ProjectionContext.Empty;
        LayoutContext = layoutContext ?? LayoutContext.Empty;
        RoutingContext = routingContext ?? RoutingContext.Empty;
        InitialEditorState = initialEditorState ?? EditorStateSnapshot.Empty;
        CommandHandlers = Copy(commandHandlers, nameof(commandHandlers));
        CommandValidators = Copy(commandValidators, nameof(commandValidators));
        HistoryPolicies = Copy(historyPolicies, nameof(historyPolicies));
        DocumentChangedSubscribers = Copy(
            documentChangedSubscribers,
            nameof(documentChangedSubscribers));
        ConnectorAnchorPolicyProvider = connectorAnchorPolicyProvider ??
            ElementConnectorAnchorPolicyRegistry.Default;
        ModelProfileCatalog = modelProfileCatalog ?? ModelProfileCatalog.Empty;
        InitialModelProfileViewState = initialModelProfileViewState ??
            ModelProfileViewStateSnapshot.Empty;
    }

    public ProjectionEngine ProjectionEngine { get; }

    public LayoutEngine LayoutEngine { get; }

    public AlgorithmId LayoutAlgorithmId { get; }

    public RoutingEngine RoutingEngine { get; }

    public AlgorithmId RoutingAlgorithmId { get; }

    public Canvas2DSceneBuilder SceneBuilder { get; }

    public ProjectionContext ProjectionContext { get; }

    public LayoutContext LayoutContext { get; }

    public RoutingContext RoutingContext { get; }

    public EditorStateSnapshot InitialEditorState { get; }

    public ImmutableArray<CommandHandlerRegistration> CommandHandlers { get; }

    public ImmutableArray<CommandValidatorRegistration> CommandValidators { get; }

    public ImmutableArray<CommandHistoryPolicyRegistration> HistoryPolicies { get; }

    public ImmutableArray<IDocumentChangedSubscriber> DocumentChangedSubscribers { get; }

    public IElementConnectorAnchorPolicyProvider ConnectorAnchorPolicyProvider { get; }

    /// <summary>
    /// Gets the immutable optional-profile definitions available to this view session.
    /// </summary>
    public ModelProfileCatalog ModelProfileCatalog { get; }

    /// <summary>
    /// Gets the transient visibility preference used for the initially active Main view.
    /// </summary>
    public ModelProfileViewStateSnapshot InitialModelProfileViewState { get; }

    private static ImmutableArray<T> Copy<T>(IEnumerable<T>? values, string parameterName)
        where T : class
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        if (Array.Exists(copy, static value => value is null))
        {
            throw new ArgumentException("Registrations cannot contain null values.", parameterName);
        }

        return [.. copy];
    }
}
