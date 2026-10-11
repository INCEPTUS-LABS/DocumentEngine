using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    internal bool CanAutoArrange
    {
        get
        {
            lock (_sync)
                return !_disposed && _initialized && !_propertiesFormOpen && !_modelViewPropertiesFormOpen &&
                    _session is { } session && TryArrangeScopes(session, session.CaptureState(), out _, out _);
        }
    }

    private static bool TryArrangeScopes(EditingSession session, EditingSessionState state, out DocumentSnapshot? document, out DocumentScopeId[] scopes)
    {
        scopes = []; document = null;
        if (state.Status != EditingSessionStatus.Ready || state.EditorState.ActiveGesture is not null ||
            state.CurrentScene is null || !session.TryCaptureDocumentSnapshot(out document) || document is null ||
            document.Revision != state.DocumentRevision) return false;
        // Document views with their own presentation transform are deliberately not offered a Process-only action.
        if (state.PresentationContext is not ProcessScopePresentationContext process) return false;
        var available = BpmnAutoArrangePlanner.ContainsArrangeableNode(document,
            state.CurrentScene.Items.Where(item => item.IsVisible).Select(item => item.Origin.VisualStateId).OfType<VisualStateId>());
        if (available) scopes = [process.ScopeId];
        return available;
    }

    internal async ValueTask<HistoryOperationResult?> AutoArrangeAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EditingSession? session;
            lock (_sync) session = _disposed || !_initialized || _propertiesFormOpen || _modelViewPropertiesFormOpen ? null : _session;
            if (session is null || !TryArrangeScopes(session, session.CaptureState(), out var document, out var scopes)) return null;
            var state = session.CaptureState();
            var plan = BpmnAutoArrangePlanner.Plan(document!, scopes, linked.Token);
            var result = await session.ExecuteAsync(new BpmnAutoArrangeCommand(plan), linked.Token).ConfigureAwait(false);
            RetainContextCommandDiagnostics(session, state, result);
            if (result.IsCommitted) await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            lock (_sync)
                ReplaceInteractionDiagnosticsUnderLock([new Diagnostic("BPMN_AUTO_ARRANGE_FAILED", DiagnosticSeverity.Error,
                    "Auto Arrange could not prepare a valid presentation. " + exception.Message)]);
            return null;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }
}
