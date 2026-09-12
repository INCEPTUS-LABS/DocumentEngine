using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.EditorState;

public sealed class EditorStateSnapshotTests
{
    [Fact]
    public void VisualSelectionHasNoPresentationSelectionAuthority()
    {
        var visualId = new VisualStateId("visual:shared-task");
        var first = new EditorStateSnapshot(selection: [visualId]);
        var equivalent = new EditorStateSnapshot(selection: [visualId]);

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.DoesNotContain(typeof(EditorStateSnapshot).GetProperties(),
            property => property.Name.Contains("Presentation", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new EditorStateSnapshot(
            selection: [visualId],
            semanticSceneSelection: new SemanticElementId("semantic:pool")));
    }

    [Fact]
    public void EmptySnapshotHasDeterministicTransientDefaults()
    {
        var snapshot = new EditorStateSnapshot();

        Assert.Empty(snapshot.Selection);
        Assert.Null(snapshot.HoveredObjectId);
        Assert.Null(snapshot.ActiveToolId);
        Assert.Null(snapshot.FocusTargetId);
        Assert.Equal(ViewportSnapshot.Default, snapshot.Viewport);
        Assert.Equal(1d, snapshot.Viewport.Zoom);
        Assert.Equal(default, snapshot.Viewport.Pan);
        Assert.Null(snapshot.ActiveGesture);
        Assert.Empty(snapshot.TemporaryFeedback);
        Assert.Empty(snapshot.ToolState);
        Assert.Equal(snapshot, EditorStateSnapshot.Empty);
    }

    [Fact]
    public void CompleteSnapshotsHaveStructuralValueSemantics()
    {
        var first = CompleteSnapshot();
        var second = CompleteSnapshot();

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(["visual:first", "visual:second"], first.Selection.Select(item => item.Value));
        Assert.Equal("scene:hover", first.HoveredObjectId?.Value);
        Assert.Equal("tool:connect", first.ActiveToolId);
        Assert.Equal("focus:canvas", first.FocusTargetId);
        Assert.Equal(2d, first.Viewport.Zoom);
        Assert.Equal(new VectorD(12d, -4d), first.Viewport.Pan);
        Assert.Equal("gesture:drag-1", first.ActiveGesture?.Id);
        Assert.Equal("feedback:selection", Assert.Single(first.TemporaryFeedback).Id);
        Assert.True(first.ToolState["snap-enabled"].BooleanValue);
    }

    [Fact]
    public void FeedbackPresentationModeIsValidatedAndParticipatesInValueSemantics()
    {
        var defaultFeedback = new EditorFeedbackSnapshot(
            "feedback:preview",
            "test:preview",
            new RectD(10d, 20d, 30d, 40d));
        var sameDefaultFeedback = new EditorFeedbackSnapshot(
            "feedback:preview",
            "test:preview",
            new RectD(10d, 20d, 30d, 40d));
        var contributorOnlyFeedback = new EditorFeedbackSnapshot(
            "feedback:preview",
            "test:preview",
            new RectD(10d, 20d, 30d, 40d),
            presentationMode: EditorFeedbackPresentationMode.ContributorOnly);

        Assert.Equal(EditorFeedbackPresentationMode.Default, defaultFeedback.PresentationMode);
        Assert.Equal(defaultFeedback, sameDefaultFeedback);
        Assert.Equal(defaultFeedback.GetHashCode(), sameDefaultFeedback.GetHashCode());
        Assert.NotEqual(defaultFeedback, contributorOnlyFeedback);
        Assert.NotEqual(defaultFeedback.GetHashCode(), contributorOnlyFeedback.GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorFeedbackSnapshot(
            "feedback:preview",
            "test:preview",
            presentationMode: (EditorFeedbackPresentationMode)int.MaxValue));
    }

    [Fact]
    public void SnapshotDefensivelyCopiesEveryCallerOwnedCollection()
    {
        var selection = new List<VisualStateId> { new("visual:first") };
        var feedbackPoints = new List<PointD> { new(1d, 2d) };
        var feedbackProperties = new List<KeyValuePair<string, PropertyValue>>
        {
            new("color", PropertyValue.FromText("blue")),
        };
        var feedback = new EditorFeedbackSnapshot(
            "feedback:guide",
            "snap-guide",
            points: feedbackPoints,
            properties: feedbackProperties);
        var temporaryFeedback = new List<EditorFeedbackSnapshot> { feedback };
        var toolState = new List<KeyValuePair<string, PropertyValue>>
        {
            new("mode", PropertyValue.FromText("orthogonal")),
        };

        var snapshot = new EditorStateSnapshot(
            selection: selection,
            temporaryFeedback: temporaryFeedback,
            toolState: toolState);

        selection.Clear();
        feedbackPoints.Add(new PointD(3d, 4d));
        feedbackProperties.Clear();
        temporaryFeedback.Clear();
        toolState.Clear();

        Assert.Equal("visual:first", Assert.Single(snapshot.Selection).Value);
        var retainedFeedback = Assert.Single(snapshot.TemporaryFeedback);
        Assert.Single(retainedFeedback.Points);
        Assert.Equal("blue", retainedFeedback.Properties["color"].TextValue);
        Assert.Equal("orthogonal", snapshot.ToolState["mode"].TextValue);
    }

    [Fact]
    public void PublicCollectionsCannotBeModified()
    {
        var snapshot = CompleteSnapshot();

        Assert.True(((IList<VisualStateId>)snapshot.Selection).IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<VisualStateId>)snapshot.Selection).Clear());
        Assert.True(((IList<EditorFeedbackSnapshot>)snapshot.TemporaryFeedback).IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<EditorFeedbackSnapshot>)snapshot.TemporaryFeedback).Clear());
        Assert.True(((IList<PointD>)snapshot.TemporaryFeedback[0].Points).IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PointD>)snapshot.TemporaryFeedback[0].Points).Clear());
    }

    [Fact]
    public void DuplicateOrInvalidTransientIdentitiesAreRejected()
    {
        var selected = new VisualStateId("visual:duplicate");
        var feedback = new EditorFeedbackSnapshot("feedback:duplicate", "guide");

        Assert.Throws<ArgumentException>(() => new EditorStateSnapshot(
            selection: [selected, selected]));
        Assert.Throws<ArgumentException>(() => new EditorStateSnapshot(selection: [null!]));
        Assert.Throws<ArgumentException>(() => new EditorStateSnapshot(
            temporaryFeedback: [feedback, feedback]));
        Assert.Throws<ArgumentException>(() => new EditorStateSnapshot(
            temporaryFeedback: [null!]));
        Assert.Throws<ArgumentException>(() => new EditorStateSnapshot(activeToolId: " "));
        Assert.Throws<ArgumentException>(() => new EditorStateSnapshot(focusTargetId: ""));
        Assert.Throws<ArgumentException>(() => new EditorGestureSnapshot(
            "",
            "drag",
            default,
            default));
        Assert.Throws<ArgumentException>(() => new EditorFeedbackSnapshot("id", " "));
    }

    [Fact]
    public void ViewportRequiresPositiveFiniteZoom()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ViewportSnapshot(0d, default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ViewportSnapshot(-1d, default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ViewportSnapshot(double.PositiveInfinity, default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ViewportSnapshot(double.NaN, default));
    }

    private static EditorStateSnapshot CompleteSnapshot() =>
        new(
            selection: [new("visual:second"), new("visual:first")],
            hoveredObjectId: new SceneObjectId("scene:hover"),
            activeToolId: "tool:connect",
            focusTargetId: "focus:canvas",
            viewport: new ViewportSnapshot(
                2d,
                new VectorD(12d, -4d),
                new RectD(10d, 20d, 800d, 600d)),
            activeGesture: new EditorGestureSnapshot(
                "gesture:drag-1",
                "drag",
                new PointD(10d, 20d),
                new PointD(30d, 40d),
                [new("axis", PropertyValue.FromText("x"))]),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "feedback:selection",
                    "selection-rectangle",
                    new RectD(10d, 20d, 20d, 20d),
                    [new PointD(10d, 20d), new PointD(30d, 40d)],
                    [new("opacity", PropertyValue.FromNumber(0.5d))]),
            ],
            toolState: [new("snap-enabled", PropertyValue.FromBoolean(true))]);
}
