using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Runtime.History;

/// <summary>Only explicit route children of a compound edit own surviving manual definitions.</summary>
internal sealed record ManualRouteHistoryDelta(VisualStateId VisualStateId,
    ConnectorRoutingType BeforeType, ConnectorRoutingType AfterType,
    ImmutableArray<PointD> Before, ImmutableArray<PointD> After)
{
    internal static ImmutableArray<ManualRouteHistoryDelta> Capture(
        ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        if (command is not CompoundDocumentCommand compound || before.VisualModel.RoutingScopes is null) return [];
        var result = ImmutableArray.CreateBuilder<ManualRouteHistoryDelta>();
        foreach (var id in compound.Commands.OfType<UpdateConnectionRouteCommand>()
            .Select(static child => child.TargetVisualStateId).Distinct())
        {
            var oldRoute = SetConnectorRoutingTypeHistoryPolicy.Find(before, id);
            var newRoute = SetConnectorRoutingTypeHistoryPolicy.Find(committed, id);
            if (oldRoute is not null && newRoute is not null &&
                !oldRoute.ManualDefinition.GetValueOrDefault().AsSpan()
                    .SequenceEqual(newRoute.ManualDefinition.GetValueOrDefault().AsSpan()))
                result.Add(new(id, oldRoute.RoutingType, newRoute.RoutingType,
                    oldRoute.ManualDefinition ?? [], newRoute.ManualDefinition ?? []));
        }
        return result.ToImmutable();
    }

    internal IEnumerable<ConnectorRoutingIntent> Replay(bool isUndo)
    {
        var targetType = isUndo ? BeforeType : AfterType;
        // The final mode remains owned by the existing mode delta. A dormant definition
        // is restored through the same ordered intents as an ordinary compound edit.
        if (BeforeType != AfterType || targetType != ConnectorRoutingType.Manual)
            yield return ConnectorRoutingIntent.SetType(VisualStateId, ConnectorRoutingType.Manual);
        yield return ConnectorRoutingIntent.ReplaceManualDefinition(VisualStateId, isUndo ? Before : After);
        if (BeforeType != AfterType || targetType != ConnectorRoutingType.Manual)
            yield return ConnectorRoutingIntent.SetType(VisualStateId, targetType);
    }
}
