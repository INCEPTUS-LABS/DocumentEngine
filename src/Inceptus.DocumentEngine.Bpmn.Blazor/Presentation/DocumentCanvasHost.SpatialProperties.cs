using Inceptus.DocumentEngine.Canvas2D.EditingSession;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private static DocumentCanvasModelViewPropertiesSnapshot CaptureModelViewPropertiesSnapshot(
        EditingSession session, EditingSessionState state) =>
        DocumentCanvasModelViewPropertiesSnapshot.Create(state);
}
