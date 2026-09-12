using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Organizational.Commands;

public enum OrganizationalPoolCreationMode
{
    AdoptEligibleUnassigned,
    Empty,
}

public enum OrganizationalPoolMoveDirection
{
    Up,
    Down,
}

public abstract class OrganizationalCommand : ICommand, ICommandPipelineInvalidation
{
    protected OrganizationalCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
    }

    public abstract CommandTypeId TypeId { get; }

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public abstract CommandCategory Category { get; }

    public abstract AuthoritativeDocumentComponent AffectedComponents { get; }

    public PipelineInvalidation PipelineInvalidation => PipelineInvalidation.Scene;
}

public sealed class CreateOrganizationalPoolCommand : OrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:organizational/command/create-pool");

    public CreateOrganizationalPoolCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId poolId,
        DocumentScopeId targetScopeId,
        OrganizationalPoolCreationMode creationMode,
        string name = "Pool",
        string description = "")
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(poolId);
        ArgumentNullException.ThrowIfNull(targetScopeId);
        if (!Enum.IsDefined(creationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(creationMode));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        PoolId = poolId;
        TargetScopeId = targetScopeId;
        CreationMode = creationMode;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public override CommandCategory Category => CommandCategory.Document;

    public override AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    public SemanticElementId PoolId { get; }

    public DocumentScopeId TargetScopeId { get; }

    public OrganizationalPoolCreationMode CreationMode { get; }

    public string Name { get; }

    public string Description { get; }
}

public sealed class AssignOrganizationalElementCommand : OrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:organizational/command/assign-element");

    public AssignOrganizationalElementCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId semanticElementId,
        SemanticElementId poolId,
        bool onlyIfEligible = false)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(poolId);
        SemanticElementId = semanticElementId;
        PoolId = poolId;
        OnlyIfEligible = onlyIfEligible;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public override CommandCategory Category => CommandCategory.Semantic;

    public override AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel;

    public SemanticElementId SemanticElementId { get; }

    public SemanticElementId PoolId { get; }

    /// <summary>
    /// Allows a creation compound to skip a newly created ineligible semantic identity,
    /// such as a Boundary Event or relationship. Explicit assignment leaves this false.
    /// </summary>
    public bool OnlyIfEligible { get; }
}

public sealed class UnassignOrganizationalElementCommand : OrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:organizational/command/unassign-element");

    public UnassignOrganizationalElementCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId semanticElementId)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        SemanticElementId = semanticElementId;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public override CommandCategory Category => CommandCategory.Semantic;

    public override AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel;

    public SemanticElementId SemanticElementId { get; }
}

public sealed class MoveOrganizationalPoolCommand : OrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:organizational/command/move-pool");

    public MoveOrganizationalPoolCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId poolId,
        OrganizationalPoolMoveDirection direction)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(poolId);
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        PoolId = poolId;
        Direction = direction;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public override CommandCategory Category => CommandCategory.Visual;

    public override AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    public SemanticElementId PoolId { get; }

    public OrganizationalPoolMoveDirection Direction { get; }
}

public sealed class DeleteOrganizationalPoolCommand : OrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:organizational/command/delete-pool");

    public DeleteOrganizationalPoolCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId poolId)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(poolId);
        PoolId = poolId;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public override CommandCategory Category => CommandCategory.Document;

    public override AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    public SemanticElementId PoolId { get; }
}
