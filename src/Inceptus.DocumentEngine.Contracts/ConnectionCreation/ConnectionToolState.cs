using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>Transient explicit connection selection in the existing EditingSession editor state.</summary>
public static class ConnectionToolState
{
    public static ElementConnectionPreview? GetPendingSource(EditorStateSnapshot state) =>
        state.TemporaryFeedback.Select(feedback => feedback.ElementConnection).SingleOrDefault(value => value is not null);

    public static EditorStateSnapshot WithPendingSource(EditorStateSnapshot state, ElementConnectionPreview? preview) =>
        new(state.Selection, null, state.ActiveToolId, state.FocusTargetId, state.Viewport, null,
            state.TemporaryFeedback.Where(feedback => feedback.ElementConnection is null)
                .Concat(preview is null ? [] : new[] { new EditorFeedbackSnapshot(preview) }), state.ToolState, state.SemanticSceneSelection);
    public const string ToolId = "inceptus:tool/connection";
    private const string CreationKey = "inceptus:tool/connection-creation-id";

    public static AnchorConnectionCreationId? GetSelection(EditorStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return StringComparer.Ordinal.Equals(state.ActiveToolId, ToolId) &&
            state.ToolState.TryGetValue(CreationKey, out var value) && value.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(value.TextValue) ? new AnchorConnectionCreationId(value.TextValue) : null;
    }

    public static EditorStateSnapshot WithSelection(EditorStateSnapshot state, AnchorConnectionCreationId? creationId)
        => WithSelection(state, creationId, null);

    public static EditorStateSnapshot WithSelection(EditorStateSnapshot state, AnchorConnectionCreationId? creationId,
        IEnumerable<VisualStateId>? allowedSourceVisuals)
    {
        ArgumentNullException.ThrowIfNull(state);
        var values = ConnectionCandidateMetadata.WithoutSources(state.ToolState)
            .Where(static entry => !StringComparer.Ordinal.Equals(entry.Key, CreationKey)).ToList();
        if (creationId is not null) values.Add(KeyValuePair.Create(CreationKey, PropertyValue.FromText(creationId.Value)));
        if (creationId is not null && allowedSourceVisuals is not null)
            values.AddRange(ConnectionCandidateMetadata.EncodeAllowedSources(allowedSourceVisuals));
        return new EditorStateSnapshot(state.Selection,
            creationId is not null || GetSelection(state) is not null ? null : state.HoveredObjectId,
            creationId is not null ? ToolId : StringComparer.Ordinal.Equals(state.ActiveToolId, ToolId) ? null : state.ActiveToolId,
            state.FocusTargetId, state.Viewport, state.ActiveGesture,
            state.TemporaryFeedback.Where(feedback => feedback.ElementConnection is null), values, state.SemanticSceneSelection);
    }
}
