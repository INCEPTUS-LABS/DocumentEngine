using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.Organizational.History;

/// <summary>Reorder owns changed order entries and vertical offsets, never semantic state or whole visual snapshots.</summary>
internal sealed class OrganizationalPoolOrderHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        if (command is not (MoveOrganizationalPoolCommand or MoveOrganizationalLaneCommand))
            return CommandHistoryPreparationResult.Failure([new Diagnostic(OrganizationalCommandDiagnosticCodes.HistoryInvalid,
                DiagnosticSeverity.Error, "Pool ordering History requires a Pool or Lane move command.")]);
        var oldOrder = Entries(before);
        var newOrder = Entries(committed);
        var changed = oldOrder.Keys.Union(newOrder.Keys).Where(id => oldOrder.GetValueOrDefault(id) != newOrder.GetValueOrDefault(id)).ToArray();
        var oldRegions = Regions(before);
        var newRegions = Regions(committed);
        var geometry = oldRegions.Keys.Intersect(newRegions.Keys).Where(id =>
            oldRegions[id].ContentBounds is { } oldBounds && newRegions[id].ContentBounds is { } newBounds &&
            (oldBounds.Y != newBounds.Y || oldRegions[id].LocalToScopeTransform!.Value.OffsetY != newRegions[id].LocalToScopeTransform!.Value.OffsetY)).ToArray();
        return CommandHistoryPreparationResult.Undoable(
            new Factory(changed.ToImmutableDictionary(id => id, id => oldOrder.GetValueOrDefault(id)),
                [.. geometry.Select(id => Position(oldRegions[id]))]),
            new Factory(changed.ToImmutableDictionary(id => id, id => newOrder.GetValueOrDefault(id)),
                [.. geometry.Select(id => Position(newRegions[id]))]));
    }

    private static Dictionary<SemanticElementId, ModelProfileElementPresentationSnapshot> Entries(DocumentSnapshot document) =>
        document.VisualModel.ProfileElementPresentations.Where(entry => entry.ProfileId == OrganizationalModelProfile.Id)
            .ToDictionary(entry => entry.SemanticElementId);
    private static Dictionary<Canvas2DSpatialRegionId, SpatialRegionGeometrySnapshot> Regions(DocumentSnapshot document) =>
        (document.VisualModel.RoutingScopes ?? []).SelectMany(scope => scope.Geometry.Regions)
            .Where(region => region.ProfileId == OrganizationalModelProfile.Id).ToDictionary(region => region.Id);
    private static OrganizationalRegionOrderPosition Position(SpatialRegionGeometrySnapshot region) =>
        new(region.Id, region.ContentBounds!.Value.Y, region.LocalToScopeTransform!.Value.OffsetY);
    private sealed class Factory(ImmutableDictionary<SemanticElementId, ModelProfileElementPresentationSnapshot?> order,
        ImmutableArray<OrganizationalRegionOrderPosition> positions) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new RestoreOrganizationalPoolOrderCommand(documentId, expectedRevision, order, positions);
    }
}

internal sealed record OrganizationalRegionOrderPosition(Canvas2DSpatialRegionId Id, double Top, double TranslationY);

internal sealed class RestoreOrganizationalPoolOrderCommand(DocumentId documentId, DocumentRevision revision,
    ImmutableDictionary<SemanticElementId, ModelProfileElementPresentationSnapshot?> order,
    ImmutableArray<OrganizationalRegionOrderPosition> positions) : ICommand, ICommandPipelineInvalidation
{
    internal static CommandTypeId KnownTypeId { get; } = new("inceptus:organizational/command/restore-pool-order");
    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId => documentId;
    public DocumentRevision ExpectedRevision => revision;
    public CommandCategory Category => CommandCategory.Visual;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.VisualModel;
    public PipelineInvalidation PipelineInvalidation => CommandPipelineInvalidation.WithoutNodeLayout;
    internal ImmutableDictionary<SemanticElementId, ModelProfileElementPresentationSnapshot?> Order => order;
    internal ImmutableArray<OrganizationalRegionOrderPosition> Positions => positions;
}

internal sealed class RestoreOrganizationalPoolOrderCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not RestoreOrganizationalPoolOrderCommand restore)
            return ValueTask.FromResult(CommandHandlerResult.Failure([new Diagnostic(OrganizationalCommandDiagnosticCodes.HistoryInvalid,
                DiagnosticSeverity.Error, "Invalid Pool ordering restore command.")]));
        var regions = (document.VisualModel.RoutingScopes ?? []).SelectMany(scope => scope.Geometry.Regions).ToDictionary(region => region.Id);
        if (restore.Order.Keys.Any(id => !document.SemanticModel.TryGetElement(id, out _)) ||
            restore.Positions.Any(position => !regions.TryGetValue(position.Id, out var region) || !region.IsActive))
            return ValueTask.FromResult(CommandHandlerResult.Failure([new Diagnostic(OrganizationalCommandDiagnosticCodes.HistoryInvalid,
                DiagnosticSeverity.Error, "Pool ordering History requires its surviving Pool identities and geometry.")]));
        var replacements = restore.Positions.ToDictionary(position => position.Id, position =>
        {
            var region = regions[position.Id];
            var bounds = region.ContentBounds!.Value;
            return new SpatialRegionGeometrySnapshot(region.Id, region.ProfileId, region.ContainerSemanticElementId, region.ExpandedHeight,
                Matrix2D.CreateTranslation(region.LocalToScopeTransform!.Value.OffsetX, position.TranslationY),
                new RectD(bounds.X, position.Top, bounds.Width, bounds.Height));
        });
        var order = document.VisualModel.ProfileElementPresentations.Where(entry => entry.ProfileId != OrganizationalModelProfile.Id ||
                !restore.Order.ContainsKey(entry.SemanticElementId))
            .Concat(restore.Order.Values.OfType<ModelProfileElementPresentationSnapshot>());
        return ValueTask.FromResult(CommandHandlerResult.Success(OrganizationalPoolOrdering.Replace(document, order, replacements),
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout, nodeGeometryImpact: NodeGeometryPipelineImpact.PreserveAll));
    }
}
