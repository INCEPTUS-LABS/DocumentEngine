using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Blazor.Demo;

public partial class BpmnDemoHost
{
    [Parameter] public IBpmnDocumentEditor? Editor { get; set; }
    private InceptusBpmnModeler? _modeler;
    private BpmnModelerDisplayMode _displayMode;
    private ElementReference _settingsButton;
    private InceptusBpmnModeler? SettingsModeler => _modeler ?? Editor as InceptusBpmnModeler;

    private Task OpenSettingsAsync()
    {
        if (_disposed || _state is not { IsInitialized: true }) return Task.CompletedTask;
        CloseDocumentMenu();
        return SettingsModeler?.OpenSettingsAsync(RestoreSettingsFocusAsync) ?? Task.CompletedTask;
    }

    private async Task RestoreSettingsFocusAsync()
    {
        if (!_disposed) await _settingsButton.FocusAsync();
    }

    private IBpmnDocumentEditor? _host;
    private DemoDocumentOperations? _operations;
    private DemoHostEditorState? _state;
    private readonly string _domIdPrefix = $"bpmn-host-{Guid.NewGuid():N}";
    private string DomId(string suffix) => $"{_domIdPrefix}-{suffix}";
    private DemoDocumentOperations Operations => _operations ??= new DemoDocumentOperations(_host!);
    protected override void OnParametersSet() { if (Editor is not null && !ReferenceEquals(Editor, _host)) AttachEditor(Editor); }
    internal void AttachEditor(IBpmnDocumentEditor editor, DemoDocumentOperations? operations = null)
    {
        InvalidateToolbarPresentation();
        _host = editor;
        _operations = operations ?? new DemoDocumentOperations(editor);
        _state = CaptureHostState(editor);
    }
    private void OnReady(BpmnModelerReadyEventArgs _) => AttachEditor(_modeler!);
    private Task HandleHostKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape") CloseDocumentMenu();
        if (args.Key == "Escape" && _publicationDialogOpen) CancelPublicationDialog();
        return Task.CompletedTask;
    }
    private void OnDocumentChanged(BpmnModelerDocumentChangedEventArgs _) => OnViewChanged();
    private void OnViewChanged()
    {
        if (_disposed || _host is null) return;
        RefreshHostState(_host);
        ReconcilePublicationDialog();
        ReconcilePublishOperation();
    }
    private DemoHostEditorState CaptureHostState(IBpmnDocumentEditor editor)
    {
        var context = editor.CaptureEditorContext();
        return new(context?.Presentation, context is not null, context?.AttachmentVersion ?? 0,
            context?.Document.Publication, Operations.ValidationSnapshot, Operations.ValidationInFlight,
            context?.HasOpenProperties == true, context?.SurfaceSize);
    }
    private void RefreshHostState(IBpmnDocumentEditor host) { _state = CaptureHostState(host); SynchronizeIssuesPanelSurface(); }
    private string? TargetName(ModelValidationTarget target) => target.SemanticElementId is { } id &&
        _host?.GetDocument().Snapshot is { } document ? ModelerLabels.ValidationTargetName(document, id) : null;
    private static string Format(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await SynchronizeIssuesPanelPointerCaptureAsync();
        await FocusPublicationDialogAsync();
    }
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        InvalidateToolbarPresentation();
        _componentLifetime.Cancel();
        _publishOperationCancellation?.Cancel();
        _issuesPanel.CancelMove();
        if (_issuesPanelPointerCapture is { } capture) { await capture.InvokeVoidAsync("dispose"); await capture.DisposeAsync(); }
        if (_issuesPanelModule is { } module) await module.DisposeAsync();
        _componentLifetime.Dispose();
        GC.SuppressFinalize(this);
    }
    internal const string NativeDocumentContentType = "application/json";
    internal const string NativeDocumentFileName = "document.inceptus.json";
    internal const string PublishContentType = "application/zip";
    internal const string PublishFileName = "published-process.zip";
    internal const long MaximumNativeDocumentFileSize = 16L * 1024L * 1024L;
    internal const string NativeDocumentFileTooLargeCode =
        "CANVAS_NATIVE_DOCUMENT_FILE_TOO_LARGE";
    internal const string NativeDocumentFileTooLargeMessage =
        "Error_FileTooLarge";
    private readonly CancellationTokenSource _componentLifetime = new();
    private readonly object _lifecycleSync = new();
    private readonly FloatingIssuesPanelState _issuesPanel = new();
    private IJSObjectReference? _issuesPanelModule;
    private IJSObjectReference? _issuesPanelPointerCapture;
    private bool _disposed;
    private bool _nativeDocumentImportInProgress;
    private bool _nativeDocumentExportInProgress;
    private int _nativeDocumentInputKey;
    private string _nativeDocumentOperationStatus = "idle";
    private string? _nativeDocumentOperationErrorCode;
    private string? _nativeDocumentOperationErrorMessage;
    private bool _publishInProgress;
    private string _publishOperationStatus = "idle";
    private string? _publishOperationErrorCode;
    private string? _publishOperationErrorMessage;
    private string _publishOperationErrorHeading = "Publication_PublishFailed";
    private ImmutableArray<Diagnostic> _publishOperationDiagnostics = [];
    private long? _publishOperationSessionVersion;
    private CancellationTokenSource? _publishOperationCancellation;
    private bool _publicationDialogOpen;
    private bool _publicationDialogFocusPending;
    private long _publicationSourceSessionVersion;
    private DocumentId? _publicationSourceDocumentId;
    private DocumentRevision? _publicationSourceRevision;
    private string _publicationCodeDraft = string.Empty;
    private string _publicationTitleDraft = string.Empty;
    private string _publicationDescriptionDraft = string.Empty;
    private bool _publicationIncludePools;
    private BpmnPublishingInitialViewMode _publicationInitialViewMode = BpmnPublishingInitialViewMode.CurrentEditorView;
    private ElementReference _publicationCodeInput;
    private bool CanUseNativeDocumentTransfer =>
        !_graphicalExportInProgress &&
        !_standardInProgress &&
        _host?.CaptureEditorContext()?.CanReplaceDocument == true &&
        !_nativeDocumentImportInProgress &&
        !_nativeDocumentExportInProgress &&
        !_publicationDialogOpen &&
        !_publishInProgress &&
        _state is
        {
            IsInitialized: true,
            IsDisposed: false,
            Session:
            {
                IsClosed: false,
                Status: EditingSessionStatus.Ready,
                EditorState.ActiveGesture: null,
            },
        } &&
        _state.PropertiesFormOpen == false;

    private bool CanPublish => CanUseNativeDocumentTransfer;

    private bool PublicationCodeIsValid =>
        DocumentPublicationSnapshot.IsValidCode(_publicationCodeDraft);

    private bool PublicationTitleIsValid =>
        !string.IsNullOrWhiteSpace(_publicationTitleDraft);

    private bool PublicationDraftIsValid =>
        DocumentPublicationSnapshot.TryCreate(
            _publicationCodeDraft,
            _publicationTitleDraft,
            _publicationDescriptionDraft,
            out _);

    private bool CanAcceptPublicationDraft =>
        _publicationDialogOpen && !_publishInProgress && PublicationDraftIsValid;

    private ValidationSnapshot? CurrentValidation =>
        _state is { ValidationSnapshot: { } validation, Session: { } session } &&
        validation.DocumentId == session.DocumentId &&
        validation.SourceRevision == session.DocumentRevision &&
        validation.PresentationContext == session.PresentationContext
            ? validation
            : null;

    private bool CanValidate =>
        !_graphicalExportInProgress &&
        !_standardInProgress &&
        _state is
        {
            IsInitialized: true,
            IsDisposed: false,
            ValidationInFlight: false,
            Session:
            {
                IsClosed: false,
                Status: EditingSessionStatus.Ready,
                CurrentScene: not null,
                ProjectedGraph: not null,
                LayoutResult: not null,
                RoutingResult: not null,
                EditorState.ActiveGesture: null,
            },
        } &&
        _state?.PropertiesFormOpen != true;

    private string CanValidateValue => CanValidate ? "true" : "false";

    private string ValidationStateValue => CurrentValidation is null
        ? "not-validated"
        : "current";

    private int ValidationIssueCount => CurrentValidation?.Issues.Length ?? 0;

    private int ValidationErrorCount => CurrentValidation?.ErrorCount ?? 0;

    private int ValidationWarningCount => CurrentValidation?.WarningCount ?? 0;

    private int ValidationInfoCount => CurrentValidation?.InfoCount ?? 0;

    private string IssuesPanelOpenValue => _issuesPanel.IsOpen ? "true" : "false";

    private string IssuesPanelLeft => Format(_issuesPanel.Left);

    private string IssuesPanelTop => Format(_issuesPanel.Top);

    private string IssuesDisclosure => _issuesPanel.IsOpen ? "▾" : "▸";

    private string IssuesPanelStyle => string.Join(
        " ",
        $"left: {Format(_issuesPanel.Left)}px;",
        $"top: {Format(_issuesPanel.Top)}px;",
        $"width: {Format(_issuesPanel.Width)}px;",
        $"height: {Format(_issuesPanel.Height)}px;");

    private async Task ExportNativeDocumentAsync()
    {
        CloseDocumentMenu();
        IBpmnDocumentEditor? host;
        lock (_lifecycleSync)
        {
            if (_disposed || !CanUseNativeDocumentTransfer)
            {
                return;
            }

            host = _host;
            _nativeDocumentExportInProgress = true;
            ClearNativeDocumentOperationError();
            _nativeDocumentOperationStatus = "exporting";
        }

        try
        {
            if (host is null)
            {
                CompleteNativeDocumentOperation(
                    "CANVAS_NATIVE_DOCUMENT_EXPORT_UNAVAILABLE",
                    "Error_ExportUnavailable");
                return;
            }

            await InvokeAsync(StateHasChanged);
            var export = await Operations.ExportNativeDocumentAsync(_componentLifetime.Token);
            if (export.Status == BpmnModelerOperationStatus.Cancelled)
            {
                return;
            }
            if (!export.Succeeded)
            {
                var diagnostic = export.Diagnostics.FirstOrDefault();
                CompleteNativeDocumentOperation(
                    diagnostic?.Code ?? "CANVAS_NATIVE_DOCUMENT_EXPORT_FAILED",
                    "Error_ExportFailed");
                return;
            }

            await BrowserFileDownload.DownloadAsync(
                JavaScript,
                export.Artifact!.Content.AsMemory(),
                NativeDocumentContentType,
                NativeDocumentFileName,
                _componentLifetime.Token);
            // Browser handoff succeeded; the browser does not expose the eventual disk-write outcome.
            if (export.SaveCheckpoint is { } checkpoint)
            {
                host.AcknowledgeNativeSave(checkpoint);
            }
            lock (_lifecycleSync)
            {
                if (!_disposed)
                {
                    _state = CaptureHostState(host);
                    _nativeDocumentOperationStatus = "idle";
                }
            }
        }
        catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested)
        {
            // Component disposal cancels the browser operation without surfacing a stale error.
        }
#pragma warning disable CA1031 // Browser failures become bounded user-facing operation messages.
        catch (Exception exception) when (IsNonFatalNativeDocumentException(exception))
        {
            CompleteNativeDocumentOperation(
                "CANVAS_NATIVE_DOCUMENT_DOWNLOAD_FAILED",
                "Error_DownloadFailed");
        }
#pragma warning restore CA1031
        finally
        {
            lock (_lifecycleSync)
            {
                _nativeDocumentExportInProgress = false;
                if (!_disposed && _nativeDocumentOperationStatus == "exporting")
                {
                    _nativeDocumentOperationStatus = "idle";
                }
            }
        }
    }

    private void OpenPublicationDialog()
    {
        lock (_lifecycleSync)
        {
            if (_disposed || !CanPublish || _state?.Session is not { } session)
            {
                return;
            }

            InitializePublicationDialogDraft(_state, session);
        }
    }

    private void InitializePublicationDialogDraft(
        DemoHostEditorState state,
        EditingSessionState session)
    {
        var publication = state.Publication;
        _publicationSourceSessionVersion = state.DocumentSessionVersion;
        _publicationSourceDocumentId = session.DocumentId;
        _publicationSourceRevision = session.DocumentRevision;
        _publicationCodeDraft = publication?.Code ?? string.Empty;
        _publicationTitleDraft = publication?.Title ?? string.Empty;
        _publicationDescriptionDraft = publication?.Description ?? string.Empty;
        _publicationIncludePools = false;
        _publicationInitialViewMode = BpmnPublishingInitialViewMode.CurrentEditorView;
        _publicationDialogOpen = true;
        _publicationDialogFocusPending = true;
        _publishOperationStatus = PublicationFeedbackStatus("editing");
    }

    private void CancelPublicationDialog()
    {
        lock (_lifecycleSync)
        {
            if (_disposed || !_publicationDialogOpen || _publishInProgress)
            {
                return;
            }

            DiscardPublicationDialogDraft();
            _publishOperationStatus = PublicationFeedbackStatus("idle");
        }
    }

    private Task SavePublicationDraftAsync() =>
        ExecutePublicationDialogActionAsync(publish: false);

    private Task PublishPublicationDraftAsync() =>
        ExecutePublicationDialogActionAsync(publish: true);

    private async Task ExecutePublicationDialogActionAsync(bool publish)
    {
        IBpmnDocumentEditor? host;
        long sourceSessionVersion;
        DocumentId sourceDocumentId;
        DocumentRevision sourceRevision;
        DocumentPublicationSnapshot publication;
        BpmnPublishingOptions options;
        CancellationTokenSource operation;
        lock (_lifecycleSync)
        {
            if (_disposed || !CanAcceptPublicationDraft ||
                _publicationSourceDocumentId is not { } documentId ||
                _publicationSourceRevision is not { } revision ||
                !DocumentPublicationSnapshot.TryCreate(
                    _publicationCodeDraft,
                    _publicationTitleDraft,
                    _publicationDescriptionDraft,
                    out var normalizedPublication))
            {
                return;
            }

            host = _host;
            publication = normalizedPublication;
            sourceSessionVersion = _publicationSourceSessionVersion;
            sourceDocumentId = documentId;
            sourceRevision = revision;
            options = new BpmnPublishingOptions { IncludePools = _publicationIncludePools, InitialViewMode = _publicationInitialViewMode };
            DiscardPublicationDialogDraft();
            operation = CancellationTokenSource.CreateLinkedTokenSource(_componentLifetime.Token);
            _publishOperationCancellation = operation;
            _publishOperationSessionVersion = sourceSessionVersion;
            _publishInProgress = true;
            _publishOperationStatus = publish ? "publishing" : "saving";
        }

        using var operationLifetime = operation;
        var downloading = false;
        try
        {
            if (host is null)
            {
                CompletePublishOperation(
                    host, sourceSessionVersion, operation, publish,
                    [new Diagnostic("CANVAS_PUBLICATION_UNAVAILABLE", DiagnosticSeverity.Error,
                        "Publication is unavailable while the editor is not ready.")]);
                return;
            }

            await InvokeAsync(StateHasChanged);
            var result = publish
                ? await Operations.PublishPublicationAsync(
                    sourceSessionVersion,
                    sourceDocumentId,
                    sourceRevision,
                    publication,
                    options,
                    operation.Token)
                : await Operations.SavePublicationAsync(
                    sourceSessionVersion,
                    sourceDocumentId,
                    sourceRevision,
                    publication,
                    operation.Token);
            if (result.Status == BpmnModelerOperationStatus.Cancelled)
            {
                return;
            }

            if (!result.Succeeded)
            {
                CompletePublishOperation(
                    host, sourceSessionVersion, operation, publish, result.Diagnostics);
                return;
            }

            lock (_lifecycleSync)
            {
                if (!IsCurrentPublishOperation(host, sourceSessionVersion, operation))
                {
                    return;
                }
            }

            if (publish)
            {
                downloading = true;
                await BrowserFileDownload.DownloadAsync(
                    JavaScript,
                    result.Payload.AsMemory(),
                    PublishContentType,
                    PublishFileName,
                    operation.Token);
            }

            lock (_lifecycleSync)
            {
                if (IsCurrentPublishOperation(host, sourceSessionVersion, operation))
                {
                    _state = CaptureHostState(host);
                    ClearPublishOperationError();
                    _publishOperationStatus = "idle";
                    if (publish && !result.Diagnostics.IsEmpty)
                    {
                        _publishOperationDiagnostics = result.Diagnostics;
                        _publishOperationErrorCode = result.Diagnostics[0].Code;
                        _publishOperationErrorMessage = result.Diagnostics[0].Message;
                        _publishOperationErrorHeading = "Publication_PublishWarnings";
                        _publishOperationStatus = "warning";
                    }
                }
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            // Disposal or a replacement session cancels delivery without a stale failure.
        }
#pragma warning disable CA1031 // Browser failures become bounded user-facing operation messages.
        catch (Exception exception) when (IsNonFatalNativeDocumentException(exception))
        {
            CompletePublishOperation(
                host, sourceSessionVersion, operation, publish,
                [new Diagnostic(
                    downloading
                        ? "CANVAS_PUBLISH_DOWNLOAD_FAILED"
                        : publish ? "CANVAS_PUBLISH_FAILED" : "CANVAS_PUBLICATION_PERSISTENCE_FAILED",
                    DiagnosticSeverity.Error,
                    downloading
                        ? "The published process download could not be started by the browser."
                        : publish
                            ? "The publication package could not be generated."
                            : "Publication metadata could not be saved.")]);
        }
#pragma warning restore CA1031
        finally
        {
            lock (_lifecycleSync)
            {
                if (ReferenceEquals(_publishOperationCancellation, operation))
                {
                    _publishOperationCancellation = null;
                    _publishInProgress = false;
                    if (!_disposed && _publishOperationStatus is "publishing" or "saving")
                    {
                        _publishOperationStatus = PublicationFeedbackStatus("idle");
                    }
                }
            }
        }
    }

    private void ReconcilePublicationDialog()
    {
        if (!_publicationDialogOpen || _state?.Session is not { } session)
        {
            return;
        }

        if (_state.DocumentSessionVersion != _publicationSourceSessionVersion ||
            session.DocumentId != _publicationSourceDocumentId ||
            session.DocumentRevision != _publicationSourceRevision)
        {
            DiscardPublicationDialogDraft();
            _publishOperationStatus = PublicationFeedbackStatus("idle");
        }
    }

    private async Task FocusPublicationDialogAsync()
    {
        lock (_lifecycleSync)
        {
            if (_disposed || !_publicationDialogOpen || !_publicationDialogFocusPending)
            {
                return;
            }

            _publicationDialogFocusPending = false;
        }

#pragma warning disable CA1031 // Focus failure must not break the editor lifecycle.
        try
        {
            await _publicationCodeInput.FocusAsync(preventScroll: true);
        }
        catch (Exception exception) when (IsNonFatalNativeDocumentException(exception))
        {
            // The dialog remains keyboard-operable if focus transfer is unavailable.
        }
#pragma warning restore CA1031
    }

    private void DiscardPublicationDialogDraft()
    {
        _publicationDialogOpen = false;
        _publicationDialogFocusPending = false;
        _publicationSourceSessionVersion = 0;
        _publicationSourceDocumentId = null;
        _publicationSourceRevision = null;
        _publicationCodeDraft = string.Empty;
        _publicationTitleDraft = string.Empty;
        _publicationDescriptionDraft = string.Empty;
    }

    private bool IsCurrentPublishOperation(
        IBpmnDocumentEditor? host,
        long sourceSessionVersion,
        CancellationTokenSource operation) =>
        !_disposed &&
        !operation.IsCancellationRequested &&
        ReferenceEquals(_host, host) &&
        ReferenceEquals(_publishOperationCancellation, operation) &&
        (host is null || CaptureHostState(host).DocumentSessionVersion == sourceSessionVersion);

    private void CompletePublishOperation(
        IBpmnDocumentEditor? host,
        long sourceSessionVersion,
        CancellationTokenSource operation,
        bool publish,
        ImmutableArray<Diagnostic> diagnostics)
    {
        lock (_lifecycleSync)
        {
            if (!IsCurrentPublishOperation(host, sourceSessionVersion, operation))
            {
                return;
            }

            _publishOperationDiagnostics = diagnostics.IsDefaultOrEmpty
                ? [new Diagnostic("CANVAS_PUBLISH_FAILED", DiagnosticSeverity.Error,
                    "The Publication operation could not be completed. Please retry.")]
                : diagnostics;
            _publishOperationErrorCode = _publishOperationDiagnostics[0].Code;
            _publishOperationErrorMessage = _publishOperationDiagnostics[0].Message;
            _publishOperationErrorHeading = publish ? "Publication_PublishFailed" : "Publication_SaveFailed";
            _publishOperationStatus = "failed";
        }
    }

    private void ClearPublishOperationError()
    {
        _publishOperationDiagnostics = [];
        _publishOperationErrorCode = null;
        _publishOperationErrorMessage = null;
    }

    private void DismissPublishOperationError()
    {
        lock (_lifecycleSync)
        {
            ClearPublishOperationError();
            if (_publishOperationStatus is "failed" or "warning")
            {
                _publishOperationStatus = "idle";
            }
        }
    }

    private string PublicationFeedbackStatus(string emptyStatus) => _publishOperationDiagnostics.IsEmpty
        ? emptyStatus : _publishOperationDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Error) ? "failed" : "warning";

    private bool HasOperationFeedback =>
        _standardFeedbackHeading is not null ||
        _operations is { Diagnostics.IsEmpty: false } ||
        _nativeDocumentOperationErrorMessage is not null ||
        _publishOperationErrorMessage is not null;

    private void DismissOperationFeedback()
    {
        lock (_lifecycleSync)
        {
            DismissPublishOperationError();
            DismissStandardFeedback();
            ClearNativeDocumentOperationError();
            if (_nativeDocumentOperationStatus == "error")
            {
                _nativeDocumentOperationStatus = "idle";
            }
            _operations?.DismissDiagnostics();
        }
    }

    private void ReconcilePublishOperation()
    {
        lock (_lifecycleSync)
        {
            if (_publishOperationSessionVersion is not { } sourceVersion ||
                _state?.DocumentSessionVersion == sourceVersion)
            {
                return;
            }

            var operation = _publishOperationCancellation;
            _publishOperationCancellation = null;
            _publishOperationSessionVersion = null;
            _publishInProgress = false;
            ClearPublishOperationError();
            _publishOperationStatus = "idle";
            operation?.Cancel();
        }
    }

    private string? PublishDiagnosticTarget(Diagnostic diagnostic)
    {
        var context = diagnostic.Context;
        if (context.TryGetValue("ElementId", out var elementId))
        {
            return context.TryGetValue("ElementName", out var name) && !string.IsNullOrWhiteSpace(name)
                ? Text["Target_NamedElement", name, elementId]
                : context.TryGetValue("ElementType", out var type)
                    ? Text["Target_TypedElement", type, elementId]
                    : Text["Target_Element", elementId];
        }

        if (context.TryGetValue("ConnectorId", out var connectorId))
        {
            return context.TryGetValue("SourceElementId", out var source) &&
                context.TryGetValue("TargetElementId", out var target)
                    ? Text["Target_ConnectedConnector", connectorId, source, target]
                    : Text["Target_Connector", connectorId];
        }

        return diagnostic.SourceIdentity is { } identity ? Text["Target_Source", identity].Value : null;
    }

    private async Task ImportNativeDocumentAsync(InputFileChangeEventArgs args)
    {
        CloseDocumentMenu();
        ArgumentNullException.ThrowIfNull(args);
        IBpmnDocumentEditor? host;
        lock (_lifecycleSync)
        {
            if (_disposed || !CanUseNativeDocumentTransfer)
            {
                return;
            }

            host = _host;
            _nativeDocumentImportInProgress = true;
            ClearNativeDocumentOperationError();
            _nativeDocumentOperationStatus = "importing";
        }

        await InvokeAsync(StateHasChanged);
        try
        {
            if (host is null)
            {
                CompleteNativeDocumentOperation(
                    "CANVAS_NATIVE_DOCUMENT_IMPORT_UNAVAILABLE",
                    "Error_ImportUnavailable");
                return;
            }

            var browserImport = await ReadAndImportNativeDocumentFileAsync(
                args.File,
                Operations.ImportNativeDocumentAsync,
                _componentLifetime.Token);
            if (browserImport.FileTooLarge)
            {
                CompleteNativeDocumentOperation(
                    NativeDocumentFileTooLargeCode,
                    NativeDocumentFileTooLargeMessage);
                return;
            }

            var import = browserImport.ImportResult!;
            if (import.Status == BpmnModelerOperationStatus.Cancelled)
            {
                return;
            }
            if (!import.Succeeded)
            {
                var (code, message) = NativeDocumentImportMessage(import);
                CompleteNativeDocumentOperation(code, message);
                return;
            }

            lock (_lifecycleSync)
            {
                if (_disposed || !ReferenceEquals(_host, host))
                {
                    return;
                }

                _state = CaptureHostState(host);
                _issuesPanel.Close();
                ClearNativeDocumentOperationError();
                _nativeDocumentOperationStatus = "idle";
            }
        }
        catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested)
        {
            // Component disposal cancels the file operation without surfacing a stale error.
        }
#pragma warning disable CA1031 // Browser file failures become bounded user-facing operation messages.
        catch (Exception exception) when (IsNonFatalNativeDocumentException(exception))
        {
            CompleteNativeDocumentOperation(
                "CANVAS_NATIVE_DOCUMENT_FILE_READ_FAILED",
                "Error_ImportReadFailed");
        }
#pragma warning restore CA1031
        finally
        {
            lock (_lifecycleSync)
            {
                _nativeDocumentImportInProgress = false;
                _nativeDocumentInputKey++;
                if (!_disposed && _nativeDocumentOperationStatus == "importing")
                {
                    _nativeDocumentOperationStatus = "idle";
                }
            }
        }
    }

    internal static async ValueTask<(
        bool FileTooLarge,
        BpmnModelerDocumentResult? ImportResult)> ReadAndImportNativeDocumentFileAsync(
        IBrowserFile file,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<BpmnModelerDocumentResult>>
            importer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(importer);
        if (file.Size > MaximumNativeDocumentFileSize)
        {
            return (true, null);
        }

        byte[] payload;
        await using (var stream = file.OpenReadStream(
            MaximumNativeDocumentFileSize,
            cancellationToken))
        {
            using var buffer = new MemoryStream(checked((int)file.Size));
            await stream.CopyToAsync(buffer, cancellationToken);
            payload = buffer.ToArray();
        }

        return (false, await importer(payload, cancellationToken));
    }

    internal static (string Code, string Message) NativeDocumentImportMessage(
        BpmnModelerDocumentResult result)
    {
        var code = result.Diagnostics.FirstOrDefault()?.Code ??
            "CANVAS_NATIVE_DOCUMENT_IMPORT_FAILED";
        var message = code switch
        {
            "NATIVE_DOCUMENT_JSON_MALFORMED" =>
                "Error_ImportMalformed",
            "NATIVE_DOCUMENT_FORMAT_INVALID" =>
                "Error_ImportFormat",
            "NATIVE_DOCUMENT_VERSION_UNSUPPORTED" =>
                "Error_ImportVersion",
            "CANVAS_NATIVE_DOCUMENT_IMPORT_UNAVAILABLE" =>
                "Error_ImportUnavailable",
            "CANVAS_NATIVE_DOCUMENT_REPLACEMENT_FAILED" =>
                "Error_ImportSession",
            _ => "Error_ImportStructure",
        };
        return (code, message);
    }

    private void CompleteNativeDocumentOperation(string code, string message)
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return;
            }

            _nativeDocumentOperationErrorCode = code;
            _nativeDocumentOperationErrorMessage = message;
            _nativeDocumentOperationStatus = "error";
        }
    }

    private void ClearNativeDocumentOperationError()
    {
        _nativeDocumentOperationErrorCode = null;
        _nativeDocumentOperationErrorMessage = null;
    }

    private static bool IsNonFatalNativeDocumentException(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private void ToggleIssuesPanel()
    {
        var (width, height) = CurrentIssuesPanelSurface();
        _issuesPanel.Toggle(width, height);
    }

    private void CloseIssuesPanel() => _issuesPanel.Close();

    private void StartIssuesPanelMove(PointerEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!_issuesPanel.IsOpen || args.Button != 0 || !args.IsPrimary ||
            _issuesPanel.ActivePointerId is not null)
        {
            return;
        }

        _ = _issuesPanel.TryBeginMove(args.PointerId, args.ClientX, args.ClientY);
    }

    private void MoveIssuesPanel(PointerEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _ = _issuesPanel.Move(args.PointerId, args.ClientX, args.ClientY);
    }

    private void EndIssuesPanelMove(PointerEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _ = _issuesPanel.EndMove(args.PointerId);
    }

    private void CancelIssuesPanelMove(PointerEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _ = _issuesPanel.EndMove(args.PointerId);
    }

    private async Task ValidateAsync()
    {
        IBpmnDocumentEditor? host;
        lock (_lifecycleSync)
        {
            host = _disposed ? null : _host;
        }

        if (host is null || !CanValidate)
        {
            return;
        }

        _ = await Operations.ValidateAsync(_componentLifetime.Token);
        RefreshHostState(host);
    }

    private async Task SelectValidationIssueAsync(ModelValidationIssueId issueId)
    {
        ArgumentNullException.ThrowIfNull(issueId);
        IBpmnDocumentEditor? host;
        lock (_lifecycleSync)
        {
            host = _disposed ? null : _host;
        }

        if (host is null)
        {
            return;
        }

        await Operations.SelectValidationIssueAsync(issueId, _componentLifetime.Token);
        RefreshHostState(host);
    }

    private void SynchronizeIssuesPanelSurface()
    {
        if (_state?.SurfaceSize is { } surfaceSize)
        {
            _issuesPanel.ResizeSurface(surfaceSize.CssWidth, surfaceSize.CssHeight);
        }
    }

    private async ValueTask SynchronizeIssuesPanelPointerCaptureAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_issuesPanel.IsOpen)
        {
            if (_issuesPanelPointerCapture is not null)
            {
                return;
            }

            _issuesPanelModule ??= await JavaScript.InvokeAsync<IJSObjectReference>(
                "import",
                _componentLifetime.Token,
                "./_content/Inceptus.DocumentEngine.Bpmn.Blazor/inceptus.presentation.js");
            var pointerCapture = await _issuesPanelModule.InvokeAsync<IJSObjectReference>(
                "createDomPointerCaptureObserver",
                _componentLifetime.Token,
                DomId("issues-panel-header"));
            await pointerCapture.InvokeVoidAsync("start", _componentLifetime.Token);
            _issuesPanelPointerCapture = pointerCapture;
            return;
        }

        if (_issuesPanelPointerCapture is { } existing)
        {
            _issuesPanelPointerCapture = null;
            await existing.InvokeVoidAsync("dispose", _componentLifetime.Token);
            await existing.DisposeAsync();
        }
    }

    private (double Width, double Height) CurrentIssuesPanelSurface() =>
        _state?.SurfaceSize is { } surfaceSize
            ? (surfaceSize.CssWidth, surfaceSize.CssHeight)
            : (
                FloatingIssuesPanelState.PreferredWidth +
                    (FloatingIssuesPanelState.DefaultMargin * 2d),
                FloatingIssuesPanelState.PreferredHeight +
                    (FloatingIssuesPanelState.DefaultMargin * 2d));

    private string ValidationTargetLabel(ModelValidationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (TargetName(target) is { } name)
        {
            return name;
        }

        if (target.VisualStateId is { } visualStateId)
        {
            return Text["Target_VisualSemantic", visualStateId.Value, target.SemanticElementId!.Value];
        }

        return target.SemanticElementId is { } semanticElementId
            ? Text["Target_Semantic", semanticElementId.Value]
            : Text["Target_Document"];
    }

}

internal sealed record DemoHostEditorState(EditingSessionState? Session, bool IsInitialized,
    long DocumentSessionVersion, DocumentPublicationSnapshot? Publication, ValidationSnapshot? ValidationSnapshot,
    bool ValidationInFlight, bool PropertiesFormOpen, Canvas2DSurfaceSize? SurfaceSize)
{
    internal bool IsDisposed => Session?.IsClosed == true;


}



