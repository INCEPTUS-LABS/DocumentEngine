using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

internal sealed record ModelerPresentationViewChoice(
    PresentationViewContext Context, string Label, string? ResourceKey = null);

internal static class ModelerPresentationViews
{
    internal static ImmutableArray<ModelerPresentationViewChoice> GetChoices(
        DocumentSnapshot document, PresentationViewContext? currentContext = null)
    {
        var choices = ImmutableArray.CreateBuilder<ModelerPresentationViewChoice>();
        choices.Add(new(new ProcessScopePresentationContext(document.SemanticModel.RootScopeId),
            string.Empty, "Navigation_MainProcess"));
        foreach (var scope in document.SemanticModel.NestedScopes.Where(scope =>
                     scope.ParentScopeId is null ||
                     currentContext == new ProcessScopePresentationContext(scope.Id))
                     .OrderBy(static scope => scope.Id.Value, StringComparer.Ordinal))
        {
            var label = scope.OwnerSemanticElementId is { } ownerId &&
                document.SemanticModel.TryGetElement(ownerId, out var owner) && owner is not null &&
                owner.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) &&
                name.Kind == PropertyValueKind.Text && !string.IsNullOrWhiteSpace(name.TextValue)
                ? name.TextValue : scope.Id.Value;
            choices.Add(new(new ProcessScopePresentationContext(scope.Id), label));
        }
        foreach (var collaboration in document.SemanticModel.Elements
                     .Where(static element => element.TypeId == BpmnSemanticTypes.Collaboration)
                     .OrderBy(static element => element.Id.Value, StringComparer.Ordinal))
        {
            var label = collaboration.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) &&
                name.Kind == PropertyValueKind.Text && !string.IsNullOrWhiteSpace(name.TextValue)
                ? name.TextValue : collaboration.Id.Value;
            choices.Add(new(new DocumentPresentationContext(collaboration.Id), label));
        }
        return choices.ToImmutable();
    }
}
