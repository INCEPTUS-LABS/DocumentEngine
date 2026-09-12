using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnInclusiveGatewayPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.InclusiveGateway,
        BpmnGatewayPropertiesSchemaFields.Create(SemanticPropertyMutationKind.Name));
}
