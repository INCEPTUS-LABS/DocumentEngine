using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>Exact element presentation acquired by a body click; no anchor is acquired yet.</summary>
public sealed record ElementConnectionEndpoint(SemanticElementId SemanticId, VisualStateId? VisualId,
    ProjectedObjectId ProjectedId, SceneObjectId SceneId, DocumentScopeId? OriginatingScopeId, RectD Bounds);

/// <summary>Transient pending source in the existing editor feedback collection.</summary>
public sealed record ElementConnectionPreview(DocumentId DocumentId, DocumentRevision Revision,
    PresentationViewContext Context, AnchorConnectionCreationId CreationId, ElementConnectionEndpoint Source,
    PointD CurrentPoint, ConnectorVisualDescriptor Descriptor)
{
    public const string FeedbackId = "inceptus:element-connection";
}

public sealed record ElementConnectionSourceRequest(DocumentSnapshot Document, PresentationViewContext Context,
    PresentationViewPlan? PresentationPlan, SemanticElementId SemanticId, VisualStateId? VisualId);

public sealed record ElementConnectionCreationRequest(DocumentSnapshot Document, PresentationViewContext Context,
    PresentationViewPlan? PresentationPlan, ElementConnectionEndpoint Source, ElementConnectionEndpoint Target,
    IDocumentCreationIdentityProvider IdentityProvider);

public sealed record ElementConnectionCandidate(AnchorConnectionCreationPlan Plan, CommandHandlerResult Proposal);

/// <summary>Additive body-click capability on the existing registered connection factory.</summary>
public interface IElementConnectionCreationCommandFactory
{
    ImmutableArray<Diagnostic> ValidateSource(ElementConnectionSourceRequest request);
    ImmutableArray<Diagnostic> ValidateEndpoints(ElementConnectionCreationRequest request);
    ValueTask<ImmutableArray<ElementConnectionCandidate>> CreateCandidatesAsync(ElementConnectionCreationRequest request,
        CancellationToken cancellationToken);
}
