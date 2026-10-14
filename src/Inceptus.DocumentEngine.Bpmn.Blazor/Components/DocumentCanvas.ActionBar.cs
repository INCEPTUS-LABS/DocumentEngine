using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Components;

public partial class DocumentCanvas
{
    private ActionBarPresentation _actionBarPresentation = ActionBarPresentation.Unavailable;
    private ActionBarInputs? _readyActionBarInputs;

    // Presentation only: handlers must continue to use the live Can* predicates and
    // host/session validation. An enabled-looking button does not authorize a command.
    private ActionBarPresentation CaptureActionBarPresentation()
    {
        if (_disposed || _state is not
            {
                IsInitialized: true, IsDisposed: false,
                Session: { IsClosed: false } session
            } state)
        {
            InvalidateActionBarPresentation();
            return _actionBarPresentation;
        }

        var editor = session.EditorState;
        var inputs = new ActionBarInputs(_host, state.DocumentSessionVersion,
            session.DocumentId, session.DocumentRevision, session.PresentationContext,
            state.SurfaceSize, session.HistoryStatus.CanUndo, session.HistoryStatus.CanRedo,
            new EditorStateSnapshot(selection: editor.Selection, activeToolId: editor.ActiveToolId, viewport: editor.Viewport,
                activeGesture: editor.ActiveGesture, toolState: editor.ToolState,
                temporaryFeedback: editor.TemporaryFeedback.Where(feedback =>
                    feedback.SpatialResize?.Phase != Canvas2DSpatialResizePhase.Hover)),
            session.ModelProfileState, session.ModelProfileViewState, session.ModelProfileElementViewState,
            _propertiesDraft is not null || state.PropertiesFormOpen,
            _modelViewPropertiesDraft is not null || state.ModelViewPropertiesFormOpen,
            AnyPropertiesDirty || state.PropertiesFormDirty || state.ModelViewPropertiesFormDirty,
            AnyPropertiesApplying || state.PropertiesApplyInFlight || state.ModelViewPropertiesApplyInFlight,
            _newDiagramConfirmationOpen, _newDiagramInProgress);

        // Hover identity and resize-hover feedback do not affect these
        // actions. Scene instances and generation numbers are deliberately absent.
        if (session.Status == EditingSessionStatus.Rebuilding && inputs == _readyActionBarInputs)
            return _actionBarPresentation;

        _readyActionBarInputs = session.Status == EditingSessionStatus.Ready ? inputs : null;
        var next = new ActionBarPresentation(CanNavigateScope, CanCreateNewDiagram, CanUndo, CanRedo,
            CanZoomOut, CanSetZoomToActualSize, CanZoomIn, CanAutoArrange, CanAlign(true), CanAlign(false));
        if (next != _actionBarPresentation) _actionBarPresentation = next;
        return _actionBarPresentation;
    }

    private void InvalidateActionBarPresentation()
    {
        _readyActionBarInputs = null;
        _actionBarPresentation = ActionBarPresentation.Unavailable;
    }

    private sealed record ActionBarPresentation(bool CanNavigateScope, bool CanCreateNewDiagram,
        bool CanUndo, bool CanRedo, bool CanZoomOut, bool CanSetZoomToActualSize,
        bool CanZoomIn, bool CanAutoArrange, bool CanAlignHorizontal, bool CanAlignVertical)
    {
        internal static readonly ActionBarPresentation Unavailable = new(false, false, false, false,
            false, false, false, false, false, false);
    }

    private sealed record ActionBarInputs(DocumentCanvasHost? Host, long SessionVersion,
        DocumentId DocumentId, DocumentRevision Revision, PresentationViewContext Context,
        Canvas2DSurfaceSize? SurfaceSize, bool CanUndo, bool CanRedo, EditorStateSnapshot Interaction,
        ModelProfileStateSnapshot Profiles, ModelProfileViewStateSnapshot ProfileViews,
        ModelProfileElementViewStateSnapshot ElementViews, bool PropertiesOpen, bool ModelViewPropertiesOpen,
        bool PropertiesDirty, bool PropertiesApplying, bool ConfirmationOpen, bool NewDiagramInProgress);
}
