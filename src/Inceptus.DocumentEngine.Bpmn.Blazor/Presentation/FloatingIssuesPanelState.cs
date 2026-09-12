namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed class FloatingIssuesPanelState
{
    internal const double PreferredWidth = 480d;
    internal const double PreferredHeight = 416d;
    internal const double DefaultMargin = 16d;
    internal const double ReachableHeaderWidth = 64d;
    internal const double HeaderHeight = 48d;

    private double _surfaceWidth = PreferredWidth + (DefaultMargin * 2d);
    private double _surfaceHeight = PreferredHeight + (DefaultMargin * 2d);
    private bool _positionInitialized;
    private MoveGesture? _moveGesture;

    internal bool IsOpen { get; private set; }

    internal double Left { get; private set; }

    internal double Top { get; private set; }

    internal double Width => Math.Max(
        1d,
        Math.Min(PreferredWidth, _surfaceWidth - (DefaultMargin * 2d)));

    internal double Height => Math.Max(
        1d,
        Math.Min(PreferredHeight, _surfaceHeight - (DefaultMargin * 2d)));

    internal long? ActivePointerId => _moveGesture?.PointerId;

    internal void Toggle(double surfaceWidth, double surfaceHeight)
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open(surfaceWidth, surfaceHeight);
        }
    }

    internal void Open(double surfaceWidth, double surfaceHeight)
    {
        ResizeSurface(surfaceWidth, surfaceHeight);
        if (!_positionInitialized)
        {
            Left = Math.Max(DefaultMargin, _surfaceWidth - Width - DefaultMargin);
            Top = Math.Max(DefaultMargin, _surfaceHeight - Height - DefaultMargin);
            _positionInitialized = true;
        }

        ClampPosition();
        IsOpen = true;
    }

    internal void Close()
    {
        IsOpen = false;
        _moveGesture = null;
    }

    internal void ResizeSurface(double surfaceWidth, double surfaceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(surfaceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(surfaceHeight);
        if (!double.IsFinite(surfaceWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceWidth));
        }

        if (!double.IsFinite(surfaceHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceHeight));
        }

        _surfaceWidth = surfaceWidth;
        _surfaceHeight = surfaceHeight;
        if (_positionInitialized)
        {
            ClampPosition();
        }
    }

    internal bool TryBeginMove(
        long pointerId,
        double clientX,
        double clientY)
    {
        if (!IsOpen || pointerId < 0 || !CoordinatesAreFinite(clientX, clientY) ||
            _moveGesture is not null)
        {
            return false;
        }

        _moveGesture = new MoveGesture(pointerId, clientX, clientY, Left, Top);
        return true;
    }

    internal bool Move(long pointerId, double clientX, double clientY)
    {
        if (_moveGesture is not { } gesture || gesture.PointerId != pointerId ||
            !CoordinatesAreFinite(clientX, clientY))
        {
            return false;
        }

        Left = gesture.StartLeft + clientX - gesture.StartClientX;
        Top = gesture.StartTop + clientY - gesture.StartClientY;
        ClampPosition();
        return true;
    }

    internal bool EndMove(long pointerId)
    {
        if (_moveGesture?.PointerId != pointerId)
        {
            return false;
        }

        _moveGesture = null;
        return true;
    }

    internal void CancelMove() => _moveGesture = null;

    private void ClampPosition()
    {
        var reachableWidth = Math.Min(ReachableHeaderWidth, _surfaceWidth);
        var reachableHeight = Math.Min(HeaderHeight, _surfaceHeight);
        var minimumLeft = Math.Min(0d, -(Width - reachableWidth));
        var maximumLeft = Math.Max(0d, _surfaceWidth - reachableWidth);
        var maximumTop = Math.Max(0d, _surfaceHeight - reachableHeight);
        Left = Math.Clamp(Left, minimumLeft, maximumLeft);
        Top = Math.Clamp(Top, 0d, maximumTop);
    }

    private static bool CoordinatesAreFinite(double x, double y) =>
        double.IsFinite(x) && double.IsFinite(y);

    private sealed record MoveGesture(
        long PointerId,
        double StartClientX,
        double StartClientY,
        double StartLeft,
        double StartTop);
}
