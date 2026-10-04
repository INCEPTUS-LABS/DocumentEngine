using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public sealed class CommandHandlerResult : IEquatable<CommandHandlerResult>
{
    private CommandHandlerResult(
        DocumentSnapshot? proposedDocument,
        IEnumerable<Diagnostic>? diagnostics,
        PipelineInvalidation? pipelineInvalidation,
        NodeGeometryPipelineImpact? nodeGeometryImpact,
        IEnumerable<ConnectorRoutingIntent>? routingIntents = null,
        IEnumerable<SpatialRegionHeightIntent>? spatialHeightIntents = null,
        bool isNoChange = false,
        IEnumerable<SpatialScopeWidthIntent>? spatialWidthIntents = null)
    {
        if (pipelineInvalidation is { } declaredInvalidation)
        {
            CommandPipelineInvalidation.Validate(
                declaredInvalidation,
                nameof(pipelineInvalidation));
        }

        if (nodeGeometryImpact is not null)
        {
            if (pipelineInvalidation is null)
            {
                throw new ArgumentException(
                    "An explicit node geometry impact requires declared pipeline invalidation.",
                    nameof(nodeGeometryImpact));
            }

            CommandPipelineInvalidation.ResolveNodeGeometryImpact(
                pipelineInvalidation.Value,
                nodeGeometryImpact,
                nameof(nodeGeometryImpact));
        }

        ProposedDocument = proposedDocument;
        Diagnostics = DiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        PipelineInvalidation = pipelineInvalidation;
        NodeGeometryImpact = nodeGeometryImpact;
        RoutingIntents = routingIntents?.ToImmutableArray() ?? [];
        SpatialHeightIntents = spatialHeightIntents?.ToImmutableArray() ?? [];
        SpatialWidthIntents = spatialWidthIntents?.ToImmutableArray() ?? [];
        if (RoutingIntents.Any(static intent => intent is null) ||
            SpatialHeightIntents.Any(static intent => intent is null) ||
            SpatialWidthIntents.Any(static intent => intent is null))
        {
            throw new ArgumentException("Preparation intents cannot contain null values.", nameof(routingIntents));
        }
        IsNoChange = isNoChange;
    }

    public bool Succeeded => ProposedDocument is not null || IsNoChange;

    public bool IsNoChange { get; }

    public ImmutableArray<ConnectorRoutingIntent> RoutingIntents { get; }

    public ImmutableArray<SpatialRegionHeightIntent> SpatialHeightIntents { get; }
    public ImmutableArray<SpatialScopeWidthIntent> SpatialWidthIntents { get; }

    public DocumentSnapshot? ProposedDocument { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public PipelineInvalidation? PipelineInvalidation { get; }

    public NodeGeometryPipelineImpact? NodeGeometryImpact { get; }

    public static CommandHandlerResult Success(
        DocumentSnapshot proposedDocument,
        IEnumerable<Diagnostic>? diagnostics = null,
        PipelineInvalidation? pipelineInvalidation = null,
        NodeGeometryPipelineImpact? nodeGeometryImpact = null)
    {
        ArgumentNullException.ThrowIfNull(proposedDocument);
        return new CommandHandlerResult(
            proposedDocument,
            diagnostics,
            pipelineInvalidation,
            nodeGeometryImpact);
    }

    public static CommandHandlerResult Failure(IEnumerable<Diagnostic>? diagnostics = null) =>
        new(
            null,
            diagnostics,
            pipelineInvalidation: null,
            nodeGeometryImpact: null);

    public static CommandHandlerResult SuccessWithPreparation(
        DocumentSnapshot proposedDocument,
        IEnumerable<ConnectorRoutingIntent> routingIntents,
        IEnumerable<SpatialRegionHeightIntent> spatialHeightIntents,
        IEnumerable<Diagnostic>? diagnostics = null,
        PipelineInvalidation? pipelineInvalidation = null,
        NodeGeometryPipelineImpact? nodeGeometryImpact = null)
    {
        ArgumentNullException.ThrowIfNull(proposedDocument);
        ArgumentNullException.ThrowIfNull(routingIntents);
        ArgumentNullException.ThrowIfNull(spatialHeightIntents);
        return new(proposedDocument, diagnostics, pipelineInvalidation, nodeGeometryImpact,
            routingIntents, spatialHeightIntents);
    }

    public static CommandHandlerResult NoChange(IEnumerable<Diagnostic>? diagnostics = null) =>
        new(null, diagnostics, null, null, isNoChange: true);

    public static CommandHandlerResult SuccessWithPreparation(
        DocumentSnapshot proposedDocument,
        IEnumerable<ConnectorRoutingIntent> routingIntents,
        IEnumerable<SpatialRegionHeightIntent> spatialHeightIntents,
        IEnumerable<SpatialScopeWidthIntent> spatialWidthIntents,
        IEnumerable<Diagnostic>? diagnostics = null,
        PipelineInvalidation? pipelineInvalidation = null,
        NodeGeometryPipelineImpact? nodeGeometryImpact = null)
    {
        ArgumentNullException.ThrowIfNull(proposedDocument);
        ArgumentNullException.ThrowIfNull(routingIntents);
        ArgumentNullException.ThrowIfNull(spatialHeightIntents);
        ArgumentNullException.ThrowIfNull(spatialWidthIntents);
        return new(proposedDocument, diagnostics, pipelineInvalidation, nodeGeometryImpact,
            routingIntents, spatialHeightIntents, spatialWidthIntents: spatialWidthIntents);
    }

    public bool Equals(CommandHandlerResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Equals(ProposedDocument, other.ProposedDocument) &&
        IsNoChange == other.IsNoChange &&
        RoutingIntents.AsSpan().SequenceEqual(other.RoutingIntents.AsSpan()) &&
        SpatialHeightIntents.AsSpan().SequenceEqual(other.SpatialHeightIntents.AsSpan()) &&
        SpatialWidthIntents.AsSpan().SequenceEqual(other.SpatialWidthIntents.AsSpan()) &&
        PipelineInvalidation == other.PipelineInvalidation &&
        NodeGeometryImpact == other.NodeGeometryImpact &&
        DiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as CommandHandlerResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProposedDocument);
        hash.Add(IsNoChange);
        foreach (var intent in RoutingIntents) { hash.Add(intent); }
        foreach (var intent in SpatialHeightIntents) { hash.Add(intent); }
        foreach (var intent in SpatialWidthIntents) { hash.Add(intent); }
        hash.Add(PipelineInvalidation);
        hash.Add(NodeGeometryImpact);
        DiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }

    public static bool operator ==(CommandHandlerResult? left, CommandHandlerResult? right) =>
        EqualityComparer<CommandHandlerResult>.Default.Equals(left, right);

    public static bool operator !=(CommandHandlerResult? left, CommandHandlerResult? right) =>
        !(left == right);
}
