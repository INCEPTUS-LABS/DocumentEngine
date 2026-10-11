using System.Text;

namespace Inceptus.DocumentEngine.Bpmn.Toolbox;

/// <summary>Plugin-owned icon artwork. Only fixed SVG fragments are emitted; caller text is never interpolated.</summary>
public static class BpmnToolboxIcons
{
    public static string Render(string iconKey)
    {
        ArgumentNullException.ThrowIfNull(iconKey);
        var svg = new StringBuilder("<svg width=\"26\" height=\"26\" viewBox=\"0 0 32 32\" aria-hidden=\"true\" focusable=\"false\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\">");
        if (iconKey.Contains("gateway", StringComparison.Ordinal))
        {
            svg.Append("<path d=\"M16 2 30 16 16 30 2 16Z\" fill=\"#fff4cc\" stroke=\"#c88a08\"/>");
            svg.Append(iconKey.Contains("parallel", StringComparison.Ordinal) ? "<path d=\"M16 9v14M9 16h14\"/>" :
                iconKey.Contains("inclusive", StringComparison.Ordinal) ? "<circle cx=\"16\" cy=\"16\" r=\"6\"/>" :
                iconKey.Contains("event-based", StringComparison.Ordinal) ? "<circle cx=\"16\" cy=\"16\" r=\"7\"/><path d=\"m16 11 5 4-2 6h-6l-2-6Z\"/>" :
                "<path d=\"m12 12 8 8m0-8-8 8\"/>");
        }
        else if (iconKey.Contains("event", StringComparison.Ordinal))
        {
            svg.Append(iconKey.Contains("end", StringComparison.Ordinal) ? "<circle cx=\"16\" cy=\"16\" r=\"13\" fill=\"#fee2e2\" stroke=\"#dc2626\" stroke-width=\"3\"/>" :
                "<circle cx=\"16\" cy=\"16\" r=\"13\" fill=\"#f8fafc\"/><circle cx=\"16\" cy=\"16\" r=\"10\"/>");
            if (iconKey.Contains("message", StringComparison.Ordinal))
                svg.Append(iconKey.Contains("throw", StringComparison.Ordinal) ? "<path d=\"M10 12h12v8H10Zm0 0 6 5 6-5\" fill=\"#64748b\"/>" : "<path d=\"M10 12h12v8H10Zm0 0 6 5 6-5\" fill=\"white\"/>");
            else if (iconKey.Contains("timer", StringComparison.Ordinal)) svg.Append("<circle cx=\"16\" cy=\"16\" r=\"6\"/><path d=\"M16 11v5l4 2\"/>");
            else if (iconKey.Contains("signal", StringComparison.Ordinal))
                svg.Append(iconKey.Contains("throw", StringComparison.Ordinal) ? "<path d=\"m16 10 6 11H10Z\" fill=\"#64748b\"/>" : "<path d=\"m16 10 6 11H10Z\" fill=\"white\"/>");
        }
        else
        {
            svg.Append("<rect x=\"2\" y=\"5\" width=\"28\" height=\"22\" rx=\"4\" fill=\"#eaf4ff\" stroke=\"#2563eb\"/>");
            if (iconKey.Contains("sub-process", StringComparison.Ordinal)) svg.Append("<path d=\"M12 18h8v7h-8m4-6v5m-3-2h6\"/>");
            else if (iconKey.Contains("user", StringComparison.Ordinal)) svg.Append("<circle cx=\"10\" cy=\"11\" r=\"2\"/><path d=\"M6 19v-2c0-4 8-4 8 0v2Z\"/>");
            else if (iconKey.Contains("service", StringComparison.Ordinal)) svg.Append("<path d=\"M10 8v10M5 13h10m-8-4 6 8m0-8-6 8\"/><circle cx=\"10\" cy=\"13\" r=\"3\" fill=\"#eaf4ff\"/>");
            else if (iconKey.Contains("manual", StringComparison.Ordinal)) svg.Append("<path d=\"M8 19 5 14l2-1 3 2V9h2v5l4-1v6Z\"/>");
            else if (iconKey.Contains("send", StringComparison.Ordinal)) svg.Append("<path d=\"M6 10h12v8H6Zm0 0 6 5 6-5\" fill=\"#64748b\"/>");
            else if (iconKey.Contains("receive", StringComparison.Ordinal)) svg.Append("<path d=\"M6 10h12v8H6Zm0 0 6 5 6-5\" fill=\"white\"/>");
        }
        return svg.Append("</svg>").ToString();
    }
}
