using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class FloatingIssuesPanelStateTests
{
    [Fact]
    public void ToggleUsesBottomRightDefaultAndRetainsPositionAcrossCloseAndReopen()
    {
        var state = new FloatingIssuesPanelState();

        state.Toggle(800d, 600d);

        Assert.True(state.IsOpen);
        Assert.Equal(480d, state.Width);
        Assert.Equal(416d, state.Height);
        Assert.Equal(304d, state.Left);
        Assert.Equal(168d, state.Top);

        Assert.True(state.TryBeginMove(7, 400d, 200d));
        Assert.True(state.Move(7, 450d, 250d));
        Assert.True(state.EndMove(7));
        Assert.Equal(354d, state.Left);
        Assert.Equal(218d, state.Top);

        state.Toggle(800d, 600d);
        Assert.False(state.IsOpen);
        state.Toggle(800d, 600d);

        Assert.True(state.IsOpen);
        Assert.Equal(354d, state.Left);
        Assert.Equal(218d, state.Top);
    }

    [Fact]
    public void DragIsPrimaryHeaderStateOnlyAndEndsOnMatchingPointer()
    {
        var state = new FloatingIssuesPanelState();
        state.Open(800d, 600d);

        Assert.True(state.TryBeginMove(11, 300d, 180d));
        Assert.False(state.TryBeginMove(12, 300d, 180d));
        Assert.False(state.Move(12, 600d, 500d));
        Assert.False(state.EndMove(12));
        Assert.Equal(11, state.ActivePointerId);

        Assert.True(state.Move(11, 325d, 205d));
        Assert.Equal(329d, state.Left);
        Assert.Equal(193d, state.Top);
        Assert.True(state.EndMove(11));
        Assert.Null(state.ActivePointerId);
    }

    [Fact]
    public void ClampKeepsDeterministicHeaderAreaReachable()
    {
        var state = new FloatingIssuesPanelState();
        state.Open(800d, 600d);

        Assert.True(state.TryBeginMove(23, 0d, 0d));
        Assert.True(state.Move(23, -10_000d, -10_000d));
        Assert.Equal(-416d, state.Left);
        Assert.Equal(0d, state.Top);

        Assert.True(state.Move(23, 10_000d, 10_000d));
        Assert.Equal(736d, state.Left);
        Assert.Equal(552d, state.Top);
        Assert.True(state.EndMove(23));
    }

    [Fact]
    public void ResizeClampsOnlyAsNeededAndUsesResponsivePanelDimensions()
    {
        var state = new FloatingIssuesPanelState();
        state.Open(800d, 600d);
        Assert.True(state.TryBeginMove(31, 0d, 0d));
        Assert.True(state.Move(31, 500d, 500d));
        Assert.True(state.EndMove(31));
        var beforeLeft = state.Left;
        var beforeTop = state.Top;

        state.ResizeSurface(400d, 300d);

        Assert.Equal(368d, state.Width);
        Assert.Equal(268d, state.Height);
        Assert.Equal(Math.Min(beforeLeft, 336d), state.Left);
        Assert.Equal(Math.Min(beforeTop, 252d), state.Top);
        Assert.True(state.IsOpen);
    }

    [Fact]
    public void OpenCloseMoveAndResizeRejectInvalidInputWithoutPersistentDependencies()
    {
        var state = new FloatingIssuesPanelState();

        Assert.False(state.TryBeginMove(1, 0d, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Open(0d, 600d));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Open(800d, double.NaN));

        state.Open(800d, 600d);
        Assert.False(state.TryBeginMove(-1, 0d, 0d));
        Assert.False(state.TryBeginMove(1, double.NaN, 0d));
        Assert.True(state.TryBeginMove(1, 0d, 0d));
        state.CancelMove();
        Assert.Null(state.ActivePointerId);
        state.Close();
        Assert.False(state.IsOpen);
    }
}
