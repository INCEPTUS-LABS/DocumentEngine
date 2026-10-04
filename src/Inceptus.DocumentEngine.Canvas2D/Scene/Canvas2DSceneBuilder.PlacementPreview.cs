using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private bool SupportsPlacementContributors() => _contributors.Descriptors.All(static descriptor =>
        descriptor.PlacementDependency is Canvas2DScenePlacementDependency.Invariant or
            Canvas2DScenePlacementDependency.BoundedFeedbackOnly);

    private static bool IsSupportedPlacementEditorState(EditorStateSnapshot state) =>
        state.ActiveGesture is null && state.TemporaryFeedback.Count(static feedback =>
            feedback.PlacementPreview is not null) <= 1;

    internal async ValueTask<(Canvas2DScene? Scene, bool ReusedSelection)> TryReuseForPlacementPreviewAsync(
        Canvas2DScene previous, DocumentSnapshot document, DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing,
        EditorStateSnapshot editorState, ITextMetricsService? textMetrics,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest>? requestFactory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (previous.BoundedPresentation is not { } prior || !SupportsPlacementContributors() ||
            !IsSupportedPlacementEditorState(prior.EditorState) ||
            !IsSupportedPlacementEditorState(editorState))
        {
            return (null, false);
        }

        if (!IsPlacementTransition(prior.EditorState, editorState))
        {
            // A successful placement retires its preview and selects the newly committed
            // node in one editor-state update. Validate both bounded transitions without
            // installing or presenting the intermediate state.
            if (prior.EditorState.TemporaryFeedback.Length != 1 ||
                prior.EditorState.TemporaryFeedback[0].PlacementPreview is null ||
                !editorState.TemporaryFeedback.IsEmpty ||
                (prior.EditorState.Selection.AsSpan().SequenceEqual(editorState.Selection.AsSpan()) &&
                 prior.EditorState.HoveredObjectId == editorState.HoveredObjectId))
            {
                return (null, false);
            }
            var oldState = prior.EditorState;
            var retiredState = new EditorStateSnapshot(oldState.Selection, oldState.HoveredObjectId,
                oldState.ActiveToolId, oldState.FocusTargetId, oldState.Viewport,
                oldState.ActiveGesture, temporaryFeedback: null, oldState.ToolState,
                oldState.SemanticSceneSelection);
            var retired = await TryReuseForPlacementPreviewAsync(previous, document, scopeId,
                profileViewState, profileElementViewState, graph, layout, routing, retiredState,
                textMetrics, requestFactory, cancellationToken).ConfigureAwait(false);
            var selected = retired.Scene is null ? null : TryReuseForSelection(retired.Scene, document, scopeId,
                profileViewState, profileElementViewState, graph, layout, routing, editorState,
                cancellationToken);
            return (selected, selected is not null);
        }

        var source = prior.Source;
        var provenance = source.Provenance;
        if (!ReferenceEquals(provenance.Builder, this) || !ReferenceEquals(provenance.Document, document) ||
            provenance.ScopeId != scopeId || !ReferenceEquals(provenance.Graph, graph) ||
            !ReferenceEquals(provenance.Layout, layout) || !ReferenceEquals(provenance.Routing, routing) ||
            !ReferenceEquals(provenance.ProfileViewState, profileViewState) ||
            !ReferenceEquals(provenance.ProfileElementViewState, profileElementViewState) ||
            previous.DocumentId != document.DocumentId || previous.SourceRevision != document.Revision)
        {
            return (null, false);
        }

        var diagnostics = new List<Diagnostic>();
        var placementItems = new List<Canvas2DSceneItem>();
        var presentationInput = new ScenePresentationInput(document, scopeId, profileViewState,
            profileElementViewState);
        // Bounded contributors cannot observe global canonical items as a new input. Their
        // declared dependence is limited to typed placement feedback and fixed provenance.
        foreach (var stage in new[] { Canvas2DSceneContributionStage.Canonical, Canvas2DSceneContributionStage.Presentation })
        {
            InvokeContributors(stage, graph, layout, routing, document.VisualModel, editorState,
                placementItems, source.ItemsById, [], [], diagnostics, presentationInput, [],
                placementOnly: true);
        }

        Canvas2DTextLayoutService? layoutService = textMetrics is not null && requestFactory is not null
            ? new Canvas2DTextLayoutService(textMetrics, requestFactory)
            : null;
        var labels = layoutService is null
            ? ImmutableArray<Canvas2DPlacementLabelLayout>.Empty
            : await CreatePlacementLabelLayoutsAsync(editorState, layoutService,
                prior.PlacementLabelLayouts.IsDefault ? [] : prior.PlacementLabelLayouts,
                cancellationToken).ConfigureAwait(false);
        if (labels is null || diagnostics.Count != 0 || layoutService?.Diagnostics.IsEmpty == false)
        {
            return (null, false);
        }
        ComposePlacementLabels(editorState, labels.Value, placementItems);

        HashSet<SceneObjectId> oldPlacementIds = prior.PlacementItems.IsDefault
            ? [] : prior.PlacementItems.Select(static item => item.Id).ToHashSet();
        var overlays = prior.Items.Where(item => !oldPlacementIds.Contains(item.Id))
            .Concat(placementItems).ToList();
        if (overlays.Count > Canvas2DBoundedPresentation.MaximumItems ||
            overlays.Any(item => source.ItemsById.ContainsKey(item.Id)) ||
            overlays.Select(static item => item.Id).Distinct().Count() != overlays.Count)
        {
            return (null, false);
        }
        foreach (var item in placementItems)
        {
            ValidateItem(item, diagnostics);
            if (item.SpatialRegion is { } region &&
                previous.SpatialPresentationPlan?.Regions.Any(candidate => candidate.Equals(region)) != true)
            {
                return (null, false);
            }
        }
        if (diagnostics.Count != 0)
        {
            return (null, false);
        }
        overlays.Sort(CompareItems);
        var bounded = CreateMovePresentation(source, editorState, overlays.ToImmutableArray()) with
        {
            PlacementItems = placementItems.ToImmutableArray(),
            PlacementLabelLayouts = labels.Value,
        };
        cancellationToken.ThrowIfCancellationRequested();
        return (previous.WithBoundedPresentation(bounded, MergeBoundedItems(source, bounded),
            CreatePanReuseSource(graph, layout, routing, document.VisualModel, editorState, presentationInput)), false);
    }

    private static bool IsPlacementTransition(EditorStateSnapshot previous, EditorStateSnapshot current) =>
        previous.Viewport.Equals(current.Viewport) && previous.ActiveGesture is null && current.ActiveGesture is null &&
        previous.Selection.AsSpan().SequenceEqual(current.Selection.AsSpan()) &&
        previous.HoveredObjectId == current.HoveredObjectId &&
        previous.SemanticSceneSelection == current.SemanticSceneSelection &&
        StringComparer.Ordinal.Equals(previous.ActiveToolId, current.ActiveToolId) &&
        StringComparer.Ordinal.Equals(previous.FocusTargetId, current.FocusTargetId) &&
        previous.ToolState.Equals(current.ToolState) &&
        previous.TemporaryFeedback.Where(static feedback => feedback.PlacementPreview is null)
            .SequenceEqual(current.TemporaryFeedback.Where(static feedback => feedback.PlacementPreview is null)) &&
        !previous.TemporaryFeedback.AsSpan().SequenceEqual(current.TemporaryFeedback.AsSpan());

    private static bool IsValidBoundedPlacementContribution(
        Canvas2DSceneContribution contribution, Canvas2DSceneContributorDescriptor descriptor,
        EditorStateSnapshot editorState, List<Diagnostic> diagnostics)
    {
        if ((editorState.TemporaryFeedback.IsEmpty && !contribution.Items.IsEmpty) ||
            contribution.Metadata.Count != 0 || !contribution.CanonicalItemVisualOverrides.IsEmpty ||
            contribution.SpatialPresentationPlan is not null ||
            contribution.Items.Length > Canvas2DBoundedPresentation.MaximumItems ||
            contribution.Items.Any(static item => item.Layer != Canvas2DSceneLayer.Overlay ||
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None ||
                item.Origin.Categories != (Canvas2DSceneOriginCategory.EditorState |
                    Canvas2DSceneOriginCategory.RegisteredExtension) ||
                item.Origin.SemanticElementId is not null || item.Origin.VisualStateId is not null ||
                item.Origin.ProjectedObjectId is not null ||
                !item.Origin.RelatedProjectedObjectIds.IsEmpty || !item.Origin.RelatedSceneObjectIds.IsEmpty ||
                item.PersistentAppearance.Count != 0 || item.Metadata.Count != 0))
        {
            diagnostics.Add(Error(Canvas2DSceneDiagnosticCodes.InvalidContribution,
                $"Canvas2D contributor '{descriptor.ContributorId}' violated its bounded placement feedback declaration.",
                descriptor.ContributorId.Value));
            return false;
        }
        return true;
    }

    private async ValueTask<ImmutableArray<Canvas2DPlacementLabelLayout>?> CreatePlacementLabelLayoutsAsync(
        EditorStateSnapshot state, Canvas2DTextLayoutService layoutService,
        ImmutableArray<Canvas2DPlacementLabelLayout> previous, CancellationToken cancellationToken)
    {
        var labels = ImmutableArray.CreateBuilder<Canvas2DPlacementLabelLayout>();
        foreach (var feedback in state.TemporaryFeedback)
        {
            if (feedback.PlacementPreview is not { Label: { } text } preview)
            {
                continue;
            }
            var reusable = previous.FirstOrDefault(candidate => candidate.FeedbackId == feedback.Id &&
                candidate.Text == text && candidate.BodySize == preview.Bounds.Size &&
                candidate.Placement.Equals(preview.LabelPlacement));
            if (reusable is not null)
            {
                labels.Add(reusable);
                continue;
            }
            var box = await CreateMeasuredLabelBoxAsync(text, preview.Bounds, Matrix2D.Identity,
                preview.LabelPlacement, layoutService, cancellationToken).ConfigureAwait(false);
            if (box is null)
            {
                return null;
            }
            labels.Add(new(feedback.Id, text, preview.Bounds.Size, preview.LabelPlacement, box.TextLayout));
        }
        return labels.ToImmutable();
    }

    private void ComposePlacementLabels(EditorStateSnapshot state,
        ImmutableArray<Canvas2DPlacementLabelLayout> labels, List<Canvas2DSceneItem> items)
    {
        foreach (var feedback in state.TemporaryFeedback)
        {
            if (feedback.PlacementPreview is not { Label: { } text } preview || feedback.Bounds is not { } displayed)
            {
                continue;
            }
            var translation = new VectorD(displayed.X - preview.Bounds.X, displayed.Y - preview.Bounds.Y);
            // Normal accepted captions retain their existing origin rule. An illegal body
            // must keep its whole prospective family moving rather than pinning its caption
            // to the document edge while the rejected body continues outside it.
            var clampCaption = DocumentGeometryBoundary.Contains(preview.Bounds);
            var prepared = labels.FirstOrDefault(label => label.FeedbackId == feedback.Id);
            if (prepared is null)
            {
                var contentBounds = ResolveAutomaticLabelContentBounds(preview.Bounds, preview.LabelPlacement, clampCaption);
                var bounds = preview.LabelPlacement.Kind == NodeLabelPlacementKind.InsideCentered
                    ? preview.Bounds : new RectD(contentBounds.X, contentBounds.Y, contentBounds.Width,
                        NodeLabelStyle.FontSize * _nodeLabelLayout.LineHeightMultiplier);
                AddPlacementLabelLine(feedback.Id, 0, text, bounds.Translate(translation),
                    bounds.Translate(translation), items);
                continue;
            }
            var box = ResolveMeasuredLabelBox(preview.Bounds, Matrix2D.Identity, preview.LabelPlacement,
                ResolveAutomaticLabelContentBounds(preview.Bounds, preview.LabelPlacement, clampCaption),
                prepared.TextLayout, clampCaption);
            var totalHeight = prepared.TextLayout.Lines.Sum(static line => line.Metrics.LineHeight);
            var top = box.PlacementBounds.Top + ((box.PlacementBounds.Height - totalHeight) / 2d);
            for (var index = 0; index < prepared.TextLayout.Lines.Length; index++)
            {
                var line = prepared.TextLayout.Lines[index];
                var bounds = new RectD(box.PlacementBounds.Left +
                    ((box.PlacementBounds.Width - line.Metrics.Width) / 2d), top,
                    line.Metrics.Width, line.Metrics.LineHeight);
                top += line.Metrics.LineHeight;
                AddPlacementLabelLine(feedback.Id, index, line.Text, bounds.Translate(translation),
                    box.ClipBounds.Translate(translation), items);
            }
        }
    }

    private static void AddPlacementLabelLine(string feedbackId, int index, string text,
        RectD bounds, RectD clip, List<Canvas2DSceneItem> items)
    {
        var key = $"feedback:{feedbackId}:label:{index}";
        items.Add(new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForEditorState(key),
            Canvas2DSceneLayer.Overlay, 6100 + index,
            Canvas2DSceneGeometry.Text(bounds, text, new PointD(bounds.X + bounds.Width / 2d,
                bounds.Y + bounds.Height / 2d), Canvas2DTextAlignment.Center, Canvas2DTextBaseline.Middle),
            new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.EditorState, stableSourceKey: key),
            clip: clip, style: NodeLabelStyle, hitTestPolicy: Canvas2DHitTestPolicy.None));
    }
}
