using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

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
    }

    internal DocumentCanvasModelViewPropertiesSnapshot Authoritative { get; }

    internal ImmutableArray<DocumentCanvasModelProfileDraft> Profiles { get; }

    internal bool IsStale { get; set; }

    internal string? Feedback { get; set; }

    internal bool IsAvailabilityDirty =>
        Profiles.Any(static profile => profile.IsAvailabilityDirty);

    internal bool IsVisibilityDirty =>
        Profiles.Any(static profile => profile.IsVisibilityDirty);

    internal bool IsDirty => IsAvailabilityDirty || IsVisibilityDirty;

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
