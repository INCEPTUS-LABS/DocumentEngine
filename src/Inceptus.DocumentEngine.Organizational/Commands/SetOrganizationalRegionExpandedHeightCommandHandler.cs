using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.Organizational.Commands;

internal sealed class SetOrganizationalRegionExpandedHeightCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not SetOrganizationalRegionExpandedHeightCommand height || Find(document, height) is not { } region)
            return ValueTask.FromResult(CommandHandlerResult.Failure([new Diagnostic(
                "ORGANIZATIONAL_REGION_HEIGHT_INVALID", DiagnosticSeverity.Error,
                "Expanded height requires a current saved Organizational region in the requested scope.", command.TypeId.Value)]));
        // Capacity is validated against the complete final proposal by the shared preparation phase.
        return ValueTask.FromResult(CommandHandlerResult.SuccessWithPreparation(ShiftFollowingRegions(document, height, region), [],
            [new SpatialRegionHeightIntent(height.ScopeId, height.RegionId, height.ExpandedHeight)],
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: NodeGeometryPipelineImpact.PreserveAll));
    }

    private static DocumentSnapshot ShiftFollowingRegions(DocumentSnapshot document,
        SetOrganizationalRegionExpandedHeightCommand command, SpatialRegionGeometrySnapshot resized)
    {
        var delta = command.ExpandedHeight - resized.ExpandedHeight;
        if (delta == 0d || !resized.IsActive ||
            !OrganizationalPoolOrdering.Resolve(document, command.ScopeId).Any(block => block.IsParticipant))
            return document;
        // Group normalization must see the space made by this explicit resize. Otherwise
        // an expanded Participant overlaps the next Pool and is relocated below it.
        // Translate following authored frames by the same delta, preserving order and gaps
        // for both growth and shrinkage; node-local geometry and the resized top stay fixed.
        var replacements = OrganizationalPoolOrdering.Regions(document, command.ScopeId)
            .Where(region => region.IsActive && region.ContentBounds!.Value.Top > resized.ContentBounds!.Value.Top)
            .ToDictionary(region => region.Id, region =>
                OrganizationalPoolOrdering.AtTop(region, region.ContentBounds!.Value.Top + delta));
        return OrganizationalPoolOrdering.Replace(document, document.VisualModel.ProfileElementPresentations, replacements);
    }

    internal static SpatialRegionGeometrySnapshot? Find(DocumentSnapshot document,
        SetOrganizationalRegionExpandedHeightCommand command) =>
        document.VisualModel.RoutingScopes?.FirstOrDefault(scope => scope.ScopeId == command.ScopeId)?
            .Geometry.Regions.FirstOrDefault(region => region.Id == command.RegionId &&
                region.ProfileId == OrganizationalModelProfile.Id);
}
