using Inceptus.DocumentEngine.Canvas2D.Interaction;
using System.Collections.Immutable;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>The reference application's document workflows. The editor only exchanges snapshots and performs edits.</summary>
internal sealed partial class DemoDocumentOperations
{
    internal const string NativeDocumentFileName = "document.inceptus.json";
    internal const string NativeDocumentContentType = "application/json";
    internal const string PublishFileName = "published-process.zip";
    internal const string PublishContentType = "application/zip";
    internal const long MaximumNativeDocumentFileSize = 16L * 1024L * 1024L;
    private readonly IBpmnDocumentEditor _editor;
    private readonly BpmnDocumentValidator _validator;
    private readonly PublishingPackageGenerator _publisher;
    private ValidationSnapshot? _validation;
    private long _validationAttachment;

    internal DemoDocumentOperations(IBpmnDocumentEditor editor, BpmnDocumentValidator? validator = null,
        PublishingPackageGenerator? publisher = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        _validator = validator ?? new();
        _publisher = publisher ?? new();
    }

    internal BpmnDocumentValidationResult? DocumentValidation { get; private set; }
    internal ImmutableArray<Diagnostic> Diagnostics { get; private set; } = [];
    internal bool ValidationInFlight { get; private set; }

    internal ValidationSnapshot? ValidationSnapshot
    {
        get
        {
            var context = _editor.CaptureEditorContext();
            if (context is null || context.AttachmentVersion != _validationAttachment ||
                context.Document.DocumentId != _validation?.DocumentId || context.Document.Revision != _validation.SourceRevision ||
                context.Presentation.PresentationContext != _validation.PresentationContext)
                _validation = null;
            return _validation;
        }
    }

    internal async ValueTask<BpmnModelerDocumentResult> ImportNativeDocumentAsync(ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default) =>
        await ImportNativeDocumentAsync(utf8Json, new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true }, cancellationToken).ConfigureAwait(false);

    internal async ValueTask<BpmnModelerDocumentResult> ImportNativeDocumentAsync(ReadOnlyMemory<byte> utf8Json,
        BpmnModelerDocumentLoadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (cancellationToken.IsCancellationRequested) return new(BpmnModelerOperationStatus.Cancelled, null, []);
        if (_editor.CaptureEditorContext() is not null)
            await _editor.GetDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (_editor.CaptureEditorContext()?.CanReplaceDocument != true)
            return new(BpmnModelerOperationStatus.Unavailable, null,
                [Error("CANVAS_NATIVE_DOCUMENT_IMPORT_UNAVAILABLE", "Native Document import is unavailable while the editor is not ready.")]);
        var read = BpmnDocumentSerializer.Deserialize(utf8Json);
        if (!read.Succeeded) return new(BpmnModelerOperationStatus.Rejected, null, read.Diagnostics);
        var migrated = BpmnDocumentMigrator.Migrate(read.Document!, read.NativeVersion!.Value);
        if (!migrated.Succeeded) return new(BpmnModelerOperationStatus.Rejected, null, migrated.Diagnostics);
        var loaded = await _editor.SetDocumentAsync(migrated.Document, new BpmnModelerDocumentLoadOptions
        {
            VerifySavedRouting = true,
            ReprepareIncompatibleRouting = options.ReprepareIncompatibleRouting,
        }, cancellationToken).ConfigureAwait(false);
        if (loaded.Succeeded)
        {
            _validation = null;
            DocumentValidation = null;
            // Recovered routing remains unsaved; a normal native import acknowledges the exact loaded content.
            if (loaded.Snapshot!.Equals(migrated.Document) && _editor.CreateSaveCheckpoint(loaded.Snapshot) is { } checkpoint)
                _editor.AcknowledgeNativeSave(checkpoint);
        }
        return new(loaded.Status, loaded.Snapshot, loaded.Diagnostics.Select(static diagnostic =>
            diagnostic.Code == "CANVAS_DOCUMENT_LOAD_REPLACEMENT_FAILED"
                ? new Diagnostic("CANVAS_NATIVE_DOCUMENT_REPLACEMENT_FAILED", diagnostic.Severity,
                    diagnostic.Message, diagnostic.SourceIdentity, diagnostic.Context)
                : diagnostic).ToImmutableArray());
    }

    internal async ValueTask<BpmnModelerFileResult> ExportNativeDocumentAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return CancelledFile();
        var context = _editor.CaptureEditorContext();
        var capture = await _editor.GetDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (capture.Status == BpmnModelerOperationStatus.Cancelled) return CancelledFile();
        context = _editor.CaptureEditorContext();
        if (context is null || context.HasOpenProperties || context.Presentation.EditorState.ActiveGesture is not null || !capture.Succeeded)
            return FileFailure(BpmnModelerOperationStatus.Unavailable,
                "CANVAS_NATIVE_DOCUMENT_EXPORT_UNAVAILABLE", "Native Document export is unavailable while the editor is not ready.");
#pragma warning disable CA1031 // Application boundaries return bounded operation feedback.
        try
        {
            var bytes = BpmnDocumentSerializer.Serialize(capture.Snapshot!);
            var checkpoint = _editor.CreateSaveCheckpoint(capture.Snapshot!);
            if (checkpoint is null) return FileFailure(BpmnModelerOperationStatus.Unavailable,
                "CANVAS_NATIVE_DOCUMENT_EXPORT_UNAVAILABLE", "The document changed during export.");
            return new BpmnModelerFileResult(BpmnModelerOperationStatus.Succeeded,
                new BpmnModelerFileArtifact(NativeDocumentFileName, NativeDocumentContentType, bytes), [], checkpoint);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return FileFailure(BpmnModelerOperationStatus.Failed,
                "CANVAS_NATIVE_DOCUMENT_EXPORT_FAILED", "The current Document could not be prepared for download.");
        }
#pragma warning restore CA1031
    }

    internal void DismissDiagnostics() => Diagnostics = [];

    internal async ValueTask<ValidationSnapshot?> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || ValidationInFlight) return null;
        var capture = await _editor.GetDocumentAsync(cancellationToken).ConfigureAwait(false);
        var context = _editor.CaptureEditorContext();
        if (!capture.Succeeded || context is null || !capture.Snapshot!.Equals(context.Document) ||
            context.Presentation is not
            {
                Status: EditingSessionStatus.Ready, EditorState.ActiveGesture: null,
                ProjectedGraph: { } graph, RoutingResult: { } routing
            }) return null;
        ValidationInFlight = true;
#pragma warning disable CA1031 // Faulting plugin validation must not fault the editor.
        try
        {
            DocumentValidation = _validator.Validate(capture.Snapshot);
            var validationContext = context.Presentation.PresentationPlan is { } plan
                ? new ModelValidationContext(capture.Snapshot, plan)
                : new ModelValidationContext(capture.Snapshot, context.Presentation.ActiveScopeId);
            _validation = _validator.Validate(validationContext, RoutingModelValidationIssueProvider.CreateIssues(graph, routing),
                context.Presentation.Generation.Value);
            _validationAttachment = context.AttachmentVersion;
            Diagnostics = [];
            return ValidationSnapshot;
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _validation = null;
            Diagnostics = [Error("CANVAS_MODEL_VALIDATION_FAILED", "Model validation failed because a validation contribution threw unexpectedly.")];
            return null;
        }
        finally { ValidationInFlight = false; }
#pragma warning restore CA1031
    }

    internal async ValueTask<Canvas2DInteractionResult?> SelectValidationIssueAsync(ModelValidationIssueId issueId, CancellationToken cancellationToken = default)
    {
        var validation = ValidationSnapshot;
        var issue = validation?.Issues.FirstOrDefault(candidate => candidate.Id == issueId);
        if (validation is not null && issue?.Target.VisualStateId is { } visual)
            return await _editor.SelectVisualAsync(validation.DocumentId, validation.SourceRevision, validation.PresentationContext,
                visual, cancellationToken).ConfigureAwait(false);
        return null;
    }

    internal ValueTask<BpmnModelerFileResult> PublishProcessAsync(CancellationToken cancellationToken = default) =>
        PublishProcessAsync(new BpmnPublishingOptions(), cancellationToken);

    internal ValueTask<BpmnModelerFileResult> PublishProcessAsync(BpmnPublishingOptions options,
        CancellationToken cancellationToken = default) =>
        PublishProcessAsync(options, null, cancellationToken);

    private async ValueTask<BpmnModelerFileResult> PublishProcessAsync(BpmnPublishingOptions options,
        BpmnEditorContext? expectedView, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (cancellationToken.IsCancellationRequested) return CancelledFile();
#pragma warning disable CA1031 // Application failures become bounded feedback, without modifying the document.
        try
        {
            var document = _editor.GetDocument();
            if (!document.Succeeded) return FileFailure(BpmnModelerOperationStatus.Unavailable,
                "CANVAS_PUBLISH_UNAVAILABLE", "Publish is unavailable while the editor is not ready.");
            if (options.InitialViewMode == BpmnPublishingInitialViewMode.CurrentEditorView)
            {
                expectedView ??= _editor.CaptureEditorContext();
                if (!TryPrepareEditorView(options, expectedView, out options) ||
                    !expectedView!.Document.Equals(document.Snapshot)) return InitialViewUnavailable();
            }
            DocumentValidation = _validator.Validate(document.Snapshot!);
            if (!DocumentValidation.IsStructurallyValid)
                return new(BpmnModelerOperationStatus.Rejected, null, DocumentValidation.Diagnostics);
            EditingSessionPresentationCaptureResult? capture;
            try { capture = await _editor.CapturePresentationAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return CancelledFile(); }
            catch (Exception exception) when (IsNonFatal(exception))
            { return FileFailure(BpmnModelerOperationStatus.Failed, "CANVAS_PUBLISH_PREPARATION_FAILED", "Could not prepare the current process presentation for publication. Please retry."); }
            if (capture?.Succeeded != true || capture.Capture is null)
                return new(capture?.Status == EditingSessionPresentationCaptureStatus.Unavailable || capture is null
                    ? BpmnModelerOperationStatus.Unavailable : BpmnModelerOperationStatus.Failed, null,
                    (capture?.Diagnostics ?? []).Insert(0, Error("CANVAS_PUBLISH_PREPARATION_FAILED", "Could not prepare the current process presentation for publication. Please retry.")));
            if (!document.Snapshot!.Equals(capture.Capture.Document))
                return FileFailure(BpmnModelerOperationStatus.Unavailable, "CANVAS_PUBLISH_UNAVAILABLE", "The document changed during publication.");
            if (expectedView is not null && (!SameEditorView(expectedView, _editor.CaptureEditorContext(), document.Snapshot) ||
                expectedView.Presentation.PresentationContext != capture.Capture.PresentationContext))
                return InitialViewUnavailable();
            var generated = _publisher.Generate(document.Snapshot, capture.Capture, options);
            cancellationToken.ThrowIfCancellationRequested();
            return generated.Succeeded && generated.Package is { } package
                ? new(BpmnModelerOperationStatus.Succeeded, new BpmnModelerFileArtifact(PublishFileName, PublishContentType, package.Archive), generated.Diagnostics)
                : new(generated.Diagnostics.Any(static diagnostic => diagnostic.Code == "PUBLISH_PACKAGE_BUILD_FAILED")
                    ? BpmnModelerOperationStatus.Failed : BpmnModelerOperationStatus.Rejected, null, generated.Diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return CancelledFile(); }
        catch (Exception exception) when (IsNonFatal(exception))
        { return FileFailure(BpmnModelerOperationStatus.Failed, "CANVAS_PUBLISH_FAILED", "The static Publish package could not be generated."); }
#pragma warning restore CA1031
    }

    internal ValueTask<DemoPublicationResult> SavePublicationAsync(long attachmentVersion, DocumentId documentId,
        DocumentRevision revision, DocumentPublicationSnapshot publication, CancellationToken cancellationToken = default) =>
        UpdatePublicationAsync(attachmentVersion, documentId, revision, publication, null, cancellationToken);

    internal ValueTask<DemoPublicationResult> PublishPublicationAsync(long attachmentVersion, DocumentId documentId,
        DocumentRevision revision, DocumentPublicationSnapshot publication, CancellationToken cancellationToken = default) =>
        PublishPublicationAsync(attachmentVersion, documentId, revision, publication, new BpmnPublishingOptions(), cancellationToken);

    internal ValueTask<DemoPublicationResult> PublishPublicationAsync(long attachmentVersion, DocumentId documentId,
        DocumentRevision revision, DocumentPublicationSnapshot publication, BpmnPublishingOptions options,
        CancellationToken cancellationToken = default) =>
        UpdatePublicationAsync(attachmentVersion, documentId, revision, publication, options, cancellationToken);

    private async ValueTask<DemoPublicationResult> UpdatePublicationAsync(long attachmentVersion, DocumentId documentId,
        DocumentRevision revision, DocumentPublicationSnapshot publication, BpmnPublishingOptions? options, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return new(BpmnModelerOperationStatus.Cancelled, false, [], []);
        BpmnEditorContext? view = null;
        if (options?.InitialViewMode == BpmnPublishingInitialViewMode.CurrentEditorView)
        {
            view = _editor.CaptureEditorContext();
            if (!TryPrepareEditorView(options, view, out var prepared) || view!.AttachmentVersion != attachmentVersion ||
                view.Document.DocumentId != documentId || view.Document.Revision != revision)
                return new(BpmnModelerOperationStatus.Unavailable, false, [], InitialViewUnavailable().Diagnostics);
            options = prepared;
        }
        var saved = await _editor.UpdatePublicationAsync(attachmentVersion, documentId, revision, publication, cancellationToken).ConfigureAwait(false);
        var changed = saved.Snapshot?.Revision != revision && saved.Succeeded;
        if (!saved.Succeeded || options is null) return new(saved.Status, changed, [], saved.Diagnostics);
        if (view is not null)
        {
            // Only the existing publication-metadata command may advance this captured view's revision.
            var expectedRevision = Equals(view.Document.Publication, publication) ? revision : revision.Increment();
            var current = _editor.CaptureEditorContext();
            if (saved.Snapshot is not { } snapshot || snapshot.Revision != expectedRevision ||
                !Equals(snapshot.Publication, publication) || !SameEditorView(view, current, snapshot))
                return new(BpmnModelerOperationStatus.Unavailable, changed, [], InitialViewUnavailable().Diagnostics);
            options = options with { EditorView = options.EditorView! with { Revision = snapshot.Revision } };
            view = current;
        }
        var package = await PublishProcessAsync(options, view, cancellationToken).ConfigureAwait(false);
        return new(package.Status, changed, package.Artifact?.Content ?? [], package.Diagnostics);
    }

    private static bool TryPrepareEditorView(BpmnPublishingOptions options, BpmnEditorContext? context,
        out BpmnPublishingOptions prepared)
    {
        prepared = options;
        if (context is not { CanReplaceDocument: true, SurfaceSize: { CssWidth: > 0, CssHeight: > 0 } } ||
            context.Presentation.Status != EditingSessionStatus.Ready ||
            context.Presentation.PresentationContext is not ProcessScopePresentationContext process ||
            context.Presentation.EditorState.Viewport.VisibleDocumentRegion is not { IsEmpty: false } region)
            return false;
        var view = new BpmnPublishingEditorView(context.Document.DocumentId, context.Document.Revision, process.ScopeId, region);
        if (options.EditorView is not null && options.EditorView != view) return false;
        prepared = options with { EditorView = view };
        return true;
    }

    private static bool SameEditorView(BpmnEditorContext expected, BpmnEditorContext? current, DocumentSnapshot document) =>
        current is { CanReplaceDocument: true } && current.AttachmentVersion == expected.AttachmentVersion &&
        current.Document.Equals(document) && current.Presentation.Status == EditingSessionStatus.Ready &&
        current.SurfaceSize == expected.SurfaceSize &&
        current.Presentation.PresentationContext == expected.Presentation.PresentationContext &&
        current.Presentation.EditorState.Viewport.Equals(expected.Presentation.EditorState.Viewport) &&
        current.Presentation.ModelProfileViewState.Equals(expected.Presentation.ModelProfileViewState) &&
        current.Presentation.ModelProfileElementViewState.Equals(expected.Presentation.ModelProfileElementViewState);

    private static BpmnModelerFileResult InitialViewUnavailable() => FileFailure(BpmnModelerOperationStatus.Unavailable,
        "CANVAS_PUBLISH_INITIAL_VIEW_UNAVAILABLE",
        "The current Editor view is unavailable or changed during publication. Wait for the process view to finish drawing and retry, or select Fit entire diagram.");

    private static BpmnModelerFileResult CancelledFile() => new(BpmnModelerOperationStatus.Cancelled, null, []);
    private static BpmnModelerFileResult FileFailure(BpmnModelerOperationStatus status, string code, string message) => new(status, null, [Error(code, message)]);
    private static Diagnostic Error(string code, string message) => new(code, DiagnosticSeverity.Error, message);
    private static bool IsNonFatal(Exception exception) => exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;
}

internal sealed record DemoPublicationResult(BpmnModelerOperationStatus Status, bool PublicationChanged,
    ImmutableArray<byte> Payload, ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool Succeeded => Status == BpmnModelerOperationStatus.Succeeded;
}
