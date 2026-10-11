using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal sealed partial class EditingSessionPipeline
{
    public async ValueTask<EditingSessionPipelineResult> RunDocumentPresentationAsync(DocumentSnapshot document,
        DocumentPresentationContext context, EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot profiles, ModelProfileElementViewStateSnapshot elements,
        CancellationToken cancellationToken)
    {
        var resolution = _configuration.ProjectionEngine.ResolvePresentation(document, context);
        if (!resolution.Succeeded || resolution.Plan is not { } plan)
            return EditingSessionPipelineResult.Failure(resolution.Diagnostics);
        var projection = _configuration.ProjectionEngine.Project(document, plan, _configuration.ProjectionContext, cancellationToken);
        if (!projection.IsSuccessful || projection.Graph is not { } graph)
            return EditingSessionPipelineResult.Failure(projection.Diagnostics);
        var layout = ConnectorRoutingStatePreparer.RestoreDocumentLayout(graph, document, plan);
        var state = document.VisualModel.DocumentRoutingStates.SingleOrDefault(state => state.OwnerId == context.OwnerId)
            ?? new DocumentRoutingSnapshot(context.OwnerId, []);
        var routing = ConnectorRoutingStatePreparer.RestoreDocumentRouting(graph, layout, document, plan, state,
            _configuration.RoutingAlgorithmId);
        var artifacts = new EditingSessionPipelineArtifacts(context, graph, layout, routing) { PresentationPlan = plan };
        return await RebuildSceneAsync(document, artifacts, editorState, profiles, elements, cancellationToken).ConfigureAwait(false);
    }
}
