using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ScopeNavigation;

/// <summary>
/// Associates one semantic type with its notation-owned scope navigation behavior.
/// </summary>
public sealed class ScopeNavigationRegistration
{
    public ScopeNavigationRegistration(
        SemanticTypeId semanticTypeId,
        string openActionLabel,
        IScopeNavigationContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(openActionLabel);
        ArgumentNullException.ThrowIfNull(contribution);

        SemanticTypeId = semanticTypeId;
        OpenActionLabel = openActionLabel;
        Contribution = contribution;
    }

    public SemanticTypeId SemanticTypeId { get; }

    public string OpenActionLabel { get; }

    public IScopeNavigationContribution Contribution { get; }
}
