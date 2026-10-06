using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    internal async ValueTask<ScopeNodeCaptionSnapshot> PrepareResizedCaptionAsync(
        ProjectedLabel label, ScopeNodeGeometrySnapshot owner, NodeLabelVisualOverride value,
        ITextMetricsService textMetrics, Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory,
        CancellationToken cancellationToken)
    {
        var service = new Canvas2DTextLayoutService(textMetrics, requestFactory);
        var box = await CreateMeasuredManualLabelBoxAsync(label.Text, owner.LocalBounds, owner.Transform,
            value, service, cancellationToken, clampToDocument: owner.RegionId is null).ConfigureAwait(false)
            ?? throw new ScopeGeometryPreparationException(service.Diagnostics);
        return new ScopeNodeCaptionSnapshot(owner.VisualStateId, label.Id,
            label.NodePlacement ?? NodeLabelPlacement.InsideCentered, value,
            box.PlacementBounds, box.ClipBounds, box.OwnerTransform,
            box.TextLayout.Lines.Select(line => new ScopeTextMeasurementSnapshot(
                requestFactory(line.Text, NodeLabelStyle, NodeLabelStyle.FontSize * _nodeLabelLayout.LineHeightMultiplier),
                line.Metrics)));
    }

    internal ImmutableArray<ScopeTextMeasurementSnapshot> RebuildCaptionMeasurementIndex(
        ScopeGeometryInputs inputs, ProjectedGraph graph, LayoutResult layout,
        ScopeGeometrySnapshot previous, IEnumerable<ScopeNodeCaptionSnapshot> captions)
    {
        var byLabel = captions.ToDictionary(static caption => caption.LabelId);
        var measurements = new Dictionary<TextMeasurementRequest, TextMetrics>();
        foreach (var label in graph.Labels)
            if (byLabel.TryGetValue(label.Id, out var caption))
                foreach (var line in caption.Lines)
                    measurements.TryAdd(line.Request, line.Metrics);

        // Reuse all contributor measurements. Base text declarations are needed to
        // retain requests shared with former caption lines; no spatial preparation,
        // text measurement, node layout or connector assessment runs here.
        var saved = previous.TextMeasurements.ToDictionary(static line => line.Request, static line => line.Metrics);
        foreach (var registration in _contributors.Registrations.Where(static registration =>
                     registration.Descriptor.PlacementDependency != Canvas2DScenePlacementDependency.BoundedFeedbackOnly))
        {
            var provider = (ICanvas2DScopeGeometryContributor)registration.Contributor;
            var declaration = provider.PrepareBase(new Canvas2DScopeGeometryBaseContext(inputs, graph, layout,
                _configuration, registration.Descriptor, previous.TextConfiguration));
            if (!declaration.Succeeded) throw new ScopeGeometryPreparationException(declaration.Diagnostics);
            foreach (var request in declaration.TextRequests.OrderBy(static item => item.Key.Value, StringComparer.Ordinal))
            {
                if (!saved.TryGetValue(request.Value, out var metrics))
                    throw new InvalidOperationException("A label-only change altered a contributor text request.");
                measurements.TryAdd(request.Value, metrics);
            }
        }
        return [.. measurements.Select(static pair => new ScopeTextMeasurementSnapshot(pair.Key, pair.Value))];
    }
}
