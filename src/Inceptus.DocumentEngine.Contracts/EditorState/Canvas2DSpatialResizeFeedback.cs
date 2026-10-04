using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.EditorState;

public enum Canvas2DSpatialResizePhase { Hover, Drag }

/// <summary>Pure temporary dimension feedback; never a saved frame or interaction target.</summary>
public sealed record Canvas2DSpatialResizeFeedback
{
    public const string FeedbackKind = "canvas2d:spatial-resize";

    public Canvas2DSpatialResizeFeedback(Canvas2DSpatialResizeTarget target, Canvas2DSpatialResizePhase phase,
        double requestedExtent)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (!double.IsFinite(requestedExtent)) throw new ArgumentOutOfRangeException(nameof(requestedExtent));
        Target = target;
        Phase = phase;
        RequestedExtent = requestedExtent;
    }

    public Canvas2DSpatialResizeTarget Target { get; }
    public Canvas2DSpatialResizePhase Phase { get; }
    public double RequestedExtent { get; }
    public double CandidateEdge => (Target.Edge == Canvas2DSpatialResizeEdge.Bottom
        ? Target.PaintedBounds.Bottom : Target.PaintedBounds.Right) + RequestedExtent - Target.AuthoredExtent;
    public ImmutableArray<Diagnostic> Diagnostics => Target.Constraints.Validate(RequestedExtent);
    public bool IsAllowed => !Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
