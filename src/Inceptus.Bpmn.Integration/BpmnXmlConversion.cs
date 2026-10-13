using System.Xml;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.Bpmn.Integration;

internal static class BpmnXmlVocabulary
{
    internal static readonly XNamespace Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    internal static readonly XNamespace BpmnDi = "http://www.omg.org/spec/BPMN/20100524/DI";
    internal static readonly XNamespace Di = "http://www.omg.org/spec/DD/20100524/DI";
    internal static readonly XNamespace Dc = "http://www.omg.org/spec/DD/20100524/DC";
    internal const int MaximumDepth = 128;

    internal sealed record Mapping(SemanticTypeId Type, string Element, string? Definition = null);
    internal static readonly Mapping[] Nodes =
    [
        new(BpmnSemanticTypes.StartEvent, "startEvent"), new(BpmnSemanticTypes.EndEvent, "endEvent"),
        new(BpmnSemanticTypes.Task, "task"), new(BpmnSemanticTypes.UserTask, "userTask"),
        new(BpmnSemanticTypes.ManualTask, "manualTask"), new(BpmnSemanticTypes.ServiceTask, "serviceTask"),
        new(BpmnSemanticTypes.SendTask, "sendTask"), new(BpmnSemanticTypes.ReceiveTask, "receiveTask"),
        new(BpmnSemanticTypes.SubProcess, "subProcess"),
        new(BpmnSemanticTypes.ExclusiveGateway, "exclusiveGateway"),
        new(BpmnSemanticTypes.ParallelGateway, "parallelGateway"),
        new(BpmnSemanticTypes.InclusiveGateway, "inclusiveGateway"),
        new(BpmnSemanticTypes.EventBasedGateway, "eventBasedGateway"),
        new(BpmnSemanticTypes.MessageCatchEvent, "intermediateCatchEvent", "messageEventDefinition"),
        new(BpmnSemanticTypes.MessageThrowEvent, "intermediateThrowEvent", "messageEventDefinition"),
        new(BpmnSemanticTypes.SignalCatchEvent, "intermediateCatchEvent", "signalEventDefinition"),
        new(BpmnSemanticTypes.SignalThrowEvent, "intermediateThrowEvent", "signalEventDefinition"),
        new(BpmnSemanticTypes.TimerCatchEvent, "intermediateCatchEvent", "timerEventDefinition"),
        new(BpmnSemanticTypes.TimerBoundaryEvent, "boundaryEvent", "timerEventDefinition"),
        new(BpmnSemanticTypes.MessageBoundaryEvent, "boundaryEvent", "messageEventDefinition"),
        new(BpmnSemanticTypes.SignalBoundaryEvent, "boundaryEvent", "signalEventDefinition"),
    ];

    internal static string? Text(PropertyMap properties, string key) =>
        properties.TryGetValue(key, out var value) && value.Kind == PropertyValueKind.Text ? value.TextValue : null;

    internal static bool IsXmlId(string value)
    {
        try { return XmlConvert.VerifyNCName(value) == value; }
        catch (XmlException) { return false; }
    }
}

internal sealed class BpmnConversionLog
{
    private readonly List<BpmnConversionLogEntry> _entries = [];
    private readonly List<Diagnostic> _diagnostics = [];
    internal bool HasErrors => _diagnostics.Any(static item => item.Severity == DiagnosticSeverity.Error);
    internal void Add(BpmnConversionDisposition disposition, string code, string message,
        string? source = null, string? property = null, string? target = null)
    {
        if (disposition is BpmnConversionDisposition.Omitted or BpmnConversionDisposition.Ignored)
        {
            _entries.Add(new(disposition, code, message, source, property, target));
            return;
        }
        var context = new Dictionary<string, string>(StringComparer.Ordinal) { ["disposition"] = disposition.ToString() };
        if (property is not null) context.Add("property", property);
        if (target is not null) context.Add("targetIdentity", target);
        _diagnostics.Add(new(code, disposition switch
        {
            BpmnConversionDisposition.Converted => DiagnosticSeverity.Information,
            BpmnConversionDisposition.Error => DiagnosticSeverity.Error,
            _ => DiagnosticSeverity.Warning,
        }, message, source, context));
    }
    internal void Omit(string source, string property, string description) =>
        Add(BpmnConversionDisposition.Omitted, "BPMN_EXPORT_OMITTED", description, source, property);

    internal void Error(string code, string message, string? source = null) =>
        Add(BpmnConversionDisposition.Error, code, message, source);
    // Only reconstruction integrity is a conversion precondition. Model-quality rules
    // belong to explicit host validation and must never run during transfer.
    internal void CheckIntegrity(DocumentSnapshot document)
    {
        var reconstruction = DocumentReconstructor.Reconstruct(document, BpmnIntegrationPolicy.ConnectorAnchors);
        foreach (var diagnostic in reconstruction.Diagnostics)
            Add(diagnostic.Severity == DiagnosticSeverity.Error ? BpmnConversionDisposition.Error : BpmnConversionDisposition.Warning,
                diagnostic.Code, diagnostic.Message, diagnostic.SourceIdentity);
    }
    internal BpmnStandardConversionResult Result(DocumentSnapshot? document = null, string? xml = null) =>
        new(HasErrors ? null : document, HasErrors ? null : xml, [.. _diagnostics]) { ConversionLog = [.. _entries] };
}

/// <summary>One deterministic XML identity table, shared by semantics, references and DI.</summary>
internal sealed class BpmnXmlIds
{
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mapped = new(StringComparer.Ordinal);
    internal BpmnXmlIds(IEnumerable<string> nativeIds)
    {
        foreach (var id in nativeIds.Where(BpmnXmlVocabulary.IsXmlId).Order(StringComparer.Ordinal))
            if (_used.Add(id)) _mapped.Add("semantic:" + id, id);
    }
    internal string Semantic(string id) => Get("semantic:" + id, XmlConvert.EncodeLocalName(id));
    internal string Generated(string key) => Get("generated:" + key, "Bpmn_" + XmlConvert.EncodeLocalName(key));
    private string Get(string key, string candidate)
    {
        if (_mapped.TryGetValue(key, out var mapped)) return mapped;
        var unique = candidate;
        var suffix = 1;
        while (!_used.Add(unique)) unique = candidate + "_" + suffix++.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _mapped.Add(key, unique);
        return unique;
    }
}
