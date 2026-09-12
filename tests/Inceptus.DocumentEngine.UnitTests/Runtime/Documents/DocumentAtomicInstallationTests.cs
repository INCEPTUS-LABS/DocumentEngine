using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class DocumentAtomicInstallationTests
{
    private static readonly DocumentId TestDocumentId = new("test:atomic-installation");
    private static readonly SemanticElementId ElementId = new("test:element");
    private static readonly VisualStateId VisualId = new("test:visual");

    [Fact]
    public void StaleExpectedStateCannotReplaceAnUnrelatedInstalledState()
    {
        var document = CreateDocument();
        var staleExpected = document.CaptureState();
        var installedSnapshot = Snapshot(
            TestDocumentId,
            new DocumentRevision(1),
            new PointD(100d, 110d),
            "installed");
        var installedState = new DocumentState(installedSnapshot);
        var staleProposal = new DocumentState(Snapshot(
            TestDocumentId,
            new DocumentRevision(1),
            new PointD(500d, 510d),
            "stale-proposal"));

        Assert.True(document.TryInstallState(staleExpected, installedState));
        Assert.False(document.TryInstallState(staleExpected, staleProposal));

        Assert.Same(installedState, document.CaptureState());
        Assert.Same(installedSnapshot, document.CaptureSnapshot());
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(
            new PointD(100d, 110d),
            Assert.Single(document.CaptureSnapshot().VisualModel.VisualStates).Position);
        Assert.Equal(
            "installed",
            document.CaptureSnapshot().Metadata.ExtensionProperties["test:state"].TextValue);
        AssertCoherent(document.CaptureSnapshot());
    }

    [Fact]
    public void ProposedStateForDifferentDocumentIsRejectedWithoutChangingCurrentState()
    {
        var document = CreateDocument();
        var expected = document.CaptureState();
        var proposed = new DocumentState(Snapshot(
            new DocumentId("test:different-document"),
            new DocumentRevision(1),
            new PointD(100d, 110d),
            "different-document"));

        Assert.Throws<ArgumentException>(() => document.TryInstallState(expected, proposed));

        Assert.Same(expected, document.CaptureState());
        Assert.Same(expected.Snapshot, document.CaptureSnapshot());
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        AssertCoherent(document.CaptureSnapshot());
    }

    [Fact]
    public void ProposedStateMustAdvanceExpectedRevisionExactlyOnce()
    {
        var document = CreateDocument();
        var expected = document.CaptureState();
        var unchangedRevision = new DocumentState(Snapshot(
            TestDocumentId,
            DocumentRevision.Zero,
            new PointD(100d, 110d),
            "same-revision"));
        var skippedRevision = new DocumentState(Snapshot(
            TestDocumentId,
            new DocumentRevision(2),
            new PointD(200d, 210d),
            "skipped-revision"));

        Assert.Throws<ArgumentException>(() =>
            document.TryInstallState(expected, unchangedRevision));
        Assert.Throws<ArgumentException>(() =>
            document.TryInstallState(expected, skippedRevision));

        Assert.Same(expected, document.CaptureState());
        Assert.Same(expected.Snapshot, document.CaptureSnapshot());
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        AssertCoherent(document.CaptureSnapshot());
    }

    private static Document CreateDocument() =>
        Assert.IsType<Document>(
            DocumentFactory.Create(Snapshot(
                TestDocumentId,
                DocumentRevision.Zero,
                new PointD(10d, 20d),
                "initial")).Document);

    private static DocumentSnapshot Snapshot(
        DocumentId documentId,
        DocumentRevision revision,
        PointD position,
        string stateValue)
    {
        var semantic = new SemanticModelSnapshot(
            documentId,
            revision,
            [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type"))]);
        var visual = new VisualModelSnapshot(
            documentId,
            revision,
            [
                new VisualStateSnapshot(
                    VisualId,
                    ElementId,
                    position,
                    new SizeD(30d, 40d),
                    VisualPlacementMode.Manual),
            ]);
        var metadata = new DocumentMetadataSnapshot(
            documentId,
            revision,
            extensionProperties:
            [
                new KeyValuePair<string, PropertyValue>(
                    "test:state",
                    PropertyValue.FromText(stateValue)),
            ]);

        return new DocumentSnapshot(semantic, visual, metadata);
    }

    private static void AssertCoherent(DocumentSnapshot snapshot)
    {
        Assert.Equal(snapshot.DocumentId, snapshot.SemanticModel.DocumentId);
        Assert.Equal(snapshot.DocumentId, snapshot.VisualModel.DocumentId);
        Assert.Equal(snapshot.DocumentId, snapshot.Metadata.DocumentId);
        Assert.Equal(snapshot.Revision, snapshot.SemanticModel.Revision);
        Assert.Equal(snapshot.Revision, snapshot.VisualModel.Revision);
        Assert.Equal(snapshot.Revision, snapshot.Metadata.Revision);
    }
}
