using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Publishing;

/// <summary>
/// Resolves BPMN semantic properties into notation-neutral published node data.
/// </summary>
public sealed class BpmnPublishedNodeDataMapper : IPublishedNodeDataMapper
{
    public PublishedNodeData Map(SemanticElementSnapshot semanticElement)
    {
        ArgumentNullException.ThrowIfNull(semanticElement);

        return semanticElement.Properties.TryGetValue(
                BpmnSemanticProperties.Description,
                out var description) &&
            description.Kind == PropertyValueKind.Text
                ? new PublishedNodeData(description.TextValue)
                : PublishedNodeData.Empty;
    }
}
