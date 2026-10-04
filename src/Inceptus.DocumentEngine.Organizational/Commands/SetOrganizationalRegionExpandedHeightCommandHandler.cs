using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.Organizational.Commands;

internal sealed class SetOrganizationalRegionExpandedHeightCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not SetOrganizationalRegionExpandedHeightCommand height || Find(document, height) is null)
            return ValueTask.FromResult(CommandHandlerResult.Failure([new Diagnostic(
                "ORGANIZATIONAL_REGION_HEIGHT_INVALID", DiagnosticSeverity.Error,
                "Expanded height requires a current saved Organizational region in the requested scope.", command.TypeId.Value)]));
        // Capacity is validated against the complete final proposal by the shared preparation phase.
        return ValueTask.FromResult(CommandHandlerResult.SuccessWithPreparation(document, [],
            [new SpatialRegionHeightIntent(height.ScopeId, height.RegionId, height.ExpandedHeight)],
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: NodeGeometryPipelineImpact.PreserveAll));
    }

    internal static SpatialRegionGeometrySnapshot? Find(DocumentSnapshot document,
        SetOrganizationalRegionExpandedHeightCommand command) =>
        document.VisualModel.RoutingScopes?.FirstOrDefault(scope => scope.ScopeId == command.ScopeId)?
            .Geometry.Regions.FirstOrDefault(region => region.Id == command.RegionId &&
                region.ProfileId == OrganizationalModelProfile.Id);
}
