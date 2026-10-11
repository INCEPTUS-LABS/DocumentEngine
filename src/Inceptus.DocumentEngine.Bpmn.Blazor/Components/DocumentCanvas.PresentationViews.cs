using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Components;

public partial class DocumentCanvas
{
    private ImmutableArray<ModelerPresentationViewChoice> PresentationViewChoices =>
        _state?.PresentationViewChoices is { IsDefault: false } choices ? choices : [];

    private string SelectedPresentationView => _state?.Session?.PresentationContext is { } context
        ? ViewChoiceValue(context) : string.Empty;

    private static string ViewChoiceValue(PresentationViewContext context) => context switch
    {
        ProcessScopePresentationContext process => $"process:{process.ScopeId.Value}",
        DocumentPresentationContext document => $"document:{document.OwnerId.Value}",
        _ => throw new ArgumentOutOfRangeException(nameof(context)),
    };

    private async Task SelectPresentationViewAsync(ChangeEventArgs args)
    {
        var choice = PresentationViewChoices.SingleOrDefault(option =>
            ViewChoiceValue(option.Context) == args.Value?.ToString());
        if (choice is null || !CanNavigateScope || _host is not { } host)
            return;
        _ = await host.ChangePresentationContextAsync(choice.Context, _componentLifetime.Token);
        RefreshHostState(host);
    }
}
