using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Organizational;

public sealed partial class OrganizationalPoolSceneContributorBoundsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AvailabilityControlsSpatialContributionWhileVisibilityControlsPainting(
        bool available, bool visible)
    {
        var fixture = CreateDestinationFixture(destinationIndex: 1, count: 3);
        var before = fixture.Document;

        var contribution = ContributeVisibility(fixture, available, visible);

        if (!available)
        {
            Assert.Null(contribution.SpatialPresentationPlan);
            Assert.Empty(contribution.Items);
        }
        else
        {
            var plan = Assert.IsType<Canvas2DSpatialPresentationPlan>(contribution.SpatialPresentationPlan);
            Assert.Equal(3, plan.Regions.Length);
            Assert.Equal(3, plan.VisualPlacements.Length);
            Assert.Equal(visible, contribution.Items.Any(IsPainted));
            Assert.Equal(visible ? 2 : 0,
                contribution.Items.Count(Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable));
            var exclusions = contribution.Items.Where(Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement).ToArray();
            Assert.Equal(2, exclusions.Length);
            Assert.All(exclusions, AssertStructuralExclusion);
            if (!visible)
            {
                Assert.Equal(exclusions, contribution.Items.ToArray());
            }
        }
        Assert.Same(before, fixture.Document);
        Assert.Equal(before.VisualModel, fixture.Document.VisualModel);
        Assert.Equal(before.SemanticModel.ProfileAssignments.ToArray(), fixture.Document.SemanticModel.ProfileAssignments.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HidingGraphicsRetainsExactSpatialPlanAndNonInteractiveStructuralExclusions(bool collapsed)
    {
        var fixture = CreateDestinationFixture(destinationIndex: 0, count: 3);
        var visible = ContributeVisibility(fixture, available: true, visible: true, collapsed);
        var visiblePlan = Assert.IsType<Canvas2DSpatialPresentationPlan>(visible.SpatialPresentationPlan);
        var visibleExclusions = visible.Items.Where(Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement).ToArray();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var hidden = ContributeVisibility(fixture, available: true, visible: false, collapsed);
            var hiddenPlan = Assert.IsType<Canvas2DSpatialPresentationPlan>(hidden.SpatialPresentationPlan);
            Assert.Equal(visiblePlan, hiddenPlan);
            Assert.Equal(visibleExclusions, hidden.Items.ToArray());
            Assert.DoesNotContain(hidden.Items, IsPainted);
            Assert.All(hidden.Items, AssertStructuralExclusion);
            foreach (var region in hiddenPlan.Regions.Where(region => region.ContainerSemanticElementId is not null))
            {
                var isCollapsed = collapsed && region.ContainerSemanticElementId == PoolAId;
                var expectedBounds = new RectD(region.Bounds.Left - 38d, region.Bounds.Top,
                    isCollapsed ? region.Bounds.Width + 38d : 38d, region.Bounds.Height);
                Assert.Single(hidden.Items, item => item.Bounds == expectedBounds);
            }
            Assert.All(hiddenPlan.VisualPlacements, placement => Assert.Equal(!collapsed, placement.IsVisible));
            Assert.Equal(visible, ContributeVisibility(fixture, available: true, visible: true, collapsed));
        }
    }

    private static bool IsPainted(Canvas2DSceneItem item) =>
        item.IsVisible && (item.Style.Fill is not null || item.Style.Stroke is not null);

    private static void AssertStructuralExclusion(Canvas2DSceneItem item)
    {
        Assert.True(item.IsVisible);
        Assert.True(Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement(item));
        Assert.False(Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item));
        Assert.Null(item.Origin.SemanticElementId);
        Assert.Null(item.Origin.VisualStateId);
        Assert.Null(item.Origin.ProjectedObjectId);
        Assert.Equal(Canvas2DSceneOriginCategory.RegisteredExtension, item.Origin.Categories);
        Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode);
        Assert.Equal(Canvas2DSceneLayer.Background, item.Layer);
        Assert.Null(item.Style.Fill);
        Assert.Null(item.Style.Stroke);
    }

    private static Canvas2DSceneContribution ContributeVisibility(
        DestinationFixture fixture, bool available, bool visible, bool collapsed = false)
    {
        var source = fixture.Document;
        var semantic = source.SemanticModel;
        var modelProfiles = available
            ? new ModelProfileStateSnapshot([OrganizationalModelProfile.Id])
            : ModelProfileStateSnapshot.Empty;
        var document = new DocumentSnapshot(new SemanticModelSnapshot(
            source.DocumentId, source.Revision, semantic.Elements, semantic.Relationships,
            semantic.NestedScopes, semantic.ScopeMemberships, modelProfiles, semantic.ProfileAssignments),
            source.VisualModel, source.Metadata);
        var viewState = ModelProfileViewStateSnapshot.Empty.WithPreferredVisibility(
            OrganizationalModelProfile.Id, visible);
        var collapseState = ModelProfileElementViewStateSnapshot.Empty.WithCollapsed(
            OrganizationalModelProfile.Id, PoolAId, collapsed);
        var context = new Canvas2DSceneContributionContext(
            fixture.Graph, fixture.Inputs.Layout, fixture.Inputs.Routing, document.VisualModel,
            EditorStateSnapshot.Empty, Canvas2DSceneConfiguration.Default, fixture.Descriptor,
            new Canvas2DScenePresentationContext(document, semantic.RootScopeId,
                viewState, collapseState, fixture.BaseItems));
        var result = fixture.Contributor.Contribute(context);
        Assert.True(result.Succeeded);
        Assert.Equal(semantic.Elements.ToArray(), document.SemanticModel.Elements.ToArray());
        Assert.Equal(semantic.ProfileAssignments.ToArray(), document.SemanticModel.ProfileAssignments.ToArray());
        Assert.Same(source.VisualModel, document.VisualModel);
        Assert.Equal(source.Revision, document.Revision);
        return Assert.IsType<Canvas2DSceneContribution>(result.Contribution);
    }
}
