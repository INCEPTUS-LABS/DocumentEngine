using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private static ImmutableArray<PointD> ResolveEditableConnectorRoute(DocumentSnapshot document, VisualStateSnapshot visual)
    {
        if (document.VisualModel.RoutingScopes is not { } scopes) return visual.Route;
        var record = scopes.SelectMany(static scope => scope.Connectors).FirstOrDefault(candidate => candidate.VisualStateId == visual.Id);
        return record?.RoutingType == ConnectorRoutingType.Manual ? record.Path : [];
    }
}
