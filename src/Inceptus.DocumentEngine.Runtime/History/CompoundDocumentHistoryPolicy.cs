using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class CompoundDocumentHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(CompoundDocumentCommand.KnownTypeId, new CompoundDocumentHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        var beforeScopes = before.VisualModel.RoutingScopes ?? [];
        var afterScopes = committed.VisualModel.RoutingScopes ?? [];
        var afterConnectors = afterScopes.SelectMany(static scope => scope.Connectors)
            .ToDictionary(static record => record.VisualStateId);
        var routingDeltas = beforeScopes.SelectMany(static scope => scope.Connectors)
            .Where(record => afterConnectors.TryGetValue(record.VisualStateId, out var after) &&
                after.RoutingType != record.RoutingType)
            .Select(record => new ConnectorRoutingTypeHistoryDelta(record.VisualStateId, record.RoutingType,
                afterConnectors[record.VisualStateId].RoutingType)).ToImmutableArray();
        var heightDeltas = ImmutableArray.CreateBuilder<SpatialRegionHeightHistoryDelta>();
        var widthDeltas = ImmutableArray.CreateBuilder<SpatialScopeWidthHistoryDelta>();
        foreach (var scope in beforeScopes)
        {
            var afterScope = afterScopes.FirstOrDefault(candidate => candidate.ScopeId == scope.ScopeId);
            if (afterScope is null) continue;
            foreach (var width in scope.Geometry.SpatialWidths)
            {
                var after = afterScope.Geometry.SpatialWidths.FirstOrDefault(candidate => candidate.ProfileId == width.ProfileId);
                if (after is not null && after.OuterWidth != width.OuterWidth)
                    widthDeltas.Add(new SpatialScopeWidthHistoryDelta(scope.ScopeId, width.ProfileId,
                        width.OuterWidth, after.OuterWidth));
            }
            foreach (var region in scope.Geometry.Regions)
            {
                var after = afterScope.Geometry.Regions.FirstOrDefault(candidate => candidate.Id == region.Id);
                if (after is not null && after.ExpandedHeight != region.ExpandedHeight)
                    heightDeltas.Add(new SpatialRegionHeightHistoryDelta(scope.ScopeId, region.Id,
                        region.ExpandedHeight, after.ExpandedHeight));
            }
        }
        if (routingDeltas.IsEmpty && heightDeltas.Count == 0 && widthDeltas.Count == 0 &&
            ManualRouteHistoryDelta.Capture(command, before, committed).IsEmpty &&
            CommandProcessor.ContentEqualsIgnoringRevision(CommandProcessor.WithoutRoutingScopes(before),
                CommandProcessor.WithoutRoutingScopes(committed)))
            return CommandHistoryPreparationResult.PreserveExistingHistory();
        return CommandHistoryPreparationResult.Undoable(new Factory(before), new Factory(committed),
            routingDeltas, heightDeltas, widthDeltas);
    }

    private sealed class Factory(DocumentSnapshot snapshot) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new RestoreCompoundDocumentCommand(documentId, expectedRevision, snapshot);
    }
}

internal sealed class RestoreCompoundDocumentCommand(
    DocumentId targetDocumentId, DocumentRevision expectedRevision, DocumentSnapshot snapshot) : ICommand
{
    internal static CommandTypeId KnownTypeId { get; } = new("inceptus:command/restore-compound-document");
    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; } = targetDocumentId;
    public DocumentRevision ExpectedRevision { get; } = expectedRevision;
    public CommandCategory Category => CommandCategory.Compound;
    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel | AuthoritativeDocumentComponent.VisualModel |
        AuthoritativeDocumentComponent.Metadata | AuthoritativeDocumentComponent.Publication;
    internal DocumentSnapshot Snapshot { get; } = snapshot;
}

internal sealed class RestoreCompoundDocumentCommandHandler : ICommandHandler, ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command) =>
        command is RestoreCompoundDocumentCommand restore && restore.Snapshot.DocumentId == command.TargetDocumentId
            ? [] : [new Diagnostic("INCEPTUS.COMPOUND.RESTORE.INVALID", DiagnosticSeverity.Error,
                "Compound restoration requires the exact same Document identity.", command.TypeId.Value)];

    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var restore = (RestoreCompoundDocumentCommand)command;
        var snapshot = DocumentSnapshotCloner.CloneAtRevision(restore.Snapshot, document.Revision);
        var beforeNodes = document.VisualModel.VisualStates.Where(visual =>
            document.SemanticModel.TryGetElement(visual.SemanticElementId, out _)).ToDictionary(visual => visual.Id);
        var afterNodes = snapshot.VisualModel.VisualStates.Where(visual =>
            snapshot.SemanticModel.TryGetElement(visual.SemanticElementId, out _)).ToDictionary(visual => visual.Id);
        var removed = beforeNodes.Keys.Except(afterNodes.Keys).ToArray();
        var changed = afterNodes.Where(pair => !beforeNodes.TryGetValue(pair.Key, out var previous) ||
                !pair.Value.Equals(previous)).Select(pair => pair.Key).ToArray();
        NodeGeometryPipelineImpact? impact = removed.Length > 0 && changed.Length > 0 ? null
            : removed.Length > 0 ? NodeGeometryPipelineImpact.ForRemovedVisualStates(removed)
            : changed.Length > 0 ? NodeGeometryPipelineImpact.ForHistoricalRestoration(changed, restore.Snapshot.Revision)
            : NodeGeometryPipelineImpact.PreserveAll;
        return ValueTask.FromResult(CommandHandlerResult.Success(snapshot,
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout, nodeGeometryImpact: impact));
    }
}
