namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Defines the supported transient placement modes for a node-owned projected label.
/// </summary>
public enum NodeLabelPlacementKind
{
    InsideCentered = 0,
    OutsideBelow = 1,
}

/// <summary>
/// Immutable, notation-neutral placement intent for a node-owned projected label.
/// Numeric values use logical document-coordinate units.
/// </summary>
public sealed class NodeLabelPlacement : IEquatable<NodeLabelPlacement>
{
    public NodeLabelPlacement(
        NodeLabelPlacementKind kind,
        double gap = 0d,
        double? maximumWidth = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The node-label placement kind must be defined.");
        }

        if (!double.IsFinite(gap) || gap < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gap),
                gap,
                "The node-label gap must be finite and non-negative.");
        }

        if (maximumWidth is { } width &&
            (!double.IsFinite(width) || width <= 0d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumWidth),
                maximumWidth,
                "The node-label maximum width must be finite and greater than zero.");
        }

        if (kind == NodeLabelPlacementKind.InsideCentered &&
            (gap != 0d || maximumWidth is not null))
        {
            throw new ArgumentException(
                "Inside-centered node-label placement does not accept an external gap or maximum width.");
        }

        if (kind == NodeLabelPlacementKind.OutsideBelow && maximumWidth is null)
        {
            throw new ArgumentException(
                "Outside-below node-label placement requires a maximum width.",
                nameof(maximumWidth));
        }

        Kind = kind;
        Gap = gap;
        MaximumWidth = maximumWidth;
    }

    public static NodeLabelPlacement InsideCentered { get; } =
        new(NodeLabelPlacementKind.InsideCentered);

    public NodeLabelPlacementKind Kind { get; }

    public double Gap { get; }

    public double? MaximumWidth { get; }

    public bool Equals(NodeLabelPlacement? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Kind == other.Kind &&
        Gap.Equals(other.Gap) &&
        MaximumWidth.Equals(other.MaximumWidth);

    public override bool Equals(object? obj) => Equals(obj as NodeLabelPlacement);

    public override int GetHashCode() => HashCode.Combine(Kind, Gap, MaximumWidth);
}
