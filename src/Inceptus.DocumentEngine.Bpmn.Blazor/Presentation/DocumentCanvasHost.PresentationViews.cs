using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Presentation;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    internal ImmutableArray<ModelerPresentationViewChoice> CapturePresentationViewChoices(
        PresentationViewContext? capturedContext = null)
    {
        lock (_sync)
        {
            return !_disposed && _session is { } session &&
                session.TryCaptureDocumentSnapshot(out var document) && document is not null
                ? ModelerPresentationViews.GetChoices(document,
                    capturedContext ?? session.CaptureState().PresentationContext) : [];
        }
    }

    internal async ValueTask<EditingSessionOperationResult?> ChangePresentationContextAsync(
        PresentationViewContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }
        try
        {
            EditingSession? session;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen || _modelViewPropertiesFormOpen
                    ? null : _session;
            }
            if (session is null || session.CaptureState().Status != EditingSessionStatus.Ready)
            {
                return null;
            }
            await ClearScopeNavigationTransientStateUnderGateAsync(linked.Token).ConfigureAwait(false);
            var result = await session.ChangePresentationContextAsync(context, linked.Token).ConfigureAwait(false);
            lock (_sync)
            {
                _ = ReplaceInteractionDiagnosticsUnderLock(result.Diagnostics);
            }
            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            _ = NotifyStateChangedSafelyAsync();
        }
    }
}
