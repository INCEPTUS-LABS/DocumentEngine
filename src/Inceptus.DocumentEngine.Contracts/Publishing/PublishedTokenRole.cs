using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Publishing;

/// <summary>
/// Small notation-neutral behavior vocabulary understood by the static Publish viewer.
/// It intentionally does not model executable process or simulation semantics.
/// </summary>
public enum PublishedTokenRole
{
    Start,
    ActivityDelay,
    SplitInvariant,
    MergeInvariant,
    ParallelSynchronize,
    PassThrough,
    End,
}

/// <summary>
/// Immutable topology supplied to notation policy while deriving a published token role.
/// </summary>
public sealed record PublishedTokenRoleClassificationRequest
{
    public PublishedTokenRoleClassificationRequest(
        SemanticElementId semanticElementId,
        SemanticTypeId semanticTypeId,
        int incomingConnectorCount,
        int outgoingConnectorCount)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        ArgumentOutOfRangeException.ThrowIfNegative(incomingConnectorCount);
        ArgumentOutOfRangeException.ThrowIfNegative(outgoingConnectorCount);

        SemanticElementId = semanticElementId;
        SemanticTypeId = semanticTypeId;
        IncomingConnectorCount = incomingConnectorCount;
        OutgoingConnectorCount = outgoingConnectorCount;
    }

    public SemanticElementId SemanticElementId { get; }

    public SemanticTypeId SemanticTypeId { get; }

    public int IncomingConnectorCount { get; }

    public int OutgoingConnectorCount { get; }
}

/// <summary>
/// Result of notation-owned mapping into the static viewer behavior vocabulary.
/// </summary>
public sealed record PublishedTokenRoleClassification
{
    private PublishedTokenRoleClassification(
        PublishedTokenRole? role,
        string? diagnosticCode,
        string? diagnosticMessage)
    {
        Role = role;
        DiagnosticCode = diagnosticCode;
        DiagnosticMessage = diagnosticMessage;
    }

    public bool Succeeded => Role is not null;

    public PublishedTokenRole? Role { get; }

    public string? DiagnosticCode { get; }

    public string? DiagnosticMessage { get; }

    public static PublishedTokenRoleClassification Success(PublishedTokenRole role) =>
        new(role, null, null);

    public static PublishedTokenRoleClassification Failure(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(null, code, message);
    }
}

/// <summary>
/// Notation/domain policy for mapping source semantics to lightweight published roles.
/// </summary>
public interface IPublishedTokenRoleClassifier
{
    PublishedTokenRoleClassification Classify(
        PublishedTokenRoleClassificationRequest request);
}
