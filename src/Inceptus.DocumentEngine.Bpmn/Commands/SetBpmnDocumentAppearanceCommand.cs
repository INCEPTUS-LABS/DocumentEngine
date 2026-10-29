using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>Patches only explicitly edited appearance keys; null removes an override.</summary>
public sealed class SetBpmnDocumentAppearanceCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/set-document-appearance");
    public SetBpmnDocumentAppearanceCommand(DocumentId targetDocumentId, DocumentRevision expectedRevision,
        IEnumerable<KeyValuePair<string, PropertyValue?>> changes)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(changes);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        Changes = changes.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public ImmutableSortedDictionary<string, PropertyValue?> Changes { get; }
    internal bool IsHistoryReplay { get; init; }
    public CommandTypeId TypeId => KnownTypeId;
    public CommandCategory Category => CommandCategory.Metadata;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.Metadata;
    public PipelineInvalidation PipelineInvalidation => PipelineInvalidation.Scene;
}

internal sealed class BpmnDocumentAppearanceCommandHandler : ICommandHandler, ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        if (command is not SetBpmnDocumentAppearanceCommand edit) return [Invalid()];
        if (!edit.IsHistoryReplay && !BpmnAppearanceSettings.Resolve(document.Metadata).CanEdit) return [Invalid()];
        foreach (var (key, value) in edit.Changes)
        {
            if (key != BpmnAppearanceSettings.VersionKey && key != BpmnAppearanceSettings.SchemeKey &&
                !BpmnAppearanceSettings.IsColourKey(key)) return [Invalid()];
            if (edit.IsHistoryReplay || value is null) continue;
            var valid = key == BpmnAppearanceSettings.VersionKey
                ? value.Kind == PropertyValueKind.Integer && value.IntegerValue == 1
                : value.Kind == PropertyValueKind.Text && (key == BpmnAppearanceSettings.SchemeKey
                    ? BpmnAppearanceSettings.Schemes.Contains(value.TextValue)
                    : BpmnAppearanceSettings.IsColour(value.TextValue));
            if (!valid) return [Invalid()];
        }
        return [];
    }

    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Validate(command, document);
        if (!diagnostics.IsEmpty) return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        var edit = (SetBpmnDocumentAppearanceCommand)command;
        var properties = document.Metadata.ExtensionProperties.ToDictionary();
        foreach (var (key, value) in edit.Changes)
        {
            if (value is null) properties.Remove(key);
            else properties[key] = !edit.IsHistoryReplay && BpmnAppearanceSettings.IsColourKey(key)
                ? PropertyValue.FromText(value.TextValue.ToUpperInvariant()) : value;
        }
        var metadata = new DocumentMetadataSnapshot(document.DocumentId, document.Revision,
            document.Metadata.SystemManagedProperties, properties);
        return ValueTask.FromResult(metadata.Equals(document.Metadata) ? CommandHandlerResult.NoChange() :
            CommandHandlerResult.Success(new DocumentSnapshot(document.SemanticModel, document.VisualModel, metadata,
                document.Publication), pipelineInvalidation: PipelineInvalidation.Scene));
    }

    private static Diagnostic Invalid() => new("BPMN.APPEARANCE.INVALID", DiagnosticSeverity.Error,
        "Appearance changes require a supported scheme, version and six-digit hexadecimal colours.");
}

internal sealed class BpmnDocumentAppearanceHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        var keys = ((SetBpmnDocumentAppearanceCommand)command).Changes.Keys;
        return CommandHistoryPreparationResult.Undoable(Capture(before), Capture(committed));
        Factory Capture(DocumentSnapshot document) => new(keys.Select(key =>
            new KeyValuePair<string, PropertyValue?>(key, document.Metadata.ExtensionProperties.GetValueOrDefault(key))).ToImmutableArray());
    }
    private sealed record Factory(ImmutableArray<KeyValuePair<string, PropertyValue?>> Changes) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new SetBpmnDocumentAppearanceCommand(documentId, expectedRevision, Changes) { IsHistoryReplay = true };
    }
}
