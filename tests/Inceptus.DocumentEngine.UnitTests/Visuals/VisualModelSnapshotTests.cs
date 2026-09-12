using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Visuals;

public sealed class VisualModelSnapshotTests
{
    private static readonly DocumentId TestDocumentId = new("test:document");
    private static readonly DocumentRevision TestRevision = new(7);

    [Fact]
    public void VisualStatesAreDefensivelyCopiedAndOrderedByOrdinalIdentity()
    {
        var states = new List<VisualStateSnapshot>
        {
            State("test:z", "test:semantic-z"),
            State("test:a", "test:semantic-a"),
            State("test:A", "test:semantic-A"),
        };

        var snapshot = new VisualModelSnapshot(TestDocumentId, TestRevision, states);
        states.Clear();

        Assert.Equal(["test:A", "test:a", "test:z"],
            snapshot.VisualStates.Select(state => state.Id.Value));
        Assert.Equal(3, snapshot.Count);
    }

    [Fact]
    public void DuplicateVisualIdentitiesAndNullEntriesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new VisualModelSnapshot(
            TestDocumentId,
            TestRevision,
            [State("test:duplicate", "test:a"), State("test:duplicate", "test:b")]));
        Assert.Throws<ArgumentException>(() => new VisualModelSnapshot(
            TestDocumentId,
            TestRevision,
            [null!]));
    }

    [Fact]
    public void VisualStatePreservesPersistentAppearanceAndSemanticAssociation()
    {
        var route = new List<PointD> { new(1d, 2d), new(3d, 4d) };
        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:stroke", PropertyValue.FromText("blue")),
        };
        var state = new VisualStateSnapshot(
            new VisualStateId("test:visual"),
            new SemanticElementId("test:semantic"),
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Pinned,
            route,
            properties);
        route.Clear();
        properties.Clear();

        Assert.Equal("test:visual", state.Id.Value);
        Assert.Equal("test:semantic", state.SemanticElementId.Value);
        Assert.Equal(new PointD(10d, 20d), state.Position);
        Assert.Equal(new SizeD(100d, 60d), state.Size);
        Assert.Equal(VisualPlacementMode.Pinned, state.PlacementMode);
        Assert.Equal(
            new[] { new PointD(1d, 2d), new PointD(3d, 4d) },
            state.Route.ToArray());
        Assert.Equal("blue", state.Properties["test:stroke"].TextValue);
    }

    [Fact]
    public void SemanticReferenceIsExposedForLaterCrossModelValidation()
    {
        var state = State("test:visual", "test:not-present-in-this-visual-snapshot");
        var snapshot = new VisualModelSnapshot(TestDocumentId, TestRevision, [state]);

        Assert.Equal(
            new SemanticElementId("test:not-present-in-this-visual-snapshot"),
            snapshot.VisualStates[0].SemanticElementId);
    }

    [Fact]
    public void LookupIsStableAndCaseSensitive()
    {
        var expected = State("test:visual", "test:semantic");
        var snapshot = new VisualModelSnapshot(TestDocumentId, TestRevision, [expected]);

        Assert.True(snapshot.TryGetVisualState(expected.Id, out var found));
        Assert.Same(expected, found);
        Assert.False(snapshot.TryGetVisualState(new VisualStateId("TEST:VISUAL"), out found));
        Assert.Null(found);
    }

    [Fact]
    public void IndependentVisualStatesAndModelsUseDeepStructuralEquality()
    {
        var firstState = State(
            "test:visual",
            "test:semantic",
            VisualPlacementMode.Manual,
            [new PointD(0d, 0d), new PointD(2d, 2d)]);
        var sameState = State(
            "test:visual",
            "test:semantic",
            VisualPlacementMode.Manual,
            [new PointD(0d, 0d), new PointD(2d, 2d)]);
        var differentState = State(
            "test:visual",
            "test:semantic",
            VisualPlacementMode.Pinned,
            [new PointD(0d, 0d), new PointD(2d, 2d)]);
        var first = new VisualModelSnapshot(TestDocumentId, TestRevision, [firstState]);
        var same = new VisualModelSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(7),
            [sameState]);
        var different = new VisualModelSnapshot(TestDocumentId, TestRevision, [differentState]);

        Assert.Equal(firstState, sameState);
        Assert.Equal(firstState.GetHashCode(), sameState.GetHashCode());
        Assert.NotEqual(firstState, differentState);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void PlacementModeMustBeDefined()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisualStateSnapshot(
            new VisualStateId("test:visual"),
            new SemanticElementId("test:semantic"),
            new PointD(0d, 0d),
            new SizeD(1d, 1d),
            (VisualPlacementMode)99));
    }

    [Fact]
    public void ExposedRouteCannotBeModified()
    {
        var state = State(
            "test:visual",
            "test:semantic",
            route: [new PointD(0d, 0d)]);
        var mutableView = (IList<PointD>)state.Route;

        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Add(new PointD(1d, 1d)));
        Assert.Single(state.Route);
    }

    private static VisualStateSnapshot State(
        string id,
        string semanticId,
        VisualPlacementMode placementMode = VisualPlacementMode.Automatic,
        IEnumerable<PointD>? route = null) =>
        new(
            new VisualStateId(id),
            new SemanticElementId(semanticId),
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            placementMode,
            route,
            [new("test:style", PropertyValue.FromText("default"))]);
}
