using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Blazor.Demo;

public partial class BpmnDemoHost
{
    private ToolbarPresentation _toolbarPresentation = ToolbarPresentation.Unavailable;
    private ToolbarInputs? _readyToolbarInputs;

    // This snapshot controls appearance only. Operation handlers retain the live
    // Can* predicates and the editor/operation services remain authoritative.
    private ToolbarPresentation CaptureToolbarPresentation()
    {
        if (_disposed || _host is null || _state is not
            { IsInitialized: true, Session: { IsClosed: false } session } state)
        {
            InvalidateToolbarPresentation();
            return _toolbarPresentation;
        }

        var editor = session.EditorState;
        var inputs = new ToolbarInputs(_host, state.DocumentSessionVersion,
            session.DocumentId, session.DocumentRevision, session.PresentationContext,
            new EditorStateSnapshot(activeToolId: editor.ActiveToolId, activeGesture: editor.ActiveGesture,
                toolState: editor.ToolState, temporaryFeedback: editor.TemporaryFeedback.Where(feedback =>
                    feedback.PlacementPreview is null &&
                    feedback.SpatialResize?.Phase != Canvas2DSpatialResizePhase.Hover)),
            session.ModelProfileState, session.ModelProfileViewState, session.ModelProfileElementViewState,
            state.PropertiesFormOpen, state.ValidationInFlight, _standardInProgress || _graphicalExportInProgress,
            _nativeDocumentImportInProgress, _nativeDocumentExportInProgress, _publicationDialogOpen, _publishInProgress);

        // Selection, hover, placement previews, viewport and scene identity do not change site actions.
        // A different document, form, operation or gesture must not reuse availability.
        if (session.Status == EditingSessionStatus.Rebuilding && inputs == _readyToolbarInputs)
            return _toolbarPresentation;

        _readyToolbarInputs = session.Status == EditingSessionStatus.Ready ? inputs : null;
        var next = new ToolbarPresentation(CanUseNativeDocumentTransfer, CanPublish, CanValidate);
        if (next != _toolbarPresentation) _toolbarPresentation = next;
        return _toolbarPresentation;
    }

    private void InvalidateToolbarPresentation()
    {
        _readyToolbarInputs = null;
        _toolbarPresentation = ToolbarPresentation.Unavailable;
    }

    private sealed record ToolbarPresentation(bool CanTransfer, bool CanPublish, bool CanValidate)
    {
        internal static readonly ToolbarPresentation Unavailable = new(false, false, false);
    }

    private sealed record ToolbarInputs(IBpmnDocumentEditor Editor, long AttachmentVersion,
        DocumentId DocumentId, DocumentRevision Revision, PresentationViewContext Context,
        EditorStateSnapshot Interaction, ModelProfileStateSnapshot Profiles, ModelProfileViewStateSnapshot ProfileViews,
        ModelProfileElementViewStateSnapshot ElementViews, bool PropertiesOpen, bool ValidationInFlight,
        bool StandardInProgress, bool ImportInProgress, bool ExportInProgress, bool PublicationDialogOpen, bool PublishInProgress);
}
