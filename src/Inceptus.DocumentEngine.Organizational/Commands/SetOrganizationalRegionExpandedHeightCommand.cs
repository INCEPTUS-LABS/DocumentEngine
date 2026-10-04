using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Organizational.Commands;

/// <summary>Sets the saved expanded height of one existing Pool or Unassigned region.</summary>
public sealed record SetOrganizationalRegionExpandedHeightCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("inceptus:organizational/command/set-region-expanded-height");

    public SetOrganizationalRegionExpandedHeightCommand(DocumentId targetDocumentId,
        DocumentRevision expectedRevision, DocumentScopeId scopeId,
        Canvas2DSpatialRegionId regionId, double expandedHeight)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(regionId);
        if (!double.IsFinite(expandedHeight) || expandedHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(expandedHeight));
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ScopeId = scopeId;
        RegionId = regionId;
        ExpandedHeight = expandedHeight;
    }

    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public DocumentScopeId ScopeId { get; }
    public Canvas2DSpatialRegionId RegionId { get; }
    public double ExpandedHeight { get; }
    public CommandCategory Category => CommandCategory.Visual;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.VisualModel;
    public PipelineInvalidation PipelineInvalidation => CommandPipelineInvalidation.WithoutNodeLayout;
}
