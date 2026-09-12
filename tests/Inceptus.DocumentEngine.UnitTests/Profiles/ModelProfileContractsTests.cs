using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Profiles;

public sealed class ModelProfileContractsTests
{
    private static readonly ModelProfileId Organizational = new("test:profile:organizational");
    private static readonly ModelProfileId Stage = new("test:profile:stage");

    [Fact]
    public void LegacyStateIsEmptyAndSparseAvailabilityIsDeterministic()
    {
        var legacy = ModelProfileStateSnapshot.Empty;
        var state = new ModelProfileStateSnapshot([Stage, Organizational]);

        Assert.False(legacy.IsAvailable(Organizational));
        Assert.Equal(
            [Organizational, Stage],
            state.AvailableProfileIds.AsEnumerable());
        Assert.Same(state, state.WithAvailability(Stage, isAvailable: true));
        Assert.Equal(
            [Stage],
            state.WithAvailability(Organizational, isAvailable: false)
                .AvailableProfileIds.AsEnumerable());
        Assert.Throws<ArgumentException>(() =>
            new ModelProfileStateSnapshot([Stage, Stage]));
    }

    [Fact]
    public void ViewPreferenceDefaultsVisibleAndAvailabilityControlsEffectiveVisibility()
    {
        var available = new ModelProfileStateSnapshot([Organizational]);
        var defaultView = ModelProfileViewStateSnapshot.Empty;
        var hidden = defaultView.WithPreferredVisibility(Organizational, isVisible: false);

        Assert.True(defaultView.IsPreferredVisible(Organizational));
        Assert.True(defaultView.IsEffectivelyVisible(Organizational, available));
        Assert.False(defaultView.IsEffectivelyVisible(Stage, available));
        Assert.False(hidden.IsEffectivelyVisible(Organizational, available));
        Assert.False(hidden.IsPreferredVisible(Organizational));
        Assert.True(hidden.WithPreferredVisibility(Organizational, true)
            .IsPreferredVisible(Organizational));
    }

    [Fact]
    public void CatalogOrdersDefinitionsAndRejectsDuplicateStableIdentities()
    {
        var catalog = new ModelProfileCatalog(
        [
            new ModelProfileDefinition(Stage, "Stage", order: 20),
            new ModelProfileDefinition(Organizational, "Organizational", order: 10),
        ]);

        Assert.Equal(
            [Organizational, Stage],
            catalog.Definitions.Select(static definition => definition.Id));
        Assert.True(catalog.TryGetDefinition(Stage, out var stage));
        Assert.Equal("Stage", stage?.DisplayName);
        Assert.Throws<ArgumentException>(() => new ModelProfileCatalog(
        [
            new ModelProfileDefinition(Stage, "Stage", order: 1),
            new ModelProfileDefinition(Stage, "Duplicate", order: 999),
        ]));
    }
}
