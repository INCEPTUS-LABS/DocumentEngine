using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes one complete persistent connection-route replacement in document coordinates.
/// An empty target clears persistent route guidance so routing may resolve the connector.
/// </summary>
public sealed class UpdateConnectionRouteCommand :
    IEquatable<UpdateConnectionRouteCommand>,
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/update-connection-route");

    public UpdateConnectionRouteCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        IEnumerable<PointD> targetRoute)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(targetRoute);

        var route = targetRoute.ToImmutableArray();
        if (route.Length == 1)
        {
            throw new ArgumentException(
                "A persistent connection route must be empty or contain at least two points.",
                nameof(targetRoute));
        }

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId;
        TargetRoute = route;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Visual;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        CommandPipelineInvalidation.ConnectorOnly;

    public VisualStateId TargetVisualStateId { get; }

    public ImmutableArray<PointD> TargetRoute { get; }

    public bool Equals(UpdateConnectionRouteCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetVisualStateId == other.TargetVisualStateId &&
        TargetRoute.AsSpan().SequenceEqual(other.TargetRoute.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as UpdateConnectionRouteCommand);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TargetDocumentId);
        hash.Add(ExpectedRevision);
        hash.Add(TargetVisualStateId);
        foreach (var point in TargetRoute)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(
        UpdateConnectionRouteCommand? left,
        UpdateConnectionRouteCommand? right) =>
        EqualityComparer<UpdateConnectionRouteCommand>.Default.Equals(left, right);

    public static bool operator !=(
        UpdateConnectionRouteCommand? left,
        UpdateConnectionRouteCommand? right) =>
        !(left == right);
}
