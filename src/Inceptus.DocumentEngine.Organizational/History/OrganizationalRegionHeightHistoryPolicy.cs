using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Commands;

namespace Inceptus.DocumentEngine.Organizational.History;

internal sealed class OrganizationalRegionHeightHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        if (command is not SetOrganizationalRegionExpandedHeightCommand height ||
            SetOrganizationalRegionExpandedHeightCommandHandler.Find(before, height) is not { } previous ||
            SetOrganizationalRegionExpandedHeightCommandHandler.Find(committed, height) is not { } current)
            return CommandHistoryPreparationResult.Failure([new Diagnostic("ORGANIZATIONAL_REGION_HEIGHT_HISTORY_INVALID",
                DiagnosticSeverity.Error, "Region height History requires the same existing region before and after commit.")]);
        return previous.ExpandedHeight == current.ExpandedHeight
            ? CommandHistoryPreparationResult.PreserveExistingHistory()
            : CommandHistoryPreparationResult.Undoable(
                new Factory(height.ScopeId, height.RegionId, previous.ExpandedHeight),
                new Factory(height.ScopeId, height.RegionId, current.ExpandedHeight), [],
                [new SpatialRegionHeightHistoryDelta(height.ScopeId, height.RegionId,
                    previous.ExpandedHeight, current.ExpandedHeight)]);
    }

    private sealed class Factory(DocumentScopeId scopeId, Canvas2DSpatialRegionId regionId, double height) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new SetOrganizationalRegionExpandedHeightCommand(documentId, expectedRevision, scopeId, regionId, height);
    }
}
