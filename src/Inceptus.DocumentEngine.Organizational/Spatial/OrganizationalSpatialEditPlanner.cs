using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Spatial;

/// <summary>
/// Pure mapping from one recognized Organizational presentation region to the ordinary
/// command path. It owns neither a Document nor a History store.
/// </summary>
public sealed class OrganizationalSpatialEditPlanner : ICanvas2DSpatialEditPlanner
{
    private readonly IOrganizationalElementEligibilityPolicy _eligibilityPolicy;

    public OrganizationalSpatialEditPlanner(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        _eligibilityPolicy = eligibilityPolicy;
    }

    public Canvas2DSpatialEditPlanResult Plan(Canvas2DSpatialEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DestinationRegion.ModelProfileId != OrganizationalModelProfile.Id)
        {
            return Failure(
                "The destination region is not owned by the Organizational profile.",
                request.DestinationRegion.Id.Value);
        }

        if (!request.Document.SemanticModel.ModelProfiles.IsAvailable(
                OrganizationalModelProfile.Id))
        {
            return Failure(
                "The Organizational profile is unavailable.",
                request.DestinationRegion.Id.Value);
        }

        if (!request.Document.SemanticModel.TryGetScope(request.ActiveScopeId, out _))
        {
            return Failure(
                $"Active scope '{request.ActiveScopeId}' does not exist.",
                request.ActiveScopeId.Value);
        }

        if (request.BaseCommand.TargetDocumentId != request.Document.DocumentId ||
            request.BaseCommand.ExpectedRevision != request.Document.Revision)
        {
            return Failure(
                "The base command does not target the supplied Document snapshot and revision.",
                request.BaseCommand.TypeId.Value);
        }

        var destinationPoolId =
            request.DestinationRegion.ContainerSemanticElementId;
        if (destinationPoolId is not null &&
            !IsPoolInActiveScope(request, destinationPoolId))
        {
            return Failure(
                "The destination Pool is not a current Pool in the exact active scope.",
                destinationPoolId.Value);
        }

        return request.Kind switch
        {
            Canvas2DSpatialEditKind.Creation => PlanCreation(
                request,
                destinationPoolId),
            Canvas2DSpatialEditKind.Move => PlanMove(
                request,
                destinationPoolId),
            _ => Failure(
                "The spatial edit kind is not supported.",
                request.DestinationRegion.Id.Value),
        };
    }

    private static Canvas2DSpatialEditPlanResult PlanCreation(
        Canvas2DSpatialEditRequest request,
        SemanticElementId? destinationPoolId)
    {
        if (destinationPoolId is null)
        {
            return Canvas2DSpatialEditPlanResult.Success(request.BaseCommand);
        }

        // Eligibility is intentionally resolved by the assignment child after the base
        // creation proposal exists. Attached Boundary Events and relationships then no-op,
        // while ordinary eligible nodes receive the destination assignment atomically.
        return Canvas2DSpatialEditPlanResult.Success(
            new CompoundDocumentCommand(
                request.Document.DocumentId,
                request.Document.Revision,
                [
                    request.BaseCommand,
                    new AssignOrganizationalElementCommand(
                        request.Document.DocumentId,
                        request.Document.Revision,
                        request.SemanticElementId,
                        destinationPoolId,
                        onlyIfEligible: true),
                ]));
    }

    private Canvas2DSpatialEditPlanResult PlanMove(
        Canvas2DSpatialEditRequest request,
        SemanticElementId? destinationPoolId)
    {
        if (!request.Document.SemanticModel.TryGetElement(
                request.SemanticElementId,
                out var source) ||
            source is null)
        {
            return Failure(
                $"Move source '{request.SemanticElementId}' does not exist as a semantic element.",
                request.SemanticElementId.Value);
        }

        if (!request.Document.SemanticModel.TryGetScope(source.Id, out var sourceScope) ||
            sourceScope is null ||
            sourceScope.Id != request.ActiveScopeId)
        {
            return Failure(
                $"Move source '{request.SemanticElementId}' is not contained by the exact active scope.",
                request.SemanticElementId.Value);
        }

        // Attached elements derive their presentation from their structural owner and do
        // not acquire an independent Pool assignment.
        if (!OrganizationalSemantics.IsDirectlyAssignable(source, _eligibilityPolicy))
        {
            return Canvas2DSpatialEditPlanResult.Success(request.BaseCommand);
        }

        _ = OrganizationalSemantics.TryGetAssignedPoolId(
            request.Document.SemanticModel,
            source.Id,
            out var currentPoolId);
        if (currentPoolId == destinationPoolId)
        {
            return Canvas2DSpatialEditPlanResult.Success(request.BaseCommand);
        }

        ICommand assignmentCommand = destinationPoolId is null
            ? new UnassignOrganizationalElementCommand(
                request.Document.DocumentId,
                request.Document.Revision,
                source.Id)
            : new AssignOrganizationalElementCommand(
                request.Document.DocumentId,
                request.Document.Revision,
                source.Id,
                destinationPoolId);
        return Canvas2DSpatialEditPlanResult.Success(
            new CompoundDocumentCommand(
                request.Document.DocumentId,
                request.Document.Revision,
                [request.BaseCommand, assignmentCommand]));
    }

    private static bool IsPoolInActiveScope(
        Canvas2DSpatialEditRequest request,
        SemanticElementId poolId) =>
        request.Document.SemanticModel.TryGetElement(poolId, out var pool) &&
        pool is not null &&
        OrganizationalSemantics.IsPool(pool) &&
        request.Document.SemanticModel.GetScope(pool.Id).Id == request.ActiveScopeId;

    private static Canvas2DSpatialEditPlanResult Failure(
        string message,
        string sourceIdentity) =>
        Canvas2DSpatialEditPlanResult.Failure(
        [
            new Diagnostic(
                "ORGANIZATIONAL_SPATIAL_EDIT_INVALID",
                DiagnosticSeverity.Error,
                message,
                sourceIdentity),
        ]);
}
