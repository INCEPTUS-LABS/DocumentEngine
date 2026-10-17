using System.Collections.Immutable;
using System.Xml;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Publishing;

namespace Inceptus.DocumentEngine.Bpmn.Publishing;

public sealed partial class BpmnPublishedTokenRoleClassifier
{
    public PublishedExecutionDescription DescribeExecution(DocumentSnapshot document,
        ImmutableHashSet<SemanticElementId> elementIds, ImmutableHashSet<SemanticElementId> relationshipIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(elementIds);
        ArgumentNullException.ThrowIfNull(relationshipIds);
        var elements = document.SemanticModel.Elements.ToDictionary(element => element.Id);
        var messages = document.SemanticModel.Relationships.Where(flow =>
            relationshipIds.Contains(flow.Id) && flow.TypeId == BpmnSemanticTypes.MessageFlow)
            .OrderBy(flow => flow.Id.Value, StringComparer.Ordinal).ToArray();
        var boundaries = elements.Values.Where(element => elementIds.Contains(element.Id) &&
            BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(element.TypeId))
            .OrderBy(element => element.Id.Value, StringComparer.Ordinal).ToArray();
        if (messages.Length == 0 && boundaries.Length == 0) return new(null, []);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var routes = ImmutableArray.CreateBuilder<PublishedCommunicationRoute>();
        var triggers = ImmutableArray.CreateBuilder<PublishedAttachedTrigger>();
        foreach (var flow in messages)
        {
            diagnostics.AddRange(BpmnMessageFlowSemantics.ValidateEndpoints(document, flow.SourceId, flow.TargetId));
            var supported = elements.TryGetValue(flow.SourceId, out var source) &&
                elements.TryGetValue(flow.TargetId, out var target) &&
                (source.TypeId == BpmnSemanticTypes.MessageThrowEvent || BpmnActivitySemanticTypes.IsActivity(source.TypeId)) &&
                (target.TypeId == BpmnSemanticTypes.MessageCatchEvent || BpmnActivitySemanticTypes.IsActivity(target.TypeId));
            routes.Add(new(flow.Id.Value, supported));
            if (!supported) diagnostics.Add(new("PUBLISH_MESSAGE_ENDPOINT_UNSUPPORTED", DiagnosticSeverity.Warning,
                $"Message Flow '{flow.Id}' is visual only. Animation supports Message Throw or Activity senders and Message Catch or Activity receivers.", flow.Id.Value));
        }
        foreach (var boundary in boundaries)
        {
            if (boundary.AttachedToElementId is not { } ownerId || !elementIds.Contains(ownerId) ||
                !elements.TryGetValue(ownerId, out var owner) || !BpmnActivitySemanticTypes.IsActivity(owner.TypeId) ||
                document.SemanticModel.GetScope(ownerId).Id != document.SemanticModel.GetScope(boundary.Id).Id)
            {
                diagnostics.Add(new("PUBLISH_BOUNDARY_ATTACHMENT_INVALID", DiagnosticSeverity.Error,
                    "A published boundary requires its captured Activity in the same Process scope.", boundary.Id.Value));
                continue;
            }
            var kind = boundary.TypeId == BpmnSemanticTypes.TimerBoundaryEvent ? PublishedTriggerKind.IdleTimer :
                boundary.TypeId == BpmnSemanticTypes.SignalBoundaryEvent ? PublishedTriggerKind.IdleSignal : PublishedTriggerKind.Completion;
            double? duration = null;
            if (kind == PublishedTriggerKind.IdleTimer)
            {
                var text = boundary.Properties.TryGetValue(BpmnSemanticProperties.TimerDefinition, out var timer) &&
                    timer.Kind == PropertyValueKind.Text ? timer.TextValue : string.Empty;
                // Publication uses a short presentation delay without changing the model's
                // timer definition. Unsupported or missing definitions use the midpoint.
                duration = TryDuration(text, out var milliseconds) ? Math.Clamp(milliseconds, 1000, 5000) : 3000;
            }
            var interrupting = true;
            if (boundary.Properties.TryGetValue(BpmnSemanticProperties.CancelActivity, out var cancel))
            {
                if (cancel.Kind != PropertyValueKind.Boolean)
                    diagnostics.Add(new("PUBLISH_BOUNDARY_INTERRUPTION_INVALID", DiagnosticSeverity.Error,
                        "The boundary Interrupting property must be Boolean.", boundary.Id.Value));
                else interrupting = cancel.BooleanValue;
            }
            triggers.Add(new(boundary.Id.Value, ownerId.Value, kind, interrupting, duration));
        }
        foreach (var group in triggers.GroupBy(trigger => (trigger.OwnerId, trigger.Kind)).Where(group => group.Count() > 1))
            diagnostics.Add(new("PUBLISH_BOUNDARY_TRIGGER_AMBIGUOUS", DiagnosticSeverity.Error,
                "Publication supports at most one boundary of each activation kind on an Activity.", group.Key.OwnerId));

        var senders = messages.Select(flow => flow.SourceId).Distinct().Where(id =>
            elements.TryGetValue(id, out var element) &&
            (element.TypeId == BpmnSemanticTypes.MessageThrowEvent || BpmnActivitySemanticTypes.IsActivity(element.TypeId)));
        var catches = messages.Where(flow => routes.Any(route => route.ConnectorId == flow.Id.Value && route.CanDeliver))
            .Select(flow => flow.TargetId).Distinct().Where(id => elements[id].TypeId == BpmnSemanticTypes.MessageCatchEvent);
        var receivers = messages.Select(flow => flow.TargetId).Distinct().Where(id =>
            elements.TryGetValue(id, out var element) && BpmnActivitySemanticTypes.IsActivity(element.TypeId));
        return new(new(Ids(senders), Ids(catches), Ids(receivers), routes.ToImmutable(), triggers.ToImmutable(),
            diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.Message).ToImmutableArray()),
            diagnostics.ToImmutable());
    }

    private static ImmutableArray<string> Ids(IEnumerable<SemanticElementId> ids) =>
        ids.Select(id => id.Value).Order(StringComparer.Ordinal).ToImmutableArray();

    private static bool TryDuration(string value, out double milliseconds)
    {
        milliseconds = 0;
        // Native timer text uses the same ISO literal interpretation as XML export. Calendar
        // months/years have no unambiguous duration on the viewer's relative clock.
        if (!value.StartsWith('P') || value.Split('T')[0].IndexOfAny(['Y', 'M']) >= 0) return false;
        try
        {
            milliseconds = XmlConvert.ToTimeSpan(value).TotalMilliseconds;
            return double.IsFinite(milliseconds) && milliseconds > 0 && milliseconds <= 9_007_199_254_740_991d;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException) { return false; }
    }
}
