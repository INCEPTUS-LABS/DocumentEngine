using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

public sealed class BpmnAutoPointsCommand(DocumentSnapshot document, VisualStateId visualId) : ICommand
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/auto-points");
    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId => document.DocumentId;
    public DocumentRevision ExpectedRevision => document.Revision;
    public CommandCategory Category => CommandCategory.Visual;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.VisualModel;
    public VisualStateId VisualStateId => visualId;
}

internal sealed class BpmnAutoPointsCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken) =>
        new BpmnAutoArrangeCommandHandler().HandleAsync(new BpmnAutoArrangeCommand(BpmnAutoPointsPlanner.Plan(document,
            ((BpmnAutoPointsCommand)command).VisualStateId, cancellationToken)), document, cancellationToken);
}
