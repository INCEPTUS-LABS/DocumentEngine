using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Visuals;

internal static class BpmnMessageFlowVisuals
{
    internal static ConnectorVisualDescriptor Descriptor { get; } = new([6d, 4d],
        ConnectorEndpointDecoration.OpenCircle, ConnectorEndpointDecoration.OpenArrow);
}
