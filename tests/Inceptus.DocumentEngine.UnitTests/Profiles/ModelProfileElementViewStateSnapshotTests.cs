using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Profiles;

public sealed class ModelProfileElementViewStateSnapshotTests
{
    [Fact]
    public void SparseCollapsedStateIsDeterministicIndependentAndImmutable()
    {
        var profile = new ModelProfileId("test:profile");
        var first = new SemanticElementId("test:participant:a");
        var second = new SemanticElementId("test:participant:b");

        var both = ModelProfileElementViewStateSnapshot.Empty
            .WithCollapsed(profile, second, isCollapsed: true)
            .WithCollapsed(profile, first, isCollapsed: true);

        Assert.True(both.IsCollapsed(profile, first));
        Assert.True(both.IsCollapsed(profile, second));
        Assert.Equal(first, both.CollapsedElements[0].SemanticElementId);
        Assert.Equal(second, both.CollapsedElements[1].SemanticElementId);

        var expandedFirst = both.WithCollapsed(profile, first, isCollapsed: false);
        Assert.False(expandedFirst.IsCollapsed(profile, first));
        Assert.True(expandedFirst.IsCollapsed(profile, second));
        Assert.True(both.IsCollapsed(profile, first));
        Assert.Same(expandedFirst, expandedFirst.WithCollapsed(
            profile,
            first,
            isCollapsed: false));
    }

    [Fact]
    public void CleanupRetainsOnlyExistingSemanticElements()
    {
        var profile = new ModelProfileId("test:profile");
        var retained = new SemanticElementId("test:participant:retained");
        var removed = new SemanticElementId("test:participant:removed");
        var state = new ModelProfileElementViewStateSnapshot(
        [
            new ModelProfileElementViewStateKey(profile, removed),
            new ModelProfileElementViewStateKey(profile, retained),
        ]);

        var cleaned = state.RetainSemanticElements([retained]);

        Assert.True(cleaned.IsCollapsed(profile, retained));
        Assert.False(cleaned.IsCollapsed(profile, removed));
        Assert.Equal(retained, Assert.Single(cleaned.CollapsedElements).SemanticElementId);
    }

    [Fact]
    public void DuplicateKeysAreRejected()
    {
        var key = new ModelProfileElementViewStateKey(
            new ModelProfileId("test:profile"),
            new SemanticElementId("test:participant"));

        Assert.Throws<ArgumentException>(() =>
            new ModelProfileElementViewStateSnapshot([key, key]));
    }
}
