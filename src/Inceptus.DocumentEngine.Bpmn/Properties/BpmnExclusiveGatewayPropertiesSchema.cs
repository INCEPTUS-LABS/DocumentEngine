using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnExclusiveGatewayPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.ExclusiveGateway,
        BpmnGatewayPropertiesSchemaFields.Create(SemanticPropertyMutationKind.Name));

    // The M3.2.3 label lifecycle deliberately permits a temporarily empty
    // Gateway Name while retaining the instance-owned visual override. The
    // generic typed-property mutation path accepts the empty text value; the
    // earlier generic Semantic Name path intentionally requires nonblank text.
    internal static ElementPropertiesSchema M323Definition { get; } = new(
        BpmnSemanticTypes.ExclusiveGateway,
        BpmnGatewayPropertiesSchemaFields.Create(SemanticPropertyMutationKind.Property));
}
