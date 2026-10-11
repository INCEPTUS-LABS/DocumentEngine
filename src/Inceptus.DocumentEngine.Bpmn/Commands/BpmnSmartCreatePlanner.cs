using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.ConnectionCreation;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Placement;
using Inceptus.DocumentEngine.Bpmn.Toolbox;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>Composes registered creation and membership handlers without installing any intermediate document.</summary>
public sealed class BpmnSmartCreatePlanner(IEnumerable<CommandHandlerRegistration> handlers,
    IEnumerable<CommandValidatorRegistration> validators)
{
    private readonly ImmutableArray<CommandHandlerRegistration> _handlers = [.. handlers];
    private readonly ImmutableArray<CommandValidatorRegistration> _validators = [.. validators];

    public async ValueTask<BpmnSmartCreateCommand> PlanAsync(DocumentSnapshot document, VisualStateId sourceVisualId,
        ToolboxItemId itemId, IDocumentCreationIdentityProvider identityProvider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(identityProvider);
        if (!document.VisualModel.TryGetVisualState(sourceVisualId, out var source) || source is null ||
            !document.SemanticModel.TryGetScope(source.SemanticElementId, out var scope) || scope is null)
            throw new InvalidOperationException("The selected source is unavailable.");
        var item = BpmnSmartNextCatalog.GetCandidates(document, source.SemanticElementId, scope.Id)
            .SingleOrDefault(candidate => candidate.ItemId == itemId)
            ?? throw new InvalidOperationException("This type cannot follow the selected source.");
        var saved = document.VisualModel.RoutingScopes?.SingleOrDefault(candidate => candidate.ScopeId == scope.Id)
            ?? throw new InvalidOperationException("Smart Create requires current presentation geometry.");
        var body = saved.Geometry.Nodes.Single(node => node.VisualStateId == sourceVisualId);
        var defaults = BpmnToolboxCreationDefaults.Resolve(document, item.ElementTypeId, out var failure);
        if (failure is not null) throw new InvalidOperationException(failure.Message);
        var bounds = FindPosition(saved.Geometry, body, defaults.Size);
        var registration = BpmnToolboxPlacementContribution.N91Registrations.Single(candidate => candidate.ToolboxItemId == itemId);
        var creation = registration.CommandFactory.CreatePlan(new(itemId, document, document.Revision,
            new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), identityProvider, scope.Id));
        if (!creation.Succeeded || creation.Plan is not { } nodePlan)
            throw new InvalidOperationException(string.Join(" ", creation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var proposed = await ApplyAsync(nodePlan.Command, document, cancellationToken).ConfigureAwait(false);
        var pool = OrganizationalSemantics.GetOwningPoolId(document.SemanticModel, source.SemanticElementId);
        if (pool is not null)
            proposed = await ApplyAsync(new AssignOrganizationalElementCommand(document.DocumentId, document.Revision,
                nodePlan.CreatedSemanticElementId, pool), proposed, cancellationToken).ConfigureAwait(false);
        var addedGeometry = new ScopeNodeGeometrySnapshot(nodePlan.CreatedVisualStateId, bounds, Matrix2D.Identity, body.RegionId);
        var expansion = OrganizationalArrangeGeometry.Expand(proposed, scope.Id,
            new Dictionary<VisualStateId, RectD> { [nodePlan.CreatedVisualStateId] = bounds }, [addedGeometry]);
        proposed = expansion.Document;
        var candidates = await BpmnElementConnectionCreation.Candidates(proposed, source.SemanticElementId, sourceVisualId,
            nodePlan.CreatedSemanticElementId, nodePlan.CreatedVisualStateId, identityProvider, false, cancellationToken).ConfigureAwait(false);
        var selected = BpmnSmartEndpointPlanner.Choose(proposed, saved.Geometry, addedGeometry, candidates, cancellationToken)
            ?? throw new InvalidOperationException("No legal routed endpoint pair is available.");
        var connected = await ApplyAsync(selected.Plan.Command, proposed, cancellationToken).ConfigureAwait(false);
        var ordered = BpmnAutoArrangeConnectorPlanner.ArrangeAttachments(connected, [scope.Id], [], cancellationToken, sourceVisualId, addedGeometry);
        connected = BpmnAutoArrangePlanner.ReplaceVisuals(connected, ordered);
        return new(document, connected, nodePlan.CreatedVisualStateId,
            selected.Plan.CreatedConnectorVisualStateId, expansion.Heights, expansion.Widths);
    }

    private async ValueTask<DocumentSnapshot> ApplyAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        var handler = _handlers.Single(registration => registration.TypeId == command.TypeId);
        var diagnostics = handler.EnvelopeValidator.Validate(command).AddRange(_validators
            .Where(registration => registration.TypeId == command.TypeId)
            .SelectMany(registration => registration.Validator.Validate(command, document)));
        if (!diagnostics.IsEmpty) throw new InvalidOperationException(string.Join(" ", diagnostics.Select(diagnostic => diagnostic.Message)));
        var result = await handler.Handler.HandleAsync(command, document, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.ProposedDocument is null)
            throw new InvalidOperationException(string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return result.ProposedDocument;
    }

    private static RectD FindPosition(ScopeGeometrySnapshot geometry, ScopeNodeGeometrySnapshot source, SizeD size)
    {
        var obstacles = geometry.Nodes.Where(node => node.RegionId == source.RegionId).Select(node => node.LocalBounds)
            .Concat(geometry.Captions.Where(caption => geometry.Nodes.Any(node =>
                node.VisualStateId == caption.OwnerVisualStateId && node.RegionId == source.RegionId)).Select(caption => caption.ContentBounds)).ToArray();
        var preferred = new PointD(source.LocalBounds.Right + BpmnAutoArrangeOptions.HorizontalGap,
            source.LocalBounds.Y + (source.LocalBounds.Height - size.Height) / 2);
        var region = geometry.Regions.SingleOrDefault(region => region.Id == source.RegionId);
        // Prefer an existing free track; expand the current region only if none fits.
        for (var expansion = 0; expansion < 2; expansion++)
            for (var index = 0; index < 65; index++)
            {
                var track = index == 0 ? 0 : (index + 1) / 2 * (index % 2 == 1 ? -1 : 1);
                var bounds = new RectD(preferred.X, preferred.Y + track * (size.Height + BpmnAutoArrangeOptions.VerticalGap), size.Width, size.Height);
                if (bounds.Top < 16 || !DocumentGeometryBoundary.Contains(bounds) || obstacles.Any(bounds.Intersects)) continue;
                if (expansion == 0 && region is not null &&
                    (bounds.Bottom + 32 > region.ExpandedHeight || bounds.Right + 32 > region.ContentBounds!.Value.Width)) continue;
                return bounds;
            }
        throw new InvalidOperationException("No free local track is available.");
    }
}
