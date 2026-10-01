using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

// Provenance of already composed content, never an independent presentation authority.
internal sealed record Canvas2DScenePanReuseSource(
    Canvas2DSceneBuilder Builder,
    DocumentSnapshot Document,
    DocumentScopeId ScopeId,
    ModelProfileViewStateSnapshot ProfileViewState,
    ModelProfileElementViewStateSnapshot ProfileElementViewState,
    ProjectedGraph Graph,
    LayoutResult Layout,
    RoutingResult Routing,
    EditorStateSnapshot EditorState);
