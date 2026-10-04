using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Runtime.History;

// Only the node body explicitly changed by this operation is replayed. This contains
// no connector path, priority, region capacity, or whole Document snapshot.
internal sealed record NodeGeometryHistorySeed(DocumentScopeId ScopeId, ScopeNodeGeometrySnapshot Geometry);

internal sealed record NodeGeometryHistoryDelta(NodeGeometryHistorySeed Before, NodeGeometryHistorySeed After);
