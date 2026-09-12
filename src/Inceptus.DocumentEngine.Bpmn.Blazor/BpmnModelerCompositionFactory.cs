using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Composition;

/// <summary>
/// Creates one independently owned initial Document and its reusable composition per modeler.
/// </summary>
internal sealed class BpmnModelerCompositionFactory : IDocumentCanvasCompositionFactory
{
    private readonly ImmutableArray<IBpmnModelerStartupDocumentProvider> _startupProviders;
    private readonly Func<IEnumerable<IBpmnModelerStartupDocumentProvider>>? _resolveStartupProviders;
    private readonly DocumentSnapshot? _initialDocument;

    internal BpmnModelerCompositionFactory(
        IEnumerable<IBpmnModelerStartupDocumentProvider>? startupProviders = null,
        DocumentSnapshot? initialDocument = null)
    {
        _startupProviders = CopyStartupProviders(startupProviders);
        _initialDocument = initialDocument;
    }

    internal BpmnModelerCompositionFactory(
        Func<IEnumerable<IBpmnModelerStartupDocumentProvider>> resolveStartupProviders,
        DocumentSnapshot? initialDocument = null)
        : this(initialDocument: initialDocument)
    {
        ArgumentNullException.ThrowIfNull(resolveStartupProviders);
        _resolveStartupProviders = resolveStartupProviders;
    }

    internal static ToolboxCatalog ToolboxCatalog => BpmnModelerComposition.ToolboxCatalog;

    internal BpmnModelerCompositionFactory WithInitialDocument(DocumentSnapshot? initialDocument) =>
        _resolveStartupProviders is null
            ? new(_startupProviders, initialDocument)
            : new(_resolveStartupProviders, initialDocument);

    public async ValueTask<DocumentCanvasComposition> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_initialDocument is not null)
        {
            var reconstruction = BpmnModelerComposition.ReconstructDocument(_initialDocument);
            if (!reconstruction.Succeeded)
            {
                throw new InvalidOperationException(
                    "The initial Document snapshot is not valid for the BPMN modeler.");
            }

            return BpmnModelerComposition.Create(reconstruction.Document!);
        }

        var startupProviders = _resolveStartupProviders is null
            ? _startupProviders
            : CopyStartupProviders(_resolveStartupProviders() ?? throw new InvalidOperationException(
                "The BPMN modeler startup Document providers could not be resolved."));
        var document = startupProviders.Length switch
        {
            0 => BpmnModelerComposition.CreateEmptyDocument(),
            1 => await startupProviders[0]
                .GetInitialDocumentAsync(cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Only one BPMN modeler startup Document provider may be registered."),
        };

        cancellationToken.ThrowIfCancellationRequested();

        return BpmnModelerComposition.Create(document ?? throw new InvalidOperationException(
            "The BPMN modeler startup Document provider returned no Document."));
    }

    private static ImmutableArray<IBpmnModelerStartupDocumentProvider> CopyStartupProviders(
        IEnumerable<IBpmnModelerStartupDocumentProvider>? startupProviders)
    {
        var providers = startupProviders?.ToImmutableArray() ?? [];
        if (providers.Any(static provider => provider is null))
        {
            throw new ArgumentException(
                "Startup Document providers cannot contain null values.",
                nameof(startupProviders));
        }

        return providers;
    }
}
