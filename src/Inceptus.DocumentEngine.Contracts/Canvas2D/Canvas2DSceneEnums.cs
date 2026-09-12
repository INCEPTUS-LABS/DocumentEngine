namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public enum Canvas2DSceneLayer
{
    Background,
    Content,
    Connector,
    Label,
    Decoration,
    Overlay,
}

public enum Canvas2DSceneGeometryKind
{
    Rectangle,
    Ellipse,
    Path,
    Text,
    Image,
}

public enum Canvas2DTextAlignment
{
    Start,
    Center,
    End,
}

public enum Canvas2DTextBaseline
{
    Top,
    Middle,
    Bottom,
}

public enum Canvas2DHitTestMode
{
    None,
    Bounds,
    Fill,
    Stroke,
    FillOrStroke,
}

[Flags]
public enum Canvas2DSceneOriginCategory
{
    None = 0,
    SemanticElement = 1 << 0,
    VisualState = 1 << 1,
    ProjectedRuntimeObject = 1 << 2,
    EditorState = 1 << 3,
    Configuration = 1 << 4,
    RegisteredExtension = 1 << 5,
}
