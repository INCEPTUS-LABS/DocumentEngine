using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Publishing;

/// <summary>Notation-neutral attached activation rules for the lightweight viewer.</summary>
public enum PublishedTriggerKind
{
    IdleTimer,
    IdleSignal,
    Completion,
}

public sealed record PublishedCommunicationRoute(string ConnectorId, bool CanDeliver);

public sealed record PublishedAttachedTrigger(
    string NodeId, string OwnerId, PublishedTriggerKind Kind, bool Interrupting, double? DelayMs);

/// <summary>
/// Optional immutable execution instructions. Connector identities reference frozen presentation
/// routes; they never enter ordinary token adjacency. Absence preserves legacy animation behavior.
/// </summary>
public sealed record PublishedExecutionMetadata(
    ImmutableArray<string> SenderIds,
    ImmutableArray<string> CatchIds,
    ImmutableArray<string> ReceiverIds,
    ImmutableArray<PublishedCommunicationRoute> CommunicationRoutes,
    ImmutableArray<PublishedAttachedTrigger> AttachedTriggers,
    ImmutableArray<string> Warnings);

public sealed record PublishedExecutionDescription(
    PublishedExecutionMetadata? Metadata, ImmutableArray<Diagnostic> Diagnostics);

/// <summary>Optional notation-owned mapping; existing role classifiers remain compatible.</summary>
public interface IPublishedExecutionPolicy
{
    PublishedExecutionDescription DescribeExecution(
        DocumentSnapshot document,
        ImmutableHashSet<SemanticElementId> elementIds,
        ImmutableHashSet<SemanticElementId> relationshipIds);
}
