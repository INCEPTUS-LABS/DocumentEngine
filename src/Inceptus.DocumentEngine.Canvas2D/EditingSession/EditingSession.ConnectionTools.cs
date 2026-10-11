using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    /// <summary>Selects one registered transient connection capability and captures source eligibility for the current view.</summary>
    public ValueTask<EditingSessionOperationResult> SelectConnectionToolAsync(
        AnchorConnectionCreationId? creationId, AnchorConnectionCreationCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var state = CaptureState();
        IEnumerable<VisualStateId>? sources = null;
        if (creationId is not null)
        {
            if (!catalog.TryGetRegistration(creationId, out var registration) || !state.IsGraphicalInteractionEnabled ||
                state.EditorState.ActiveGesture is not null || state.ProjectedGraph is null ||
                !TryCaptureDocumentSnapshot(out var document) || document.DocumentId != state.DocumentId ||
                document.Revision != state.DocumentRevision)
                return ValueTask.FromResult(OperationResult(EditingSessionOperationStatus.Rejected,
                    [new Diagnostic(EditingSessionDiagnosticCodes.InvalidOperation, DiagnosticSeverity.Error,
                        "The connection capability is unavailable for the current document view.", creationId.Value)]));
            sources = CaptureConnectionSources(state, document, registration);
        }
        return UpdateEditorStateCoreAsync(ConnectionToolState.WithSelection(state.EditorState, creationId, sources),
            cancellationToken, state.PresentationContext, state.Generation);
    }

    private ImmutableArray<VisualStateId> CaptureConnectionSources(
        EditingSessionState state, DocumentSnapshot document, AnchorConnectionCreationRegistration registration)
    {
        var result = ImmutableArray.CreateBuilder<VisualStateId>();
        foreach (var node in state.ProjectedGraph!.Nodes)
        {
            if (node.Source.VisualStateId is not { } id ||
                !document.VisualModel.TryGetVisualState(id, out var visual) || visual is null ||
                !document.SemanticModel.TryGetElement(visual.SemanticElementId, out var element) || element is null) continue;
#pragma warning disable CA1031 // An unavailable extension capability cannot expose a source anchor.
            try
            {
                if (registration.CommandFactory is IElementConnectionCreationCommandFactory elementFactory)
                {
                    if (elementFactory.ValidateSource(new(document, state.PresentationContext, state.PresentationPlan,
                        element.Id, id)).IsEmpty) result.Add(id);
                    continue;
                }
                if (ElementConnectorAnchorResolver.Resolve(visual, element.TypeId, ConnectorAnchorPolicyProvider)
                    .Any(anchor => anchor.Allows(ConnectorAnchorRole.Source) &&
                        !ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, anchor.Id) &&
                        registration.CommandFactory.CanStart(new AnchorConnectionCreationSourceRequest(state.PresentationContext,
                            state.PresentationPlan, document, document.Revision, element.Id, visual.Id, anchor.Id))))
                    result.Add(id);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException) { }
#pragma warning restore CA1031
        }
        return result.ToImmutable();
    }
}
