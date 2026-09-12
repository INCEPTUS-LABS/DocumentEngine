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
        NodeGeometryPipelineImpact? nodeGeometryImpact)
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
    }

    public bool Succeeded => ProposedDocument is not null;

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

    public bool Equals(CommandHandlerResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Equals(ProposedDocument, other.ProposedDocument) &&
        PipelineInvalidation == other.PipelineInvalidation &&
        NodeGeometryImpact == other.NodeGeometryImpact &&
        DiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as CommandHandlerResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProposedDocument);
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
