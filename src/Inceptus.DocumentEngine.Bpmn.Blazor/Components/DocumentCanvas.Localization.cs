using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Components;

public partial class DocumentCanvas
{
    private string PropertyFieldLabel(ElementPropertyFieldDefinition definition) =>
        ModelerLabels.PropertyFieldLabel(Text, definition);

    private string BreadcrumbLabel(DocumentCanvasScopeBreadcrumbSegment segment) =>
        segment.LabelResourceKey is { } key ? Text[key] : segment.Label;

    private string ScopeActionLabel(DocumentCanvasScopeNavigationContextAction action) =>
        ModelerLabels.ScopeActionLabel(Text, action.SemanticTypeId, action.Label);

    private string BackgroundGroupLabel(IGrouping<string, CanvasBackgroundActionDefinition> group) =>
        group.All(action => ModelerLabels.ActionKey(action.Id.Value) is not null)
            ? Text["Context_Add"]
            : group.Key;

    private string PropertyFeedback(DocumentCanvasPropertiesDraft draft)
    {
        if (draft.IsStale)
        {
            return Text["Properties_Stale"];
        }

        if (!draft.TryValidate(out _, out var messages, Text))
        {
            return string.Join(" ", messages);
        }

        // Canonical diagnostics remain English contract data. Only their UI wrapper changes.
        return draft.FeedbackDiagnostic is { } diagnostic
            ? $"{Text["Properties_Failed"]} {diagnostic.Message}"
            : Text["Properties_Failed"];
    }

    private string ModelViewFeedback(DocumentCanvasModelViewPropertiesDraft draft) =>
        draft.IsStale
            ? Text["Properties_ModelStale"]
            : draft.FeedbackDiagnostic is { } diagnostic
                ? $"{Text["Properties_ModelFailed"]} {diagnostic.Message}"
                : Text["Properties_ModelFailed"];
}
