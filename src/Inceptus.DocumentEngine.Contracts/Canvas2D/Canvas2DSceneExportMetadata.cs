using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Notation-neutral export policy for editor-only extension graphics without EditorState origins.</summary>
public static class Canvas2DSceneExportMetadata
{
    public const string ExcludeFromExport = "inceptus.canvas2d:exclude-from-graphical-export";

    public static KeyValuePair<string, PropertyValue> Excluded { get; } =
        new(ExcludeFromExport, PropertyValue.FromBoolean(true));

    public static bool IsExcluded(Canvas2DSceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Metadata.TryGetValue(ExcludeFromExport, out var value) &&
            value.Kind == PropertyValueKind.Boolean && value.BooleanValue;
    }
}
