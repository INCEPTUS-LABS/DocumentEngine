using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSceneItemTests
{
    [Fact]
    public void GeometryDefensivelyCopiesPathAndUsesStructuralEquality()
    {
        var callerPoints = new List<PointD>
        {
            new(30d, 20d),
            new(10d, 5d),
            new(40d, 25d),
        };
        var first = Canvas2DSceneGeometry.Path(callerPoints);
        var equivalent = Canvas2DSceneGeometry.Path(callerPoints.ToArray());

        callerPoints.Clear();

        Assert.Equal(3, first.Points.Length);
        Assert.Equal(new RectD(10d, 5d, 30d, 20d), first.Bounds);
        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PointD>)first.Points).Add(new PointD(1d, 1d)));
    }

    [Fact]
    public void GeometryStyleAndHitPolicyRejectMalformedValues()
    {
        Assert.Throws<ArgumentException>(() => Canvas2DSceneGeometry.Path([new PointD(0d, 0d)]));
        Assert.Throws<ArgumentException>(() => Canvas2DSceneGeometry.Path(
            [new PointD(0d, 0d), new PointD(1d, 1d)],
            isClosed: true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DSceneStyle(strokeWidth: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DSceneStyle(opacity: 1.01d));
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas2DSceneGeometry.Text(
            new RectD(0d, 0d, 10d, 10d),
            "neutral",
            new PointD(5d, 5d),
            (Canvas2DTextAlignment)999,
            Canvas2DTextBaseline.Middle));
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas2DSceneGeometry.Text(
            new RectD(0d, 0d, 10d, 10d),
            "neutral",
            new PointD(5d, 5d),
            Canvas2DTextAlignment.Center,
            (Canvas2DTextBaseline)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Stroke, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DHitTestPolicy((Canvas2DHitTestMode)999));
    }

    [Fact]
    public void GeometryFactoriesPreserveRectangleEllipseTextAndImageDescriptions()
    {
        var bounds = new RectD(4d, 5d, 60d, 20d);
        var rectangle = Canvas2DSceneGeometry.Rectangle(bounds);
        var ellipse = Canvas2DSceneGeometry.Ellipse(bounds);
        var text = Canvas2DSceneGeometry.Text(bounds, "neutral");
        var image = Canvas2DSceneGeometry.Image(bounds, "resource:neutral");

        Assert.Equal(Canvas2DSceneGeometryKind.Rectangle, rectangle.Kind);
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, ellipse.Kind);
        Assert.Equal("neutral", text.Content);
        Assert.Equal("resource:neutral", image.Content);
        Assert.Equal(bounds.TopLeft, text.TextAnchor);
        Assert.Equal(Canvas2DTextAlignment.Start, text.TextAlignment);
        Assert.Equal(Canvas2DTextBaseline.Top, text.TextBaseline);
        Assert.All([rectangle, ellipse, text, image], geometry =>
            Assert.Equal(bounds, geometry.Bounds));
    }

    [Fact]
    public void TextGeometryPreservesExplicitLocalAnchorAlignmentAndBaseline()
    {
        var bounds = new RectD(10d, 20d, 80d, 40d);
        var anchor = new PointD(50d, 40d);

        var first = Canvas2DSceneGeometry.Text(
            bounds,
            "neutral",
            anchor,
            Canvas2DTextAlignment.Center,
            Canvas2DTextBaseline.Middle);
        var equivalent = Canvas2DSceneGeometry.Text(
            bounds,
            "neutral",
            anchor,
            Canvas2DTextAlignment.Center,
            Canvas2DTextBaseline.Middle);

        Assert.Equal(anchor, first.TextAnchor);
        Assert.Equal(Canvas2DTextAlignment.Center, first.TextAlignment);
        Assert.Equal(Canvas2DTextBaseline.Middle, first.TextBaseline);
        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, Canvas2DSceneGeometry.Text(bounds, "neutral"));
    }

    [Fact]
    public void TextGeometryRetainsBinaryCompatibleTwoParameterFactory()
    {
        var factories = typeof(Canvas2DSceneGeometry).GetMethods()
            .Where(method => method.IsPublic && method.IsStatic && method.Name == "Text")
            .ToArray();

        Assert.Contains(factories, method =>
            method.GetParameters().Select(static parameter => parameter.ParameterType)
                .SequenceEqual([typeof(RectD), typeof(string)]));
        Assert.Contains(factories, method =>
            method.GetParameters().Select(static parameter => parameter.ParameterType)
                .SequenceEqual(
                [
                    typeof(RectD),
                    typeof(string),
                    typeof(PointD),
                    typeof(Canvas2DTextAlignment),
                    typeof(Canvas2DTextBaseline),
                ]));
        Assert.All(
            factories.SelectMany(static method => method.GetParameters()),
            parameter => Assert.False(parameter.IsOptional));
    }

    [Fact]
    public void OriginTraceRequiresDeclaredIdentityCategoriesAndCanonicalizesRelatedIds()
    {
        var projectedId = new ProjectedObjectId("projected:node");
        var relatedA = new ProjectedObjectId("projected:a");
        var relatedB = new ProjectedObjectId("projected:b");
        var trace = new Canvas2DSceneOriginTrace(
            Canvas2DSceneOriginCategory.SemanticElement |
            Canvas2DSceneOriginCategory.VisualState |
            Canvas2DSceneOriginCategory.ProjectedRuntimeObject,
            new SemanticElementId("semantic:node"),
            new VisualStateId("visual:node"),
            projectedId,
            relatedProjectedObjectIds: [relatedB, relatedA]);

        Assert.Equal(
            new[] { relatedA, relatedB }.AsEnumerable(),
            trace.RelatedProjectedObjectIds.AsEnumerable());
        Assert.Throws<ArgumentException>(() => new Canvas2DSceneOriginTrace(
            Canvas2DSceneOriginCategory.SemanticElement));
        Assert.Throws<ArgumentException>(() => new Canvas2DSceneOriginTrace(
            Canvas2DSceneOriginCategory.EditorState));
        Assert.Throws<ArgumentException>(() => new Canvas2DSceneOriginTrace(
            Canvas2DSceneOriginCategory.ProjectedRuntimeObject,
            projectedObjectId: projectedId,
            relatedProjectedObjectIds: [projectedId]));
    }

    [Fact]
    public void SceneItemPreservesImmutableRenderingPlanAndPersistentAppearance()
    {
        var projectedId = new ProjectedObjectId("projected:node");
        var item = new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForProjected(projectedId),
            Canvas2DSceneLayer.Content,
            12,
            Canvas2DSceneGeometry.Rectangle(new RectD(10d, 20d, 80d, 40d)),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.ProjectedRuntimeObject,
                projectedObjectId: projectedId),
            transform: Matrix2D.CreateTranslation(2d, 3d),
            clip: new RectD(0d, 0d, 200d, 100d),
            style: new Canvas2DSceneStyle("white", "black", 2d, [3d, 1d], 0.75d),
            hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.FillOrStroke, 4d),
            persistentAppearance:
            [
                new KeyValuePair<string, PropertyValue>(
                    "test:color",
                    PropertyValue.FromText("blue")),
            ]);

        var equivalent = new Canvas2DSceneItem(
            item.Id,
            item.Layer,
            item.ZIndex,
            item.Geometry,
            item.Origin,
            item.Transform,
            item.Clip,
            item.Style,
            item.IsVisible,
            item.HitTestPolicy,
            item.PersistentAppearance,
            bounds: item.Bounds);

        Assert.Equal("blue", item.PersistentAppearance["test:color"].TextValue);
        Assert.Equal(new RectD(12d, 23d, 80d, 40d), item.Bounds);
        Assert.Equal(item, equivalent);
        Assert.All(
            typeof(Canvas2DSceneItem).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void ContributionDefensivelyCopiesAndCanonicallyOrdersItems()
    {
        var connector = CreateItem("scene:connector", Canvas2DSceneLayer.Connector, 0);
        var frontContent = CreateItem("scene:z-content", Canvas2DSceneLayer.Content, 2);
        var backContent = CreateItem("scene:a-content", Canvas2DSceneLayer.Content, 2);
        var callerItems = new List<Canvas2DSceneItem>
        {
            connector,
            frontContent,
            backContent,
        };
        var contribution = new Canvas2DSceneContribution(callerItems);

        callerItems.Clear();

        Assert.Equal(
            new[] { backContent.Id, frontContent.Id, connector.Id }.AsEnumerable(),
            contribution.Items.Select(static item => item.Id));
        Assert.Throws<ArgumentException>(() =>
            new Canvas2DSceneContribution([backContent, backContent]));
    }

    [Fact]
    public void SceneObjectIdentityIsStableAndRoleSensitive()
    {
        var projectedId = new ProjectedObjectId("projected:node");

        Assert.Equal(
            Canvas2DSceneObjectIdentity.ForProjected(projectedId, "body"),
            Canvas2DSceneObjectIdentity.ForProjected(new ProjectedObjectId("projected:node"), "body"));
        Assert.NotEqual(
            Canvas2DSceneObjectIdentity.ForProjected(projectedId, "body"),
            Canvas2DSceneObjectIdentity.ForProjected(projectedId, "label"));
        Assert.NotEqual(
            Canvas2DSceneObjectIdentity.ForEditorState("selection"),
            Canvas2DSceneObjectIdentity.ForConfiguration("selection"));
    }

    private static Canvas2DSceneItem CreateItem(
        string id,
        Canvas2DSceneLayer layer,
        int zIndex) =>
        new(
            new SceneObjectId(id),
            layer,
            zIndex,
            Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 10d, 10d)),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.Configuration,
                stableSourceKey: id));
}
