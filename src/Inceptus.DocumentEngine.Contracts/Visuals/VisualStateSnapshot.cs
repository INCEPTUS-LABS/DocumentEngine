using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

public sealed class VisualStateSnapshot : IEquatable<VisualStateSnapshot>
{
    public VisualStateSnapshot(
        VisualStateId id,
        SemanticElementId semanticElementId,
        PointD position,
        SizeD size,
        VisualPlacementMode placementMode,
        IEnumerable<PointD>? route = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? properties = null,
        IEnumerable<ConnectorAnchor>? connectorAnchors = null,
        ConnectorAnchorId? sourceAnchorId = null,
        ConnectorAnchorId? targetAnchorId = null,
        BoundaryAttachmentPlacement? boundaryAttachment = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(semanticElementId);

        if (!Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placementMode),
                placementMode,
                "The placement mode must be defined.");
        }

        Id = id;
        SemanticElementId = semanticElementId;
        Position = position;
        Size = size;
        PlacementMode = placementMode;
        Route = route?.ToImmutableArray() ?? [];
        Properties = new PropertyMap(properties);
        ConnectorAnchors = CopyAndOrderConnectorAnchors(connectorAnchors);
        SourceAnchorId = sourceAnchorId;
        TargetAnchorId = targetAnchorId;
        BoundaryAttachment = boundaryAttachment;
    }

    public VisualStateId Id { get; }

    public SemanticElementId SemanticElementId { get; }

    public PointD Position { get; }

    public SizeD Size { get; }

    public VisualPlacementMode PlacementMode { get; }

    /// <summary>
    /// Gets an explicitly persisted route in document coordinates, not a transient RoutingResult.
    /// </summary>
    public ImmutableArray<PointD> Route { get; }

    public PropertyMap Properties { get; }

    /// <summary>
    /// Gets persistent connector anchors owned by this Visual State, canonically
    /// ordered by side and then by their per-side order.
    /// </summary>
    public ImmutableArray<ConnectorAnchor> ConnectorAnchors { get; }

    /// <summary>
    /// Gets the optional visual attachment used by a relationship's source endpoint.
    /// </summary>
    public ConnectorAnchorId? SourceAnchorId { get; }

    /// <summary>
    /// Gets the optional visual attachment used by a relationship's target endpoint.
    /// </summary>
    public ConnectorAnchorId? TargetAnchorId { get; }

    /// <summary>
    /// Gets the optional authoritative placement on the structurally attached owner boundary.
    /// Position and Size remain synchronized derived compatibility bounds when this is present.
    /// </summary>
    public BoundaryAttachmentPlacement? BoundaryAttachment { get; }

    public bool Equals(VisualStateSnapshot? other) =>
        ReferenceEquals(this, other) ||
        (other is not null &&
         Id == other.Id &&
         SemanticElementId == other.SemanticElementId &&
         Position == other.Position &&
         Size == other.Size &&
         PlacementMode == other.PlacementMode &&
         Route.AsSpan().SequenceEqual(other.Route.AsSpan()) &&
         Properties.Equals(other.Properties) &&
         ConnectorAnchors.AsSpan().SequenceEqual(other.ConnectorAnchors.AsSpan()) &&
         SourceAnchorId == other.SourceAnchorId &&
         TargetAnchorId == other.TargetAnchorId &&
         Equals(BoundaryAttachment, other.BoundaryAttachment));

    public override bool Equals(object? obj) => Equals(obj as VisualStateSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(SemanticElementId);
        hash.Add(Position);
        hash.Add(Size);
        hash.Add(PlacementMode);

        foreach (var point in Route)
        {
            hash.Add(point);
        }

        hash.Add(Properties);
        foreach (var connectorAnchor in ConnectorAnchors)
        {
            hash.Add(connectorAnchor);
        }

        hash.Add(SourceAnchorId);
        hash.Add(TargetAnchorId);
        hash.Add(BoundaryAttachment);
        return hash.ToHashCode();
    }

    private static ImmutableArray<ConnectorAnchor> CopyAndOrderConnectorAnchors(
        IEnumerable<ConnectorAnchor>? connectorAnchors)
    {
        if (connectorAnchors is null)
        {
            return [];
        }

        var copy = connectorAnchors.ToArray();
        if (Array.Exists(copy, static anchor => anchor is null))
        {
            throw new ArgumentException(
                "Connector anchors cannot contain null values.",
                nameof(connectorAnchors));
        }

        var duplicateId = copy
            .GroupBy(static anchor => anchor.Id)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Duplicate connector-anchor ID '{duplicateId.Key}'.",
                nameof(connectorAnchors));
        }

        foreach (var sideGroup in copy.GroupBy(static anchor => anchor.Side))
        {
            var orders = sideGroup
                .Select(static anchor => anchor.Order)
                .Order()
                .ToArray();
            for (var index = 0; index < orders.Length; index++)
            {
                if (orders[index] != index)
                {
                    throw new ArgumentException(
                        $"Connector-anchor orders on side '{sideGroup.Key}' must be contiguous from zero.",
                        nameof(connectorAnchors));
                }
            }
        }

        return copy
            .OrderBy(static anchor => anchor.Side)
            .ThenBy(static anchor => anchor.Order)
            .ToImmutableArray();
    }
}
