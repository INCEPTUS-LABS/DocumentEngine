using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Documents;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.Bpmn.Integration;

/// <summary>
/// Generates the existing static ZIP from a document and its immutable presentation data.
/// The presentation carries active-scope and visibility choices which are intentionally not
/// persisted in the document. No editor instance, browser or publishing workflow is required.
/// </summary>
public sealed class PublishingPackageGenerator
{
    private readonly PublishedProcessPackageBuilder _builder;

    public PublishingPackageGenerator() : this(new PublishedProcessPackageBuilder(
        new BpmnPublishedTokenRoleClassifier(), new BpmnPublishedNodeDataMapper()))
    { }

    public PublishingPackageGenerator(PublishedProcessPackageBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    public BpmnPublishingPackageResult Generate(DocumentSnapshot document, EditingSessionPresentationCapture presentation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(presentation);
        if (!document.Equals(presentation.Document))
            throw new ArgumentException("The presentation must describe the exact supplied document.", nameof(presentation));
        var result = _builder.Build(presentation);
        var diagnostics = result.Diagnostics.Select(diagnostic =>
        {
            if (!diagnostic.Context.TryGetValue("ElementId", out var elementId) ||
                string.IsNullOrWhiteSpace(elementId) ||
                !document.SemanticModel.TryGetElement(new SemanticElementId(elementId), out var element))
                return diagnostic;
            var context = diagnostic.Context.SetItem("ElementId", element!.Id.Value)
                .SetItem("ElementType", element.TypeId.Value);
            if (element.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) && name.Kind == PropertyValueKind.Text)
                context = context.SetItem("ElementName", name.TextValue);
            return new Diagnostic(diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.SourceIdentity, context);
        }).ToImmutableArray();
        return new(result.Package, diagnostics);
    }
}

public sealed record BpmnPublishingPackageResult(PublishedProcessPackage? Package, ImmutableArray<Diagnostic> Diagnostics)
{
    public bool Succeeded => Package is not null;
}
