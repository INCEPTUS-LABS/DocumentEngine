using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Runtime.Routing;

public sealed partial class RoutingEngine
{
    private static void ValidatePreparedInput(
        ProjectedGraph graph,
        LayoutResult layout,
        PreparedRoutingInput? prepared,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (prepared is null)
        {
            return;
        }

        if (!graph.Equals(prepared.SourceGraph) || !layout.Equals(prepared.SourceLayout))
        {
            diagnostics.Add(Error(RoutingDiagnosticCodes.InvalidInput,
                "Prepared routing input does not match the current immutable graph and layout.",
                graph.DocumentId.Value));
            return;
        }

        var nodeIds = graph.Nodes.Select(static node => node.Id).ToHashSet();
        var edgeIds = graph.Edges.Select(static edge => edge.Id).ToHashSet();
        if (!edgeIds.SetEquals(prepared.EdgeDomains.Keys))
        {
            diagnostics.Add(Error(RoutingDiagnosticCodes.InvalidInput,
                "Prepared routing input must cover exactly every projected edge.",
                graph.DocumentId.Value));
            return;
        }

        // Validate each immutable set once, even when many edges share it.
        foreach (var domain in prepared.EdgeDomains.Values.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (domain.NodeIds.Any(id => !nodeIds.Contains(id)))
            {
                diagnostics.Add(Error(RoutingDiagnosticCodes.InvalidInput,
                    "Prepared routing input references an unknown obstacle node.",
                    graph.DocumentId.Value));
            }
        }

        var ports = graph.Ports.ToDictionary(static port => port.Id);
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var domain = prepared.EdgeDomains[edge.Id];
            if (!domain.Contains(edge.SourceNodeId) ||
                !domain.Contains(edge.TargetNodeId) ||
                (edge.SourcePortId is not null && ports.TryGetValue(edge.SourcePortId, out var source) &&
                 !domain.Contains(source.OwnerNodeId)) ||
                (edge.TargetPortId is not null && ports.TryGetValue(edge.TargetPortId, out var target) &&
                 !domain.Contains(target.OwnerNodeId)))
            {
                diagnostics.Add(Error(RoutingDiagnosticCodes.InvalidInput,
                    "Prepared routing input excludes a required endpoint owner.", edge.Id.Value));
            }
        }
    }
}
