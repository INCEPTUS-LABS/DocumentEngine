using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Framework-owned construction of one immutable Canvas2D rendering plan.
/// </summary>
public sealed partial class Canvas2DSceneBuilder
{
    private readonly Canvas2DSceneConfiguration _configuration;
    private readonly Canvas2DSceneContributorRegistry _contributors;
    private readonly Canvas2DNodeLabelLayoutConfiguration _nodeLabelLayout;
    private readonly Canvas2DConnectorLabelLayoutConfiguration _connectorLabelLayout;

    public Canvas2DSceneBuilder(
        Canvas2DSceneConfiguration? configuration = null,
        IEnumerable<Canvas2DSceneContributorRegistration>? contributors = null)
    {
        _configuration = configuration ?? Canvas2DSceneConfiguration.Default;
        _contributors = new Canvas2DSceneContributorRegistry(contributors ?? []);
        _nodeLabelLayout = Canvas2DNodeLabelLayoutConfiguration.Default;
        _connectorLabelLayout = Canvas2DConnectorLabelLayoutConfiguration.Default;
    }

    /// <summary>
    /// Resolves the visible logical Document rectangle for one Canvas CSS surface.
    /// Device-pixel ratio is intentionally irrelevant to this conversion.
    /// </summary>
    public static RectD CalculateVisibleDocumentRegion(
        ViewportSnapshot viewport,
        Canvas2DSurfaceSize surfaceSize)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        var transform = CreateViewportTransform(viewport);
        if (!transform.TryInvert(out var inverse))
        {
            throw new InvalidOperationException("The viewport transform is not invertible.");
        }

        var topLeft = inverse.TransformPoint(default);
        var bottomRight = inverse.TransformPoint(new PointD(
            surfaceSize.CssWidth,
            surfaceSize.CssHeight));
        return new RectD(
            Math.Min(topLeft.X, bottomRight.X),
            Math.Min(topLeft.Y, bottomRight.Y),
            Math.Abs(bottomRight.X - topLeft.X),
            Math.Abs(bottomRight.Y - topLeft.Y));
    }

    internal static Matrix2D CreateViewportTransform(ViewportSnapshot viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        return Matrix2D.CreateScale(viewport.Zoom, viewport.Zoom)
            .Then(Matrix2D.CreateTranslation(viewport.Pan));
    }

    internal static Canvas2DSurfaceSize CalculateCanvasCssSurface(
        ViewportSnapshot viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (viewport.VisibleDocumentRegion is not { } visibleRegion)
        {
            throw new InvalidOperationException(
                "The viewport has no visible Document region observation.");
        }

        var transform = CreateViewportTransform(viewport);
        var topLeft = transform.TransformPoint(visibleRegion.TopLeft);
        var bottomRight = transform.TransformPoint(new PointD(
            visibleRegion.Right,
            visibleRegion.Bottom));
        return new Canvas2DSurfaceSize(
            Math.Abs(bottomRight.X - topLeft.X),
            Math.Abs(bottomRight.Y - topLeft.Y),
            devicePixelRatio: 1d);
    }

    /// <summary>
    /// Builds a complete Scene after resolving automatic node labels through the approved
    /// logical-unit text-metrics boundary.
    /// </summary>
    internal async ValueTask<Canvas2DSceneBuildResult> BuildMeasuredAsync(
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        ITextMetricsService textMetrics,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory,
        CancellationToken cancellationToken = default)
        => await BuildMeasuredCoreAsync(
            projectedGraph,
            layoutResult,
            routingResult,
            visualModel,
            editorState,
            textMetrics,
            requestFactory,
            presentation: null,
            cancellationToken).ConfigureAwait(false);

    internal ValueTask<Canvas2DSceneBuildResult> BuildMeasuredAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        ITextMetricsService textMetrics,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory,
        CancellationToken cancellationToken = default)
        => BuildMeasuredAsync(
            document,
            activeScopeId,
            modelProfileViewState,
            ModelProfileElementViewStateSnapshot.Empty,
            projectedGraph,
            layoutResult,
            routingResult,
            visualModel,
            editorState,
            textMetrics,
            requestFactory,
            cancellationToken);

    internal ValueTask<Canvas2DSceneBuildResult> BuildMeasuredAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        ITextMetricsService textMetrics,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(modelProfileViewState);
        ArgumentNullException.ThrowIfNull(modelProfileElementViewState);
        return BuildMeasuredCoreAsync(
            projectedGraph,
            layoutResult,
            routingResult,
            visualModel,
            editorState,
            textMetrics,
            requestFactory,
            new ScenePresentationInput(
                document,
                activeScopeId,
                modelProfileViewState,
                modelProfileElementViewState),
            cancellationToken);
    }

    private async ValueTask<Canvas2DSceneBuildResult> BuildMeasuredCoreAsync(
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        ITextMetricsService textMetrics,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory,
        ScenePresentationInput? presentation,
        CancellationToken cancellationToken)
    {
        var missingInputs = new List<Diagnostic>();
        AddMissingInput(projectedGraph, nameof(projectedGraph), missingInputs);
        AddMissingInput(layoutResult, nameof(layoutResult), missingInputs);
        AddMissingInput(routingResult, nameof(routingResult), missingInputs);
        AddMissingInput(visualModel, nameof(visualModel), missingInputs);
        AddMissingInput(editorState, nameof(editorState), missingInputs);
        AddMissingInput(textMetrics, nameof(textMetrics), missingInputs);
        AddMissingInput(requestFactory, nameof(requestFactory), missingInputs);
        if (missingInputs.Count > 0)
        {
            return Canvas2DSceneBuildResult.Failure(missingInputs);
        }

        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA1031 // Measurement/construction faults become deterministic diagnostics.
        try
        {
            var compatibilityDiagnostics = new List<Diagnostic>();
            ValidateCompatibility(
                projectedGraph,
                layoutResult,
                routingResult,
                visualModel,
                editorState,
                compatibilityDiagnostics);
            if (HasErrors(compatibilityDiagnostics))
            {
                return Canvas2DSceneBuildResult.Failure(compatibilityDiagnostics);
            }

            var layoutService = new Canvas2DTextLayoutService(textMetrics, requestFactory);
            var nodeLabelLayouts = await CreateMeasuredNodeLabelLayoutsAsync(
                projectedGraph,
                layoutResult,
                visualModel,
                editorState,
                layoutService,
                cancellationToken).ConfigureAwait(false);
            var connectorLabelLayouts = await CreateMeasuredConnectorLabelLayoutsAsync(
                projectedGraph,
                routingResult,
                visualModel,
                layoutService,
                cancellationToken).ConfigureAwait(false);
            var diagnostics = compatibilityDiagnostics
                .Concat(layoutService.Diagnostics)
                .ToArray();
            if (nodeLabelLayouts is null || connectorLabelLayouts is null ||
                HasErrors(diagnostics))
            {
                if (!HasErrors(diagnostics))
                {
                    diagnostics =
                    [
                        .. diagnostics,
                        Error(
                            Canvas2DSceneDiagnosticCodes.ConstructionFailure,
                            "Canvas2D text layout did not complete.",
                            projectedGraph.DocumentId?.Value ?? "ProjectedGraph"),
                    ];
                }

                return Canvas2DSceneBuildResult.Failure(diagnostics);
            }

            diagnostics = compatibilityDiagnostics.Concat(layoutService.Diagnostics).ToArray();
            if (HasErrors(diagnostics))
            {
                return Canvas2DSceneBuildResult.Failure(diagnostics);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return BuildCore(
                projectedGraph,
                layoutResult,
                routingResult,
                visualModel,
                editorState,
                nodeLabelLayouts,
                connectorLabelLayouts,
                diagnostics,
                presentation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return Canvas2DSceneBuildResult.Failure(
            [
                Error(
                    Canvas2DSceneDiagnosticCodes.ConstructionFailure,
                    "Canvas2D measured scene construction failed.",
                    projectedGraph?.DocumentId?.Value ?? "ProjectedGraph",
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name)),
            ]);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Builds one complete compatibility Scene from exactly the five approved immutable model
    /// inputs. The renderer-backed EditingSession pipeline augments this compatibility surface
    /// with measured automatic-label layout before constructing its authoritative Scene.
    /// </summary>
    public Canvas2DSceneBuildResult Build(
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState)
    {
        var missingInputs = new List<Diagnostic>();
        AddMissingInput(projectedGraph, nameof(projectedGraph), missingInputs);
        AddMissingInput(layoutResult, nameof(layoutResult), missingInputs);
        AddMissingInput(routingResult, nameof(routingResult), missingInputs);
        AddMissingInput(visualModel, nameof(visualModel), missingInputs);
        AddMissingInput(editorState, nameof(editorState), missingInputs);
        if (missingInputs.Count > 0)
        {
            return Canvas2DSceneBuildResult.Failure(missingInputs);
        }

#pragma warning disable CA1031 // Contributor and malformed-input faults become deterministic scene diagnostics.
        try
        {
            return BuildCore(
                projectedGraph,
                layoutResult,
                routingResult,
                visualModel,
                editorState);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return Canvas2DSceneBuildResult.Failure(
            [
                Error(
                    Canvas2DSceneDiagnosticCodes.ConstructionFailure,
                    "Canvas2D scene construction failed.",
                    projectedGraph.DocumentId?.Value ?? "ProjectedGraph",
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name)),
            ]);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Builds one complete Scene with read-only Document/profile presentation context for
    /// registered contributors.
    /// </summary>
    public Canvas2DSceneBuildResult Build(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState)
        => Build(
            document,
            activeScopeId,
            modelProfileViewState,
            ModelProfileElementViewStateSnapshot.Empty,
            projectedGraph,
            layoutResult,
            routingResult,
            visualModel,
            editorState);

    internal Canvas2DSceneBuildResult Build(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(modelProfileViewState);
        ArgumentNullException.ThrowIfNull(modelProfileElementViewState);
        var missingInputs = new List<Diagnostic>();
        AddMissingInput(projectedGraph, nameof(projectedGraph), missingInputs);
        AddMissingInput(layoutResult, nameof(layoutResult), missingInputs);
        AddMissingInput(routingResult, nameof(routingResult), missingInputs);
        AddMissingInput(visualModel, nameof(visualModel), missingInputs);
        AddMissingInput(editorState, nameof(editorState), missingInputs);
        if (missingInputs.Count > 0)
        {
            return Canvas2DSceneBuildResult.Failure(missingInputs);
        }

#pragma warning disable CA1031 // Contributor and malformed-input faults become deterministic scene diagnostics.
        try
        {
            return BuildCore(
                projectedGraph,
                layoutResult,
                routingResult,
                visualModel,
                editorState,
                presentation: new ScenePresentationInput(
                    document,
                    activeScopeId,
                    modelProfileViewState,
                    modelProfileElementViewState));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return Canvas2DSceneBuildResult.Failure(
            [
                Error(
                    Canvas2DSceneDiagnosticCodes.ConstructionFailure,
                    "Canvas2D scene construction failed.",
                    projectedGraph.DocumentId?.Value ?? "ProjectedGraph",
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name)),
            ]);
        }
#pragma warning restore CA1031
    }

    private Canvas2DSceneBuildResult BuildCore(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>?
            measuredNodeLabels = null,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel>?
            measuredConnectorLabels = null,
        IEnumerable<Diagnostic>? initialDiagnostics = null,
        ScenePresentationInput? presentation = null)
    {
        var diagnostics = initialDiagnostics?.ToList() ?? [];
        ValidateCompatibility(graph, layout, routing, visualModel, editorState, diagnostics);
        if (HasErrors(diagnostics))
        {
            return Canvas2DSceneBuildResult.Failure(diagnostics);
        }

        var items = new List<Canvas2DSceneItem>();
        ComposePipelineItems(
            graph,
            layout,
            routing,
            visualModel,
            items,
            measuredNodeLabels,
            measuredConnectorLabels);

        var pipelineItems = items.ToDictionary(static item => item.Id);
        var canonicalItemVisualOverrides =
            new Dictionary<SceneObjectId, CanonicalItemVisualOverrideRegistration>();

        var contributorMetadata = new List<KeyValuePair<string, PropertyValue>>();
        InvokeContributors(
            Canvas2DSceneContributionStage.Canonical,
            graph,
            layout,
            routing,
            visualModel,
            editorState,
            items,
            pipelineItems,
            canonicalItemVisualOverrides,
            contributorMetadata,
            diagnostics,
            presentation,
            spatialPresentations: null);
        if (HasErrors(diagnostics))
        {
            return Canvas2DSceneBuildResult.Failure(diagnostics);
        }

        ApplyCanonicalItemVisualOverrides(items, canonicalItemVisualOverrides);
        var activeBaseSceneItems = items
            .Where(static item =>
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)
            .ToArray();
        var spatialPresentations = new List<SpatialPresentationRegistration>();
        InvokeContributors(
            Canvas2DSceneContributionStage.Presentation,
            graph,
            layout,
            routing,
            visualModel,
            editorState,
            items,
            activeBaseSceneItems.ToDictionary(static item => item.Id),
            canonicalItemVisualOverrides: [],
            contributorMetadata,
            diagnostics,
            presentation,
            spatialPresentations);
        if (HasErrors(diagnostics))
        {
            return Canvas2DSceneBuildResult.Failure(diagnostics);
        }

        SpatialPresentationRegistration? spatialPresentation = null;
        if (spatialPresentations.Count > 1)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidContribution,
                "More than one presentation contributor supplied a spatial presentation plan.",
                string.Join(",", spatialPresentations.Select(static entry =>
                    entry.ContributorId.Value))));
        }
        else if (spatialPresentations.Count == 1)
        {
            spatialPresentation = spatialPresentations[0];
            ApplySpatialPresentation(
                graph,
                routing,
                visualModel,
                items,
                spatialPresentation.Plan,
                spatialPresentation.ConnectorRouter,
                diagnostics);
        }

        if (HasErrors(diagnostics))
        {
            return Canvas2DSceneBuildResult.Failure(diagnostics);
        }

        SuppressNodeLabelHitTestingDuringAnchorConnection(editorState, items);

        var editorOverlayStartIndex = items.Count;
        ComposeEditorOverlays(
            editorState,
            graph,
            visualModel,
            items,
            diagnostics,
            measuredNodeLabels,
            measuredConnectorLabels);
        AssociateEditorOverlaysWithSpatialPresentation(items, editorOverlayStartIndex);
        ValidateAndOrderSceneItems(
            graph,
            routing,
            visualModel,
            items,
            diagnostics,
            presentation,
            spatialPresentation?.Plan);
        if (HasErrors(diagnostics))
        {
            return Canvas2DSceneBuildResult.Failure(diagnostics);
        }

        var viewportTransform = CreateViewportTransform(editorState);
        var scene = new Canvas2DScene(
            graph.DocumentId,
            graph.SourceRevision,
            layout.AlgorithmId,
            routing.RoutingAlgorithmId,
            _configuration,
            _contributors.Descriptors,
            editorState.Viewport,
            viewportTransform,
            editorState.ActiveToolId,
            editorState.FocusTargetId,
            editorState.ToolState,
            new PropertyMap(contributorMetadata),
            items,
            diagnostics,
            spatialPresentation?.Plan);

        return Canvas2DSceneBuildResult.Success(scene);
    }

    private void InvokeContributors(
        Canvas2DSceneContributionStage stage,
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        List<Canvas2DSceneItem> items,
        IReadOnlyDictionary<SceneObjectId, Canvas2DSceneItem> canonicalItems,
        Dictionary<SceneObjectId, CanonicalItemVisualOverrideRegistration>
            canonicalItemVisualOverrides,
        List<KeyValuePair<string, PropertyValue>> contributorMetadata,
        List<Diagnostic> diagnostics,
        ScenePresentationInput? presentation,
        List<SpatialPresentationRegistration>? spatialPresentations)
    {
        var presentationContext = presentation is null
            ? null
            : new Canvas2DScenePresentationContext(
                presentation.Document,
                presentation.ActiveScopeId,
                presentation.ModelProfileViewState,
                presentation.ModelProfileElementViewState,
                canonicalItems.Values);
        foreach (var registration in _contributors.Registrations.Where(
                     registration => registration.Stage == stage))
        {
            var descriptor = registration.Descriptor;
            var context = new Canvas2DSceneContributionContext(
                graph,
                layout,
                routing,
                visualModel,
                editorState,
                _configuration,
                descriptor,
                presentationContext);

            Canvas2DSceneContributionResult? result;
#pragma warning disable CA1031 // Plugin faults are isolated as deterministic diagnostics.
            try
            {
                result = registration.Contributor.Contribute(context);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.ContributorFailure,
                    $"Canvas2D scene contributor '{descriptor.ContributorId}' failed.",
                    descriptor.ContributorId.Value,
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name)));
                continue;
            }
#pragma warning restore CA1031

            if (result is null || result.Diagnostics.IsDefault ||
                result.Diagnostics.Any(static diagnostic => diagnostic is null))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidContribution,
                    $"Canvas2D scene contributor '{descriptor.ContributorId}' returned an invalid result.",
                    descriptor.ContributorId.Value));
                continue;
            }

            diagnostics.AddRange(result.Diagnostics);
            if (!result.Succeeded || result.Contribution is null)
            {
                if (!HasErrors(result.Diagnostics))
                {
                    diagnostics.Add(Error(
                        Canvas2DSceneDiagnosticCodes.InvalidContribution,
                        $"Canvas2D scene contributor '{descriptor.ContributorId}' returned no contribution data.",
                        descriptor.ContributorId.Value));
                }

                continue;
            }

            var contribution = result.Contribution;
            if (contribution.Items.IsDefault ||
                contribution.CanonicalItemVisualOverrides.IsDefault ||
                contribution.Metadata is null)
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidContribution,
                    $"Canvas2D scene contributor '{descriptor.ContributorId}' returned uninitialized data.",
                    descriptor.ContributorId.Value));
                continue;
            }

            if (stage == Canvas2DSceneContributionStage.Canonical &&
                contribution.SpatialPresentationPlan is not null)
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidContribution,
                    $"Canonical Canvas2D scene contributor '{descriptor.ContributorId}' returned presentation-stage data.",
                    descriptor.ContributorId.Value));
                continue;
            }

            if (stage == Canvas2DSceneContributionStage.Presentation &&
                !contribution.CanonicalItemVisualOverrides.IsEmpty)
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidContribution,
                    $"Presentation Canvas2D scene contributor '{descriptor.ContributorId}' returned a canonical visual override.",
                    descriptor.ContributorId.Value));
                continue;
            }

            foreach (var item in contribution.Items)
            {
                if (!IsValidContributorItem(item, descriptor, diagnostics))
                {
                    continue;
                }

                items.Add(item);
            }

            foreach (var visualOverride in contribution.CanonicalItemVisualOverrides)
            {
                if (!IsValidCanonicalItemVisualOverride(
                        visualOverride,
                        descriptor,
                        canonicalItems,
                        diagnostics))
                {
                    continue;
                }

                var overrideRegistration = new CanonicalItemVisualOverrideRegistration(
                    descriptor.ContributorId,
                    visualOverride);
                if (canonicalItemVisualOverrides.TryAdd(
                        visualOverride.TargetSceneObjectId,
                        overrideRegistration))
                {
                    continue;
                }

                var existing = canonicalItemVisualOverrides[
                    visualOverride.TargetSceneObjectId];
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.ConflictingCanonicalItemVisualOverride,
                    $"Canonical Scene item '{visualOverride.TargetSceneObjectId}' is targeted " +
                    $"by both contributor '{existing.ContributorId}' and contributor " +
                    $"'{descriptor.ContributorId}'.",
                    visualOverride.TargetSceneObjectId.Value,
                    new KeyValuePair<string, string>(
                        "FirstContributorId",
                        existing.ContributorId.Value),
                    new KeyValuePair<string, string>(
                        "ConflictingContributorId",
                        descriptor.ContributorId.Value)));
            }

            if (stage == Canvas2DSceneContributionStage.Presentation)
            {
                if (contribution.SpatialPresentationPlan is not null)
                {
                    if (registration.Contributor is not
                        ICanvas2DConnectorPresentationRouter connectorRouter)
                    {
                        diagnostics.Add(Error(
                            Canvas2DSceneDiagnosticCodes.InvalidContribution,
                            $"Spatial presentation contributor '{descriptor.ContributorId}' " +
                            "does not provide a connector presentation router.",
                            descriptor.ContributorId.Value));
                    }
                    else
                    {
                        spatialPresentations!.Add(new SpatialPresentationRegistration(
                            descriptor.ContributorId,
                            contribution.SpatialPresentationPlan,
                            connectorRouter));
                    }
                }
            }

            foreach (var entry in contribution.Metadata)
            {
                contributorMetadata.Add(new KeyValuePair<string, PropertyValue>(
                    CreateContributorMetadataKey(descriptor.ContributorId, entry.Key),
                    entry.Value));
            }
        }
    }

    private sealed record ScenePresentationInput(
        DocumentSnapshot Document,
        DocumentScopeId ActiveScopeId,
        ModelProfileViewStateSnapshot ModelProfileViewState,
        ModelProfileElementViewStateSnapshot ModelProfileElementViewState);

    private sealed record SpatialPresentationRegistration(
        Canvas2DSceneContributorId ContributorId,
        Canvas2DSpatialPresentationPlan Plan,
        ICanvas2DConnectorPresentationRouter ConnectorRouter);

    private static void ApplyCanonicalItemVisualOverrides(
        List<Canvas2DSceneItem> items,
        IReadOnlyDictionary<SceneObjectId, CanonicalItemVisualOverrideRegistration>
            visualOverrides)
    {
        if (visualOverrides.Count == 0)
        {
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!visualOverrides.TryGetValue(item.Id, out var registration))
            {
                continue;
            }

            var visualOverride = registration.VisualOverride;
            items[index] = new Canvas2DSceneItem(
                item.Id,
                item.Layer,
                item.ZIndex,
                visualOverride.Geometry,
                item.Origin,
                item.Transform,
                item.Clip,
                visualOverride.Style,
                item.IsVisible,
                item.HitTestPolicy,
                item.PersistentAppearance,
                item.Metadata,
                item.Bounds,
                item.SpatialRegion,
                item.ConnectorPresentationMapping);
        }
    }

    private static void SuppressNodeLabelHitTestingDuringAnchorConnection(
        EditorStateSnapshot editorState,
        List<Canvas2DSceneItem> items)
    {
        if (!StringComparer.Ordinal.Equals(
                editorState.ActiveGesture?.Kind,
                Canvas2DAnchorConnectionGestureMetadata.Kind))
        {
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Layer != Canvas2DSceneLayer.Label ||
                item.Origin.VisualStateId is null ||
                item.HitTestPolicy.Mode == Canvas2DHitTestMode.None)
            {
                continue;
            }

            items[index] = new Canvas2DSceneItem(
                item.Id,
                item.Layer,
                item.ZIndex,
                item.Geometry,
                item.Origin,
                item.Transform,
                item.Clip,
                item.Style,
                item.IsVisible,
                Canvas2DHitTestPolicy.None,
                item.PersistentAppearance,
                item.Metadata,
                item.Bounds,
                item.SpatialRegion,
                item.ConnectorPresentationMapping);
        }
    }

    private sealed record CanonicalItemVisualOverrideRegistration(
        Canvas2DSceneContributorId ContributorId,
        Canvas2DCanonicalSceneItemVisualOverride VisualOverride);

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private static bool HasErrors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static string CreateContributorMetadataKey(
        Canvas2DSceneContributorId contributorId,
        string key) =>
        $"{contributorId.Value.Length}:{contributorId.Value}{key.Length}:{key}";

    private static void AddMissingInput<T>(
        T? value,
        string parameterName,
        List<Diagnostic> diagnostics)
        where T : class
    {
        if (value is null)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidInput,
                $"Required Canvas2D Scene Builder input '{parameterName}' is missing.",
                parameterName));
        }
    }

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);

    private static Diagnostic Warning(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Warning, message, sourceIdentity, context);
}
