using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnEventBasedGatewayPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.EventBasedGateway,
        BpmnGatewayPropertiesSchemaFields.Create(SemanticPropertyMutationKind.Property));
}
