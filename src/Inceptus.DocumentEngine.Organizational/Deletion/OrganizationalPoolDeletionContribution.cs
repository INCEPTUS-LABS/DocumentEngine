using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Deletion;

public static class OrganizationalPoolDeletionContribution
{
    public static DiagramDeletionId DeletionId { get; } =
        new("inceptus:organizational/deletion/pool");

    internal static DiagramDeletionRegistration CreateRegistration(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy) =>
        new(
            DeletionId,
            new OrganizationalPoolDeletionCommandFactory(eligibilityPolicy));
}

internal sealed class OrganizationalPoolDeletionCommandFactory :
    IDiagramDeletionCommandFactory
{
    private readonly IOrganizationalElementEligibilityPolicy _eligibilityPolicy;

    internal OrganizationalPoolDeletionCommandFactory(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        _eligibilityPolicy = eligibilityPolicy;
    }

    public bool CanDelete(DiagramDeletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TryCreateCommand(request, out var command) &&
            command is not null &&
            Validate(command, request).All(static diagnostic =>
                diagnostic.Severity != DiagnosticSeverity.Error);
    }

    public DiagramDeletionPlanResult CreatePlan(DiagramDeletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryCreateCommand(request, out var command) || command is null)
        {
            return DiagramDeletionPlanResult.Failure(
            [
                OrganizationalCommandSupport.Error(
                    OrganizationalCommandDiagnosticCodes.Invalid,
                    "The semantic-only deletion target is not a current Organizational Pool.",
                    request.SemanticId.Value),
            ]);
        }

        var diagnostics = Validate(command, request);
        return diagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            ? DiagramDeletionPlanResult.Failure(diagnostics)
            : DiagramDeletionPlanResult.Success(
                new DiagramDeletionPlan(
                    command,
                    request.TargetKind,
                    request.SemanticId,
                    visualStateId: null),
                diagnostics);
    }

    private static bool TryCreateCommand(
        DiagramDeletionRequest request,
        out DeleteOrganizationalPoolCommand? command)
    {
        command = null;
        if (request.TargetKind != DiagramDeletionTargetKind.Element ||
            request.VisualStateId is not null ||
            !request.Document.SemanticModel.TryGetElement(
                request.SemanticId,
                out var element) ||
            element is null ||
            !OrganizationalSemantics.IsPool(element))
        {
            return false;
        }

        command = new DeleteOrganizationalPoolCommand(
            request.Document.DocumentId,
            request.ExpectedRevision,
            request.SemanticId);
        return true;
    }

    private ImmutableArray<Diagnostic> Validate(
        DeleteOrganizationalPoolCommand command,
        DiagramDeletionRequest request) =>
        new OrganizationalCommandValidator<DeleteOrganizationalPoolCommand>(
            _eligibilityPolicy).Validate(command, request.Document);
}
