using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Bpmn.History;

/// <summary>Checks the prepared routes before the command transaction installs its document.</summary>
internal sealed class BpmnAutoPointsHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        var selected = ((BpmnAutoPointsCommand)command).VisualStateId;
        before.VisualModel.TryGetVisualState(selected, out var node);
        var flows = before.SemanticModel.Relationships.Where(flow => flow.SourceId == node!.SemanticElementId || flow.TargetId == node.SemanticElementId)
            .Select(flow => flow.Id).ToHashSet();
        var ids = before.VisualModel.VisualStates.Where(visual => flows.Contains(visual.SemanticElementId)).Select(visual => visual.Id).ToHashSet();
        var oldRecords = before.VisualModel.EnumerateConnectorRoutingRecords().Where(record => ids.Contains(record.VisualStateId)).ToArray();
        var newRecords = committed.VisualModel.EnumerateConnectorRoutingRecords().Where(record => ids.Contains(record.VisualStateId)).ToArray();
        if (oldRecords.Any(record => record.RoutingType == ConnectorRoutingType.Manual && !newRecords.Contains(record)) ||
            newRecords.Any(record => record.Outcome != ConnectorRoutingOutcome.Path) ||
            Score(committed, ids).CompareTo(Score(before, ids)) > 0)
            return CommandHistoryPreparationResult.Failure([new Diagnostic("BPMN_ELEMENT_TOOLBOX_FAILED", DiagnosticSeverity.Error,
                "The proposed attachment arrangement did not preserve routing quality; the document is unchanged.")]);
        return new BpmnAutoArrangeHistoryPolicy().Prepare(command, before, committed);
    }

    internal static (int Crossings, int Bends, double Length) Score(DocumentSnapshot document, HashSet<VisualStateId> ids)
    {
        var crossings = 0; var bends = 0; var length = 0d;
        foreach (var records in (document.VisualModel.RoutingScopes ?? []).Select(scope => scope.Connectors)
                     .Concat(document.VisualModel.DocumentRoutingStates.Select(owner => owner.Connectors)))
            foreach (var record in records.Where(record => ids.Contains(record.VisualStateId)))
            {
                var score = ConnectorPathQuality.Score(record.Path, records.Where(other => other.VisualStateId != record.VisualStateId).Select(other => other.Path));
                crossings += score.Crossings; bends += score.Bends; length += score.Length;
            }
        return (crossings, bends, length);
    }
}
