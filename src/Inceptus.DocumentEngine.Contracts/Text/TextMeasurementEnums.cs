namespace Inceptus.DocumentEngine.Contracts.Text;

public enum TextMeasurementStatus
{
    Succeeded,
    Failed,
    Cancelled,
}

public enum TextDirection
{
    LeftToRight,
    RightToLeft,
}

public enum TextWritingMode
{
    HorizontalTopToBottom,
    VerticalRightToLeft,
    VerticalLeftToRight,
}

public enum TextFontStyle
{
    Normal,
    Italic,
    Oblique,
}
