using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Presentation;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    /// <summary>Selects an explicit view without changing semantic containment or persistent geometry.</summary>
    public async ValueTask<EditingSessionOperationResult> ChangePresentationContextAsync(PresentationViewContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context is ProcessScopePresentationContext process && _presentationContext is ProcessScopePresentationContext)
            return await NavigateToScopeAsync(process.ScopeId, cancellationToken).ConfigureAwait(false);
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
            return OperationResult(EditingSessionOperationStatus.Cancelled);
        try
        {
            return await ChangePresentationContextUnderCommandGateAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally { _commandGate.Release(); }
    }

    private async ValueTask<EditingSessionOperationResult> ChangePresentationContextUnderCommandGateAsync(
        PresentationViewContext context, CancellationToken cancellationToken)
    {
        Task<EditingSessionOperationResult>? run;
        lock (_sync)
        {
            if (_closing || _closed) return OperationResult(EditingSessionOperationStatus.Closed);
            var document = AttachedDocument.CaptureSnapshot();
            var resolved = _projectionEngine.ResolvePresentation(document, context);
            if (!resolved.Succeeded) return OperationResult(EditingSessionOperationStatus.Rejected, resolved.Diagnostics);
            if (context == _presentationContext) return OperationResult(EditingSessionOperationStatus.Succeeded);
            var editor = EditorState.CaptureSnapshot();
            EditorState.TryUpdate(editor, new EditorStateSnapshot(viewport: editor.Viewport));
            _presentationContext = context;
            if (context is ProcessScopePresentationContext target)
            {
                _activeScopeId = target.ScopeId;
                _activeScopePath = CreateScopePath(document.SemanticModel, target.ScopeId);
            }
            _compatibleArtifacts = null;
            run = BeginRun(document, null, cancellationToken).Task;
        }
        NotifyStateChanged();
        return run is null ? OperationResult(EditingSessionOperationStatus.Superseded)
            : await run.ConfigureAwait(false);
    }
}
