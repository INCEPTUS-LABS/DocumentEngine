namespace Inceptus.DocumentEngine.Bpmn.Semantics;

public static class BpmnSemanticProperties
{
    public const string Name = "BPMN.Name";

    public const string Description = "BPMN.Description";

    public const string Code = "BPMN.Code";

    public const string ElementNumber = "BPMN.ElementNumber";

    public const string TimerDefinition = "BPMN.TimerDefinition";

    public const string CancelActivity = "BPMN.CancelActivity";

    /// <summary>
    /// The sole persisted Participant-to-Collaboration membership authority.
    /// </summary>
    public const string CollaborationId = "BPMN.CollaborationId";

    /// <summary>
    /// The optional Participant reference to a top-level Process scope.
    /// Absence represents a black-box Participant.
    /// </summary>
    public const string ProcessScopeId = "BPMN.ProcessScopeId";
}
