using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal interface ICanvasPresentationPointerObserver : IAsyncDisposable
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask ReleaseCaptureAsync(long captureGeneration, CancellationToken cancellationToken);

    ValueTask SetCursorAsync(string cssCursor, CancellationToken cancellationToken);
}

internal interface ICanvasPresentationPointerObserverFactory
{
    ValueTask<ICanvasPresentationPointerObserver> CreateAsync(
        string canvasElementId,
        Func<CanvasPointerInput, Task> onPointerInput,
        Func<CanvasWheelInput, Task> onWheelInput,
        CancellationToken cancellationToken);
}

internal enum CanvasWheelDeltaMode
{
    Pixel = 0,
    Line = 1,
    Page = 2,
}

internal readonly record struct CanvasWheelInput(
    double DeltaX,
    double DeltaY,
    CanvasWheelDeltaMode DeltaMode,
    bool ControlKey,
    bool MetaKey)
{
    private const double CssPixelsPerLine = 16d;

    internal bool IsFinite => double.IsFinite(DeltaX) && double.IsFinite(DeltaY);

    internal bool IsModified => ControlKey || MetaKey;

    internal bool HasDelta => DeltaX != 0d || DeltaY != 0d;

    internal VectorD ResolveCanvasTranslation(Canvas2DSurfaceSize surfaceSize) =>
        DeltaMode switch
        {
            CanvasWheelDeltaMode.Pixel => new VectorD(-DeltaX, -DeltaY),
            CanvasWheelDeltaMode.Line => new VectorD(
                -(DeltaX * CssPixelsPerLine),
                -(DeltaY * CssPixelsPerLine)),
            CanvasWheelDeltaMode.Page => new VectorD(
                -(DeltaX * surfaceSize.CssWidth),
                -(DeltaY * surfaceSize.CssHeight)),
            _ => throw new InvalidOperationException(
                "The browser wheel delta mode is unsupported."),
        };
}

internal enum CanvasPointerEventKind
{
    Down,
    Move,
    Up,
    Cancel,
    Leave,
    ContextMenu,
    DoubleClick,
}

internal readonly record struct CanvasPointerInput(
    CanvasPointerEventKind Kind,
    long PointerId,
    int Button,
    int Buttons,
    bool IsPrimary,
    double ClientX,
    double ClientY,
    double CanvasLeft,
    double CanvasTop,
    bool AltKey,
    bool ControlKey,
    bool MetaKey,
    bool ShiftKey,
    long CaptureGeneration = 0)
{
    internal bool IsFinite =>
        double.IsFinite(ClientX) &&
        double.IsFinite(ClientY) &&
        double.IsFinite(CanvasLeft) &&
        double.IsFinite(CanvasTop);
}
