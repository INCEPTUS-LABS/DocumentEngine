using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Presentation;

/// <summary>Notation-owned membership and composition for explicit document views.</summary>
public interface IPresentationViewProvider
{
    ImmutableArray<DocumentPresentationContext> GetContexts(DocumentSnapshot document);

    PresentationViewResolution Resolve(DocumentSnapshot document, DocumentPresentationContext context);
}

/// <summary>Adds explicit document-owned targets to an existing scope presentation without changing scope membership.</summary>
public interface IProcessPresentationViewProvider
{
    PresentationViewPlan IncludeDocumentElements(DocumentSnapshot document, ProcessScopePresentationContext context, PresentationViewPlan scopePlan);
}

public sealed class PresentationViewProviderRegistration
{
    public PresentationViewProviderRegistration(SemanticTypeId ownerTypeId, IPresentationViewProvider provider)
    {
        ArgumentNullException.ThrowIfNull(ownerTypeId);
        ArgumentNullException.ThrowIfNull(provider);
        OwnerTypeId = ownerTypeId;
        Provider = provider;
    }

    public SemanticTypeId OwnerTypeId { get; }

    public IPresentationViewProvider Provider { get; }
}

public sealed class PresentationViewResolution
{
    private PresentationViewResolution(PresentationViewPlan? plan, IEnumerable<Diagnostic>? diagnostics)
    {
        Plan = plan;
        Diagnostics = diagnostics?.ToImmutableArray() ?? [];
    }

    public bool Succeeded => Plan is not null;

    public PresentationViewPlan? Plan { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static PresentationViewResolution Success(PresentationViewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(plan, null);
    }

    public static PresentationViewResolution Failure(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(null, diagnostics);
    }
}
