using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Scene;

/// <summary>Renders one derived Participant label band beside its Lane-mode Pools.</summary>
internal static class BpmnParticipantSceneContributor
{
    internal static Canvas2DSceneContribution Contribute(ProjectedGraph graph, LayoutResult localLayout,
        Canvas2DSceneContributorId contributorId, BpmnElementColours colours)
    {
        var overrides = new List<Canvas2DCanonicalSceneItemVisualOverride>();
        var decorations = new List<Canvas2DSceneItem>();
        foreach (var node in graph.Nodes.Where(static node => node.Source.SemanticTypeId == BpmnSemanticTypes.Participant))
        {
            var layout = localLayout.Nodes.Single(geometry => geometry.ProjectedObjectId == node.Id);
            var bounds = new RectD(0d, 0d, layout.Bounds.Width, layout.Bounds.Height);
            overrides.Add(new Canvas2DCanonicalSceneItemVisualOverride(Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"),
                Canvas2DSceneGeometry.Rectangle(bounds), new Canvas2DSceneStyle(fill: colours.Background, stroke: colours.Border, strokeWidth: 1.5d)));
            var headerWidth = bounds.Width;
            Canvas2DSceneOriginTrace Trace(string key) => new(Canvas2DSceneOriginCategory.SemanticElement |
                (node.Source.VisualStateId is null ? Canvas2DSceneOriginCategory.None : Canvas2DSceneOriginCategory.VisualState) | Canvas2DSceneOriginCategory.ProjectedRuntimeObject |
                Canvas2DSceneOriginCategory.RegisteredExtension, node.Source.SemanticElementId, node.Source.VisualStateId,
                node.Id, key);
            if (node.SemanticProperties.TryGetValue(BpmnSemanticProperties.Name, out var name) && name.Kind == PropertyValueKind.Text)
            {
                var textBounds = new RectD(-bounds.Height / 2d, -headerWidth / 2d, bounds.Height, headerWidth);
                var transform = Matrix2D.CreateRotation(-Math.PI / 2d)
                    .Then(Matrix2D.CreateTranslation(headerWidth / 2d, bounds.Height / 2d)).Then(layout.Transform);
                var nameKey = $"participant:name:{node.Id.Value}";
                decorations.Add(new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForExtension(contributorId, nameKey),
                    Canvas2DSceneLayer.Decoration, 1,
                    Canvas2DSceneGeometry.Text(textBounds, name.TextValue, new PointD(0d, 0d),
                        Canvas2DTextAlignment.Center, Canvas2DTextBaseline.Middle), Trace(nameKey), transform: transform, clip: layout.Bounds,
                    style: new Canvas2DSceneStyle(fill: colours.Text, fontSize: 12d)));
            }
        }

        return new Canvas2DSceneContribution(decorations, canonicalItemVisualOverrides: overrides);
    }
}
