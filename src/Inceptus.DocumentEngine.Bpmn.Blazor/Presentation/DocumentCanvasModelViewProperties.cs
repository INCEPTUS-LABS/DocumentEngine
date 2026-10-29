using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Presentation;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed record DocumentCanvasModelProfileSnapshot(
    ModelProfileDefinition Definition,
    bool IsAvailable,
    bool IsPreferredVisible)
{
    internal bool IsEffectivelyVisible => IsAvailable && IsPreferredVisible;
}

internal sealed record DocumentCanvasModelViewPropertiesSnapshot(
    DocumentId DocumentId,
    DocumentRevision Revision,
    DocumentScopeId ScopeId,
    ImmutableArray<DocumentCanvasModelProfileSnapshot> Profiles)
{
    internal DocumentMetadataSnapshot? AppearanceMetadata { get; init; }
    internal PresentationViewContext ViewContext { get; init; } = new ProcessScopePresentationContext(ScopeId);

    internal static DocumentCanvasModelViewPropertiesSnapshot Create(
        EditingSessionState state) =>
        new(
            state.DocumentId,
            state.DocumentRevision,
            state.ActiveScopeId,
            [.. state.ModelProfileCatalog.Definitions.Select(definition =>
                new DocumentCanvasModelProfileSnapshot(
                    definition,
                    state.ModelProfileState.IsAvailable(definition.Id),
                    state.ModelProfileViewState.IsPreferredVisible(definition.Id)))]);
}

internal sealed class DocumentCanvasModelProfileDraft
{
    internal DocumentCanvasModelProfileDraft(DocumentCanvasModelProfileSnapshot authoritative)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        Authoritative = authoritative;
        IsAvailable = authoritative.IsAvailable;
        IsPreferredVisible = authoritative.IsPreferredVisible;
    }

    internal DocumentCanvasModelProfileSnapshot Authoritative { get; }

    internal ModelProfileDefinition Definition => Authoritative.Definition;

    internal bool IsAvailable { get; set; }

    internal bool IsPreferredVisible { get; set; }

    internal bool IsEffectivelyVisible => IsAvailable && IsPreferredVisible;

    internal bool IsAvailabilityDirty => IsAvailable != Authoritative.IsAvailable;

    internal bool IsVisibilityDirty =>
        IsPreferredVisible != Authoritative.IsPreferredVisible;
}

internal sealed class DocumentCanvasModelViewPropertiesDraft
{
    internal DocumentCanvasModelViewPropertiesDraft(
        DocumentCanvasModelViewPropertiesSnapshot authoritative)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        Authoritative = authoritative;
        Profiles = [.. authoritative.Profiles.Select(
            static profile => new DocumentCanvasModelProfileDraft(profile))];
        Appearance = new DocumentCanvasAppearanceDraft(authoritative.AppearanceMetadata);
    }

    internal DocumentCanvasModelViewPropertiesSnapshot Authoritative { get; }

    internal ImmutableArray<DocumentCanvasModelProfileDraft> Profiles { get; }
    internal DocumentCanvasAppearanceDraft Appearance { get; private set; }
    internal DocumentCanvasAppearanceDraft? ColourEditor { get; private set; }

    internal void BeginColourEditing()
    {
        if (!IsStale && ColourEditor is null) ColourEditor = Appearance.Copy();
    }

    internal bool AcceptColourEditing()
    {
        if (IsStale || ColourEditor is not { CanEdit: true, IsValid: true } editor) return false;
        Appearance = editor.Copy();
        ColourEditor = null;
        return true;
    }

    internal void CancelColourEditing() => ColourEditor = null;

    internal bool IsColourEditorDirty => ColourEditor is { } editor && !editor.HasSameEdits(Appearance);

    internal bool IsStale { get; set; }

    internal string? Feedback { get; set; }

    internal Diagnostic? FeedbackDiagnostic { get; set; }

    internal bool IsAvailabilityDirty =>
        Profiles.Any(static profile => profile.IsAvailabilityDirty);

    internal bool IsVisibilityDirty =>
        Profiles.Any(static profile => profile.IsVisibilityDirty);

    internal bool IsDirty => IsAvailabilityDirty || IsVisibilityDirty || Appearance.IsDirty || IsColourEditorDirty;

    internal ImmutableArray<ModelProfileAvailabilityChange> CreateAvailabilityChanges() =>
        [.. Profiles
            .Where(static profile => profile.IsAvailabilityDirty)
            .Select(static profile => new ModelProfileAvailabilityChange(
                profile.Definition.Id,
                profile.IsAvailable))];

    internal ModelProfileViewStateSnapshot CreateTargetViewState(
        ModelProfileViewStateSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var target = current;
        foreach (var profile in Profiles)
        {
            target = target.WithPreferredVisibility(
                profile.Definition.Id,
                profile.IsPreferredVisible);
        }

        return target;
    }

    internal bool TryGetProfile(
        ModelProfileId profileId,
        out DocumentCanvasModelProfileDraft? profile)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        profile = Profiles.FirstOrDefault(candidate => candidate.Definition.Id == profileId);
        return profile is not null;
    }
}

internal sealed class DocumentCanvasAppearanceDraft
{
    private readonly DocumentMetadataSnapshot? _original;
    private readonly Dictionary<string, string?> _edits = new(StringComparer.Ordinal);
    private bool _schemeEdited;
    internal DocumentCanvasAppearanceDraft(DocumentMetadataSnapshot? original)
    {
        _original = original;
        var resolved = BpmnAppearanceSettings.Resolve(original);
        Scheme = resolved.Scheme;
        CanEdit = resolved.CanEdit;
        InvalidKeys = BpmnAppearanceSettings.Schemes.SelectMany(scheme =>
            BpmnAppearanceSettings.Resolve(original, scheme).InvalidKeys).Distinct().ToImmutableArray();
    }
    internal string Scheme { get; private set; }
    internal bool CanEdit { get; }
    internal ImmutableArray<string> InvalidKeys { get; }
    internal bool IsDirty => _schemeEdited || _edits.Count > 0;
    internal bool IsValid => _edits.Values.All(value => value is null || BpmnAppearanceSettings.IsColour(value));

    internal DocumentCanvasAppearanceDraft Copy()
    {
        var copy = new DocumentCanvasAppearanceDraft(_original) { Scheme = Scheme, _schemeEdited = _schemeEdited };
        foreach (var edit in _edits) copy._edits.Add(edit.Key, edit.Value);
        return copy;
    }

    internal bool HasSameEdits(DocumentCanvasAppearanceDraft other) =>
        ReferenceEquals(_original, other._original) && Scheme == other.Scheme && _schemeEdited == other._schemeEdited &&
        _edits.Count == other._edits.Count && _edits.All(pair => other._edits.TryGetValue(pair.Key, out var value) && value == pair.Value);
    internal void SelectScheme(string scheme)
    {
        if (!CanEdit || !BpmnAppearanceSettings.Schemes.Contains(scheme)) return;
        Scheme = scheme;
        _schemeEdited = true;
    }
    internal string? Get(string family, string channel)
    {
        var key = BpmnAppearanceSettings.Key(Scheme, family, channel);
        if (_edits.TryGetValue(key, out var edit) && edit is not null) return edit;
        var colours = _edits.ContainsKey(key) ? BpmnAppearanceSettings.Preset(Scheme, family) :
            BpmnAppearanceSettings.Resolve(_original, Scheme).Colours[family];
        return channel switch { "background" => colours.Background, "border" => colours.Border, _ => colours.Text };
    }
    internal void Set(string family, string channel, string value)
    {
        if (CanEdit && BpmnAppearanceSettings.Families.Contains(family) && BpmnAppearanceSettings.Channels.Contains(channel))
            _edits[BpmnAppearanceSettings.Key(Scheme, family, channel)] = value;
    }
    internal void RestorePreset()
    {
        if (!CanEdit) return;
        foreach (var family in BpmnAppearanceSettings.Families)
            foreach (var channel in BpmnAppearanceSettings.Channels)
                _edits[BpmnAppearanceSettings.Key(Scheme, family, channel)] = null;
    }

    internal void RestoreDefaultBackground(string family)
    {
        if (CanEdit && BpmnAppearanceSettings.Families.Contains(family))
            _edits[BpmnAppearanceSettings.Key(Scheme, family, "background")] = null;
    }
    internal IEnumerable<KeyValuePair<string, PropertyValue?>> CreateChanges()
    {
        if (!CanEdit || !IsDirty) yield break;
        var changes = new List<KeyValuePair<string, PropertyValue?>>();
        var originalScheme = _original?.ExtensionProperties.GetValueOrDefault(BpmnAppearanceSettings.SchemeKey);
        if (_schemeEdited && !(originalScheme is null && Scheme == BpmnAppearanceSettings.White) &&
            !Equals(originalScheme, PropertyValue.FromText(Scheme)))
            changes.Add(new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText(Scheme)));
        foreach (var (key, value) in _edits)
        {
            var edited = value is null ? null : PropertyValue.FromText(value.ToUpperInvariant());
            if (!Equals(_original?.ExtensionProperties.GetValueOrDefault(key), edited)) changes.Add(new(key, edited));
        }
        if (changes.Count == 0) yield break;
        yield return new(BpmnAppearanceSettings.VersionKey, PropertyValue.FromInteger(1));
        foreach (var change in changes) yield return change;
    }
}

internal enum DocumentCanvasModelViewPropertiesApplyStatus
{
    NoChange,
    Committed,
    ValidationFailed,
    Stale,
    Unavailable,
    Failed,
}

internal sealed record DocumentCanvasModelViewPropertiesApplyResult(
    DocumentCanvasModelViewPropertiesApplyStatus Status,
    DocumentCanvasModelViewPropertiesSnapshot? Authoritative,
    ImmutableArray<Diagnostic> Diagnostics,
    string? Message)
{
    internal bool Succeeded => Status is
        DocumentCanvasModelViewPropertiesApplyStatus.NoChange or
        DocumentCanvasModelViewPropertiesApplyStatus.Committed;
}
