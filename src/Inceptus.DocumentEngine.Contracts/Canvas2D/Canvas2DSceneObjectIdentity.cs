using System.Globalization;
using System.Text;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public static class Canvas2DSceneObjectIdentity
{
    public static SceneObjectId ForProjected(ProjectedObjectId projectedObjectId, string localKey = "primary")
    {
        ArgumentNullException.ThrowIfNull(projectedObjectId);
        return Create("projected", projectedObjectId.Value, localKey);
    }

    public static SceneObjectId ForEditorState(string stableSourceKey) =>
        Create("editor-state", stableSourceKey);

    public static SceneObjectId ForConfiguration(string stableSourceKey) =>
        Create("configuration", stableSourceKey);

    public static SceneObjectId ForExtension(
        Canvas2DSceneContributorId contributorId,
        string stableSourceKey)
    {
        ArgumentNullException.ThrowIfNull(contributorId);
        return Create("extension", contributorId.Value, stableSourceKey);
    }

    private static SceneObjectId Create(string category, params string[] parts)
    {
        var builder = new StringBuilder("scene:");
        Append(builder, category);
        foreach (var part in parts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(part);
            Append(builder, part);
        }

        return new SceneObjectId(builder.ToString());
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}
