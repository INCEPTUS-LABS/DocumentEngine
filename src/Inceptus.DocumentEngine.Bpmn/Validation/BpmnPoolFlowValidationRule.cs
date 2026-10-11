using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Validation;

/// <summary>Read-only validation shares the same Pool ownership authority as editing commands.</summary>
public sealed class BpmnPoolFlowValidationRule : IModelValidationRule
{
    public ModelValidationRuleId RuleId { get; } = new("bpmn:validation/pool-flow-ownership");

    public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var document = context.Document;
        var issues = ImmutableArray.CreateBuilder<ModelValidationIssue>();
        foreach (var pool in document.SemanticModel.Elements.Where(OrganizationalSemantics.IsPool))
            if (!BpmnEffectiveParticipant.TryResolve(document, pool.Id, out _, out var diagnostic))
                issues.Add(new(RuleId, ModelValidationSeverity.Error, diagnostic!.Code, diagnostic.Message,
                    ModelValidationTarget.ForSemanticElement(pool.Id)));
        foreach (var flow in document.SemanticModel.Relationships.Where(flow => flow.TypeId == BpmnSemanticTypes.SequenceFlow))
            foreach (var diagnostic in BpmnEffectiveParticipant.ValidateSequenceEndpoints(document, flow.SourceId, flow.TargetId))
                issues.Add(new(RuleId, ModelValidationSeverity.Error, diagnostic.Code, diagnostic.Message,
                    ModelValidationTarget.ForSemanticElement(flow.Id)));
        return issues.ToImmutable();
    }
}
