using Inceptus.DocumentEngine.Bpmn.History;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>Installs a complete revision-bound arrangement through the normal atomic command transaction.</summary>
public sealed class BpmnAutoArrangeCommand : ICommand
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/auto-arrange");
    public BpmnAutoArrangeCommand(BpmnAutoArrangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan; TargetDocumentId = plan.DocumentId; ExpectedRevision = plan.ExpectedRevision;
    }
    internal BpmnAutoArrangeCommand(DocumentId documentId, DocumentRevision revision, BpmnArrangeHistoryDelta replay)
    { TargetDocumentId = documentId; ExpectedRevision = revision; Replay = replay; }
    internal BpmnAutoArrangePlan? Plan { get; }
    internal BpmnArrangeHistoryDelta? Replay { get; }
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public CommandTypeId TypeId => KnownTypeId;
    public CommandCategory Category => CommandCategory.Visual;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.VisualModel;
}

internal sealed class BpmnAutoArrangeCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var arrange = (BpmnAutoArrangeCommand)command;
        if (arrange.Replay is { } replay) return ValueTask.FromResult(replay.Apply(document));
        var plan = arrange.Plan!;
        // Compare the immutable authority as well as the envelope revision. This covers exact visual identities,
        // Pool membership, Participant assignment, attachments and saved presentation inputs without trusting a UI cache.
        if (!document.Equals(plan.Basis))
            return ValueTask.FromResult(CommandHandlerResult.Failure([new Diagnostic("BPMN_AUTO_ARRANGE_STALE",
                DiagnosticSeverity.Error, "The arrangement no longer matches the current document presentation.")]));
        if (plan.IsNoChange) return ValueTask.FromResult(CommandHandlerResult.NoChange());
        return ValueTask.FromResult(CommandHandlerResult.SuccessWithPreparation(plan.Proposal, [], plan.Heights, plan.Widths,
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: plan.ChangedVisualStateIds.IsEmpty ? NodeGeometryPipelineImpact.PreserveAll :
                NodeGeometryPipelineImpact.ForChangedVisualStates(plan.ChangedVisualStateIds)));
    }
}
