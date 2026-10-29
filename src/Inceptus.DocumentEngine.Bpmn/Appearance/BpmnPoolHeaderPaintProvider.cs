using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Appearance;

/// <summary>Maps existing Pool ownership to document-wide BPMN header colours.</summary>
public sealed class BpmnPoolHeaderPaintProvider : IOrganizationalPoolHeaderPaintProvider
{
    public OrganizationalPoolHeaderPaint Resolve(DocumentSnapshot document, SemanticElementSnapshot pool)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pool);
        var family = OrganizationalSemantics.GetParticipantId(pool) is null ? "pool-header" : "lane-header";
        var appearance = BpmnAppearanceSettings.Resolve(document.Metadata);
        var colours = appearance.Colours[family];
        var key = BpmnAppearanceSettings.Key(appearance.Scheme, family, "background");
        var hasCustomBackground = appearance.CanEdit && document.Metadata?.ExtensionProperties.TryGetValue(key, out var value) == true &&
            value.Kind == PropertyValueKind.Text && BpmnAppearanceSettings.IsColour(value.TextValue);
        var background = colours.Background!;
        return new(background, colours.Border, colours.Text,
            appearance.Scheme == BpmnAppearanceSettings.White && !hasCustomBackground ? "#dbeafe" : background);
    }
}
