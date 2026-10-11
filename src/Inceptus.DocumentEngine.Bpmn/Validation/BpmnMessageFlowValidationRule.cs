using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Validation;

namespace Inceptus.DocumentEngine.Bpmn.Validation;

/// <summary>Communication validation is independent from Sequence Flow topology.</summary>
public sealed class BpmnMessageFlowValidationRule : IModelValidationRule
{
    public ModelValidationRuleId RuleId { get; } = new("bpmn:validation/message-flow");

    public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. context.Document.SemanticModel.Relationships.Where(flow => flow.TypeId == BpmnSemanticTypes.MessageFlow)
            .SelectMany(flow => BpmnMessageFlowSemantics.ValidateEndpoints(context.Document, flow.SourceId, flow.TargetId)
                .Select(diagnostic => new ModelValidationIssue(RuleId, ModelValidationSeverity.Error,
                    diagnostic.Code,
                    $"Message Flow '{(flow.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) && name.Kind == PropertyValueKind.Text ? name.TextValue : flow.Id.Value)}': {diagnostic.Message}",
                    context.Document.VisualModel.VisualStates.FirstOrDefault(visual => visual.SemanticElementId == flow.Id) is { } visual
                        ? ModelValidationTarget.ForVisualState(flow.Id, visual.Id) : ModelValidationTarget.ForSemanticElement(flow.Id),
                    diagnostic.SourceIdentity)))];
    }
}
