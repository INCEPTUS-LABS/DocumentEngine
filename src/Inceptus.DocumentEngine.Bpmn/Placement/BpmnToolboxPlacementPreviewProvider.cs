using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.Bpmn.Placement;

internal sealed class BpmnToolboxPlacementPreviewProvider : IToolboxPlacementPreviewProvider
{
    private readonly ToolboxItemId _itemId;
    private readonly SemanticTypeId _type;
    private readonly BpmnBoundaryEventPlacementCandidateProvider? _attachmentResolver;

    internal BpmnToolboxPlacementPreviewProvider(
        ToolboxItemId itemId, SemanticTypeId type, string? attachmentFeedbackKind = null)
    {
        _itemId = itemId;
        _type = type;
        _attachmentResolver = attachmentFeedbackKind is null ? null
            : new BpmnBoundaryEventPlacementCandidateProvider(type, attachmentFeedbackKind);
    }

    public ToolboxPlacementPreview Evaluate(ToolboxPlacementPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var defaults = BpmnToolboxCreationDefaults.Resolve(request.Document, _type, out var defaultsFailure);
        var bounds = defaults.BoundsAt(request.DocumentPoint);
        var diagnostics = new List<Diagnostic>();
        if (defaultsFailure is not null)
        {
            diagnostics.Add(new Diagnostic(defaultsFailure.Code, defaultsFailure.Severity,
                defaultsFailure.Message, request.ToolboxItemId.Value));
        }

        if (request.ToolboxItemId != _itemId)
        {
            diagnostics.Add(new Diagnostic(BpmnToolboxPlacementDiagnosticCodes.ToolboxItemMismatch,
                DiagnosticSeverity.Error, "The Toolbox preview provider cannot evaluate this tool.",
                request.ToolboxItemId.Value));
        }

        var candidate = _attachmentResolver?.ResolveCandidate(request);
        if (candidate is not null)
        {
            bounds = candidate.PreviewBounds;
        }
        else if (_attachmentResolver is not null)
        {
            diagnostics.Add(new Diagnostic(BpmnToolboxPlacementDiagnosticCodes.AttachmentCandidateRequired,
                DiagnosticSeverity.Error,
                "A Boundary Event can only be placed on or near a visible Activity boundary.",
                request.ToolboxItemId.Value));
        }

        if (!DocumentGeometryBoundary.Contains(bounds))
        {
            diagnostics.Add(new Diagnostic(CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                DiagnosticSeverity.Error, "The prospective node body crosses the Document boundary.",
                request.ToolboxItemId.Value));
        }

        return new ToolboxPlacementPreview(request.ToolboxItemId, _type, bounds,
            new PointD(bounds.Left + (bounds.Width / 2d), bounds.Top + (bounds.Height / 2d)),
            defaults.Name, BpmnNodeLabelPlacementPolicy.Resolve(_type), candidate,
            diagnostics.Count == 0, diagnostics);
    }
}
