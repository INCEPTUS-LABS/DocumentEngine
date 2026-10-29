using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Appearance;

public sealed record BpmnElementColours(string? Background, string Border, string Text);

public enum BpmnAppearancePreviewShape
{
    Circle,
    ThickCircle,
    RoundedRectangle,
    Diamond,
    Header,
}

/// <summary>Resolved presentation values. The original metadata remains the persistence authority.</summary>
public sealed record BpmnDocumentAppearance(
    string Scheme,
    bool CanEdit,
    ImmutableDictionary<string, BpmnElementColours> Colours,
    ImmutableArray<string> InvalidKeys);

public static class BpmnAppearanceSettings
{
    private static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");
    public const string VersionKey = "bpmn:appearance.version";
    public const string SchemeKey = "bpmn:appearance.scheme";
    public const string White = "white";
    public const string Mixed = "mixed";
    public static ImmutableArray<string> Schemes { get; } = [White, Mixed];
    public static ImmutableArray<string> Families { get; } =
        ["start-event", "task", "gateway", "end-event", "pool-header", "lane-header", "participant-header"];
    public static ImmutableArray<string> Channels { get; } = ["background", "border", "text"];

    /// <summary>Notation-owned sample geometry for the document appearance editor.</summary>
    public static BpmnAppearancePreviewShape PreviewShape(string family) => family switch
    {
        "start-event" => BpmnAppearancePreviewShape.Circle,
        "end-event" => BpmnAppearancePreviewShape.ThickCircle,
        "task" => BpmnAppearancePreviewShape.RoundedRectangle,
        "gateway" => BpmnAppearancePreviewShape.Diamond,
        "pool-header" or "lane-header" or "participant-header" => BpmnAppearancePreviewShape.Header,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    public static string Key(string scheme, string family, string channel) =>
        $"bpmn:appearance.{scheme}.{family}.{channel}";

    public static bool IsColourKey(string key) => Schemes.Any(scheme =>
        Families.Any(family => Channels.Any(channel => Key(scheme, family, channel) == key)));

    public static bool IsColour(string? value) => value is { Length: 7 } && value[0] == '#' &&
        value.AsSpan(1).IndexOfAnyExcept(HexDigits) < 0;

    public static BpmnElementColours Preset(string scheme, string family) => scheme == Mixed ? family switch
    {
        "start-event" => new("#E5F1E8", "#60846B", "#223D2B"),
        "task" => new("#E7EFF8", "#5D7FA5", "#263C56"),
        "gateway" => new("#F8F1D9", "#9B8744", "#493E20"),
        "end-event" => new("#F7E5E5", "#A46B6B", "#552D2D"),
        "pool-header" => new("#E9EEF5", "#7B8DA5", "#334155"),
        "lane-header" => new("#E7F0EF", "#76938F", "#294A45"),
        "participant-header" => new("#ECE9F4", "#8B80A3", "#433957"),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    } : family switch
    {
        "pool-header" or "lane-header" => new("#eef2f7", "#64748b", "#0f172a"),
        "participant-header" => new(null, "#111827", "#111827"),
        _ => new("#ffffff", "#000000", "#000000"),
    };

    public static string? Family(SemanticTypeId type) => type == BpmnSemanticTypes.StartEvent ? "start-event" :
        type == BpmnSemanticTypes.EndEvent ? "end-event" : BpmnTaskSemanticTypes.IsTask(type) ? "task" :
        BpmnGatewaySemanticTypes.IsGateway(type) ? "gateway" : null;

    public static BpmnDocumentAppearance Resolve(DocumentMetadataSnapshot? metadata, string? requestedScheme = null)
    {
        var properties = metadata?.ExtensionProperties ?? new PropertyMap();
        var invalid = ImmutableArray.CreateBuilder<string>();
        var canEdit = !properties.TryGetValue(VersionKey, out var version) ||
            version.Kind == PropertyValueKind.Integer && version.IntegerValue == 1;
        if (!canEdit) invalid.Add(VersionKey);
        var scheme = White;
        if (properties.TryGetValue(SchemeKey, out var stored))
        {
            if (stored.Kind == PropertyValueKind.Text && Schemes.Contains(stored.TextValue)) scheme = stored.TextValue;
            else invalid.Add(SchemeKey);
        }
        if (requestedScheme is not null && Schemes.Contains(requestedScheme)) scheme = requestedScheme;
        if (!canEdit) scheme = White;
        var colours = ImmutableDictionary.CreateBuilder<string, BpmnElementColours>(StringComparer.Ordinal);
        foreach (var family in Families)
        {
            var preset = Preset(scheme, family);
            string? Read(string channel, string? fallback)
            {
                var key = Key(scheme, family, channel);
                if (!canEdit || !properties.TryGetValue(key, out var value)) return fallback;
                if (value.Kind == PropertyValueKind.Text && IsColour(value.TextValue)) return value.TextValue;
                invalid.Add(key);
                return fallback;
            }
            colours.Add(family, new(Read("background", preset.Background), Read("border", preset.Border)!, Read("text", preset.Text)!));
        }
        return new(scheme, canEdit, colours.ToImmutable(), invalid.ToImmutable());
    }
}
