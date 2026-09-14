using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Components;

/// <summary>
/// A component-owned BPMN editor. Hosts exchange immutable snapshots and generated files,
/// never its live Document, session, History, or renderer.
/// </summary>
public partial class InceptusBpmnModeler : IDisposable
{
    private BpmnModelerFacade? _facade;
    private bool _disposed;

    // A host may cascade its UI culture to notify parameterless modelers of a re-render.
    // Resource lookup still uses CurrentUICulture; this is only a Blazor render dependency.
    [CascadingParameter]
    private System.Globalization.CultureInfo? HostUICulture { get; set; }

    [Inject]
    private IServiceProvider Services { get; set; } = null!;

    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>
    /// Consumed once at initialization, ahead of the optional startup provider and canonical
    /// empty default. Later parameter updates do not replace the live model; use LoadDocumentAsync.
    /// </summary>
    [Parameter]
    public DocumentSnapshot? InitialDocument { get; set; }

    /// <summary>Invoked once after successful initial attachment and usable presentation.</summary>
    [Parameter]
    public EventCallback<BpmnModelerReadyEventArgs> Ready { get; set; }

    /// <summary>
    /// Invoked once per accepted persistent revision or successful document replacement.
    /// Invocation order follows modeler operation order; asynchronous host handlers may finish
    /// in a different order and may await subsequent modeler operations. Transient edits are excluded.
    /// </summary>
    [Parameter]
    public EventCallback<BpmnModelerDocumentChangedEventArgs> DocumentChanged { get; set; }

    /// <summary>Reports bounded operation failures, not read-only model-validation Issues.</summary>
    [Parameter]
    public EventCallback<BpmnModelerOperationFailedEventArgs> OperationFailed { get; set; }

    /// <summary>
    /// Captures current authoritative persistent state without changing revision, History, or
    /// presentation. During replacement preparation this remains the old accepted Document;
    /// before startup or after disposal the result is Unavailable.
    /// </summary>
    public BpmnModelerDocumentResult CaptureDocumentSnapshot() => Facade.CaptureDocumentSnapshot();

    /// <summary>Atomically opens a fresh empty Document without a browser confirmation dialog.</summary>
    public ValueTask<BpmnModelerDocumentResult> NewDocumentAsync(
        CancellationToken cancellationToken = default) => Facade.NewDocumentAsync(cancellationToken);

    /// <summary>Reconstructs exact persistent identities in a fresh, component-owned session.</summary>
    public ValueTask<BpmnModelerDocumentResult> LoadDocumentAsync(
        DocumentSnapshot? document,
        CancellationToken cancellationToken = default) => Facade.LoadDocumentAsync(document, cancellationToken);

    /// <summary>Imports native bytes through the canonical serializer, without a browser file picker.</summary>
    public ValueTask<BpmnModelerDocumentResult> ImportNativeDocumentAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default) => Facade.ImportNativeDocumentAsync(utf8Json, cancellationToken);

    /// <summary>Returns native Document bytes without initiating a browser download.</summary>
    public ValueTask<BpmnModelerFileResult> ExportNativeDocumentAsync(
        CancellationToken cancellationToken = default) => Facade.ExportNativeDocumentAsync(cancellationToken);

    /// <summary>Returns the standalone Publish ZIP without changing publication metadata or downloading.</summary>
    public ValueTask<BpmnModelerFileResult> PublishAsync(
        CancellationToken cancellationToken = default) => Facade.PublishAsync(cancellationToken);

    protected override void OnInitialized()
    {
        // The snapshot value is captured here, not read again on later parameter updates.
        var factory = Services.GetService<BpmnModelerCompositionFactory>() ??
            new BpmnModelerCompositionFactory(() => Services.GetServices<IBpmnModelerStartupDocumentProvider>());
        _facade?.Dispose();
        _facade = new BpmnModelerFacade(factory.WithInitialDocument(InitialDocument), InvokeNotificationAsync);
    }

    public void Dispose()
    {
        _disposed = true;
        _facade?.Dispose();
        GC.SuppressFinalize(this);
    }

    private BpmnModelerFacade Facade => _facade ??= CreateUnavailableFacade();

    private BpmnModelerFacade CreateUnavailableFacade()
    {
        // Before component initialization there is no renderer to dispatch callbacks through.
        // Operations still return bounded results; OnInitialized installs the real dispatcher.
        var facade = new BpmnModelerFacade(
            new BpmnModelerCompositionFactory([]), static _ => Task.CompletedTask);
        if (_disposed)
        {
            facade.Dispose();
        }

        return facade;
    }

    private string RootClass => string.IsNullOrWhiteSpace(Class)
        ? "inceptus-bpmn-modeler"
        : $"inceptus-bpmn-modeler {Class}";

    private async Task InvokeNotificationAsync(object notification)
    {
#pragma warning disable CA1031 // Consumer callback failures belong to Blazor's error boundary, not modeler results.
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    var callback = notification switch
                    {
                        BpmnModelerReadyEventArgs ready => Ready.InvokeAsync(ready),
                        BpmnModelerDocumentChangedEventArgs changed => DocumentChanged.InvokeAsync(changed),
                        BpmnModelerOperationFailedEventArgs failed => OperationFailed.InvokeAsync(failed),
                        _ => Task.CompletedTask,
                    };
                    _ = ObserveCallbackAsync(callback);
                }
                catch (Exception exception)
                {
                    _ = DispatchExceptionAsync(exception);
                }
            });
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _ = DispatchExceptionAsync(exception);
            }
        }
#pragma warning restore CA1031
    }

    private async Task ObserveCallbackAsync(Task callback)
    {
#pragma warning disable CA1031 // Route asynchronous host callback faults through the renderer without rollback.
        try
        {
            await callback;
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                await DispatchExceptionAsync(exception);
            }
        }
#pragma warning restore CA1031
    }
}
