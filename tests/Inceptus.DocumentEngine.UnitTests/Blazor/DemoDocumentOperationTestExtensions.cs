using System.Runtime.CompilerServices;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Validation;

namespace Inceptus.DocumentEngine.Tests;

// Existing regressions now exercise the reference host's operations against the public editor
// boundary. This helper contains no serialization, validation, publication or mutation logic.
internal static class DemoDocumentOperationTestExtensions
{
    private static readonly ConditionalWeakTable<IBpmnDocumentEditor, DemoDocumentOperations> Instances = new();
    internal static DemoDocumentOperations Operations(this IBpmnDocumentEditor editor) => Instances.GetValue(editor, static value => new(value));
    internal static void ConfigureOperations(this IBpmnDocumentEditor editor, ModelValidationCatalog? rules = null, PublishedProcessPackageBuilder? publisher = null)
    {
        Instances.Remove(editor);
        Instances.Add(editor, new DemoDocumentOperations(editor, rules is null ? null : new BpmnDocumentValidator(rules),
            publisher is null ? null : new PublishingPackageGenerator(publisher)));
    }
    internal static ValueTask<BpmnModelerDocumentResult> ImportNativeDocumentAsync(this IBpmnDocumentEditor editor,
        ReadOnlyMemory<byte> bytes, CancellationToken token = default) => editor.Operations().ImportNativeDocumentAsync(bytes, token);
    internal static ValueTask<BpmnModelerDocumentResult> ImportNativeDocumentAsync(this IBpmnDocumentEditor editor,
        ReadOnlyMemory<byte> bytes, BpmnModelerNativeImportOptions options, CancellationToken token = default) =>
        editor.Operations().ImportNativeDocumentAsync(bytes, new BpmnModelerDocumentLoadOptions
        { VerifySavedRouting = true, ReprepareIncompatibleRouting = options.ReprepareIncompatibleV2 }, token);
    internal static ValueTask<BpmnModelerFileResult> ExportNativeDocumentAsync(this IBpmnDocumentEditor editor,
        CancellationToken token = default) => editor.Operations().ExportNativeDocumentAsync(token);
    internal static ValueTask<BpmnModelerFileResult> PublishProcessAsync(this IBpmnDocumentEditor editor,
        CancellationToken token = default) => editor.Operations().PublishProcessAsync(token);
    internal static ValueTask<BpmnModelerFileResult> PublishAsync(this IBpmnDocumentEditor editor,
        CancellationToken token = default) => editor.Operations().PublishProcessAsync(token);
    internal static ValueTask<ValidationSnapshot?> ValidateAsync(this IBpmnDocumentEditor editor,
        CancellationToken token = default) => editor.Operations().ValidateAsync(token);
    internal static ValueTask<Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DInteractionResult?> SelectValidationIssueAsync(this IBpmnDocumentEditor editor,
        ModelValidationIssueId id, CancellationToken token = default) => editor.Operations().SelectValidationIssueAsync(id, token);
    internal static ValueTask<DemoPublicationResult> SavePublicationAsync(this IBpmnDocumentEditor editor,
        long version, DocumentId id, DocumentRevision revision, DocumentPublicationSnapshot publication, CancellationToken token = default) =>
        editor.Operations().SavePublicationAsync(version, id, revision, publication, token);
    internal static ValueTask<DemoPublicationResult> PublishPublicationAsync(this IBpmnDocumentEditor editor,
        long version, DocumentId id, DocumentRevision revision, DocumentPublicationSnapshot publication, CancellationToken token = default) =>
        editor.Operations().PublishPublicationAsync(version, id, revision, publication, token);
}
