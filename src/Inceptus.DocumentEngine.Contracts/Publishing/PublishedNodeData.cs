using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Contracts.Publishing;

/// <summary>
/// Notation-resolved semantic data copied into one derived published node.
/// </summary>
public sealed record PublishedNodeData
{
    public PublishedNodeData(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        Description = description;
    }

    public string Description { get; }

    public static PublishedNodeData Empty { get; } = new(string.Empty);
}

/// <summary>
/// Notation/domain mapping from an immutable semantic element into generic Publish data.
/// </summary>
public interface IPublishedNodeDataMapper
{
    PublishedNodeData Map(SemanticElementSnapshot semanticElement);
}
