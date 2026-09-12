using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn;

public static class BpmnAlgorithmIds
{
    public static AlgorithmId DefaultLayout { get; } = new("bpmn:layout/default");

    public static AlgorithmId DefaultRouting { get; } = new("bpmn:routing/default");
}

public static class BpmnAlgorithmDiagnosticCodes
{
    public const string UnsupportedLayoutContent = "BPMN_LAYOUT_UNSUPPORTED_CONTENT";

    public const string UnsupportedRoutingContent = "BPMN_ROUTING_UNSUPPORTED_CONTENT";

    public const string InvalidRoutingInput = "BPMN_ROUTING_INVALID_INPUT";

    public const string InvalidRoutingOutput = "BPMN_ROUTING_INVALID_OUTPUT";

    public const string NoLegalRoute = "BPMN_ROUTING_NO_LEGAL_ROUTE";
}
