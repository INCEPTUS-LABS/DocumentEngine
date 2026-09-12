using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Commands;

internal static class OrganizationalCommandDiagnosticCodes
{
    internal const string Invalid = "ORGANIZATIONAL_COMMAND_INVALID";
    internal const string ProfileUnavailable = "ORGANIZATIONAL_PROFILE_UNAVAILABLE";
    internal const string DuplicateIdentity = "ORGANIZATIONAL_DUPLICATE_IDENTITY";
    internal const string PoolInvalid = "ORGANIZATIONAL_POOL_INVALID";
    internal const string ScopeInvalid = "ORGANIZATIONAL_SCOPE_INVALID";
    internal const string AssignmentInvalid = "ORGANIZATIONAL_ASSIGNMENT_INVALID";
    internal const string Unchanged = "ORGANIZATIONAL_COMMAND_UNCHANGED";
    internal const string HistoryInvalid = "ORGANIZATIONAL_HISTORY_INVALID";
}

internal sealed class OrganizationalCommandEnvelopeValidator<TCommand> :
    ICommandEnvelopeValidator
    where TCommand : class, ICommand
{
    public ImmutableArray<Diagnostic> Validate(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command is TCommand
            ? []
            : [OrganizationalCommandSupport.Error(
                OrganizationalCommandDiagnosticCodes.Invalid,
                $"Command type '{command.TypeId}' does not use the registered Organizational request shape.",
                command.TypeId.Value)];
    }
}

internal sealed class OrganizationalCommandHandler<TCommand> : ICommandHandler
    where TCommand : OrganizationalCommand
{
    private readonly IOrganizationalElementEligibilityPolicy _eligibilityPolicy;

    internal OrganizationalCommandHandler(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        _eligibilityPolicy = eligibilityPolicy;
    }

    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not TCommand)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                OrganizationalCommandSupport.Error(
                    OrganizationalCommandDiagnosticCodes.Invalid,
                    "The Organizational command request has an invalid shape.",
                    command.TypeId.Value),
            ]));
        }

        if (!OrganizationalCommandSupport.TryApply(
                command,
                document,
                _eligibilityPolicy,
                out var proposed,
                out var diagnostics) ||
            proposed is null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        return ValueTask.FromResult(CommandHandlerResult.Success(
            proposed,
            pipelineInvalidation: PipelineInvalidation.Scene));
    }
}

internal sealed class OrganizationalCommandValidator<TCommand> : ICommandValidator
    where TCommand : OrganizationalCommand
{
    private readonly IOrganizationalElementEligibilityPolicy _eligibilityPolicy;

    internal OrganizationalCommandValidator(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        _eligibilityPolicy = eligibilityPolicy;
    }

    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not TCommand)
        {
            return [OrganizationalCommandSupport.Error(
                OrganizationalCommandDiagnosticCodes.Invalid,
                "The Organizational command request has an invalid shape.",
                command.TypeId.Value)];
        }

        _ = OrganizationalCommandSupport.TryApply(
            command,
            document,
            _eligibilityPolicy,
            out _,
            out var diagnostics);
        return diagnostics;
    }
}

internal sealed class OrganizationalPoolGenericUpdateValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        var targetId = command switch
        {
            UpdateSemanticElementNameCommand update => update.TargetSemanticElementId,
            UpdateSemanticElementPropertyCommand update => update.TargetSemanticElementId,
            _ => null,
        };
        if (targetId is null ||
            !document.SemanticModel.TryGetElement(targetId, out var target) ||
            target is null ||
            target.TypeId != OrganizationalSemanticTypes.Pool)
        {
            return [];
        }

        if (!document.SemanticModel.ModelProfiles.IsAvailable(OrganizationalModelProfile.Id))
        {
            return [OrganizationalCommandSupport.Error(
                OrganizationalCommandDiagnosticCodes.ProfileUnavailable,
                "Organizational Pool properties cannot be edited while the profile is unavailable.",
                targetId.Value)];
        }

        var propertyKey = command switch
        {
            UpdateSemanticElementNameCommand update => update.NamePropertyKey,
            UpdateSemanticElementPropertyCommand update => update.PropertyKey,
            _ => string.Empty,
        };
        var allowed = command switch
        {
            UpdateSemanticElementNameCommand => StringComparer.Ordinal.Equals(
                propertyKey,
                OrganizationalSemanticProperties.Name),
            UpdateSemanticElementPropertyCommand => StringComparer.Ordinal.Equals(
                propertyKey,
                OrganizationalSemanticProperties.Description),
            _ => false,
        };
        return allowed
            ? []
            : [OrganizationalCommandSupport.Error(
                OrganizationalCommandDiagnosticCodes.Invalid,
                "An Organizational Pool exposes only its Name and Description for generic editing.",
                targetId.Value)];
    }
}

internal static class OrganizationalCommandSupport
{
    internal static bool TryApply(
        ICommand command,
        DocumentSnapshot document,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy,
        out DocumentSnapshot? proposed,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        proposed = null;
        if (command is not OrganizationalCommand)
        {
            diagnostics = [Error(
                OrganizationalCommandDiagnosticCodes.Invalid,
                "The command is not an Organizational command.",
                command.TypeId.Value)];
            return false;
        }

        if (!document.SemanticModel.ModelProfiles.IsAvailable(OrganizationalModelProfile.Id))
        {
            diagnostics = [Error(
                OrganizationalCommandDiagnosticCodes.ProfileUnavailable,
                "Organizational data cannot be changed while the profile is unavailable.",
                command.TypeId.Value)];
            return false;
        }

        return command switch
        {
            CreateOrganizationalPoolCommand creation => TryCreatePool(
                creation,
                document,
                eligibilityPolicy,
                out proposed,
                out diagnostics),
            AssignOrganizationalElementCommand assignment => TryAssign(
                assignment,
                document,
                eligibilityPolicy,
                out proposed,
                out diagnostics),
            UnassignOrganizationalElementCommand unassignment => TryUnassign(
                unassignment,
                document,
                eligibilityPolicy,
                out proposed,
                out diagnostics),
            MoveOrganizationalPoolCommand move => TryMovePool(
                move,
                document,
                out proposed,
                out diagnostics),
            DeleteOrganizationalPoolCommand deletion => TryDeletePool(
                deletion,
                document,
                out proposed,
                out diagnostics),
            _ => Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.Invalid,
                    "The Organizational command is not supported.",
                    command.TypeId.Value),
                out proposed,
                out diagnostics),
        };
    }

    private static bool TryCreatePool(
        CreateOrganizationalPoolCommand creation,
        DocumentSnapshot document,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy,
        out DocumentSnapshot? proposed,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (SemanticIdentityExists(document, creation.PoolId))
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.DuplicateIdentity,
                    $"Semantic identity '{creation.PoolId}' already exists.",
                    creation.PoolId.Value),
                out proposed,
                out diagnostics);
        }

        if (!document.SemanticModel.TryGetScope(creation.TargetScopeId, out _))
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.ScopeInvalid,
                    $"Target scope '{creation.TargetScopeId}' does not exist.",
                    creation.TargetScopeId.Value),
                out proposed,
                out diagnostics);
        }

        var orderedPools = OrganizationalSemantics.GetOrderedPoolsInScope(
            document,
            creation.TargetScopeId);
        var requiredMode = orderedPools.IsEmpty
            ? OrganizationalPoolCreationMode.AdoptEligibleUnassigned
            : OrganizationalPoolCreationMode.Empty;
        if (creation.CreationMode != requiredMode)
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.Invalid,
                    orderedPools.IsEmpty
                        ? "The first Pool in a scope must explicitly adopt eligible unassigned elements."
                        : "A subsequent Pool must be created empty.",
                    creation.PoolId.Value),
                out proposed,
                out diagnostics);
        }

        var pool = OrganizationalSemanticFactory.CreatePool(
            creation.PoolId,
            creation.Name,
            creation.Description);
        var memberships = document.SemanticModel.ScopeMemberships.AsEnumerable();
        if (creation.TargetScopeId != document.SemanticModel.RootScopeId)
        {
            memberships = memberships.Append(
                new SemanticElementScopeMembershipSnapshot(
                    creation.PoolId,
                    creation.TargetScopeId));
        }

        var assignments = document.SemanticModel.ProfileAssignments.AsEnumerable();
        if (creation.CreationMode ==
            OrganizationalPoolCreationMode.AdoptEligibleUnassigned)
        {
            var alreadyAssigned = document.SemanticModel.ProfileAssignments
                .Where(static assignment =>
                    assignment.ProfileId == OrganizationalModelProfile.Id)
                .Select(static assignment => assignment.SemanticElementId)
                .ToHashSet();
            assignments = assignments.Concat(document.SemanticModel.Elements
                .Where(element =>
                    OrganizationalSemantics.IsDirectlyAssignable(
                        element,
                        eligibilityPolicy) &&
                    document.SemanticModel.GetScope(element.Id).Id ==
                        creation.TargetScopeId &&
                    !alreadyAssigned.Contains(element.Id))
                .Select(element => new ModelProfileElementAssignmentSnapshot(
                    OrganizationalModelProfile.Id,
                    element.Id,
                    creation.PoolId)));
        }

        var poolIds = orderedPools.Select(static pool => pool.Id).ToHashSet();
        var presentations = document.VisualModel.ProfileElementPresentations
            .Where(presentation =>
                presentation.ProfileId != OrganizationalModelProfile.Id ||
                !poolIds.Contains(presentation.SemanticElementId))
            .Concat(orderedPools.Select(static (pool, index) =>
                new ModelProfileElementPresentationSnapshot(
                    OrganizationalModelProfile.Id,
                    pool.Id,
                    index)))
            .Append(new ModelProfileElementPresentationSnapshot(
                OrganizationalModelProfile.Id,
                creation.PoolId,
                orderedPools.Length));
        proposed = Replace(
            document,
            elements: document.SemanticModel.Elements.Append(pool),
            memberships: memberships,
            assignments: assignments,
            presentations: presentations);
        diagnostics = [];
        return true;
    }

    private static bool TryAssign(
        AssignOrganizationalElementCommand assignment,
        DocumentSnapshot document,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy,
        out DocumentSnapshot? proposed,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (!TryGetPool(document, assignment.PoolId, out var pool, out var poolDiagnostic))
        {
            return Fail(poolDiagnostic!, out proposed, out diagnostics);
        }

        if (!document.SemanticModel.TryGetElement(
                assignment.SemanticElementId,
                out var source) ||
            source is null)
        {
            if (assignment.OnlyIfEligible &&
                document.SemanticModel.TryGetRelationship(
                    assignment.SemanticElementId,
                    out _))
            {
                proposed = document;
                diagnostics = [];
                return true;
            }

            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.AssignmentInvalid,
                    $"Assignment source '{assignment.SemanticElementId}' does not exist as an eligible semantic element.",
                    assignment.SemanticElementId.Value),
                out proposed,
                out diagnostics);
        }

        if (!OrganizationalSemantics.IsDirectlyAssignable(source, eligibilityPolicy))
        {
            if (assignment.OnlyIfEligible)
            {
                proposed = document;
                diagnostics = [];
                return true;
            }

            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.AssignmentInvalid,
                    $"Semantic element '{assignment.SemanticElementId}' is not directly assignable to an Organizational Pool.",
                    assignment.SemanticElementId.Value),
                out proposed,
                out diagnostics);
        }

        var sourceScopeId = document.SemanticModel.GetScope(source.Id).Id;
        var poolScopeId = document.SemanticModel.GetScope(pool!.Id).Id;
        if (sourceScopeId != poolScopeId)
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.ScopeInvalid,
                    "An Organizational assignment cannot cross semantic scopes.",
                    assignment.SemanticElementId.Value),
                out proposed,
                out diagnostics);
        }

        if (OrganizationalSemantics.TryGetAssignedPoolId(
                document.SemanticModel,
                source.Id,
                out var currentPoolId) &&
            currentPoolId == assignment.PoolId)
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.Unchanged,
                    $"Semantic element '{source.Id}' is already assigned to Pool '{assignment.PoolId}'.",
                    source.Id.Value),
                out proposed,
                out diagnostics);
        }

        var assignments = document.SemanticModel.ProfileAssignments
            .Where(candidate =>
                candidate.ProfileId != OrganizationalModelProfile.Id ||
                candidate.SemanticElementId != source.Id)
            .Append(new ModelProfileElementAssignmentSnapshot(
                OrganizationalModelProfile.Id,
                source.Id,
                assignment.PoolId));
        proposed = Replace(document, assignments: assignments);
        diagnostics = [];
        return true;
    }

    private static bool TryUnassign(
        UnassignOrganizationalElementCommand unassignment,
        DocumentSnapshot document,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy,
        out DocumentSnapshot? proposed,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (!document.SemanticModel.TryGetElement(
                unassignment.SemanticElementId,
                out var source) ||
            source is null ||
            !OrganizationalSemantics.IsDirectlyAssignable(source, eligibilityPolicy))
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.AssignmentInvalid,
                    $"Semantic element '{unassignment.SemanticElementId}' is not directly assignable.",
                    unassignment.SemanticElementId.Value),
                out proposed,
                out diagnostics);
        }

        if (!OrganizationalSemantics.TryGetAssignedPoolId(
                document.SemanticModel,
                source.Id,
                out _))
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.Unchanged,
                    $"Semantic element '{source.Id}' is already unassigned.",
                    source.Id.Value),
                out proposed,
                out diagnostics);
        }

        proposed = Replace(
            document,
            assignments: document.SemanticModel.ProfileAssignments.Where(candidate =>
                candidate.ProfileId != OrganizationalModelProfile.Id ||
                candidate.SemanticElementId != source.Id));
        diagnostics = [];
        return true;
    }

    private static bool TryMovePool(
        MoveOrganizationalPoolCommand move,
        DocumentSnapshot document,
        out DocumentSnapshot? proposed,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (!TryGetPool(document, move.PoolId, out var pool, out var poolDiagnostic))
        {
            return Fail(poolDiagnostic!, out proposed, out diagnostics);
        }

        var scopeId = document.SemanticModel.GetScope(pool!.Id).Id;
        var ordered = OrganizationalSemantics.GetOrderedPoolsInScope(document, scopeId)
            .ToArray();
        var currentIndex = Array.FindIndex(ordered, candidate => candidate.Id == move.PoolId);
        var targetIndex = move.Direction == OrganizationalPoolMoveDirection.Up
            ? currentIndex - 1
            : currentIndex + 1;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= ordered.Length)
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.Unchanged,
                    $"Pool '{move.PoolId}' cannot move {move.Direction.ToString().ToLowerInvariant()}.",
                    move.PoolId.Value),
                out proposed,
                out diagnostics);
        }

        (ordered[currentIndex], ordered[targetIndex]) =
            (ordered[targetIndex], ordered[currentIndex]);
        var poolIds = ordered.Select(static candidate => candidate.Id).ToHashSet();
        var presentations = document.VisualModel.ProfileElementPresentations
            .Where(presentation =>
                presentation.ProfileId != OrganizationalModelProfile.Id ||
                !poolIds.Contains(presentation.SemanticElementId))
            .Concat(ordered.Select(static (candidate, index) =>
                new ModelProfileElementPresentationSnapshot(
                    OrganizationalModelProfile.Id,
                    candidate.Id,
                    index)));
        proposed = Replace(document, presentations: presentations);
        diagnostics = [];
        return true;
    }

    private static bool TryDeletePool(
        DeleteOrganizationalPoolCommand deletion,
        DocumentSnapshot document,
        out DocumentSnapshot? proposed,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (!TryGetPool(document, deletion.PoolId, out _, out var poolDiagnostic))
        {
            return Fail(poolDiagnostic!, out proposed, out diagnostics);
        }

        if (document.SemanticModel.Relationships.Any(relationship =>
                relationship.SourceId == deletion.PoolId ||
                relationship.TargetId == deletion.PoolId) ||
            document.VisualModel.VisualStates.Any(visual =>
                visual.SemanticElementId == deletion.PoolId))
        {
            return Fail(
                Error(
                    OrganizationalCommandDiagnosticCodes.PoolInvalid,
                    "A Pool with unsupported relationship or Visual State references cannot be deleted safely.",
                    deletion.PoolId.Value),
                out proposed,
                out diagnostics);
        }

        proposed = Replace(
            document,
            elements: document.SemanticModel.Elements.Where(element =>
                element.Id != deletion.PoolId),
            memberships: document.SemanticModel.ScopeMemberships.Where(membership =>
                membership.SemanticElementId != deletion.PoolId),
            assignments: document.SemanticModel.ProfileAssignments.Where(assignment =>
                assignment.SemanticElementId != deletion.PoolId &&
                assignment.ContainerSemanticElementId != deletion.PoolId),
            presentations: document.VisualModel.ProfileElementPresentations.Where(
                presentation => presentation.SemanticElementId != deletion.PoolId));
        diagnostics = [];
        return true;
    }

    private static bool TryGetPool(
        DocumentSnapshot document,
        SemanticElementId poolId,
        out SemanticElementSnapshot? pool,
        out Diagnostic? diagnostic)
    {
        if (document.SemanticModel.TryGetElement(poolId, out pool) &&
            pool is not null &&
            OrganizationalSemantics.IsPool(pool))
        {
            diagnostic = null;
            return true;
        }

        diagnostic = Error(
            OrganizationalCommandDiagnosticCodes.PoolInvalid,
            $"Semantic element '{poolId}' is not a scope-contained Organizational Pool.",
            poolId.Value);
        return false;
    }

    private static DocumentSnapshot Replace(
        DocumentSnapshot document,
        IEnumerable<SemanticElementSnapshot>? elements = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null,
        IEnumerable<ModelProfileElementAssignmentSnapshot>? assignments = null,
        IEnumerable<ModelProfileElementPresentationSnapshot>? presentations = null)
    {
        var semanticModel = new SemanticModelSnapshot(
            document.DocumentId,
            document.Revision,
            elements ?? document.SemanticModel.Elements,
            document.SemanticModel.Relationships,
            document.SemanticModel.NestedScopes,
            memberships ?? document.SemanticModel.ScopeMemberships,
            document.SemanticModel.ModelProfiles,
            assignments ?? document.SemanticModel.ProfileAssignments);
        var visualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates,
            presentations ?? document.VisualModel.ProfileElementPresentations);
        return new DocumentSnapshot(
            semanticModel,
            visualModel,
            document.Metadata,
            document.Publication);
    }

    private static bool SemanticIdentityExists(
        DocumentSnapshot document,
        SemanticElementId id) =>
        document.SemanticModel.TryGetElement(id, out _) ||
        document.SemanticModel.TryGetRelationship(id, out _);

    private static bool Fail(
        Diagnostic diagnostic,
        out DocumentSnapshot? proposed,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        proposed = null;
        diagnostics = [diagnostic];
        return false;
    }

    internal static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
