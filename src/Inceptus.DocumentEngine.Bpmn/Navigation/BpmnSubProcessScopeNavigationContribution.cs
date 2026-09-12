using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.ScopeNavigation;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Navigation;

/// <summary>
/// Supplies BPMN-owned SubProcess navigation semantics while the generic editor
/// remains responsible for scope switching and breadcrumb mechanics.
/// </summary>
internal sealed class BpmnSubProcessScopeNavigationContribution :
    IScopeNavigationContribution
{
    public bool TryResolveTargetScope(
        DocumentSnapshot document,
        SemanticElementSnapshot ownerElement,
        [NotNullWhen(true)]
        out DocumentScopeId? targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ownerElement);

        targetScopeId = null;
        if (ownerElement.TypeId != BpmnSemanticTypes.SubProcess ||
            !document.SemanticModel.TryGetElement(ownerElement.Id, out var currentOwner) ||
            currentOwner != ownerElement)
        {
            return false;
        }

        var ownedScopes = document.SemanticModel.NestedScopes
            .Where(scope => scope.OwnerSemanticElementId == ownerElement.Id)
            .Take(2)
            .ToArray();
        if (ownedScopes.Length != 1 ||
            ownedScopes[0].ParentScopeId != document.SemanticModel.GetScope(ownerElement.Id).Id)
        {
            return false;
        }

        targetScopeId = ownedScopes[0].Id;
        return true;
    }

    public string ResolveBreadcrumbLabel(
        DocumentSnapshot document,
        SemanticElementSnapshot ownerElement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ownerElement);

        return TryGetText(ownerElement, BpmnSemanticProperties.Name, out var name)
            ? name
            : TryGetText(ownerElement, BpmnSemanticProperties.Code, out var code)
                ? code
                : "SubProcess";
    }

    private static bool TryGetText(
        SemanticElementSnapshot element,
        string propertyName,
        out string value)
    {
        if (element.Properties.TryGetValue(propertyName, out var property) &&
            property.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(property.TextValue))
        {
            value = property.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
