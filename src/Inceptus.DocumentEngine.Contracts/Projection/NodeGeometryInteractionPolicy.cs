namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Declares notation-supplied node-body geometry interaction intent.
/// The policy is transient projection metadata and is never persistent instance state.
/// </summary>
public enum NodeGeometryInteractionPolicy
{
    FreeMoveAndResize = 0,
    AttachedBoundaryMoveFixedSize = 1,
}
