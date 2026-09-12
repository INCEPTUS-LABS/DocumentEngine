using Inceptus.DocumentEngine.Canvas2D.Rendering;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal interface ICanvasPresentationSurfaceObserver : IAsyncDisposable
{
    ValueTask<Canvas2DSurfaceSize> StartAsync(CancellationToken cancellationToken);
}

internal interface ICanvasPresentationSurfaceObserverFactory
{
    ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
        string containerElementId,
        Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
        CancellationToken cancellationToken);
}
