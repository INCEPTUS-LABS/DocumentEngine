using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private const string PublishUnavailable = "CANVAS_PUBLISH_UNAVAILABLE";
    private const string PublishFailed = "CANVAS_PUBLISH_FAILED";
    private const string PublishPreparationFailed = "CANVAS_PUBLISH_PREPARATION_FAILED";
    private const string PublicationUnavailable = "CANVAS_PUBLICATION_UNAVAILABLE";
    private const string PublicationStale = "CANVAS_PUBLICATION_STALE";
    private const string PublicationPersistenceFailed =
        "CANVAS_PUBLICATION_PERSISTENCE_FAILED";

    private static readonly PublishedProcessPackageBuilder PublishPackageBuilder =
        new(
            new BpmnPublishedTokenRoleClassifier(),
            new BpmnPublishedNodeDataMapper());

    private readonly PublishedProcessPackageBuilder _publishPackageBuilder = PublishPackageBuilder;

    internal ValueTask<PublicationDialogHostResult> SavePublicationAsync(
        long expectedDocumentSessionVersion,
        DocumentId expectedDocumentId,
        DocumentRevision expectedRevision,
        DocumentPublicationSnapshot publication,
        CancellationToken cancellationToken = default) =>
        ExecutePublicationDialogActionAsync(
            expectedDocumentSessionVersion,
            expectedDocumentId,
            expectedRevision,
            publication,
            publish: false,
            cancellationToken);

    internal ValueTask<PublicationDialogHostResult> PublishPublicationAsync(
        long expectedDocumentSessionVersion,
        DocumentId expectedDocumentId,
        DocumentRevision expectedRevision,
        DocumentPublicationSnapshot publication,
        CancellationToken cancellationToken = default) =>
        ExecutePublicationDialogActionAsync(
            expectedDocumentSessionVersion,
            expectedDocumentId,
            expectedRevision,
            publication,
            publish: true,
            cancellationToken);

    internal async ValueTask<PublishedProcessHostResult> PublishProcessAsync(
        CancellationToken cancellationToken = default)
    {
        PublishedProcessHostResult? outcome = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return outcome = PublishedProcessHostResult.Cancelled;
        }

        try
        {
            if (!TryGetPublishSession(out var session))
            {
                return outcome = Failure(
                    PublishedProcessHostOperationStatus.Unavailable,
                    PublishUnavailable,
                    "Publish is unavailable while the editor is not ready.");
            }

            return outcome = await PublishCurrentUnderGateAsync(session!, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return outcome = PublishedProcessHostResult.Cancelled;
        }
#pragma warning disable CA1031 // Publish failures become bounded transient diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return outcome = Failure(
                PublishedProcessHostOperationStatus.Failed,
                PublishFailed,
                "The static Publish package could not be generated.",
                exception.GetType().FullName ?? exception.GetType().Name);
        }
#pragma warning restore CA1031
        finally
        {
            try
            {
                await ReportModelerOperationFailureUnderGateAsync(outcome).ConfigureAwait(false);
            }
            finally
            {
                await CompleteModelerOperationAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<PublicationDialogHostResult>
        ExecutePublicationDialogActionAsync(
            long expectedDocumentSessionVersion,
            DocumentId expectedDocumentId,
            DocumentRevision expectedRevision,
            DocumentPublicationSnapshot publication,
            bool publish,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedDocumentId);
        ArgumentNullException.ThrowIfNull(publication);
        PublicationDialogHostResult? outcome = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return outcome = PublicationDialogHostResult.Cancelled;
        }

        var publicationChanged = false;
        try
        {
            if (!TryGetPublishSession(out var session) ||
                !session!.TryCaptureDocumentSnapshot(out var document))
            {
                return outcome = PublicationFailure(
                    PublishedProcessHostOperationStatus.Unavailable,
                    PublicationUnavailable,
                    "Publication is unavailable while the editor is not ready.");
            }

            if (!IsCurrentDocumentSession(expectedDocumentSessionVersion) ||
                document.DocumentId != expectedDocumentId ||
                document.Revision != expectedRevision)
            {
                return outcome = PublicationFailure(
                    PublishedProcessHostOperationStatus.Rejected,
                    PublicationStale,
                    "The active Document changed after the Publication dialog opened.");
            }

            if (!Equals(document.Publication, publication))
            {
                var persisted = await session.ExecuteAsync(
                    new UpdateDocumentPublicationCommand(
                        expectedDocumentId,
                        expectedRevision,
                        publication),
                    linked.Token).ConfigureAwait(false);
                if (!persisted.IsCommitted)
                {
                    return outcome = new PublicationDialogHostResult(
                        linked.IsCancellationRequested
                            ? PublishedProcessHostOperationStatus.Cancelled
                            : PublishedProcessHostOperationStatus.Rejected,
                        PublicationChanged: false,
                        [],
                        persisted.Diagnostics);
                }

                publicationChanged = true;
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
                var state = session.CaptureState();
                if (state.Status != EditingSessionStatus.Ready ||
                    !session.TryCaptureDocumentSnapshot(out document) ||
                    document.DocumentId != expectedDocumentId ||
                    document.Revision != expectedRevision.Increment() ||
                    !Equals(document.Publication, publication))
                {
                    return outcome = PublicationFailure(
                        PublishedProcessHostOperationStatus.Failed,
                        PublicationPersistenceFailed,
                        "Publication was saved, but its current presentation could not be prepared.",
                        publicationChanged: true);
                }
            }

            if (!publish)
            {
                return outcome = PublicationDialogHostResult.Success(
                    publicationChanged,
                    payload: []);
            }

            var published = await PublishCurrentUnderGateAsync(session, linked.Token)
                .ConfigureAwait(false);
            return outcome = new PublicationDialogHostResult(
                published.Status,
                publicationChanged,
                published.Payload,
                published.Diagnostics);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return outcome = new PublicationDialogHostResult(
                PublishedProcessHostOperationStatus.Cancelled,
                publicationChanged,
                [],
                []);
        }
#pragma warning disable CA1031 // Publication failures become bounded transient diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return outcome = PublicationFailure(
                PublishedProcessHostOperationStatus.Failed,
                publicationChanged ? PublishFailed : PublicationPersistenceFailed,
                publicationChanged
                    ? "Publication was saved, but the static Publish package could not be generated."
                    : "Publication metadata could not be saved.",
                exception.GetType().FullName ?? exception.GetType().Name,
                publicationChanged);
        }
#pragma warning restore CA1031
        finally
        {
            try
            {
                await ReportModelerOperationFailureUnderGateAsync(outcome).ConfigureAwait(false);
            }
            finally
            {
                await CompleteModelerOperationAsync().ConfigureAwait(false);
            }
        }
    }

    private bool TryGetPublishSession(out EditingSession? session)
    {
        lock (_sync)
        {
            session = _session;
            return !_disposed &&
                _initialized &&
                session is not null &&
                !_propertiesFormOpen &&
                !_modelViewPropertiesFormOpen;
        }
    }

    private bool IsCurrentDocumentSession(long expectedDocumentSessionVersion)
    {
        lock (_sync)
        {
            return expectedDocumentSessionVersion == _documentSessionVersion;
        }
    }

    private async ValueTask<PublishedProcessHostResult>
        PublishCurrentUnderGateAsync(
            EditingSession session,
            CancellationToken cancellationToken)
    {
        var editorState = session.CaptureState();
        if (editorState.Status != EditingSessionStatus.Ready ||
            editorState.CurrentScene is null ||
            editorState.ProjectedGraph is null ||
            editorState.LayoutResult is null ||
            editorState.RoutingResult is null ||
            editorState.EditorState.ActiveGesture is not null)
        {
            return Failure(
                PublishedProcessHostOperationStatus.Unavailable,
                PublishUnavailable,
                "Could not prepare the current process presentation for publication. " +
                    "Wait for the editor to become ready, then retry.");
        }

        var publishProfileState = editorState.ModelProfileViewState
            .WithPreferredVisibility(OrganizationalModelProfile.Id, isVisible: false);
        var publishElementState = new ModelProfileElementViewStateSnapshot(
            editorState.ModelProfileElementViewState.CollapsedElements.Where(entry =>
                entry.ProfileId != OrganizationalModelProfile.Id));
        EditingSessionPresentationCaptureResult capture;
        try
        {
            capture = await session.CapturePresentationAsync(
                publishProfileState,
                publishElementState,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PublishedProcessHostResult.Cancelled;
        }
#pragma warning disable CA1031 // Preparation failures must not be mislabeled as package failures.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return Failure(
                PublishedProcessHostOperationStatus.Failed,
                PublishPreparationFailed,
                "Could not prepare the current process presentation for publication. Please retry.",
                exception.GetType().FullName ?? exception.GetType().Name);
        }
#pragma warning restore CA1031

        if (!capture.Succeeded || capture.Capture is null)
        {
            if (capture.Status == EditingSessionPresentationCaptureStatus.Cancelled)
            {
                return PublishedProcessHostResult.Cancelled;
            }

            return new PublishedProcessHostResult(
                capture.Status == EditingSessionPresentationCaptureStatus.Unavailable
                    ? PublishedProcessHostOperationStatus.Unavailable
                    : PublishedProcessHostOperationStatus.Failed,
                [],
                capture.Diagnostics.Insert(0, Diagnostic(
                    PublishPreparationFailed,
                    "Could not prepare the current process presentation for publication. Please retry.")));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var current = session.CaptureState();
        if (current.Status != EditingSessionStatus.Ready ||
            current.DocumentId != capture.Capture.Document.DocumentId ||
            current.DocumentRevision != capture.Capture.Document.Revision ||
            current.ActiveScopeId != capture.Capture.ActiveScopeId ||
            current.Generation != capture.Capture.Generation)
        {
            return Failure(
                PublishedProcessHostOperationStatus.Unavailable,
                PublishUnavailable,
                "The current process presentation changed during publication. Please retry.");
        }

        var package = _publishPackageBuilder.Build(capture.Capture);
        cancellationToken.ThrowIfCancellationRequested();
        return package.Succeeded && package.Package is not null
            ? PublishedProcessHostResult.Success(package.Package.Archive)
            : new PublishedProcessHostResult(
                package.Diagnostics.Any(static diagnostic =>
                    diagnostic.Code == "PUBLISH_PACKAGE_BUILD_FAILED")
                        ? PublishedProcessHostOperationStatus.Failed
                        : PublishedProcessHostOperationStatus.Rejected,
                [],
                AddPublishElementNames(package.Diagnostics, capture.Capture.Document));
    }

    private static ImmutableArray<Diagnostic> AddPublishElementNames(
        ImmutableArray<Diagnostic> diagnostics,
        DocumentSnapshot document) =>
        diagnostics.Select(diagnostic =>
        {
            if (!diagnostic.Context.TryGetValue("ElementId", out var elementId) ||
                string.IsNullOrWhiteSpace(elementId) ||
                !document.SemanticModel.TryGetElement(new SemanticElementId(elementId), out var element))
            {
                return diagnostic;
            }

            var context = diagnostic.Context
                .SetItem("ElementId", element!.Id.Value)
                .SetItem("ElementType", element.TypeId.Value);
            if (element.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) &&
                name.Kind == PropertyValueKind.Text)
            {
                context = context.SetItem("ElementName", name.TextValue);
            }

            return new Diagnostic(
                diagnostic.Code, diagnostic.Severity, diagnostic.Message,
                diagnostic.SourceIdentity, context);
        }).ToImmutableArray();

    private static PublishedProcessHostResult Failure(
        PublishedProcessHostOperationStatus status,
        string code,
        string message,
        string? exceptionType = null) =>
        new(status, [], [Diagnostic(code, message, exceptionType)]);

    private static PublicationDialogHostResult PublicationFailure(
        PublishedProcessHostOperationStatus status,
        string code,
        string message,
        string? exceptionType = null,
        bool publicationChanged = false) =>
        new(status, publicationChanged, [], [Diagnostic(code, message, exceptionType)]);

    private static Diagnostic Diagnostic(
        string code,
        string message,
        string? exceptionType = null) =>
        new(
            code,
            DiagnosticSeverity.Error,
            message,
            "Publish",
            exceptionType is null
                ? null
                :
                [
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exceptionType),
                ]);
}

internal enum PublishedProcessHostOperationStatus
{
    Succeeded,
    Rejected,
    Unavailable,
    Failed,
    Cancelled,
}

internal sealed record PublishedProcessHostResult(
    PublishedProcessHostOperationStatus Status,
    ImmutableArray<byte> Payload,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool Succeeded => Status == PublishedProcessHostOperationStatus.Succeeded;

    internal static PublishedProcessHostResult Success(ImmutableArray<byte> payload) =>
        new(PublishedProcessHostOperationStatus.Succeeded, payload, []);

    internal static PublishedProcessHostResult Cancelled { get; } =
        new(PublishedProcessHostOperationStatus.Cancelled, [], []);
}

internal sealed record PublicationDialogHostResult(
    PublishedProcessHostOperationStatus Status,
    bool PublicationChanged,
    ImmutableArray<byte> Payload,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool Succeeded => Status == PublishedProcessHostOperationStatus.Succeeded;

    internal static PublicationDialogHostResult Success(
        bool publicationChanged,
        ImmutableArray<byte> payload) =>
        new(
            PublishedProcessHostOperationStatus.Succeeded,
            publicationChanged,
            payload,
            []);

    internal static PublicationDialogHostResult Cancelled { get; } =
        new(PublishedProcessHostOperationStatus.Cancelled, false, [], []);
}
