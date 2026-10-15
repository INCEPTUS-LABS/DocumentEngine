using Microsoft.AspNetCore.Components;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed record BpmnModelerDisplayPresentation(BpmnModelerDisplayMode Mode, EventCallback Toggle);
