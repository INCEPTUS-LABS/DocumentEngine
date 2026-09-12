using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public sealed record ModelProfileAvailabilityChange
{
    public ModelProfileAvailabilityChange(ModelProfileId profileId, bool isAvailable)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ProfileId = profileId;
        IsAvailable = isAvailable;
    }

    public ModelProfileId ProfileId { get; }

    public bool IsAvailable { get; }
}

/// <summary>
/// Atomically replaces availability for one or more optional model profiles.
/// </summary>
public sealed class SetModelProfileAvailabilityCommand :
    ICommand,
    ICommandPipelineInvalidation,
    IEquatable<SetModelProfileAvailabilityCommand>
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/set-model-profile-availability");

    public SetModelProfileAvailabilityCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        IEnumerable<ModelProfileAvailabilityChange> changes)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(changes);
        var copy = changes.ToArray();
        if (copy.Length == 0 || Array.Exists(copy, static change => change is null))
        {
            throw new ArgumentException(
                "At least one non-null model profile availability change is required.",
                nameof(changes));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.ProfileId.Value, right.ProfileId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].ProfileId == copy[index].ProfileId)
            {
                throw new ArgumentException(
                    $"Duplicate model profile identity '{copy[index].ProfileId}'.",
                    nameof(changes));
            }
        }

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        Changes = [.. copy];
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Semantic;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel;

    public ImmutableArray<ModelProfileAvailabilityChange> Changes { get; }

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PipelineInvalidation.Scene;

    public bool Equals(SetModelProfileAvailabilityCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        Changes.AsSpan().SequenceEqual(other.Changes.AsSpan());

    public override bool Equals(object? obj) =>
        Equals(obj as SetModelProfileAvailabilityCommand);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TargetDocumentId);
        hash.Add(ExpectedRevision);
        foreach (var change in Changes)
        {
            hash.Add(change);
        }

        return hash.ToHashCode();
    }
}
