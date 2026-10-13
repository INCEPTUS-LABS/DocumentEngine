namespace Inceptus.Bpmn.Integration;

// Import policy reporting is deliberately isolated from the shared result collector and export path.
internal sealed partial class BpmnXmlImporter
{
    private readonly HashSet<(string Code, BpmnImportReportScope Scope, string Identity, string? Property, string? Value)> _reported = [];

    private void UnmappedLoss(string source, string property, string description) =>
        ReportLoss(profile.ImportRules.UnmappedAttribute, source, property, message: description, deduplicate: false);

    private void ReportLoss(BpmnImportValueRule rule, string source, string? property = null,
        string? element = null, string? value = null, string? name = null, string? owner = null,
        string? diagram = null, string? document = null, string? message = null, bool deduplicate = true)
    {
        if (rule.Report is not { } report) return;
        var identity = report.Scope switch
        {
            BpmnImportReportScope.Owner => owner ?? source,
            BpmnImportReportScope.Diagram => diagram ?? document ?? source,
            BpmnImportReportScope.Document => document ?? source,
            _ => source,
        };
        // Identity uses the stable code and scope, never human-readable message text.
        var perElement = report.Scope == BpmnImportReportScope.Element;
        if (deduplicate && !_reported.Add((report.Code, report.Scope, identity, perElement ? property : null, perElement ? value : null))) return;
        var text = System.Text.RegularExpressions.Regex.Replace(report.Message, @"\{(source|element|property|value|name|message)\}", match => match.Groups[1].Value switch
        {
            "source" => identity,
            "element" => element ?? property ?? string.Empty,
            "property" => property ?? string.Empty,
            "value" => value ?? string.Empty,
            "name" => name ?? identity,
            _ => message ?? string.Empty,
        });
        _log.Add(BpmnConversionDisposition.Ignored,
            report.Code, text, identity, property);
    }
}
