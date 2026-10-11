using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

public sealed class SetBpmnPoolParticipantCommand : ICommand
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/set-pool-participant");
    public SetBpmnPoolParticipantCommand(DocumentId targetDocumentId, DocumentRevision expectedRevision,
        SemanticElementId poolId, SemanticElementId? participantId)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(poolId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        PoolId = poolId;
        ParticipantId = participantId;
    }
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public SemanticElementId PoolId { get; }
    public SemanticElementId? ParticipantId { get; }
    // Null is a new assignment; even an empty delta marks History replay.
    internal ImmutableArray<BpmnPoolRegionDelta>? RegionDeltas { get; init; }
    public CommandTypeId TypeId => KnownTypeId;
    public CommandCategory Category => CommandCategory.Document;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel | AuthoritativeDocumentComponent.VisualModel;
}

internal sealed class BpmnPoolParticipantCommandHandler : ICommandHandler, ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        if (command is not SetBpmnPoolParticipantCommand assignment ||
            !document.SemanticModel.TryGetElement(assignment.PoolId, out var pool) || pool is null || !OrganizationalSemantics.IsPool(pool) ||
            assignment.ParticipantId is { } id && (!document.SemanticModel.TryGetElement(id, out var participant) ||
                participant?.TypeId != BpmnSemanticTypes.Participant || participant.ContainmentKind != SemanticElementContainmentKind.Document))
            return [new(BpmnEffectiveParticipant.AssignmentInvalid, DiagnosticSeverity.Error,
                "Select an existing Participant or leave the Pool unassigned.", command.TypeId.Value)];
        if (OrganizationalSemantics.GetParticipantId(pool) == assignment.ParticipantId)
            return [new(CommandExecutionDiagnosticCodes.SemanticPropertyUnchanged, DiagnosticSeverity.Error,
                "The Pool already has this Participant assignment.", pool.Id.Value)];
        return BpmnPoolFlowValidation.ValidateChanges(document, Replace(document, assignment), [pool.Id]);
    }

    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Validate(command, document);
        return ValueTask.FromResult(diagnostics.IsEmpty
            ? CommandHandlerResult.Success(Replace(document, (SetBpmnPoolParticipantCommand)command))
            : CommandHandlerResult.Failure(diagnostics));
    }

    internal static DocumentSnapshot Replace(DocumentSnapshot document, SetBpmnPoolParticipantCommand command)
    {
        document.SemanticModel.TryGetElement(command.PoolId, out var pool);
        var properties = pool!.Properties.Where(entry => entry.Key != OrganizationalSemanticProperties.ParticipantId);
        if (command.ParticipantId is { } id)
            properties = properties.Append(new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(id.Value)));
        var replacement = new SemanticElementSnapshot(pool.Id, pool.TypeId, properties, pool.AttachedToElementId, pool.ContainmentKind);
        var semantic = document.SemanticModel;
        var visuals = document.VisualModel;
        var restored = (command.RegionDeltas ?? []).ToDictionary(delta => delta.From.Id);
        var routing = visuals.RoutingScopes?.Select(scope =>
        {
            var geometry = scope.Geometry;
            return new ScopeRoutingSnapshot(scope.ScopeId, new ScopeGeometrySnapshot(geometry.PolicyId,
                geometry.PolicyVersion, geometry.LayoutAlgorithmId, geometry.Configuration, geometry.TextConfiguration,
                geometry.Contributors, geometry.Nodes, geometry.Regions.Select(region => restored.TryGetValue(region.Id, out var delta)
                    ? delta.Apply(region) : region),
                geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors);
        });
        var visual = restored.Count == 0 ? visuals : new VisualModelSnapshot(document.DocumentId, document.Revision,
            visuals.VisualStates, visuals.ProfileElementPresentations, routing, visuals.DocumentRoutingStates);
        var proposed = new DocumentSnapshot(new SemanticModelSnapshot(document.DocumentId, document.Revision,
            semantic.Elements.Select(element => element.Id == pool.Id ? replacement : element), semantic.Relationships,
            semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments),
            visual, document.Metadata, document.Publication);
        if (command.RegionDeltas is not null) return proposed;
        return command.ParticipantId is not null
            ? OrganizationalParticipantGrouping.AppendAssignedPool(proposed, command.PoolId, document)
            : OrganizationalParticipantGrouping.SeparateUnassignedPool(proposed, command.PoolId, document);
    }
}

/// <summary>Only the region components changed by assignment belong to its History action.</summary>
internal sealed record BpmnPoolRegionDelta(SpatialRegionGeometrySnapshot From, SpatialRegionGeometrySnapshot To)
{
    internal SpatialRegionGeometrySnapshot Apply(SpatialRegionGeometrySnapshot current)
    {
        if (From.ContentBounds is not { } before || To.ContentBounds is not { } after ||
            current.ContentBounds is not { } bounds) return To;
        var height = From.ExpandedHeight != To.ExpandedHeight ? To.ExpandedHeight : current.ExpandedHeight;
        var previousTransform = From.LocalToScopeTransform!.Value;
        var nextTransform = To.LocalToScopeTransform!.Value;
        var transform = current.LocalToScopeTransform!.Value;
        return new(current.Id, current.ProfileId, current.ContainerSemanticElementId, height,
            Inceptus.DocumentEngine.Contracts.Geometry.Matrix2D.CreateTranslation(
                previousTransform.OffsetX != nextTransform.OffsetX ? nextTransform.OffsetX : transform.OffsetX,
                previousTransform.OffsetY != nextTransform.OffsetY ? nextTransform.OffsetY : transform.OffsetY),
            new Inceptus.DocumentEngine.Contracts.Geometry.RectD(before.X != after.X ? after.X : bounds.X,
                before.Y != after.Y ? after.Y : bounds.Y, before.Width != after.Width ? after.Width : bounds.Width, height));
    }
}

internal sealed class BpmnPoolMembershipFlowValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        var id = command switch
        {
            AssignOrganizationalElementCommand assign => assign.SemanticElementId,
            UnassignOrganizationalElementCommand unassign => unassign.SemanticElementId,
            DeleteOrganizationalPoolCommand deletion => deletion.PoolId,
            _ => null,
        };
        if (id is null) return [];
        if (command is AssignOrganizationalElementCommand { OnlyIfEligible: true } &&
            (!document.SemanticModel.TryGetElement(id, out var element) || element?.AttachedToElementId is not null)) return [];
        var assignments = document.SemanticModel.ProfileAssignments.Where(assignment =>
            assignment.ProfileId != OrganizationalModelProfile.Id ||
            (assignment.SemanticElementId != id && (command is not DeleteOrganizationalPoolCommand || assignment.ContainerSemanticElementId != id)));
        if (command is AssignOrganizationalElementCommand assignPool)
            assignments = assignments.Append(new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, id, assignPool.PoolId));
        var semantic = document.SemanticModel;
        var proposed = new DocumentSnapshot(new SemanticModelSnapshot(document.DocumentId, document.Revision,
            semantic.Elements.Where(element => command is not DeleteOrganizationalPoolCommand || element.Id != id),
            semantic.Relationships, semantic.NestedScopes,
            semantic.ScopeMemberships.Where(member => command is not DeleteOrganizationalPoolCommand || member.SemanticElementId != id),
            semantic.ModelProfiles, assignments), document.VisualModel, document.Metadata, document.Publication);
        return BpmnPoolFlowValidation.ValidateChanges(document, proposed, [id]);
    }
}

internal sealed class BpmnPoolProfileAvailabilityValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        if (command is not SetModelProfileAvailabilityCommand availability ||
            !availability.Changes.Any(change => change.ProfileId == OrganizationalModelProfile.Id && !change.IsAvailable)) return [];
        bool RequiresBoundary(SemanticElementId id) => document.SemanticModel.TryGetElement(id, out var element) &&
            element is not null && (OrganizationalSemantics.IsPool(element) || element.TypeId == BpmnSemanticTypes.Participant);
        return document.SemanticModel.Relationships.Any(flow => flow.TypeId == BpmnSemanticTypes.MessageFlow &&
            (RequiresBoundary(flow.SourceId) || RequiresBoundary(flow.TargetId)))
            ? [new(BpmnMessageFlowSemantics.PresentationUnavailable, DiagnosticSeverity.Error,
                "Direct Pool and Participant Message Flow endpoints require the Pool presentation profile.")]
            : [];
    }
}

internal static class BpmnPoolFlowValidation
{
    internal static ImmutableArray<Diagnostic> ValidateChanges(DocumentSnapshot before, DocumentSnapshot after, HashSet<SemanticElementId> changed)
    {
        bool Affected(SemanticElementId id) => changed.Contains(id) ||
            OrganizationalSemantics.GetOwningPoolId(before.SemanticModel, id) is { } oldPool && changed.Contains(oldPool) ||
            OrganizationalSemantics.GetOwningPoolId(after.SemanticModel, id) is { } newPool && changed.Contains(newPool) ||
            after.SemanticModel.TryGetElement(id, out var element) && element?.AttachedToElementId is { } owner && changed.Contains(owner) ||
            before.SemanticModel.Elements.Concat(after.SemanticModel.Elements).Any(pool => changed.Contains(pool.Id) &&
                OrganizationalSemantics.IsPool(pool) && OrganizationalSemantics.GetParticipantId(pool) == id);
        return [.. after.SemanticModel.Relationships.Where(flow => Affected(flow.SourceId) || Affected(flow.TargetId))
            .SelectMany(flow => flow.TypeId == BpmnSemanticTypes.SequenceFlow
                ? BpmnEffectiveParticipant.ValidateSequenceEndpoints(after, flow.SourceId, flow.TargetId)
                : flow.TypeId == BpmnSemanticTypes.MessageFlow
                    ? BpmnMessageFlowSemantics.ValidateEndpointContext(after, flow.SourceId, flow.TargetId) : [])];
    }
}
