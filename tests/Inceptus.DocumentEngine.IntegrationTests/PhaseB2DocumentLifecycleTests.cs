using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.IntegrationTests.Fixtures;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseB2DocumentLifecycleTests
{
    [Fact]
    public void CreateMaterializesACompleteCoherentRevisionZeroDocument()
    {
        var fixture = new PluginNeutralDocumentFixture();
        var candidate = fixture.CreateSnapshot(DocumentRevision.Zero);

        var result = DocumentFactory.Create(candidate);

        var document = RequireSuccess(result);
        var captured = document.CaptureSnapshot();

        Assert.Equal(candidate, captured);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(3, captured.SemanticModel.ElementCount);
        Assert.Equal(2, captured.SemanticModel.RelationshipCount);
        Assert.Equal(3, captured.VisualModel.Count);
        Assert.Equal(
            captured.Revision,
            captured.SemanticModel.Revision);
        Assert.Equal(
            captured.Revision,
            captured.VisualModel.Revision);
        Assert.Equal(
            captured.Revision,
            captured.Metadata.Revision);
        Assert.Equal(
            captured.DocumentId,
            captured.SemanticModel.DocumentId);
        Assert.Equal(
            captured.DocumentId,
            captured.VisualModel.DocumentId);
        Assert.Equal(
            captured.DocumentId,
            captured.Metadata.DocumentId);

        Assert.Contains(
            captured.VisualModel.VisualStates,
            visualState => visualState.PlacementMode == VisualPlacementMode.Manual);
        Assert.Contains(
            captured.VisualModel.VisualStates,
            visualState => visualState.PlacementMode == VisualPlacementMode.Pinned);

        var routedVisual = Assert.Single(
            captured.VisualModel.VisualStates,
            visualState => !visualState.Route.IsEmpty);
        Assert.Equal(fixture.AlphaToBetaId, routedVisual.SemanticElementId);
        Assert.Equal(3, routedVisual.Route.Length);
        Assert.DoesNotContain(
            captured.VisualModel.VisualStates,
            visualState => visualState.SemanticElementId == fixture.BetaToGammaId);

        Assert.Equal(
            1L,
            captured.Metadata.SystemManagedProperties["test:format-version"].IntegerValue);
        Assert.True(
            captured.Metadata.ExtensionProperties["test:extension-setting"].BooleanValue);
    }

    [Fact]
    public void CallerCollectionMutationCannotChangeTheMaterializedDocument()
    {
        var fixture = new PluginNeutralDocumentFixture();
        var candidate = fixture.CreateSnapshot(DocumentRevision.Zero);
        var document = RequireSuccess(DocumentFactory.Create(candidate));
        var expected = document.CaptureSnapshot();

        fixture.MutateCallerOwnedCollections();

        var capturedAfterMutation = document.CaptureSnapshot();
        Assert.Same(expected, capturedAfterMutation);
        Assert.Equal(candidate, capturedAfterMutation);
        Assert.Equal(3, capturedAfterMutation.SemanticModel.ElementCount);
        Assert.Equal(2, capturedAfterMutation.SemanticModel.RelationshipCount);
        Assert.Equal(3, capturedAfterMutation.VisualModel.Count);
        Assert.Equal(
            3,
            Assert.Single(
                capturedAfterMutation.VisualModel.VisualStates,
                visualState => !visualState.Route.IsEmpty).Route.Length);
        Assert.Single(capturedAfterMutation.SemanticModel.Elements[0].Properties);
        Assert.Single(capturedAfterMutation.Metadata.SystemManagedProperties);
        Assert.Single(capturedAfterMutation.Metadata.ExtensionProperties);
    }

    [Fact]
    public void ReconstructionCreatesAnIndependentStructurallyEquivalentDocument()
    {
        var fixture = new PluginNeutralDocumentFixture();
        var createdDocument = RequireSuccess(
            DocumentFactory.Create(fixture.CreateSnapshot(DocumentRevision.Zero)));
        var originalSnapshot = createdDocument.CaptureSnapshot();

        var reconstructedDocument = RequireSuccess(
            DocumentReconstructor.Reconstruct(originalSnapshot));
        var reconstructedSnapshot = reconstructedDocument.CaptureSnapshot();

        Assert.NotSame(createdDocument, reconstructedDocument);
        Assert.NotSame(originalSnapshot, reconstructedSnapshot);
        Assert.NotSame(originalSnapshot.SemanticModel, reconstructedSnapshot.SemanticModel);
        Assert.NotSame(originalSnapshot.VisualModel, reconstructedSnapshot.VisualModel);
        Assert.NotSame(originalSnapshot.Metadata, reconstructedSnapshot.Metadata);
        Assert.NotSame(
            originalSnapshot.SemanticModel.Elements[0],
            reconstructedSnapshot.SemanticModel.Elements[0]);
        Assert.Equal(originalSnapshot, reconstructedSnapshot);
        Assert.Equal(originalSnapshot.GetHashCode(), reconstructedSnapshot.GetHashCode());
    }

    [Fact]
    public void ReconstructionPreservesANonzeroDocumentRevision()
    {
        var revision = new DocumentRevision(27);
        var candidate = new PluginNeutralDocumentFixture().CreateSnapshot(revision);

        var result = DocumentReconstructor.Reconstruct(candidate);

        var document = RequireSuccess(result);
        var captured = document.CaptureSnapshot();
        Assert.Equal(revision, document.Revision);
        Assert.Equal(candidate, captured);
        Assert.Equal(revision, captured.SemanticModel.Revision);
        Assert.Equal(revision, captured.VisualModel.Revision);
        Assert.Equal(revision, captured.Metadata.Revision);
    }

    private static Document RequireSuccess(DocumentConstructionResult result)
    {
        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Document);
        return result.Document!;
    }
}
