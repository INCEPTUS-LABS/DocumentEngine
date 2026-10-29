using Inceptus.DocumentEngine.Canvas2D.EditingSession;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private static DocumentCanvasModelViewPropertiesSnapshot CaptureModelViewPropertiesSnapshot(
        EditingSession session, EditingSessionState state) =>
        DocumentCanvasModelViewPropertiesSnapshot.Create(state) with
        {
            AppearanceMetadata = session.TryCaptureDocumentSnapshot(out var document) ? document.Metadata : null,
            ViewContext = state.PresentationContext,
        };
}
