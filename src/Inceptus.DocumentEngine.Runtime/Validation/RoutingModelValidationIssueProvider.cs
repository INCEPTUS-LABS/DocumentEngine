using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Validation;

namespace Inceptus.DocumentEngine.Runtime.Validation;

/// <summary>
/// Projects current generic no-route outcomes into the transient Issues experience.
/// </summary>
public static class RoutingModelValidationIssueProvider
{
    public const string NoLegalRouteCode = "ROUTING_NO_LEGAL_ROUTE";

    public static ModelValidationRuleId RuleId { get; } =
        new("routing:validation/no-legal-route");

    public static ImmutableArray<ModelValidationIssue> CreateIssues(
        ProjectedGraph graph,
        RoutingResult routingResult)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(routingResult);
        if (graph.DocumentId != routingResult.DocumentId ||
            graph.SourceRevision != routingResult.SourceRevision)
        {
            throw new ArgumentException(
                "Routing validation requires a ProjectedGraph and RoutingResult from the same Document revision.",
                nameof(routingResult));
        }

        var edges = graph.Edges.ToDictionary(static edge => edge.Id);
        var issues = ImmutableArray.CreateBuilder<ModelValidationIssue>(
            routingResult.NoRouteEdgeIds.Length);
        foreach (var edgeId in routingResult.NoRouteEdgeIds)
        {
            if (!edges.TryGetValue(edgeId, out var edge))
            {
                throw new InvalidOperationException(
                    $"No-route edge '{edgeId}' is missing from the current ProjectedGraph.");
            }

            var target = edge.Source.VisualStateId is { } visualStateId
                ? ModelValidationTarget.ForVisualState(
                    edge.Source.SemanticElementId,
                    visualStateId)
                : ModelValidationTarget.ForSemanticElement(edge.Source.SemanticElementId);
            issues.Add(new ModelValidationIssue(
                RuleId,
                ModelValidationSeverity.Warning,
                NoLegalRouteCode,
                "Connector has no legal route; a straight fallback is shown.",
                target));
        }

        return issues.MoveToImmutable();
    }
}
