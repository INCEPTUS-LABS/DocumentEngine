using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

// The approved correction replaces numeric dimension authoring with Canvas boundaries.
public sealed class DocumentCanvasSpatialHeightPropertiesTests
{
    [Fact]
    public void PoolPropertiesRemainSemanticOnly()
    {
        Assert.Equal(["name", "description"],
            OrganizationalPoolPropertiesSchema.Definition.Fields.Select(field => field.FieldId.Value));
    }

    [Fact]
    public void ModelViewDraftHasNoDimensionAuthority()
    {
        var snapshot = new DocumentCanvasModelViewPropertiesSnapshot(
            new DocumentId("test:model-view"), new DocumentRevision(4), new DocumentScopeId("test:scope"), []);
        var draft = new DocumentCanvasModelViewPropertiesDraft(snapshot);
        Assert.False(draft.IsDirty);
        Assert.Empty(draft.CreateAvailabilityChanges());
        var members = typeof(DocumentCanvasModelViewPropertiesDraft)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.DoesNotContain(members, member => member.Name.Contains("Height", StringComparison.Ordinal) ||
            member.Name.Contains("Width", StringComparison.Ordinal) || member.Name.Contains("Dimension", StringComparison.Ordinal));
    }
}
