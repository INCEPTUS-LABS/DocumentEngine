using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Toolbox;

public sealed class ToolboxPlacementPreviewContractTests
{
    [Fact]
    public void PreviewRequestHasNoAllocatorAndDefensivelyOrdersVisibleTargets()
    {
        var document = Document();
        var targets = new List<ToolboxPlacementTarget> { Target("z"), Target("a") };
        var request = new ToolboxPlacementPreviewRequest(new("tool"), document, new(40d, 50d),
            visibleTargets: targets);
        targets.Clear();
        Assert.Equal(["a", "z"], request.VisibleTargets.Select(static target => target.VisualStateId.Value));
        Assert.Same(document, request.Document);
        Assert.Equal(document.SemanticModel.RootScopeId, request.TargetScopeId);
        Assert.DoesNotContain(typeof(ToolboxPlacementPreviewRequest).GetProperties(), property =>
            property.PropertyType.Name.Contains("IdentityProvider", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementPreviewRequest(new("tool"), document,
            default, visibleTargets: [Target("a"), Target("a")]));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementPreviewRequest(new("tool"), document,
            default, new DocumentScopeId("missing")));
    }

    [Fact]
    public void UnattachedAndRejectedPreviewsNeedNoFabricatedPersistentTarget()
    {
        var allowed = new ToolboxPlacementPreview(new("tool"), new("type"), new(10d, 20d, 20d, 30d), new(20d, 35d));
        Assert.Null(allowed.AttachmentCandidate);
        Assert.True(allowed.IsAllowed);
        var rejected = new ToolboxPlacementPreview(new("tool"), new("type"), new(-4d, 5d, 20d, 30d),
            new(6d, 20d), isAllowed: false, diagnostics: [Reason()]);
        Assert.False(rejected.IsAllowed);
        Assert.Equal(-4d, rejected.Bounds.Left);
        Assert.Null(rejected.AttachmentCandidate);
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementPreview(new("tool"), new("type"),
            new(0d, 0d, 20d, 30d), default, isAllowed: false));
        Assert.Throws<ArgumentException>(() => new ToolboxPlacementPreview(new("tool"), new("type"),
            new(0d, 0d, 20d, 30d), default, diagnostics: [Reason()]));
    }

    [Fact]
    public void PreviewAndFeedbackEqualityUseStructuralAttachmentAndDiagnosticValues()
    {
        var left = Preview(new ToolboxPlacementCandidate(Target("a"), "attachment",
            new(10d, 20d, 20d, 30d), [new("side", PropertyValue.FromInteger(2))]));
        var right = Preview(new ToolboxPlacementCandidate(Target("a"), "attachment",
            new(10d, 20d, 20d, 30d), [new("side", PropertyValue.FromInteger(2))]));
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        var feedback = new EditorFeedbackSnapshot("preview", new(200d, 400d, 20d, 30d), left);
        var equivalent = new EditorFeedbackSnapshot("preview", new(200d, 400d, 20d, 30d), right);
        Assert.Equal(feedback, equivalent);
        Assert.Equal(feedback.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(EditorFeedbackPresentationMode.ContributorOnly, feedback.PresentationMode);
        Assert.Same(left, feedback.PlacementPreview);
        Assert.NotEqual(feedback, new EditorFeedbackSnapshot("preview", new(201d, 400d, 20d, 30d), right));
        Assert.Throws<ArgumentException>(() => new EditorFeedbackSnapshot("preview", new(0d, 0d, 21d, 30d), left));
        Assert.Throws<ArgumentNullException>(() => new EditorFeedbackSnapshot("preview", (string)null!));
    }

    private static ToolboxPlacementPreview Preview(ToolboxPlacementCandidate? candidate = null) =>
        new(new("tool"), new("type"), new(10d, 20d, 20d, 30d), new(20d, 35d), "Prospective",
            attachmentCandidate: candidate, isAllowed: false, diagnostics: [Reason()]);

    private static Diagnostic Reason() => new("REJECTED", DiagnosticSeverity.Error, "Rejected here.",
        context: [new("reason", "test")]);

    private static ToolboxPlacementTarget Target(string id) => new(new("semantic:" + id), new("type"),
        new(id), new("projected:" + id), new(10d, 20d, 120d, 80d));

    private static DocumentSnapshot Document()
    {
        var id = new DocumentId("preview:document");
        return new(new SemanticModelSnapshot(id, DocumentRevision.Zero),
            new VisualModelSnapshot(id, DocumentRevision.Zero), new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
    }
}
