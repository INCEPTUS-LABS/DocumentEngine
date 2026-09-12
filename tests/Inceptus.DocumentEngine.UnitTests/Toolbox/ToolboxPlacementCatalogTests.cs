using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.UnitTests.Toolbox;

public sealed class ToolboxPlacementCatalogTests
{
    [Fact]
    public void CatalogOrdersRegistrationsAndProvidesDeterministicLookup()
    {
        var zulu = Registration("test:item:zulu");
        var alpha = Registration("test:item:alpha");
        var catalog = new ToolboxPlacementCatalog([zulu, alpha]);

        Assert.Equal(
            ["test:item:alpha", "test:item:zulu"],
            catalog.Registrations.Select(static entry => entry.ToolboxItemId.Value));
        Assert.True(catalog.TryGetRegistration(alpha.ToolboxItemId, out var resolved));
        Assert.Same(alpha, resolved);
        Assert.False(catalog.TryGetRegistration(
            new ToolboxItemId("test:item:missing"),
            out resolved));
        Assert.Null(resolved);
        Assert.Throws<ArgumentNullException>(() =>
            catalog.TryGetRegistration(null!, out _));
    }

    [Fact]
    public void CatalogRejectsNullDuplicateAndUnknownRegistrations()
    {
        var first = Registration("test:item:duplicate");
        var duplicate = Registration("test:item:duplicate");
        var toolboxCatalog = ToolboxCatalog("test:item:visible");

        Assert.Throws<ArgumentException>(() =>
            new ToolboxPlacementCatalog([null!]));
        Assert.Throws<ArgumentException>(() =>
            new ToolboxPlacementCatalog([first, duplicate]));
        Assert.Throws<ArgumentException>(() =>
            new ToolboxPlacementCatalog([first], toolboxCatalog));
    }

    [Fact]
    public void CrossValidationAcceptsRegisteredItemsWithoutRequiringEveryVisibleItem()
    {
        var registered = Registration("test:item:registered");
        var toolboxCatalog = ToolboxCatalog(
            "test:item:registered",
            "test:item:selection-only");
        var catalog = new ToolboxPlacementCatalog([registered], toolboxCatalog);

        Assert.True(catalog.TryGetRegistration(registered.ToolboxItemId, out _));
        Assert.False(catalog.TryGetRegistration(
            new ToolboxItemId("test:item:selection-only"),
            out _));
    }

    [Fact]
    public void CatalogDefensivelyCopiesAndExposesImmutableRegistrations()
    {
        var registration = Registration("test:item:stable");
        var mutable = new List<ToolboxPlacementRegistration> { registration };
        var catalog = new ToolboxPlacementCatalog(mutable);
        mutable.Clear();

        Assert.Same(registration, Assert.Single(catalog.Registrations));
        AssertReadOnly(catalog.Registrations);
    }

    [Fact]
    public void EmptyCatalogIsStableAndRegistrationRequiresAllInputs()
    {
        Assert.Empty(ToolboxPlacementCatalog.Empty.Registrations);
        Assert.False(ToolboxPlacementCatalog.Empty.TryGetRegistration(
            new ToolboxItemId("test:item:missing"),
            out _));

        var itemId = new ToolboxItemId("test:item");
        var factory = new TestFactory();
        var registration = new ToolboxPlacementRegistration(itemId, factory);
        Assert.Equal(itemId, registration.ToolboxItemId);
        Assert.Same(factory, registration.CommandFactory);
        Assert.Null(registration.CandidateProvider);

        var provider = new TestCandidateProvider();
        var candidateRegistration = new ToolboxPlacementRegistration(
            itemId,
            factory,
            provider);
        Assert.Same(provider, candidateRegistration.CandidateProvider);
        Assert.Throws<ArgumentNullException>(() =>
            new ToolboxPlacementRegistration(null!, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new ToolboxPlacementRegistration(itemId, null!));
    }

    private static ToolboxPlacementRegistration Registration(string itemId) =>
        new(new ToolboxItemId(itemId), new TestFactory());

    private static ToolboxCatalog ToolboxCatalog(params string[] itemIds)
    {
        var groupId = new ToolboxGroupId("test:group");
        var typeId = new SemanticTypeId("test:type");
        return new ToolboxCatalog(
        [
            new ToolboxContribution(
                [new ToolboxGroupDefinition(groupId, "Test", 0)],
                itemIds.Select((itemId, index) => new ToolboxItemDefinition(
                    new ToolboxItemId(itemId),
                    typeId,
                    groupId,
                    itemId,
                    index,
                    new ToolboxIconDescriptor("test:icon", "□")))),
        ]);
    }

    private static void AssertReadOnly<T>(ImmutableArray<T> values)
    {
        var mutableView = (IList<T>)values;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    private sealed class TestFactory : IToolboxPlacementCommandFactory
    {
        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request) =>
            throw new NotSupportedException();
    }

    private sealed class TestCandidateProvider : IToolboxPlacementCandidateProvider
    {
        public ToolboxPlacementCandidate? ResolveCandidate(ToolboxPlacementRequest request) =>
            throw new NotSupportedException();
    }
}
