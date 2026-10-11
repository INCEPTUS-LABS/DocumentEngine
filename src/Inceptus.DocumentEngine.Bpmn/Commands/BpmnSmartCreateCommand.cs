using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>Installs a complete planned continuation through the normal routing/invariant transaction.</summary>
public sealed class BpmnSmartCreateCommand : ICommand
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/smart-create");
    internal BpmnSmartCreateCommand(DocumentSnapshot basis, DocumentSnapshot proposal, VisualStateId node, VisualStateId connector,
        ImmutableArray<SpatialRegionHeightIntent> heights, ImmutableArray<SpatialScopeWidthIntent> widths)
    { Basis = basis; Proposal = proposal; CreatedVisualStateId = node; ConnectorVisualStateId = connector; Heights = heights; Widths = widths; }
    internal DocumentSnapshot Basis { get; }
    internal DocumentSnapshot Proposal { get; }
    internal ImmutableArray<SpatialRegionHeightIntent> Heights { get; }
    internal ImmutableArray<SpatialScopeWidthIntent> Widths { get; }
    public VisualStateId CreatedVisualStateId { get; }
    public VisualStateId ConnectorVisualStateId { get; }
    public DocumentId TargetDocumentId => Basis.DocumentId;
    public DocumentRevision ExpectedRevision => Basis.Revision;
    public CommandTypeId TypeId => KnownTypeId;
    public CommandCategory Category => CommandCategory.Document;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel | AuthoritativeDocumentComponent.VisualModel;
}

internal sealed class BpmnSmartCreateCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var create = (BpmnSmartCreateCommand)command;
        return ValueTask.FromResult(!document.Equals(create.Basis)
            ? CommandHandlerResult.Failure([new Diagnostic("BPMN_SMART_CREATE_STALE", DiagnosticSeverity.Error,
                "The source or document changed while preparing the new element.")])
            : CommandHandlerResult.SuccessWithPreparation(create.Proposal, [], create.Heights, create.Widths,
                pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
                nodeGeometryImpact: NodeGeometryPipelineImpact.ForChangedVisualStates([create.CreatedVisualStateId])));
    }
}
