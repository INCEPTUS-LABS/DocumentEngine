using Inceptus.DocumentEngine.Contracts.Validation;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    internal string? GetValidationTargetName(ModelValidationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_sync)
        {
            if (_disposed || target.SemanticElementId is not { } id || _session is not { } session ||
                !session.TryCaptureDocumentSnapshot(out var document)) return null;
            return ModelerLabels.ValidationTargetName(document, id);
        }
    }
}
