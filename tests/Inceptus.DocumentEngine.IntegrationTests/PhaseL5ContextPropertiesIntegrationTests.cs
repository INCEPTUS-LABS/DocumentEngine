using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseL5ContextPropertiesIntegrationTests
{
    private static readonly ElementPropertyFieldId NameFieldId =
        NeutralDemoPropertiesSchemas.NameFieldId;
    private static readonly ElementPropertyFieldId ElementNumberFieldId =
        NeutralDemoPropertiesSchemas.ElementNumberFieldId;
    private static readonly ElementPropertyFieldId DescriptionFieldId =
        NeutralDemoPropertiesSchemas.DescriptionFieldId;
    private static readonly VisualStateId AlphaId = new("demo:visual:alpha");
    private static readonly VisualStateId BetaId = new("demo:visual:beta");
    private static readonly VisualStateId GammaId = new("demo:visual:gamma");
    private static readonly VisualStateId AlphaBetaConnectorId =
        new("demo:visual:alpha-beta");
    private static readonly VisualStateId BetaGammaConnectorId =
        new("demo:visual:beta-gamma");

    [Fact]
    public async Task ContextMenuUsesCanonicalHitSelectionRulesAndNormalizesHelpersToOwner()
    {
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        var initialDocument = CaptureDocument(session);
        var initialRevision = session.CaptureState().DocumentRevision;
        var initialHistory = session.CaptureState().HistoryStatus;

        var scene = CurrentScene(session);
        var betaBody = MovableContent(scene, BetaId);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            scene,
            new PointD(betaBody.Bounds.Left + 20d, betaBody.Bounds.Top + 18d));

        AssertSelection(session, BetaId);
        Assert.Equal(BetaId, harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);

        scene = CurrentScene(session);
        var gammaLabel = Label(scene, GammaId);
        await harness.Pointer.ContextMenuDocumentPointAsync(scene, Center(gammaLabel.Bounds));

        AssertSelection(session, GammaId);
        Assert.Equal(GammaId, harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);

        await SetSelectionAsync(session, AlphaId, BetaId);
        scene = CurrentScene(session);
        var betaLabel = Label(scene, BetaId);
        await harness.Pointer.ContextMenuDocumentPointAsync(scene, Center(betaLabel.Bounds));

        AssertSelection(session, AlphaId, BetaId);
        Assert.Equal(BetaId, harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        var multiSelectionProperties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BetaId));
        Assert.Equal(BetaId, multiSelectionProperties.VisualStateId);
        Assert.True(harness.Host.CaptureState().PropertiesFormOpen);
        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);

        scene = CurrentScene(session);
        await harness.Pointer.ContextMenuDocumentPointAsync(scene, new PointD(-500d, -500d));

        AssertSelection(session, AlphaId, BetaId);
        Assert.Null(harness.Host.CaptureState().ContextMenu);

        await SetSelectionAsync(session, GammaId);
        scene = CurrentScene(session);
        var northEdge = ResizeInteraction(scene, GammaId, "north");
        await harness.Pointer.ContextMenuDocumentPointAsync(scene, Center(northEdge.Bounds));

        AssertSelection(session, GammaId);
        Assert.Equal(GammaId, harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);

        scene = CurrentScene(session);
        var northWest = ResizeInteraction(scene, GammaId, "northwest");
        await harness.Pointer.ContextMenuDocumentPointAsync(scene, Center(northWest.Bounds));

        AssertSelection(session, GammaId);
        Assert.Equal(GammaId, harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        Assert.Equal(initialRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(initialHistory, session.CaptureState().HistoryStatus);
        Assert.Same(initialDocument, CaptureDocument(session));
    }

    [Fact]
    public async Task DraftCancelAndNoOpApplyPerformNoPersistentWorkAndConnectorIsReadOnly()
    {
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        var beforeDocument = CaptureDocument(session);
        var beforeState = session.CaptureState();
        var beforeCounters = harness.CaptureCounters();
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaId));
        var draft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            X = "123.5",
            Y = "234.5",
        };

        harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            AlphaId,
            isDirty: draft.IsDirty);

        Assert.True(draft.IsDirty);
        Assert.Equal(beforeState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeCounters, harness.CaptureCounters());
        Assert.Same(beforeDocument, CaptureDocument(session));

        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);

        Assert.False(harness.Host.CaptureState().PropertiesFormOpen);
        AssertSelection(session, AlphaId);
        Assert.Same(beforeDocument, CaptureDocument(session));
        Assert.Equal(beforeCounters, harness.CaptureCounters());

        authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaId));
        var noChangeDraft = new DocumentCanvasPropertiesDraft(authoritative);
        var noChange = await harness.Host.ApplyPropertiesAsync(noChangeDraft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.NoChange, noChange.Status);
        Assert.True(noChange.Succeeded);
        AssertPropertySnapshotEqual(
            authoritative,
            Assert.IsType<DocumentCanvasPropertySnapshot>(noChange.Authoritative));
        Assert.True(harness.Host.CaptureState().PropertiesFormOpen);
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);
        Assert.Equal(beforeState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeCounters, harness.CaptureCounters());
        Assert.Same(beforeDocument, CaptureDocument(session));

        var document = CaptureDocument(session);
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(
            document,
            BetaGammaConnectorId,
            NeutralDemoPropertiesSchemas.Catalog,
            out var connectorSnapshot));
        var connector = Assert.IsType<DocumentCanvasPropertySnapshot>(connectorSnapshot);
        var connectorDraft = new DocumentCanvasPropertiesDraft(connector)
        {
            X = "999",
            Width = "999",
        };

        Assert.True(connector.IsConnector);
        Assert.False(connector.CanEditBounds);
        Assert.Equal(new SemanticElementId("demo:beta"), connector.SourceId);
        Assert.Equal(new SemanticElementId("demo:gamma"), connector.TargetId);
        Assert.False(connectorDraft.IsDirty);
        Assert.True(connectorDraft.TryParseBounds(out _, out var messages));
        Assert.Empty(messages);
    }

    [Fact]
    public async Task MoveAndFullBoundsApplyAreEachOneAtomicHistoryOperationAndUndoRedoRoundTrip()
    {
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaId));
        var moveDraft = new DocumentCanvasPropertiesDraft(original)
        {
            X = DocumentCanvasPropertiesDraft.Format(original.Bounds.X + 35d),
            Y = DocumentCanvasPropertiesDraft.Format(original.Bounds.Y + 22d),
        };
        harness.Host.UpdatePropertiesFormState(isOpen: true, AlphaId, isDirty: true);
        var beforeMove = session.CaptureState();
        var beforeMoveCounters = harness.CaptureCounters();
        var beforeMoveEvents = harness.Events.Events.Count;

        var move = await harness.Host.ApplyPropertiesAsync(moveDraft);
        var movedState = session.CaptureState();
        var moved = Assert.IsType<DocumentCanvasPropertySnapshot>(move.Authoritative);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, move.Status);
        Assert.True(move.Succeeded);
        Assert.Equal(original.Bounds.Width, moved.Bounds.Width);
        Assert.Equal(original.Bounds.Height, moved.Bounds.Height);
        Assert.Equal(original.Bounds.X + 35d, moved.Bounds.X);
        Assert.Equal(original.Bounds.Y + 22d, moved.Bounds.Y);
        Assert.Equal(VisualPlacementMode.Pinned, moved.PlacementMode);
        Assert.Equal(beforeMove.DocumentRevision.Increment(), movedState.DocumentRevision);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), movedState.HistoryStatus);
        AssertSelection(session, AlphaId);
        Assert.True(harness.Host.CaptureState().PropertiesFormOpen);
        Assert.Equal(AlphaId, harness.Host.CaptureState().PropertiesTargetVisualStateId);
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);
        AssertOnePipelineWithoutLayout(beforeMoveCounters, harness.CaptureCounters());
        await harness.WaitForEventCountAsync(beforeMoveEvents + 1);
        Assert.Equal(movedState.DocumentRevision,
            harness.Events.Events.Last().CommittedRevision);

        await harness.Host.UndoAsync();
        var undone = CaptureProperties(session, AlphaId);

        Assert.Equal(original.Bounds, undone.Bounds);
        Assert.Equal(new HistoryStatus(1, canUndo: false, canRedo: true),
            session.CaptureState().HistoryStatus);
        AssertSelection(session, AlphaId);
        Assert.True(harness.Host.CaptureState().PropertiesFormOpen);

        await harness.Host.RedoAsync();
        var redone = CaptureProperties(session, AlphaId);

        Assert.Equal(moved.Bounds, redone.Bounds);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
        AssertSelection(session, AlphaId);

        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);
        var beforeResize = session.CaptureState();
        var beforeResizeCounters = harness.CaptureCounters();
        var beforeResizeEvents = harness.Events.Events.Count;
        var resizeSource = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaId));
        var resizeDraft = new DocumentCanvasPropertiesDraft(resizeSource)
        {
            X = DocumentCanvasPropertiesDraft.Format(resizeSource.Bounds.X - 12d),
            Y = DocumentCanvasPropertiesDraft.Format(resizeSource.Bounds.Y - 9d),
            Width = DocumentCanvasPropertiesDraft.Format(resizeSource.Bounds.Width + 44d),
            Height = DocumentCanvasPropertiesDraft.Format(resizeSource.Bounds.Height + 31d),
        };
        harness.Host.UpdatePropertiesFormState(isOpen: true, AlphaId, isDirty: true);

        var resize = await harness.Host.ApplyPropertiesAsync(resizeDraft);
        var resizedState = session.CaptureState();
        var resized = Assert.IsType<DocumentCanvasPropertySnapshot>(resize.Authoritative);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, resize.Status);
        Assert.Equal(new RectD(
            resizeSource.Bounds.X - 12d,
            resizeSource.Bounds.Y - 9d,
            resizeSource.Bounds.Width + 44d,
            resizeSource.Bounds.Height + 31d), resized.Bounds);
        Assert.Equal(beforeResize.DocumentRevision.Increment(), resizedState.DocumentRevision);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false),
            resizedState.HistoryStatus);
        AssertOnePipelineWithoutLayout(beforeResizeCounters, harness.CaptureCounters());
        await harness.WaitForEventCountAsync(beforeResizeEvents + 1);
        Assert.Equal(resizedState.DocumentRevision,
            harness.Events.Events.Last().CommittedRevision);

        await harness.Host.UndoAsync();
        Assert.Equal(resizeSource.Bounds, CaptureProperties(session, AlphaId).Bounds);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: true),
            session.CaptureState().HistoryStatus);

        await harness.Host.RedoAsync();
        Assert.Equal(resized.Bounds, CaptureProperties(session, AlphaId).Bounds);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task NegativeTypedBoundsAreRejectedWithoutClampingOrHistory()
    {
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaId));
        var draft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            X = "-20",
            Y = "-10",
        };
        harness.Host.UpdatePropertiesFormState(isOpen: true, AlphaId, isDirty: true);
        var before = session.CaptureState();
        var beforeDocument = CaptureDocument(session);
        var beforeCounters = harness.CaptureCounters();
        var beforeEvents = harness.Events.Events.Count;

        var result = await harness.Host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Failed, result.Status);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid);
        Assert.Equal(authoritative.Bounds, result.Authoritative?.Bounds);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeCounters, harness.CaptureCounters());
        Assert.Equal(beforeEvents, harness.Events.Events.Count);
        Assert.Same(beforeDocument, CaptureDocument(session));
        Assert.True(harness.Host.CaptureState().PropertiesFormDirty);
    }

    [Fact]
    public async Task SemanticNameDraftCommitsOnceWrapsLabelAndUndoRedoRefreshAuthoritativeInspector()
    {
        const string longName = "Beta sales order verification process";
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        await SetSelectionAsync(session, BetaId);
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BetaId));
        var draft = new DocumentCanvasPropertiesDraft(original);
        var beforeTypingDocument = CaptureDocument(session);
        var beforeTypingState = session.CaptureState();
        var beforeTypingCounters = harness.CaptureCounters();
        var beforeTypingEvents = harness.Events.Events.Count;

        DraftField(draft, NameFieldId).EditorValue = longName;
        harness.Host.UpdatePropertiesFormState(isOpen: true, BetaId, isDirty: draft.IsDirty);

        Assert.True(DraftField(draft, NameFieldId).IsDirty);
        Assert.False(draft.IsBoundsDirty);
        Assert.Same(beforeTypingDocument, CaptureDocument(session));
        Assert.Equal(beforeTypingState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeTypingState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeTypingCounters, harness.CaptureCounters());
        Assert.Equal(beforeTypingEvents, harness.Events.Events.Count);

        var committed = await harness.Host.ApplyPropertiesAsync(draft);
        var committedState = session.CaptureState();
        var committedDocument = CaptureDocument(session);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, committed.Status);
        Assert.Equal(
            longName,
            TextFieldValue(
                Assert.IsType<DocumentCanvasPropertySnapshot>(committed.Authoritative),
                NameFieldId));
        Assert.Equal(original.Bounds, committed.Authoritative?.Bounds);
        Assert.Equal(beforeTypingState.DocumentRevision.Increment(), committedState.DocumentRevision);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false),
            committedState.HistoryStatus);
        await harness.WaitForEventCountAsync(beforeTypingEvents + 1);
        Assert.Equal(committedState.DocumentRevision,
            harness.Events.Events.Last().CommittedRevision);
        AssertOneFullPipeline(beforeTypingCounters, harness.CaptureCounters());
        Assert.Equal(
            beforeTypingDocument.VisualModel.VisualStates.AsEnumerable(),
            committedDocument.VisualModel.VisualStates.AsEnumerable());
        AssertSelection(session, BetaId);
        var committedScene = CurrentScene(session);
        var committedNode = MovableContent(committedScene, BetaId);
        var committedLines = LabelLines(committedScene, BetaId);
        Assert.True(committedLines.Length > 1);
        AssertWrappedText(longName, committedLines);
        AssertCenteredBlock(committedNode.Bounds, committedLines);
        Assert.All(committedLines, line =>
        {
            Assert.Equal(new SemanticElementId("demo:beta"), line.Origin.SemanticElementId);
            Assert.Equal(BetaId, line.Origin.VisualStateId);
            Assert.NotNull(line.Origin.ProjectedObjectId);
        });
        Assert.True(harness.Host.CaptureState().PropertiesFormOpen);
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);

        var beforeUndoCounters = harness.CaptureCounters();
        var beforeUndoEvents = harness.Events.Events.Count;
        await harness.Host.UndoAsync();
        var undone = CaptureProperties(session, BetaId);

        Assert.Equal("Beta", TextFieldValue(undone, NameFieldId));
        Assert.Equal(original.Bounds, undone.Bounds);
        var undoneLines = LabelLines(CurrentScene(session), BetaId);
        Assert.Equal("Beta", Assert.Single(undoneLines).Geometry.Content);
        AssertSelection(session, BetaId);
        Assert.Equal(new HistoryStatus(1, canUndo: false, canRedo: true),
            session.CaptureState().HistoryStatus);
        AssertOneFullPipeline(beforeUndoCounters, harness.CaptureCounters());
        await harness.WaitForEventCountAsync(beforeUndoEvents + 1);

        var beforeRedoCounters = harness.CaptureCounters();
        var beforeRedoEvents = harness.Events.Events.Count;
        await harness.Host.RedoAsync();
        var redone = CaptureProperties(session, BetaId);

        Assert.Equal(longName, TextFieldValue(redone, NameFieldId));
        Assert.Equal(original.Bounds, redone.Bounds);
        var redoneScene = CurrentScene(session);
        var redoneLines = LabelLines(redoneScene, BetaId);
        Assert.Equal(
            committedLines.Select(static line => line.Geometry.Content),
            redoneLines.Select(static line => line.Geometry.Content));
        AssertCenteredBlock(MovableContent(redoneScene, BetaId).Bounds, redoneLines);
        AssertSelection(session, BetaId);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
        AssertOneFullPipeline(beforeRedoCounters, harness.CaptureCounters());
        await harness.WaitForEventCountAsync(beforeRedoEvents + 1);

        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);
        var lineAnchors = redoneLines.Select(DocumentTextAnchor).ToArray();
        for (var index = 0; index < lineAnchors.Length; index++)
        {
            await SetSelectionAsync(session, GammaId);
            var scene = CurrentScene(session);
            await harness.Pointer.ClickDocumentPointAsync(
                scene,
                lineAnchors[index],
                pointerId: 100 + index);
            AssertSelection(session, BetaId);

            await SetSelectionAsync(session, GammaId);
            scene = CurrentScene(session);
            await harness.Pointer.ContextMenuDocumentPointAsync(scene, lineAnchors[index]);
            AssertSelection(session, BetaId);
            Assert.Equal(BetaId, harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        }
    }

    [Fact]
    public async Task DataFieldsStartTypedRemainTransientWhileEditingAndCommitSeparately()
    {
        const string renamed = "Alpha customer approval";
        const long changedElementNumber = 314L;
        const string multilineDescription =
            "First approval line\nSecond approval line\r\nThird approval line";
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaId));

        Assert.Equal("Alpha", TextFieldValue(original, NameFieldId));
        Assert.Equal(10L, IntegerFieldValue(original, ElementNumberFieldId));
        Assert.Equal(
            "Initial Alpha description",
            TextFieldValue(original, DescriptionFieldId));
        Assert.True(DataField(original, NameFieldId).CanEdit);
        Assert.True(DataField(original, ElementNumberFieldId).CanEdit);
        Assert.True(DataField(original, DescriptionFieldId).CanEdit);
        AssertSemanticData(
            CaptureDocument(session),
            original.SemanticId,
            "Alpha",
            10L,
            "Initial Alpha description");

        var nameDraft = CreateDataDraft(original, NameFieldId, renamed);
        var beforeTypingDocument = CaptureDocument(session);
        var beforeTypingState = session.CaptureState();
        var beforeTypingCounters = harness.CaptureCounters();
        var beforeTypingEvents = harness.Events.Events.Count;

        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            AlphaId,
            isDirty: nameDraft.IsDirty));

        Assert.True(DraftField(nameDraft, NameFieldId).IsDirty);
        Assert.False(DraftField(nameDraft, ElementNumberFieldId).IsDirty);
        Assert.False(DraftField(nameDraft, DescriptionFieldId).IsDirty);
        Assert.Equal(beforeTypingState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeTypingState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeTypingCounters, harness.CaptureCounters());
        Assert.Equal(beforeTypingEvents, harness.Events.Events.Count);
        Assert.Same(beforeTypingDocument, CaptureDocument(session));

        var renamedSnapshot = await ApplyDataAndAssertAsync(
            harness,
            nameDraft,
            renamed,
            10L,
            "Initial Alpha description",
            UpdateSemanticElementNameCommand.KnownTypeId,
            AlphaId);
        var elementNumberDraft = new DocumentCanvasPropertiesDraft(renamedSnapshot);
        var beforeNumberTypingDocument = CaptureDocument(session);
        var beforeNumberTypingState = session.CaptureState();
        var beforeNumberTypingCounters = harness.CaptureCounters();
        var beforeNumberTypingEvents = harness.Events.Events.Count;
        DraftField(elementNumberDraft, ElementNumberFieldId).EditorValue =
            changedElementNumber.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            AlphaId,
            isDirty: elementNumberDraft.IsDirty));
        Assert.True(DraftField(elementNumberDraft, ElementNumberFieldId).IsDirty);
        Assert.Equal(beforeNumberTypingState.DocumentRevision,
            session.CaptureState().DocumentRevision);
        Assert.Equal(beforeNumberTypingState.HistoryStatus,
            session.CaptureState().HistoryStatus);
        Assert.Equal(beforeNumberTypingCounters, harness.CaptureCounters());
        Assert.Equal(beforeNumberTypingEvents, harness.Events.Events.Count);
        Assert.Same(beforeNumberTypingDocument, CaptureDocument(session));
        var renumberedSnapshot = await ApplyDataAndAssertAsync(
            harness,
            elementNumberDraft,
            renamed,
            changedElementNumber,
            "Initial Alpha description",
            UpdateSemanticElementPropertyCommand.KnownTypeId,
            AlphaId);
        var descriptionDraft = new DocumentCanvasPropertiesDraft(renumberedSnapshot);
        var beforeDescriptionTypingDocument = CaptureDocument(session);
        var beforeDescriptionTypingState = session.CaptureState();
        var beforeDescriptionTypingCounters = harness.CaptureCounters();
        var beforeDescriptionTypingEvents = harness.Events.Events.Count;
        DraftField(descriptionDraft, DescriptionFieldId).EditorValue = multilineDescription;
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            AlphaId,
            isDirty: descriptionDraft.IsDirty));
        Assert.True(DraftField(descriptionDraft, DescriptionFieldId).IsDirty);
        Assert.Equal(beforeDescriptionTypingState.DocumentRevision,
            session.CaptureState().DocumentRevision);
        Assert.Equal(beforeDescriptionTypingState.HistoryStatus,
            session.CaptureState().HistoryStatus);
        Assert.Equal(beforeDescriptionTypingCounters, harness.CaptureCounters());
        Assert.Equal(beforeDescriptionTypingEvents, harness.Events.Events.Count);
        Assert.Same(beforeDescriptionTypingDocument, CaptureDocument(session));
        var describedSnapshot = await ApplyDataAndAssertAsync(
            harness,
            descriptionDraft,
            renamed,
            changedElementNumber,
            multilineDescription,
            UpdateSemanticElementPropertyCommand.KnownTypeId,
            AlphaId);

        Assert.Equal(
            multilineDescription,
            TextFieldValue(describedSnapshot, DescriptionFieldId));
        Assert.Equal(3, session.CaptureState().HistoryStatus.EntryCount);
        AssertWrappedText(renamed, LabelLines(CurrentScene(session), AlphaId));
    }

    [Fact]
    public async Task InvalidElementNumberAndExactDataNoOpPerformNoPersistentOrRuntimeWork()
    {
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaId));
        var invalid = CreateDataDraft(
            authoritative,
            ElementNumberFieldId,
            "9223372036854775808");
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            AlphaId,
            isDirty: invalid.IsDirty));
        var beforeInvalidState = session.CaptureState();
        var beforeInvalidDocument = CaptureDocument(session);
        var beforeInvalidCounters = harness.CaptureCounters();
        var beforeInvalidEvents = harness.Events.Events.Count;

        var invalidResult = await harness.Host.ApplyPropertiesAsync(invalid);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.ValidationFailed, invalidResult.Status);
        Assert.Contains("Element number", invalidResult.Message, StringComparison.Ordinal);
        Assert.Equal(beforeInvalidState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeInvalidState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeInvalidCounters, harness.CaptureCounters());
        Assert.Equal(beforeInvalidEvents, harness.Events.Events.Count);
        Assert.Same(beforeInvalidDocument, CaptureDocument(session));
        Assert.True(harness.Host.CaptureState().PropertiesFormDirty);

        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);
        authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaId));
        var exactNoOp = new DocumentCanvasPropertiesDraft(authoritative);
        var beforeNoOpState = session.CaptureState();
        var beforeNoOpDocument = CaptureDocument(session);
        var beforeNoOpCounters = harness.CaptureCounters();
        var beforeNoOpEvents = harness.Events.Events.Count;

        var noOpResult = await harness.Host.ApplyPropertiesAsync(exactNoOp);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.NoChange, noOpResult.Status);
        AssertPropertySnapshotEqual(
            authoritative,
            Assert.IsType<DocumentCanvasPropertySnapshot>(noOpResult.Authoritative));
        Assert.Equal(beforeNoOpState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeNoOpState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeNoOpCounters, harness.CaptureCounters());
        Assert.Equal(beforeNoOpEvents, harness.Events.Events.Count);
        Assert.Same(beforeNoOpDocument, CaptureDocument(session));
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);
    }

    [Fact]
    public async Task MultiSelectionTargetDataAndVisualHistoryRoundTripWithoutChangingSelection()
    {
        const string renamed = "Beta target only";
        const long changedElementNumber = 2026L;
        const string multilineDescription = "Beta first line\nBeta second line";
        VisualStateId[] selection = [AlphaId, BetaId, GammaId];
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        await SetSelectionAsync(session, selection);
        var alphaOriginal = CaptureProperties(session, AlphaId);
        var betaOriginal = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BetaId));
        var gammaOriginal = CaptureProperties(session, GammaId);

        var renamedSnapshot = await ApplyDataAndAssertAsync(
            harness,
            CreateDataDraft(betaOriginal, NameFieldId, renamed),
            renamed,
            IntegerFieldValue(betaOriginal, ElementNumberFieldId),
            TextFieldValue(betaOriginal, DescriptionFieldId),
            UpdateSemanticElementNameCommand.KnownTypeId,
            selection);
        var renumberedSnapshot = await ApplyDataAndAssertAsync(
            harness,
            CreateDataDraft(
                renamedSnapshot,
                ElementNumberFieldId,
                changedElementNumber.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            renamed,
            changedElementNumber,
            TextFieldValue(betaOriginal, DescriptionFieldId),
            UpdateSemanticElementPropertyCommand.KnownTypeId,
            selection);
        var describedSnapshot = await ApplyDataAndAssertAsync(
            harness,
            CreateDataDraft(renumberedSnapshot, DescriptionFieldId, multilineDescription),
            renamed,
            changedElementNumber,
            multilineDescription,
            UpdateSemanticElementPropertyCommand.KnownTypeId,
            selection);

        var beforeMoveDocument = CaptureDocument(session);
        var beforeMoveState = session.CaptureState();
        var beforeMoveCounters = harness.CaptureCounters();
        var beforeMoveEvents = harness.Events.Events.Count;
        var movedBounds = new RectD(
            betaOriginal.Bounds.X + 27d,
            betaOriginal.Bounds.Y + 19d,
            betaOriginal.Bounds.Width,
            betaOriginal.Bounds.Height);
        var moveDraft = new DocumentCanvasPropertiesDraft(describedSnapshot)
        {
            X = DocumentCanvasPropertiesDraft.Format(movedBounds.X),
            Y = DocumentCanvasPropertiesDraft.Format(movedBounds.Y),
        };
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BetaId,
            isDirty: moveDraft.IsDirty));

        var movedResult = await harness.Host.ApplyPropertiesAsync(moveDraft);
        await harness.WaitForEventCountAsync(beforeMoveEvents + 1);
        var movedState = session.CaptureState();

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, movedResult.Status);
        Assert.Equal(movedBounds, movedResult.Authoritative?.Bounds);
        Assert.Equal(beforeMoveState.DocumentRevision.Increment(), movedState.DocumentRevision);
        Assert.Equal(beforeMoveState.HistoryStatus.EntryCount + 1,
            movedState.HistoryStatus.EntryCount);
        Assert.Equal(MoveVisualStateCommand.KnownTypeId,
            harness.Events.Events.Last().CommandTypeId);
        AssertOnePipelineWithoutLayout(beforeMoveCounters, harness.CaptureCounters());
        AssertSelection(session, selection);
        AssertDataAndBounds(alphaOriginal, CaptureProperties(session, AlphaId));
        AssertDataAndBounds(gammaOriginal, CaptureProperties(session, GammaId));
        var beforeVisuals = beforeMoveDocument.VisualModel.VisualStates.ToDictionary(
            static visual => visual.Id);
        var afterVisuals = CaptureDocument(session).VisualModel.VisualStates.ToDictionary(
            static visual => visual.Id);
        Assert.Equal(beforeVisuals.Count, afterVisuals.Count);
        foreach (var (visualStateId, beforeVisual) in beforeVisuals)
        {
            var afterVisual = afterVisuals[visualStateId];
            if (visualStateId == BetaId)
            {
                Assert.Equal(movedBounds.TopLeft, afterVisual.Position);
                Assert.Equal(beforeVisual.Size, afterVisual.Size);
                Assert.Equal(beforeVisual.Properties, afterVisual.Properties);
            }
            else
            {
                Assert.Equal(beforeVisual, afterVisual);
            }
        }

        var expectedUndo = new[]
        {
            describedSnapshot with { Bounds = betaOriginal.Bounds },
            renumberedSnapshot with { Bounds = betaOriginal.Bounds },
            renamedSnapshot with { Bounds = betaOriginal.Bounds },
            betaOriginal,
        };
        for (var index = 0; index < expectedUndo.Length; index++)
        {
            var actual = index == 0
                ? await UndoAndAssertPreservingNodeLayoutAsync(harness, selection)
                : await UndoAndAssertAsync(harness, selection);
            AssertDataAndBounds(expectedUndo[index], actual);
        }

        var expectedRedo = new[]
        {
            renamedSnapshot with { Bounds = betaOriginal.Bounds },
            renumberedSnapshot with { Bounds = betaOriginal.Bounds },
            describedSnapshot with { Bounds = betaOriginal.Bounds },
            describedSnapshot with
            {
                Bounds = movedBounds,
                PlacementMode = VisualPlacementMode.Pinned,
            },
        };
        for (var index = 0; index < expectedRedo.Length; index++)
        {
            var actual = index == expectedRedo.Length - 1
                ? await RedoAndAssertPreservingNodeLayoutAsync(harness, selection)
                : await RedoAndAssertAsync(harness, selection);
            AssertDataAndBounds(expectedRedo[index], actual);
        }

        var rejectedDraft = CreateDataDraft(
            CaptureProperties(session, BetaId),
            DescriptionFieldId,
            "Must not commit after Beta leaves selection");
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BetaId,
            isDirty: true));
        await SetSelectionAsync(session, AlphaId, GammaId);
        var beforeRejectedState = session.CaptureState();
        var beforeRejectedDocument = CaptureDocument(session);
        var beforeRejectedCounters = harness.CaptureCounters();
        var beforeRejectedEvents = harness.Events.Events.Count;

        var rejected = await harness.Host.ApplyPropertiesAsync(rejectedDraft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Unavailable, rejected.Status);
        Assert.Equal(beforeRejectedState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeRejectedState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeRejectedCounters, harness.CaptureCounters());
        Assert.Equal(beforeRejectedEvents, harness.Events.Events.Count);
        Assert.Same(beforeRejectedDocument, CaptureDocument(session));
        AssertSelection(session, AlphaId, GammaId);
        Assert.False(harness.Host.CaptureState().PropertiesFormOpen);
    }

    [Theory]
    [InlineData("east", -70d, 0d)]
    [InlineData("west", 70d, 0d)]
    [InlineData("northwest", 70d, 10d)]
    [InlineData("southeast", -70d, 10d)]
    public async Task LongNameReflowsInResizePreviewCommitUndoAndRedoWithoutTextPersistence(
        string role,
        double deltaX,
        double deltaY)
    {
        const string longName = "Beta sales order verification process";
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        await SetSelectionAsync(session, BetaId);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BetaId));
        var rename = CreateDataDraft(properties, NameFieldId, longName);
        harness.Host.UpdatePropertiesFormState(isOpen: true, BetaId, isDirty: true);
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await harness.Host.ApplyPropertiesAsync(rename)).Status);
        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);

        var baselineState = session.CaptureState();
        var baselineDocument = CaptureDocument(session);
        var baselineScene = CurrentScene(session);
        var baselineNode = MovableContent(baselineScene, BetaId);
        var baselineLines = LabelLines(baselineScene, BetaId);
        var baselineContents = baselineLines.Select(static line => line.Geometry.Content).ToArray();
        var start = Center(ResizeInteraction(baselineScene, BetaId, role).Bounds);
        var finish = start + new VectorD(deltaX, deltaY);
        var baselineCounters = harness.CaptureCounters();
        var baselineEvents = harness.Events.Events.Count;
        const long pointerId = 300;

        await harness.Pointer.DownDocumentPointAsync(
            baselineScene,
            start,
            pointerId);
        var pressedState = session.CaptureState();
        var pressedCounters = harness.CaptureCounters();

        Assert.NotNull(pressedState.EditorState.ActiveGesture);
        Assert.Equal(baselineState.DocumentRevision, pressedState.DocumentRevision);
        Assert.Equal(baselineState.HistoryStatus, pressedState.HistoryStatus);
        Assert.Equal(baselineEvents, harness.Events.Events.Count);
        AssertSceneOnlyChange(baselineCounters, pressedCounters);

        await harness.Pointer.MoveDocumentPointAsync(
            pressedState.CurrentScene!,
            finish,
            pointerId,
            buttons: 1);
        var previewState = session.CaptureState();
        var previewCounters = harness.CaptureCounters();
        var previewNode = ResizePreviewNode(
            previewState.CurrentScene!,
            BetaId,
            baselineNode.Id);
        var previewLines = ResizePreviewLabelLines(previewState.CurrentScene!, BetaId);

        Assert.True(previewNode.Bounds.Width < baselineNode.Bounds.Width);
        Assert.True(previewLines.Length > baselineLines.Length);
        AssertWrappedText(longName, previewLines);
        AssertCenteredBlock(previewNode.Bounds, previewLines);
        Assert.Equal(baselineState.DocumentRevision, previewState.DocumentRevision);
        Assert.Equal(baselineState.HistoryStatus, previewState.HistoryStatus);
        Assert.Equal(baselineEvents, harness.Events.Events.Count);
        Assert.Same(baselineDocument, CaptureDocument(session));
        AssertSceneOnlyChange(pressedCounters, previewCounters);

        await harness.Pointer.UpDocumentPointAsync(
            previewState.CurrentScene!,
            finish,
            pointerId);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var committedState = session.CaptureState();
        var committedCounters = harness.CaptureCounters();
        var committedLines = LabelLines(committedState.CurrentScene!, BetaId);

        Assert.Equal(baselineState.DocumentRevision.Increment(), committedState.DocumentRevision);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false),
            committedState.HistoryStatus);
        Assert.Equal(previewNode.Bounds, MovableContent(committedState.CurrentScene!, BetaId).Bounds);
        Assert.Equal(
            previewLines.Select(static line => line.Geometry.Content),
            committedLines.Select(static line => line.Geometry.Content));
        AssertCenteredBlock(previewNode.Bounds, committedLines);
        Assert.Equal(
            longName,
            TextFieldValue(CaptureProperties(session, BetaId), NameFieldId));
        AssertOnePipelineWithoutLayout(previewCounters, committedCounters);
        await harness.WaitForEventCountAsync(baselineEvents + 1);

        await harness.Host.UndoAsync();
        var undoneState = session.CaptureState();
        Assert.Equal(baselineNode.Bounds, MovableContent(undoneState.CurrentScene!, BetaId).Bounds);
        Assert.Equal(
            baselineContents,
            LabelLines(undoneState.CurrentScene!, BetaId)
                .Select(static line => line.Geometry.Content));
        Assert.Equal(
            longName,
            TextFieldValue(CaptureProperties(session, BetaId), NameFieldId));

        await harness.Host.RedoAsync();
        var redoneState = session.CaptureState();
        Assert.Equal(previewNode.Bounds, MovableContent(redoneState.CurrentScene!, BetaId).Bounds);
        Assert.Equal(
            committedLines.Select(static line => line.Geometry.Content),
            LabelLines(redoneState.CurrentScene!, BetaId)
                .Select(static line => line.Geometry.Content));
        Assert.Equal(
            longName,
            TextFieldValue(CaptureProperties(session, BetaId), NameFieldId));
    }

    [Theory]
    [InlineData(0.75d, 1d)]
    [InlineData(1d, 1.25d)]
    [InlineData(1.5d, 2d)]
    public async Task LogicalWrappingIsInvariantAcrossZoomAndDevicePixelRatio(
        double zoom,
        double devicePixelRatio)
    {
        const string longName = "Beta sales order verification process";
        await using var harness = await HostHarness.CreateAsync(devicePixelRatio);
        var session = harness.Session;
        await SetSelectionAsync(session, BetaId);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BetaId));
        var draft = CreateDataDraft(properties, NameFieldId, longName);
        harness.Host.UpdatePropertiesFormState(isOpen: true, BetaId, isDirty: true);
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await harness.Host.ApplyPropertiesAsync(draft)).Status);
        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);

        var beforeState = session.CaptureState();
        var beforeDocument = CaptureDocument(session);
        var beforeCounters = harness.CaptureCounters();
        var beforeLines = LabelLines(beforeState.CurrentScene!, BetaId);
        Assert.Equal(
            ["Beta sales order", "verification process"],
            beforeLines.Select(static line => line.Geometry.Content));

        var source = beforeState.EditorState;
        var viewportOnly = new EditorStateSnapshot(
            source.Selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            new ViewportSnapshot(zoom, source.Viewport.Pan),
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState);
        Assert.True((await session.UpdateEditorStateAsync(viewportOnly)).Succeeded);
        var afterState = session.CaptureState();
        var afterCounters = harness.CaptureCounters();
        var afterLines = LabelLines(afterState.CurrentScene!, BetaId);

        Assert.Equal(zoom, afterState.EditorState.Viewport.Zoom);
        Assert.Equal(
            beforeLines.Select(static line => line.Geometry.Content),
            afterLines.Select(static line => line.Geometry.Content));
        Assert.Equal(
            beforeLines.Select(DocumentTextAnchor),
            afterLines.Select(DocumentTextAnchor));
        Assert.Equal(beforeState.DocumentRevision, afterState.DocumentRevision);
        Assert.Equal(beforeState.HistoryStatus, afterState.HistoryStatus);
        Assert.Same(beforeDocument, CaptureDocument(session));
        AssertSceneOnlyChange(beforeCounters, afterCounters);
    }

    [Fact]
    public async Task StraightConnectorPathAndSelectedEndpointsNormalizeWithoutPersistenceAcrossViewportChanges()
    {
        await using var harness = await HostHarness.CreateAsync(devicePixelRatio: 2d);
        var session = harness.Session;
        var initialDocument = CaptureDocument(session);
        var initialState = session.CaptureState();
        var initialCounters = harness.CaptureCounters();
        var initialEventCount = harness.Events.Events.Count;
        var scene = CurrentScene(session);
        var connector = Connector(scene, AlphaBetaConnectorId);
        var path = DocumentPath(connector);

        Assert.Equal(2, path.Length);
        Assert.Empty(ConnectorEndpoints(scene, AlphaBetaConnectorId));
        var lineMiddle = new PointD(
            (path[0].X + path[1].X) / 2d,
            (path[0].Y + path[1].Y) / 2d);

        await harness.Pointer.ContextMenuDocumentPointAsync(scene, lineMiddle);

        AssertSelection(session, AlphaBetaConnectorId);
        Assert.Equal(AlphaBetaConnectorId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        Assert.Same(initialDocument, CaptureDocument(session));
        Assert.Equal(initialState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(initialState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(initialEventCount, harness.Events.Events.Count);
        AssertSceneOnlyChange(initialCounters, harness.CaptureCounters());

        scene = CurrentScene(session);
        connector = Connector(scene, AlphaBetaConnectorId);
        path = DocumentPath(connector);
        var endpoints = ConnectorEndpoints(scene, AlphaBetaConnectorId);
        var start = Assert.Single(endpoints, item => EndpointRole(item) ==
            Canvas2DConnectorEndpointMetadata.StartEndpointRole);
        var end = Assert.Single(endpoints, item => EndpointRole(item) ==
            Canvas2DConnectorEndpointMetadata.EndEndpointRole);
        Assert.Empty(RouteBendHandles(scene, AlphaBetaConnectorId));
        Assert.Equal(path[0], Center(start.Bounds));
        Assert.Equal(path[^1], Center(end.Bounds));
        Assert.NotEqual(start.Id, end.Id);

        foreach (var endpoint in new[] { start, end })
        {
            await harness.Pointer.ContextMenuDocumentPointAsync(
                CurrentScene(session),
                Center(endpoint.Bounds));
            Assert.Equal(AlphaBetaConnectorId,
                harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
            var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
                await harness.Host.OpenPropertiesAsync(AlphaBetaConnectorId));
            Assert.True(properties.IsConnector);
            Assert.Equal(new SemanticElementId("demo:alpha-beta"), properties.SemanticId);
            harness.Host.UpdatePropertiesFormState(
                isOpen: false,
                targetVisualStateId: null,
                isDirty: false);
        }

        var beforeEndpointDragDocument = CaptureDocument(session);
        var beforeEndpointDragState = session.CaptureState();
        var beforeEndpointDragCounters = harness.CaptureCounters();
        var beforeEndpointDragEvents = harness.Events.Events.Count;
        var direction = path[1] - path[0];
        var length = Math.Sqrt((direction.X * direction.X) +
            (direction.Y * direction.Y));
        var unit = new VectorD(direction.X / length, direction.Y / length);
        var startPoint = Center(start.Bounds);
        var draggedPoint = startPoint + (unit * 20d);
        await harness.Pointer.DownDocumentPointAsync(
            CurrentScene(session),
            startPoint,
            pointerId: 501L);
        await harness.Pointer.MoveDocumentPointAsync(
            CurrentScene(session),
            draggedPoint,
            pointerId: 501L,
            buttons: 1);
        await harness.Pointer.UpDocumentPointAsync(
            CurrentScene(session),
            draggedPoint,
            pointerId: 501L);

        var afterEndpointDragState = session.CaptureState();
        Assert.Null(afterEndpointDragState.EditorState.ActiveGesture);
        Assert.Same(beforeEndpointDragDocument, CaptureDocument(session));
        Assert.Equal(beforeEndpointDragState.DocumentRevision,
            afterEndpointDragState.DocumentRevision);
        Assert.Equal(beforeEndpointDragState.HistoryStatus,
            afterEndpointDragState.HistoryStatus);
        Assert.Equal(beforeEndpointDragCounters, harness.CaptureCounters());
        Assert.Equal(beforeEndpointDragEvents, harness.Events.Events.Count);
        AssertSelection(session, AlphaBetaConnectorId);

        foreach (var zoom in new[] { 0.75d, 1d, 1.5d })
        {
            var viewport = session.CaptureState().EditorState.Viewport;
            var zoomResult = await session.UpdateViewportAsync(new ViewportSnapshot(
                zoom,
                viewport.Pan,
                viewport.VisibleDocumentRegion));
            Assert.True(zoomResult.Succeeded);
            scene = CurrentScene(session);
            connector = Connector(scene, AlphaBetaConnectorId);
            path = DocumentPath(connector);
            AssertEndpointsMatchPath(scene, AlphaBetaConnectorId, path);
            lineMiddle = new PointD(
                (path[0].X + path[1].X) / 2d,
                (path[0].Y + path[1].Y) / 2d);
            await harness.Pointer.ContextMenuDocumentPointAsync(scene, lineMiddle);
            Assert.Equal(AlphaBetaConnectorId,
                harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        }

        await harness.Surface.RaiseAsync(new Canvas2DSurfaceSize(960d, 540d, 1.25d));
        scene = CurrentScene(session);
        connector = Connector(scene, AlphaBetaConnectorId);
        path = DocumentPath(connector);
        AssertEndpointsMatchPath(scene, AlphaBetaConnectorId, path);
        lineMiddle = new PointD(
            (path[0].X + path[1].X) / 2d,
            (path[0].Y + path[1].Y) / 2d);
        await harness.Pointer.ContextMenuDocumentPointAsync(scene, lineMiddle);
        Assert.Equal(AlphaBetaConnectorId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        Assert.Equal(initialState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(initialState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Same(initialDocument, CaptureDocument(session));
        Assert.Equal(initialEventCount, harness.Events.Events.Count);

        var semanticBeforeGeometryChanges = Relationship(
            initialDocument,
            new SemanticElementId("demo:alpha-beta"));
        var connectorVisualBefore = initialDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaBetaConnectorId);
        var beforeMovePath = path;
        var alpha = initialDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaId);
        var move = await session.ExecuteAsync(new MoveVisualStateCommand(
            initialDocument.DocumentId,
            initialDocument.Revision,
            AlphaId,
            alpha.Position + new VectorD(24d, 16d),
            VisualPlacementMode.Pinned));
        Assert.True(move.IsCommitted);
        await session.WaitForIdleAsync();
        var movedDocument = CaptureDocument(session);
        var movedScene = CurrentScene(session);
        var movedPath = DocumentPath(Connector(movedScene, AlphaBetaConnectorId));
        Assert.NotEqual(beforeMovePath[0], movedPath[0]);
        Assert.Equal(beforeMovePath[^1], movedPath[^1]);
        AssertEndpointsMatchPath(movedScene, AlphaBetaConnectorId, movedPath);
        Assert.Equal(semanticBeforeGeometryChanges, Relationship(
            movedDocument,
            semanticBeforeGeometryChanges.Id));
        Assert.Equal(connectorVisualBefore.Route,
            movedDocument.VisualModel.VisualStates.Single(
                visual => visual.Id == AlphaBetaConnectorId).Route);

        var beta = movedDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BetaId);
        var resize = await session.ExecuteAsync(new ResizeVisualStateCommand(
            movedDocument.DocumentId,
            movedDocument.Revision,
            BetaId,
            new RectD(
                beta.Position.X,
                beta.Position.Y,
                beta.Size.Width + 20d,
                beta.Size.Height + 30d),
            VisualPlacementMode.Pinned));
        Assert.True(resize.IsCommitted);
        await session.WaitForIdleAsync();
        var resizedDocument = CaptureDocument(session);
        var resizedScene = CurrentScene(session);
        var resizedPath = DocumentPath(Connector(resizedScene, AlphaBetaConnectorId));
        Assert.Equal(movedPath[0], resizedPath[0]);
        Assert.NotEqual(movedPath[^1], resizedPath[^1]);
        AssertEndpointsMatchPath(resizedScene, AlphaBetaConnectorId, resizedPath);
        Assert.Equal(semanticBeforeGeometryChanges, Relationship(
            resizedDocument,
            semanticBeforeGeometryChanges.Id));
        Assert.Equal(connectorVisualBefore.Route,
            resizedDocument.VisualModel.VisualStates.Single(
                visual => visual.Id == AlphaBetaConnectorId).Route);

        var connectorProperties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaBetaConnectorId));
        var nameDraft = CreateDataDraft(connectorProperties, NameFieldId, "event");
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await harness.Host.ApplyPropertiesAsync(nameDraft)).Status);
        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);
        AssertRelationshipData(
            CaptureDocument(session),
            connectorProperties.SemanticId,
            "event",
            "Initial Alpha to Beta description");

        await SetSelectionAsync(session, AlphaId);
        var labelScene = CurrentScene(session);
        Assert.Empty(ConnectorEndpoints(labelScene, AlphaBetaConnectorId));
        var connectorLabel = Assert.Single(LabelLines(labelScene, AlphaBetaConnectorId));
        var labelPoint = DocumentTextAnchor(connectorLabel);
        await harness.Pointer.ClickDocumentPointAsync(labelScene, labelPoint, pointerId: 502L);
        AssertSelection(session, AlphaBetaConnectorId);
        labelScene = CurrentScene(session);
        AssertEndpointsMatchPath(
            labelScene,
            AlphaBetaConnectorId,
            DocumentPath(Connector(labelScene, AlphaBetaConnectorId)));
        await harness.Pointer.ContextMenuDocumentPointAsync(labelScene, labelPoint);
        Assert.Equal(AlphaBetaConnectorId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
    }

    [Fact]
    public async Task ConnectorDataIsSemanticLabelIsDerivedAndUndoRedoRefreshProperties()
    {
        const string approved = "Approved";
        const string description = "Connection after approval.\nSecond line.";
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        await SetSelectionAsync(session, BetaGammaConnectorId);
        var initialDocument = CaptureDocument(session);
        var initialVisuals = initialDocument.VisualModel.VisualStates;
        var initial = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BetaGammaConnectorId));

        Assert.True(initial.IsConnector);
        Assert.Equal(string.Empty, TextFieldValue(initial, NameFieldId));
        Assert.Equal(
            "Initial Beta to Gamma description",
            TextFieldValue(initial, DescriptionFieldId));
        Assert.True(DataField(initial, NameFieldId).CanEdit);
        Assert.True(DataField(initial, DescriptionFieldId).CanEdit);
        Assert.False(initial.TryGetDataField(ElementNumberFieldId, out _));
        Assert.Equal(ConnectorLabelPlacement.Default, initial.LabelPlacement);
        Assert.Empty(LabelLines(CurrentScene(session), BetaGammaConnectorId));

        var beforeName = session.CaptureState();
        var beforeNameCounters = harness.CaptureCounters();
        var beforeNameEvents = harness.Events.Events.Count;
        var nameDraft = CreateDataDraft(initial, NameFieldId, approved);
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BetaGammaConnectorId,
            isDirty: nameDraft.IsDirty));
        var nameResult = await harness.Host.ApplyPropertiesAsync(nameDraft);
        await harness.WaitForEventCountAsync(beforeNameEvents + 1);

        var named = Assert.IsType<DocumentCanvasPropertySnapshot>(nameResult.Authoritative);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, nameResult.Status);
        Assert.Equal(approved, TextFieldValue(named, NameFieldId));
        Assert.Equal(beforeName.DocumentRevision.Increment(),
            session.CaptureState().DocumentRevision);
        Assert.Equal(beforeName.HistoryStatus.EntryCount + 1,
            session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(UpdateSemanticElementPropertyCommand.KnownTypeId,
            harness.Events.Events.Last().CommandTypeId);
        AssertOnePipelineWithoutLayout(beforeNameCounters, harness.CaptureCounters());
        Assert.Equal(
            initialVisuals.AsEnumerable(),
            CaptureDocument(session).VisualModel.VisualStates.AsEnumerable());
        AssertRelationshipData(CaptureDocument(session), named.SemanticId, approved,
            TextFieldValue(initial, DescriptionFieldId));
        AssertSelection(session, BetaGammaConnectorId);

        var namedScene = CurrentScene(session);
        var label = Assert.Single(LabelLines(namedScene, BetaGammaConnectorId));
        Assert.Equal(approved, label.Geometry.Content);
        Assert.Equal(BetaGammaConnectorId, label.Origin.VisualStateId);
        var connector = Connector(namedScene, BetaGammaConnectorId);
        var route = DocumentPath(connector);
        var expectedAnchor = Canvas2DConnectorPathGeometry.ResolvePoint(route, 0.5d) +
            ConnectorLabelPlacement.Default.Offset;
        Assert.Equal(expectedAnchor, DocumentTextAnchor(label));

        var beforeDescription = session.CaptureState();
        var beforeDescriptionCounters = harness.CaptureCounters();
        var beforeDescriptionEvents = harness.Events.Events.Count;
        var descriptionDraft = CreateDataDraft(named, DescriptionFieldId, description);
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BetaGammaConnectorId,
            isDirty: descriptionDraft.IsDirty));
        var descriptionResult = await harness.Host.ApplyPropertiesAsync(descriptionDraft);
        await harness.WaitForEventCountAsync(beforeDescriptionEvents + 1);
        var described = Assert.IsType<DocumentCanvasPropertySnapshot>(
            descriptionResult.Authoritative);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, descriptionResult.Status);
        Assert.Equal(description, TextFieldValue(described, DescriptionFieldId));
        Assert.Equal(beforeDescription.DocumentRevision.Increment(),
            session.CaptureState().DocumentRevision);
        Assert.Equal(beforeDescription.HistoryStatus.EntryCount + 1,
            session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(UpdateSemanticElementPropertyCommand.KnownTypeId,
            harness.Events.Events.Last().CommandTypeId);
        AssertOnePipelineWithoutLayout(
            beforeDescriptionCounters,
            harness.CaptureCounters());
        Assert.Equal(
            initialVisuals.AsEnumerable(),
            CaptureDocument(session).VisualModel.VisualStates.AsEnumerable());
        AssertRelationshipData(CaptureDocument(session), described.SemanticId, approved,
            description);

        await harness.Host.UndoAsync();
        await harness.WaitForEventCountAsync(beforeDescriptionEvents + 2);
        var undoneDescription = CaptureProperties(session, BetaGammaConnectorId);
        Assert.Equal(
            "Initial Beta to Gamma description",
            TextFieldValue(undoneDescription, DescriptionFieldId));
        Assert.Equal(approved, TextFieldValue(undoneDescription, NameFieldId));
        Assert.Single(LabelLines(CurrentScene(session), BetaGammaConnectorId));

        await harness.Host.UndoAsync();
        await harness.WaitForEventCountAsync(beforeDescriptionEvents + 3);
        var undoneName = CaptureProperties(session, BetaGammaConnectorId);
        Assert.Equal(string.Empty, TextFieldValue(undoneName, NameFieldId));
        Assert.Empty(LabelLines(CurrentScene(session), BetaGammaConnectorId));

        await harness.Host.RedoAsync();
        await harness.WaitForEventCountAsync(beforeDescriptionEvents + 4);
        Assert.Equal(
            approved,
            TextFieldValue(
                CaptureProperties(session, BetaGammaConnectorId),
                NameFieldId));
        Assert.Single(LabelLines(CurrentScene(session), BetaGammaConnectorId));
        await harness.Host.RedoAsync();
        await harness.WaitForEventCountAsync(beforeDescriptionEvents + 5);
        Assert.Equal(
            description,
            TextFieldValue(
                CaptureProperties(session, BetaGammaConnectorId),
                DescriptionFieldId));

        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);
        var hitScene = CurrentScene(session);
        var hitLine = Assert.Single(LabelLines(hitScene, BetaGammaConnectorId));
        var hitPoint = DocumentTextAnchor(hitLine);
        var beforeHit = session.CaptureState();
        await harness.Pointer.ClickDocumentPointAsync(hitScene, hitPoint, pointerId: 301L);
        AssertSelection(session, BetaGammaConnectorId);
        Assert.Equal(beforeHit.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeHit.HistoryStatus, session.CaptureState().HistoryStatus);
        hitScene = CurrentScene(session);
        await harness.Pointer.ContextMenuDocumentPointAsync(hitScene, hitPoint);
        Assert.Equal(BetaGammaConnectorId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);

        hitScene = CurrentScene(session);
        var connectorPath = DocumentPath(Connector(hitScene, BetaGammaConnectorId));
        Assert.Equal(3, connectorPath.Length);
        AssertEndpointsMatchPath(hitScene, BetaGammaConnectorId, connectorPath);
        var bend = Assert.Single(RouteBendHandles(hitScene, BetaGammaConnectorId));
        var endpointTargets = ConnectorEndpoints(hitScene, BetaGammaConnectorId)
            .Select(static item => Center(item.Bounds));
        var normalizationTargets = new[]
            {
                new PointD(
                    (connectorPath[0].X + connectorPath[1].X) / 2d,
                    (connectorPath[0].Y + connectorPath[1].Y) / 2d),
                Center(bend.Bounds),
                hitPoint,
            }
            .Concat(endpointTargets);
        foreach (var target in normalizationTargets)
        {
            await harness.Pointer.ContextMenuDocumentPointAsync(CurrentScene(session), target);
            Assert.Equal(BetaGammaConnectorId,
                harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        }
    }

    [Fact]
    public async Task ConnectorLabelDragIsSceneOnlyUntilCommitAndFollowsRouteChanges()
    {
        await using var harness = await HostHarness.CreateAsync(devicePixelRatio: 2d);
        var session = harness.Session;
        await SetSelectionAsync(session, BetaGammaConnectorId);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BetaGammaConnectorId));
        var nameDraft = CreateDataDraft(properties, NameFieldId, "Approved");
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BetaGammaConnectorId,
            isDirty: true));
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await harness.Host.ApplyPropertiesAsync(nameDraft)).Status);
        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);

        var semanticBefore = Relationship(CaptureDocument(session));
        var scene = CurrentScene(session);
        AssertEndpointsMatchPath(
            scene,
            BetaGammaConnectorId,
            DocumentPath(Connector(scene, BetaGammaConnectorId)));
        Assert.Single(RouteBendHandles(scene, BetaGammaConnectorId));
        var label = Assert.Single(LabelLines(scene, BetaGammaConnectorId));
        var originalAnchor = DocumentTextAnchor(label);
        var dragDelta = new VectorD(72d, 26d);
        var pointerStart = new PointD(label.Bounds.Left + 1d, originalAnchor.Y);
        var pointerTarget = pointerStart + dragDelta;
        var targetAnchor = originalAnchor + dragDelta;
        var before = session.CaptureState();
        var beforeCounters = harness.CaptureCounters();
        var beforeEvents = harness.Events.Events.Count;

        await harness.Pointer.DownDocumentPointAsync(scene, pointerStart, pointerId: 401L);
        var pressed = session.CaptureState();
        Assert.NotNull(pressed.EditorState.ActiveGesture);
        Assert.Equal(before.DocumentRevision, pressed.DocumentRevision);
        Assert.Equal(before.HistoryStatus, pressed.HistoryStatus);
        Assert.Equal(beforeEvents, harness.Events.Events.Count);
        AssertSceneOnlyChange(beforeCounters, harness.CaptureCounters());

        var beforeMoveCounters = harness.CaptureCounters();
        await harness.Pointer.MoveDocumentPointAsync(
            pressed.CurrentScene!,
            pointerTarget,
            pointerId: 401L,
            buttons: 1);
        var preview = session.CaptureState();
        Assert.Equal(before.DocumentRevision, preview.DocumentRevision);
        Assert.Equal(before.HistoryStatus, preview.HistoryStatus);
        Assert.Equal(beforeEvents, harness.Events.Events.Count);
        AssertSceneOnlyChange(beforeMoveCounters, harness.CaptureCounters());
        var previewLine = Assert.Single(preview.CurrentScene!.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == BetaGammaConnectorId &&
            item.Origin.StableSourceKey?.StartsWith(
                "connector-label-preview:",
                StringComparison.Ordinal) == true);
        Assert.Equal(targetAnchor, DocumentTextAnchor(previewLine));

        var beforeReleaseCounters = harness.CaptureCounters();
        await harness.Pointer.UpDocumentPointAsync(
            preview.CurrentScene!,
            pointerTarget,
            pointerId: 401L);
        await harness.WaitForEventCountAsync(beforeEvents + 1);
        await session.WaitForIdleAsync();
        var committed = session.CaptureState();
        Assert.Null(committed.EditorState.ActiveGesture);
        Assert.Equal(before.DocumentRevision.Increment(), committed.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1,
            committed.HistoryStatus.EntryCount);
        Assert.Equal(MoveLabelCommand.KnownTypeId,
            harness.Events.Events.Last().CommandTypeId);
        AssertOnePipelineWithoutLayout(
            beforeReleaseCounters,
            harness.CaptureCounters());
        AssertSelection(session, BetaGammaConnectorId);
        Assert.Equal(semanticBefore, Relationship(CaptureDocument(session)));
        var connectorVisual = CaptureDocument(session).VisualModel.VisualStates.Single(
            visual => visual.Id == BetaGammaConnectorId);
        Assert.True(ConnectorLabelPlacement.TryRead(
            connectorVisual.Properties,
            out var committedPlacement));
        Assert.NotNull(committedPlacement);
        Assert.InRange(committedPlacement.PathPosition, 0d, 1d);
        Assert.Equal(targetAnchor,
            DocumentTextAnchor(Assert.Single(LabelLines(
                committed.CurrentScene!,
                BetaGammaConnectorId))));

        var placementDocument = CaptureDocument(session);
        var placementCounters = harness.CaptureCounters();
        await harness.Host.UndoAsync();
        await harness.WaitForEventCountAsync(beforeEvents + 2);
        var undoneVisual = CaptureDocument(session).VisualModel.VisualStates.Single(
            visual => visual.Id == BetaGammaConnectorId);
        Assert.False(ConnectorLabelPlacement.TryRead(undoneVisual.Properties, out _));
        Assert.Equal(originalAnchor,
            DocumentTextAnchor(Assert.Single(LabelLines(
                CurrentScene(session),
                BetaGammaConnectorId))));
        AssertOnePipelineWithoutLayout(placementCounters, harness.CaptureCounters());

        var undoCounters = harness.CaptureCounters();
        await harness.Host.RedoAsync();
        await harness.WaitForEventCountAsync(beforeEvents + 3);
        Assert.Equal(
            placementDocument.VisualModel.VisualStates.AsEnumerable(),
            CaptureDocument(session).VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(targetAnchor,
            DocumentTextAnchor(Assert.Single(LabelLines(
                CurrentScene(session),
                BetaGammaConnectorId))));
        AssertOnePipelineWithoutLayout(undoCounters, harness.CaptureCounters());

        var beforeNodeMoveAnchor = DocumentTextAnchor(Assert.Single(LabelLines(
            CurrentScene(session),
            BetaGammaConnectorId)));
        var beforeNodeMoveDocument = CaptureDocument(session);
        var beta = beforeNodeMoveDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BetaId);
        var moveResult = await session.ExecuteAsync(new MoveVisualStateCommand(
            beforeNodeMoveDocument.DocumentId,
            beforeNodeMoveDocument.Revision,
            BetaId,
            beta.Position + new VectorD(25d, 18d),
            VisualPlacementMode.Pinned));
        Assert.True(moveResult.IsCommitted);
        await session.WaitForIdleAsync();
        var afterNodeMove = CaptureDocument(session);
        var afterMoveVisual = afterNodeMove.VisualModel.VisualStates.Single(
            visual => visual.Id == BetaGammaConnectorId);
        Assert.Equal(committedPlacement,
            ConnectorLabelPlacement.Resolve(afterMoveVisual.Properties));
        Assert.Equal(semanticBefore, Relationship(afterNodeMove));
        Assert.NotEqual(beforeNodeMoveAnchor,
            DocumentTextAnchor(Assert.Single(LabelLines(
                CurrentScene(session),
                BetaGammaConnectorId))));
        var afterMoveScene = CurrentScene(session);
        AssertEndpointsMatchPath(
            afterMoveScene,
            BetaGammaConnectorId,
            DocumentPath(Connector(afterMoveScene, BetaGammaConnectorId)));

        var beforeNodeResizeAnchor = DocumentTextAnchor(Assert.Single(LabelLines(
            CurrentScene(session),
            BetaGammaConnectorId)));
        var beforeNodeResizeDocument = CaptureDocument(session);
        var beforeNodeResizeState = session.CaptureState();
        var beforeNodeResizeCounters = harness.CaptureCounters();
        var beforeNodeResizeEvents = harness.Events.Events.Count;
        var gamma = beforeNodeResizeDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == GammaId);
        var resizeResult = await session.ExecuteAsync(new ResizeVisualStateCommand(
            beforeNodeResizeDocument.DocumentId,
            beforeNodeResizeDocument.Revision,
            GammaId,
            new RectD(
                gamma.Position.X,
                gamma.Position.Y,
                gamma.Size.Width + 30d,
                gamma.Size.Height + 24d),
            VisualPlacementMode.Pinned));
        Assert.True(resizeResult.IsCommitted);
        await harness.WaitForEventCountAsync(beforeNodeResizeEvents + 1);
        await session.WaitForIdleAsync();
        var afterNodeResizeState = session.CaptureState();
        var afterNodeResize = CaptureDocument(session);
        Assert.Equal(
            beforeNodeResizeState.DocumentRevision.Increment(),
            afterNodeResizeState.DocumentRevision);
        Assert.Equal(
            beforeNodeResizeState.HistoryStatus.EntryCount + 1,
            afterNodeResizeState.HistoryStatus.EntryCount);
        Assert.Equal(
            ResizeVisualStateCommand.KnownTypeId,
            harness.Events.Events.Last().CommandTypeId);
        AssertOnePipelineWithoutLayout(beforeNodeResizeCounters, harness.CaptureCounters());
        Assert.Equal(committedPlacement,
            ConnectorLabelPlacement.Resolve(afterNodeResize.VisualModel.VisualStates.Single(
                visual => visual.Id == BetaGammaConnectorId).Properties));
        Assert.Equal(semanticBefore, Relationship(afterNodeResize));
        Assert.NotEqual(beforeNodeResizeAnchor,
            DocumentTextAnchor(Assert.Single(LabelLines(
                CurrentScene(session),
                BetaGammaConnectorId))));
        var afterResizeScene = CurrentScene(session);
        AssertEndpointsMatchPath(
            afterResizeScene,
            BetaGammaConnectorId,
            DocumentPath(Connector(afterResizeScene, BetaGammaConnectorId)));

        await SetSelectionAsync(session, BetaGammaConnectorId);
        var bendScene = CurrentScene(session);
        var bendHandle = bendScene.Items.Single(item =>
            item.Origin.VisualStateId == BetaGammaConnectorId &&
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true);
        var bendPoint = Center(bendHandle.Bounds);
        await harness.Pointer.DownDocumentPointAsync(bendScene, bendPoint, pointerId: 402L);
        var bendPressed = session.CaptureState();
        var bendTarget = bendPoint + new VectorD(0d, 22d);
        await harness.Pointer.MoveDocumentPointAsync(
            bendPressed.CurrentScene!,
            bendTarget,
            pointerId: 402L,
            buttons: 1);
        var bendPreview = session.CaptureState();
        await harness.Pointer.UpDocumentPointAsync(
            bendPreview.CurrentScene!,
            bendTarget,
            pointerId: 402L);
        await session.WaitForIdleAsync();
        var afterBend = CaptureDocument(session);
        Assert.Equal(committedPlacement,
            ConnectorLabelPlacement.Resolve(afterBend.VisualModel.VisualStates.Single(
                visual => visual.Id == BetaGammaConnectorId).Properties));
        Assert.Equal(semanticBefore, Relationship(afterBend));
        var afterBendScene = CurrentScene(session);
        AssertEndpointsMatchPath(
            afterBendScene,
            BetaGammaConnectorId,
            DocumentPath(Connector(afterBendScene, BetaGammaConnectorId)));
        Assert.Single(RouteBendHandles(afterBendScene, BetaGammaConnectorId));
    }

    [Fact]
    public async Task ConnectorRoutePointContextActionsAreRoleSpecificAndZoomDprInvariant()
    {
        await using var harness = await HostHarness.CreateAsync(devicePixelRatio: 2d);
        var session = harness.Session;
        var initialDocument = CaptureDocument(session);
        var initialState = session.CaptureState();
        var initialHistory = initialState.HistoryStatus;
        var initialEvents = harness.Events.Events.Count;
        var straightVisual = initialDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaBetaConnectorId);
        Assert.Empty(straightVisual.Route);

        PointD? invariantRoutePoint = null;
        foreach (var zoom in new[] { 0.75d, 1d, 1.5d })
        {
            var viewport = session.CaptureState().EditorState.Viewport;
            Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(
                zoom,
                viewport.Pan,
                viewport.VisibleDocumentRegion))).Succeeded);
            var scene = CurrentScene(session);
            var path = DocumentPath(Connector(scene, AlphaBetaConnectorId));
            var direction = path[1] - path[0];
            var length = Math.Sqrt((direction.X * direction.X) +
                (direction.Y * direction.Y));
            var click = new PointD(
                path[0].X + (direction.X * 0.4d) - ((direction.Y / length) * 3d),
                path[0].Y + (direction.Y * 0.4d) + ((direction.X / length) * 3d));
            var projected = Canvas2DConnectorPathGeometry.FindNearest(path, click);

            await harness.Pointer.ContextMenuDocumentPointAsync(scene, click);

            var menu = Assert.IsType<DocumentCanvasContextMenuState>(
                harness.Host.CaptureState().ContextMenu);
            var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(
                menu.ConnectorRouteAction);
            Assert.Equal(AlphaBetaConnectorId, menu.TargetVisualStateId);
            Assert.Equal(Canvas2DConnectorRouteContextActionKind.AddPoint, action.Kind);
            Assert.Equal(0, action.RouteIndex);
            Assert.Equal(click, action.DocumentPoint);
            Assert.Equal(projected.RoutePoint, action.RoutePoint);
            Assert.Equal(invariantRoutePoint ?? action.RoutePoint, action.RoutePoint);
            invariantRoutePoint = action.RoutePoint;
            harness.Host.CloseContextMenu();
        }

        await harness.Surface.RaiseAsync(new Canvas2DSurfaceSize(960d, 540d, 1.25d));
        var dprScene = CurrentScene(session);
        var dprPath = DocumentPath(Connector(dprScene, AlphaBetaConnectorId));
        var dprDirection = dprPath[1] - dprPath[0];
        var dprLength = Math.Sqrt((dprDirection.X * dprDirection.X) +
            (dprDirection.Y * dprDirection.Y));
        var dprClick = new PointD(
            dprPath[0].X + (dprDirection.X * 0.4d) - ((dprDirection.Y / dprLength) * 3d),
            dprPath[0].Y + (dprDirection.Y * 0.4d) + ((dprDirection.X / dprLength) * 3d));
        await harness.Pointer.ContextMenuDocumentPointAsync(dprScene, dprClick);
        var dprAction = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.Equal(invariantRoutePoint, dprAction.RoutePoint);
        harness.Host.CloseContextMenu();

        await SetSelectionAsync(session, GammaId);
        var nearEndpointScene = CurrentScene(session);
        var nearEndpointPath = DocumentPath(Connector(
            nearEndpointScene,
            AlphaBetaConnectorId));
        var nearDirection = nearEndpointPath[1] - nearEndpointPath[0];
        var nearLength = Math.Sqrt((nearDirection.X * nearDirection.X) +
            (nearDirection.Y * nearDirection.Y));
        var nearEndpoint = nearEndpointPath[0] +
            new VectorD(nearDirection.X / nearLength, nearDirection.Y / nearLength) * 4.9d;
        await harness.Pointer.ContextMenuDocumentPointAsync(nearEndpointScene, nearEndpoint);
        Assert.Equal(AlphaBetaConnectorId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        Assert.Null(harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);

        var selectedStraightScene = CurrentScene(session);
        foreach (var endpoint in ConnectorEndpoints(selectedStraightScene, AlphaBetaConnectorId))
        {
            await harness.Pointer.ContextMenuDocumentPointAsync(
                CurrentScene(session),
                Center(endpoint.Bounds));
            Assert.Equal(AlphaBetaConnectorId,
                harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
            Assert.Null(harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        }

        await SetSelectionAsync(session, BetaGammaConnectorId);
        var bentScene = CurrentScene(session);
        var bentPath = DocumentPath(Connector(bentScene, BetaGammaConnectorId));
        var firstSegmentPoint = new PointD(
            (bentPath[0].X + bentPath[1].X) / 2d,
            (bentPath[0].Y + bentPath[1].Y) / 2d);
        await harness.Pointer.ContextMenuDocumentPointAsync(bentScene, firstSegmentPoint);
        var pathAction = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.AddPoint, pathAction.Kind);

        bentScene = CurrentScene(session);
        var bend = Assert.Single(RouteBendHandles(bentScene, BetaGammaConnectorId));
        await harness.Pointer.ContextMenuDocumentPointAsync(bentScene, Center(bend.Bounds));
        var deleteAction = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.DeletePoint, deleteAction.Kind);
        Assert.Equal(1, deleteAction.RouteIndex);

        var connectorProperties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BetaGammaConnectorId));
        var nameDraft = CreateDataDraft(connectorProperties, NameFieldId, "Approved");
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await harness.Host.ApplyPropertiesAsync(nameDraft)).Status);
        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);
        await SetSelectionAsync(session, AlphaId);
        var labelScene = CurrentScene(session);
        var label = Assert.Single(LabelLines(labelScene, BetaGammaConnectorId));
        await harness.Pointer.ContextMenuDocumentPointAsync(
            labelScene,
            DocumentTextAnchor(label));
        Assert.Equal(BetaGammaConnectorId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        Assert.Null(harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);

        await SetSelectionAsync(session, BetaId);
        var nodeScene = CurrentScene(session);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            nodeScene,
            Center(MovableContent(nodeScene, BetaId).Bounds));
        Assert.Equal(BetaId, harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        Assert.Null(harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);

        Assert.Equal(initialHistory.EntryCount + 1,
            session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(initialEvents + 1, harness.Events.Events.Count);
        Assert.Equal(initialDocument.VisualModel.VisualStates.AsEnumerable(),
            CaptureDocument(session).VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task StraightConnectorAddMoveDeleteHasAtomicMixedHistoryAndLabelInvariants()
    {
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        await SetSelectionAsync(session, AlphaBetaConnectorId);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(AlphaBetaConnectorId));
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await harness.Host.ApplyPropertiesAsync(
                CreateDataDraft(properties, NameFieldId, "Approved"))).Status);
        harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false);

        var baselineDocument = CaptureDocument(session);
        var baselineState = session.CaptureState();
        var baselineSemantic = Relationship(
            baselineDocument,
            new SemanticElementId("demo:alpha-beta"));
        var baselineVisual = baselineDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaBetaConnectorId);
        var baselinePlacement = ConnectorLabelPlacement.Resolve(baselineVisual.Properties);
        Assert.Empty(baselineVisual.Route);
        Assert.Equal("Approved", Assert.Single(
            LabelLines(CurrentScene(session), AlphaBetaConnectorId)).Geometry.Content);

        var addScene = CurrentScene(session);
        var displayedStraightRoute = DocumentPath(Connector(
            addScene,
            AlphaBetaConnectorId));
        var addPoint = new PointD(
            (displayedStraightRoute[0].X + displayedStraightRoute[1].X) / 2d,
            (displayedStraightRoute[0].Y + displayedStraightRoute[1].Y) / 2d);
        await harness.Pointer.ContextMenuDocumentPointAsync(addScene, addPoint);
        var addAction = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.True(addAction.TryResolveTargetRoute(
            CurrentScene(session),
            baselineVisual.Route,
            out var insertedRoute));
        var beforeAddCounters = harness.CaptureCounters();
        var beforeAddEvents = harness.Events.Events.Count;
        var add = await harness.Host.ExecuteConnectorRouteContextActionAsync();
        await harness.WaitForEventCountAsync(beforeAddEvents + 1);

        Assert.True(Assert.IsType<HistoryOperationResult>(add).IsCommitted);
        Assert.Equal(UpdateConnectionRouteCommand.KnownTypeId,
            harness.Events.Events.Last().CommandTypeId);
        AssertOnePipelineWithoutLayout(beforeAddCounters, harness.CaptureCounters());
        var insertedDocument = CaptureDocument(session);
        Assert.Equal(insertedRoute.AsEnumerable(), insertedDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaBetaConnectorId).Route.AsEnumerable());
        Assert.Single(RouteBendHandles(CurrentScene(session), AlphaBetaConnectorId));
        AssertEndpointsMatchPath(
            CurrentScene(session),
            AlphaBetaConnectorId,
            DocumentPath(Connector(CurrentScene(session), AlphaBetaConnectorId)));
        Assert.Equal(baselineSemantic, Relationship(
            insertedDocument,
            baselineSemantic.Id));
        Assert.Equal(baselinePlacement, ConnectorLabelPlacement.Resolve(
            insertedDocument.VisualModel.VisualStates.Single(
                visual => visual.Id == AlphaBetaConnectorId).Properties));

        var moveScene = CurrentScene(session);
        var insertedHandle = Assert.Single(RouteBendHandles(
            moveScene,
            AlphaBetaConnectorId));
        var insertedPoint = Center(insertedHandle.Bounds);
        var movedPoint = insertedPoint + new VectorD(28d, -22d);
        await harness.Pointer.DownDocumentPointAsync(
            moveScene,
            insertedPoint,
            pointerId: 701L);
        await harness.Pointer.MoveDocumentPointAsync(
            CurrentScene(session),
            movedPoint,
            pointerId: 701L,
            buttons: 1);
        var beforeMoveReleaseCounters = harness.CaptureCounters();
        var beforeMoveEvents = harness.Events.Events.Count;
        await harness.Pointer.UpDocumentPointAsync(
            CurrentScene(session),
            movedPoint,
            pointerId: 701L);
        await harness.WaitForEventCountAsync(beforeMoveEvents + 1);
        await session.WaitForIdleAsync();
        AssertOnePipelineWithoutLayout(
            beforeMoveReleaseCounters,
            harness.CaptureCounters());
        var movedRoute = CaptureDocument(session).VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaBetaConnectorId).Route;
        Assert.Equal(movedPoint, movedRoute[1]);

        var deleteScene = CurrentScene(session);
        var movedHandle = Assert.Single(RouteBendHandles(
            deleteScene,
            AlphaBetaConnectorId));
        await harness.Pointer.ContextMenuDocumentPointAsync(
            deleteScene,
            Center(movedHandle.Bounds));
        var deleteAction = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.DeletePoint, deleteAction.Kind);
        var beforeDeleteCounters = harness.CaptureCounters();
        var beforeDeleteEvents = harness.Events.Events.Count;
        var delete = await harness.Host.ExecuteConnectorRouteContextActionAsync();
        await harness.WaitForEventCountAsync(beforeDeleteEvents + 1);

        Assert.True(Assert.IsType<HistoryOperationResult>(delete).IsCommitted);
        AssertOnePipelineWithoutLayout(beforeDeleteCounters, harness.CaptureCounters());
        var deletedDocument = CaptureDocument(session);
        var deletedVisual = deletedDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaBetaConnectorId);
        Assert.Empty(deletedVisual.Route);
        Assert.Empty(RouteBendHandles(CurrentScene(session), AlphaBetaConnectorId));
        Assert.Equal(baselineSemantic, Relationship(deletedDocument, baselineSemantic.Id));
        Assert.Equal(baselineVisual.Properties, deletedVisual.Properties);
        Assert.Equal(baselinePlacement, ConnectorLabelPlacement.Resolve(deletedVisual.Properties));
        Assert.Equal("Approved", Assert.Single(
            LabelLines(CurrentScene(session), AlphaBetaConnectorId)).Geometry.Content);
        Assert.Equal(baselineState.HistoryStatus.EntryCount + 3,
            session.CaptureState().HistoryStatus.EntryCount);
        AssertSelection(session, AlphaBetaConnectorId);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(movedRoute.AsEnumerable(), CaptureDocument(session).VisualModel.VisualStates
            .Single(visual => visual.Id == AlphaBetaConnectorId).Route.AsEnumerable());
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(insertedRoute.AsEnumerable(), CaptureDocument(session).VisualModel.VisualStates
            .Single(visual => visual.Id == AlphaBetaConnectorId).Route.AsEnumerable());
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Empty(CaptureDocument(session).VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaBetaConnectorId).Route);

        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(insertedRoute.AsEnumerable(), CaptureDocument(session).VisualModel.VisualStates
            .Single(visual => visual.Id == AlphaBetaConnectorId).Route.AsEnumerable());
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(movedRoute.AsEnumerable(), CaptureDocument(session).VisualModel.VisualStates
            .Single(visual => visual.Id == AlphaBetaConnectorId).Route.AsEnumerable());
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(deletedVisual.Route.AsEnumerable(), CaptureDocument(session).VisualModel
            .VisualStates.Single(visual => visual.Id == AlphaBetaConnectorId).Route.AsEnumerable());

        var redoneDocument = CaptureDocument(session);
        Assert.Equal(baselineSemantic, Relationship(redoneDocument, baselineSemantic.Id));
        Assert.Equal(baselineVisual.Properties, redoneDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaBetaConnectorId).Properties);
        Assert.Equal("Approved", Assert.Single(
            LabelLines(CurrentScene(session), AlphaBetaConnectorId)).Geometry.Content);
        AssertSelection(session, AlphaBetaConnectorId);
    }

    [Fact]
    public async Task BentConnectorInsertionUsesHitSegmentOrderingAndDeletionRejoinsAdjacentSegments()
    {
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        await SetSelectionAsync(session, BetaGammaConnectorId);
        var beforeDocument = CaptureDocument(session);
        var beforeSemantic = Relationship(beforeDocument);
        var beforeVisual = beforeDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BetaGammaConnectorId);
        var beforePlacement = ConnectorLabelPlacement.Resolve(beforeVisual.Properties);
        var beforeRoute = beforeVisual.Route;
        Assert.Equal(3, beforeRoute.Length);

        var scene = CurrentScene(session);
        var path = DocumentPath(Connector(scene, BetaGammaConnectorId));
        var click = new PointD(
            path[1].X + ((path[2].X - path[1].X) * 0.35d) + 2d,
            path[1].Y + ((path[2].Y - path[1].Y) * 0.35d));
        var expectedProjection = Canvas2DConnectorPathGeometry.FindNearest(path, click);
        Assert.Equal(1, expectedProjection.SegmentIndex);
        await harness.Pointer.ContextMenuDocumentPointAsync(scene, click);
        var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.AddPoint, action.Kind);
        Assert.Equal(1, action.RouteIndex);
        AssertPointApproximately(expectedProjection.RoutePoint, action.RoutePoint);
        var events = harness.Events.Events.Count;
        Assert.True(Assert.IsType<HistoryOperationResult>(
            await harness.Host.ExecuteConnectorRouteContextActionAsync()).IsCommitted);
        await harness.WaitForEventCountAsync(events + 1);

        var insertedDocument = CaptureDocument(session);
        var insertedVisual = insertedDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BetaGammaConnectorId);
        Assert.Equal(4, insertedVisual.Route.Length);
        Assert.Equal(beforeRoute[0], insertedVisual.Route[0]);
        Assert.Equal(beforeRoute[1], insertedVisual.Route[1]);
        AssertPointApproximately(expectedProjection.RoutePoint, insertedVisual.Route[2]);
        Assert.Equal(beforeRoute[2], insertedVisual.Route[3]);
        Assert.Equal(beforeSemantic, Relationship(insertedDocument));
        Assert.Equal(beforeVisual.Properties, insertedVisual.Properties);
        Assert.Equal(beforePlacement, ConnectorLabelPlacement.Resolve(insertedVisual.Properties));
        Assert.Equal(2, RouteBendHandles(CurrentScene(session), BetaGammaConnectorId).Length);

        var insertedHandle = RouteBendHandles(CurrentScene(session), BetaGammaConnectorId)
            .OrderBy(handle => DistanceSquared(
                Center(handle.Bounds),
                insertedVisual.Route[2]))
            .First();
        AssertPointApproximately(insertedVisual.Route[2], Center(insertedHandle.Bounds));
        await harness.Pointer.ContextMenuDocumentPointAsync(
            CurrentScene(session),
            Center(insertedHandle.Bounds));
        var delete = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.DeletePoint, delete.Kind);
        Assert.Equal(2, delete.RouteIndex);
        events = harness.Events.Events.Count;
        Assert.True(Assert.IsType<HistoryOperationResult>(
            await harness.Host.ExecuteConnectorRouteContextActionAsync()).IsCommitted);
        await harness.WaitForEventCountAsync(events + 1);

        var deletedDocument = CaptureDocument(session);
        var deletedVisual = deletedDocument.VisualModel.VisualStates.Single(
            visual => visual.Id == BetaGammaConnectorId);
        Assert.Equal(beforeRoute.AsEnumerable(), deletedVisual.Route.AsEnumerable());
        Assert.Equal(beforeSemantic, Relationship(deletedDocument));
        Assert.Equal(beforeVisual.Properties, deletedVisual.Properties);
        Assert.Single(RouteBendHandles(CurrentScene(session), BetaGammaConnectorId));

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(insertedVisual.Route.AsEnumerable(), CaptureDocument(session).VisualModel
            .VisualStates.Single(visual => visual.Id == BetaGammaConnectorId).Route.AsEnumerable());
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(beforeRoute.AsEnumerable(), CaptureDocument(session).VisualModel.VisualStates
            .Single(visual => visual.Id == BetaGammaConnectorId).Route.AsEnumerable());
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(insertedVisual.Route.AsEnumerable(), CaptureDocument(session).VisualModel
            .VisualStates.Single(visual => visual.Id == BetaGammaConnectorId).Route.AsEnumerable());
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(beforeRoute.AsEnumerable(), CaptureDocument(session).VisualModel.VisualStates
            .Single(visual => visual.Id == BetaGammaConnectorId).Route.AsEnumerable());
    }

    [Fact]
    public async Task ActivityAnchorEdgeActionsRedistributeMixedRolesAndDeleteWithAtomicHistory()
    {
        await using var harness = await HostHarness.CreateAsync();
        var session = harness.Session;
        await SetSelectionAsync(session, AlphaId);
        var baseline = CaptureDocument(session);
        var baselineRelationship = Relationship(
            baseline,
            new SemanticElementId("demo:alpha-beta"));
        Assert.Empty(baseline.VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaId).ConnectorAnchors);

        (double Parameter, ConnectorAnchorRole Role, int InsertionIndex)[] additions =
        [
            (0.50d, ConnectorAnchorRole.Source, 0),
            (0.10d, ConnectorAnchorRole.Source, 0),
            (0.50d, ConnectorAnchorRole.Source, 1),
            (0.38d, ConnectorAnchorRole.Target, 1),
        ];
        ConnectorAnchorId[] priorIds = [];
        foreach (var addition in additions)
        {
            var scene = CurrentScene(session);
            var alpha = MovableContent(scene, AlphaId);
            var contextPoint = new PointD(
                alpha.Bounds.Right,
                alpha.Bounds.Top + (alpha.Bounds.Height * addition.Parameter));
            await harness.Pointer.ContextMenuDocumentPointAsync(scene, contextPoint);

            var menu = Assert.IsType<DocumentCanvasContextMenuState>(
                harness.Host.CaptureState().ContextMenu);
            var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
                menu.ConnectorAnchorAction);
            Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, action.Kind);
            Assert.Equal(AlphaId, action.TargetVisualStateId);
            Assert.Equal(ConnectorAnchorSide.Right, action.Side);
            Assert.Equal(addition.InsertionIndex, action.InsertionIndex);

            var beforeState = session.CaptureState();
            var beforeCounters = harness.CaptureCounters();
            var beforeEvents = harness.Events.Events.Count;
            var result = Assert.IsType<HistoryOperationResult>(
                await harness.Host.ExecuteConnectorAnchorContextActionAsync(addition.Role));
            Assert.True(result.IsCommitted);
            Assert.Equal(AddConnectorAnchorCommand.KnownTypeId, result.CommandTypeId);
            await harness.WaitForEventCountAsync(beforeEvents + 1);

            var afterState = session.CaptureState();
            Assert.Equal(beforeState.DocumentRevision.Increment(), afterState.DocumentRevision);
            Assert.Equal(beforeState.HistoryStatus.EntryCount + 1,
                afterState.HistoryStatus.EntryCount);
            AssertOnePipelineWithoutLayout(beforeCounters, harness.CaptureCounters());
            AssertSelection(session, AlphaId);

            var document = CaptureDocument(session);
            Assert.Equal(baselineRelationship, Relationship(
                document,
                baselineRelationship.Id));
            var anchors = document.VisualModel.VisualStates.Single(
                visual => visual.Id == AlphaId).ConnectorAnchors;
            Assert.Equal(priorIds.Length + 1, anchors.Length);
            Assert.All(priorIds, id => Assert.Contains(anchors, anchor => anchor.Id == id));
            priorIds = anchors.Select(static anchor => anchor.Id).ToArray();
        }

        var addedVisual = CaptureDocument(session).VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaId);
        Assert.Equal(
            [
                ConnectorAnchorRole.Source,
                ConnectorAnchorRole.Target,
                ConnectorAnchorRole.Source,
                ConnectorAnchorRole.Source,
            ],
            addedVisual.ConnectorAnchors.Select(static anchor => anchor.Role));
        Assert.Equal(
            [0, 1, 2, 3],
            addedVisual.ConnectorAnchors.Select(static anchor => anchor.Order));

        var anchorScene = CurrentScene(session);
        var alphaBounds = MovableContent(anchorScene, AlphaId).Bounds;
        var handles = ConnectorAnchorHandles(anchorScene, AlphaId);
        Assert.Equal(4, handles.Length);
        for (var index = 0; index < handles.Length; index++)
        {
            var anchor = addedVisual.ConnectorAnchors[index];
            var handle = Assert.Single(handles, item => AnchorId(item) == anchor.Id);
            AssertPointApproximately(
                ConnectorAnchorGeometryResolver.ResolvePoint(
                    alphaBounds,
                    ConnectorAnchorSide.Right,
                    index,
                    4),
                Center(handle.Bounds));
        }

        var removedAnchor = addedVisual.ConnectorAnchors[1];
        var removedHandle = Assert.Single(handles, item => AnchorId(item) == removedAnchor.Id);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            anchorScene,
            Center(removedHandle.Bounds));
        var deleteMenu = Assert.IsType<DocumentCanvasContextMenuState>(
            harness.Host.CaptureState().ContextMenu);
        var deleteAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            deleteMenu.ConnectorAnchorAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.DeleteAnchor, deleteAction.Kind);
        Assert.True(deleteAction.CanDelete);
        Assert.Equal(removedAnchor.Id, deleteAction.AnchorId);

        var beforeDeleteState = session.CaptureState();
        var beforeDeleteCounters = harness.CaptureCounters();
        var beforeDeleteEvents = harness.Events.Events.Count;
        var deleteResult = Assert.IsType<HistoryOperationResult>(
            await harness.Host.ExecuteConnectorAnchorContextActionAsync());
        Assert.True(deleteResult.IsCommitted);
        Assert.Equal(RemoveConnectorAnchorCommand.KnownTypeId, deleteResult.CommandTypeId);
        await harness.WaitForEventCountAsync(beforeDeleteEvents + 1);
        Assert.Equal(beforeDeleteState.DocumentRevision.Increment(),
            session.CaptureState().DocumentRevision);
        Assert.Equal(beforeDeleteState.HistoryStatus.EntryCount + 1,
            session.CaptureState().HistoryStatus.EntryCount);
        AssertOnePipelineWithoutLayout(beforeDeleteCounters, harness.CaptureCounters());

        var remaining = CaptureDocument(session).VisualModel.VisualStates.Single(
            visual => visual.Id == AlphaId).ConnectorAnchors;
        Assert.Equal(
            addedVisual.ConnectorAnchors
                .Where(anchor => anchor.Id != removedAnchor.Id)
                .Select(static anchor => anchor.Id),
            remaining.Select(static anchor => anchor.Id));
        Assert.Equal([0, 1, 2], remaining.Select(static anchor => anchor.Order));
        Assert.Equal(baselineRelationship, Relationship(
            CaptureDocument(session),
            baselineRelationship.Id));

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(
            addedVisual.ConnectorAnchors.AsEnumerable(),
            CaptureDocument(session).VisualModel.VisualStates.Single(
                visual => visual.Id == AlphaId).ConnectorAnchors.AsEnumerable());
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(
            remaining.AsEnumerable(),
            CaptureDocument(session).VisualModel.VisualStates.Single(
                visual => visual.Id == AlphaId).ConnectorAnchors.AsEnumerable());
    }

    [Theory]
    [InlineData("demo:visual:alpha-beta")]
    [InlineData("demo:visual:beta-gamma")]
    public async Task DirectedConnectorArrowUsesActualTargetAndNormalizesToConnector(
        string connectorVisualStateId)
    {
        await using var harness = await HostHarness.CreateAsync();
        var connectorId = new VisualStateId(connectorVisualStateId);
        var session = harness.Session;
        var scene = CurrentScene(session);
        var connector = Connector(scene, connectorId);
        var arrow = ConnectorTargetArrow(scene, connectorId);
        var path = DocumentPath(connector);

        Assert.Equal(connector.Origin.VisualStateId, arrow.Origin.VisualStateId);
        Assert.Equal(connector.Origin.ProjectedObjectId, arrow.Origin.ProjectedObjectId);
        Assert.True(arrow.Geometry.IsClosed);
        Assert.Equal(3, arrow.Geometry.Points.Length);
        AssertPointApproximately(path[^1],
            arrow.Transform.TransformPoint(arrow.Geometry.Points[0]));

        var arrowHitPoint = new PointD(
            arrow.Geometry.Points.Average(static point => point.X),
            arrow.Geometry.Points.Average(static point => point.Y));
        arrowHitPoint = arrow.Transform.TransformPoint(arrowHitPoint);
        await harness.Pointer.ContextMenuDocumentPointAsync(scene, arrowHitPoint);
        var menu = Assert.IsType<DocumentCanvasContextMenuState>(
            harness.Host.CaptureState().ContextMenu);
        Assert.Equal(connectorId, menu.TargetVisualStateId);
        Assert.Null(menu.ConnectorRouteAction);
        Assert.Null(menu.ConnectorAnchorAction);

        var before = arrow.Geometry.Points.ToArray();
        harness.Host.CloseContextMenu();
        await SetZoomAsync(session, 0.75d);
        Assert.Equal(before, ConnectorTargetArrow(CurrentScene(session), connectorId)
            .Geometry.Points.AsEnumerable());
        await SetZoomAsync(session, 1.5d);
        Assert.Equal(before, ConnectorTargetArrow(CurrentScene(session), connectorId)
            .Geometry.Points.AsEnumerable());
    }

    private static async ValueTask SetZoomAsync(EditingSession session, double zoom)
    {
        var source = session.CaptureState().EditorState;
        var updated = new EditorStateSnapshot(
            source.Selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            new ViewportSnapshot(zoom, source.Viewport.Pan),
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState);
        Assert.True((await session.UpdateEditorStateAsync(updated)).Succeeded);
    }

    private static async ValueTask SetSelectionAsync(
        EditingSession session,
        params VisualStateId[] selection)
    {
        var source = session.CaptureState().EditorState;
        var updated = new EditorStateSnapshot(
            selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState);
        var result = await session.UpdateEditorStateAsync(updated);
        Assert.True(result.Succeeded);
    }

    private static DocumentSnapshot CaptureDocument(EditingSession session)
    {
        Assert.True(session.TryCaptureDocumentSnapshot(out var snapshot));
        return Assert.IsType<DocumentSnapshot>(snapshot);
    }

    private static DocumentCanvasPropertySnapshot CaptureProperties(
        EditingSession session,
        VisualStateId visualStateId)
    {
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(
            CaptureDocument(session),
            visualStateId,
            NeutralDemoPropertiesSchemas.Catalog,
            out var snapshot));
        return Assert.IsType<DocumentCanvasPropertySnapshot>(snapshot);
    }

    private static DocumentCanvasDataPropertySnapshot DataField(
        DocumentCanvasPropertySnapshot snapshot,
        ElementPropertyFieldId fieldId)
    {
        Assert.True(snapshot.TryGetDataField(fieldId, out var field));
        return Assert.IsType<DocumentCanvasDataPropertySnapshot>(field);
    }

    private static DocumentCanvasDataPropertyDraft DraftField(
        DocumentCanvasPropertiesDraft draft,
        ElementPropertyFieldId fieldId)
    {
        Assert.True(draft.TryGetDataField(fieldId, out var field));
        return Assert.IsType<DocumentCanvasDataPropertyDraft>(field);
    }

    private static string TextFieldValue(
        DocumentCanvasPropertySnapshot snapshot,
        ElementPropertyFieldId fieldId)
    {
        var field = DataField(snapshot, fieldId);
        Assert.Equal(PropertyValueKind.Text, field.Value?.Kind);
        return Assert.IsType<PropertyValue>(field.Value).TextValue;
    }

    private static long IntegerFieldValue(
        DocumentCanvasPropertySnapshot snapshot,
        ElementPropertyFieldId fieldId)
    {
        var field = DataField(snapshot, fieldId);
        Assert.Equal(PropertyValueKind.Integer, field.Value?.Kind);
        return Assert.IsType<PropertyValue>(field.Value).IntegerValue;
    }

    private static DocumentCanvasPropertiesDraft CreateDataDraft(
        DocumentCanvasPropertySnapshot snapshot,
        ElementPropertyFieldId fieldId,
        string editorValue)
    {
        var draft = new DocumentCanvasPropertiesDraft(snapshot);
        DraftField(draft, fieldId).EditorValue = editorValue;
        return draft;
    }

    private static global::Inceptus.DocumentEngine.Contracts.Semantics.SemanticRelationshipSnapshot
        Relationship(DocumentSnapshot document) => Relationship(
            document,
            new SemanticElementId("demo:beta-gamma"));

    private static global::Inceptus.DocumentEngine.Contracts.Semantics.SemanticRelationshipSnapshot
        Relationship(DocumentSnapshot document, SemanticElementId semanticRelationshipId)
    {
        Assert.True(document.SemanticModel.TryGetRelationship(
            semanticRelationshipId,
            out var relationship));
        return Assert.IsType<
            global::Inceptus.DocumentEngine.Contracts.Semantics.SemanticRelationshipSnapshot>(
                relationship);
    }

    private static void AssertRelationshipData(
        DocumentSnapshot document,
        SemanticElementId relationshipId,
        string expectedName,
        string expectedDescription)
    {
        Assert.True(document.SemanticModel.TryGetRelationship(
            relationshipId,
            out var relationship));
        var semantic = Assert.IsType<
            global::Inceptus.DocumentEngine.Contracts.Semantics.SemanticRelationshipSnapshot>(
                relationship);
        Assert.Equal(expectedName,
            semantic.Properties[NeutralDemoPipeline.LabelPropertyKey].TextValue);
        Assert.Equal(expectedDescription,
            semantic.Properties[NeutralDemoPipeline.DescriptionPropertyKey].TextValue);
        Assert.False(semantic.Properties.ContainsKey(
            NeutralDemoPipeline.ElementNumberPropertyKey));
    }

    private static async Task<DocumentCanvasPropertySnapshot> ApplyDataAndAssertAsync(
        HostHarness harness,
        DocumentCanvasPropertiesDraft draft,
        string expectedName,
        long expectedElementNumber,
        string expectedDescription,
        CommandTypeId expectedCommandTypeId,
        params VisualStateId[] expectedSelection)
    {
        var session = harness.Session;
        Assert.True(draft.IsSemanticDirty);
        Assert.False(draft.IsBoundsDirty);
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            draft.Authoritative.VisualStateId,
            isDirty: draft.IsDirty));
        var beforeState = session.CaptureState();
        var beforeDocument = CaptureDocument(session);
        var beforeCounters = harness.CaptureCounters();
        var beforeEvents = harness.Events.Events.Count;

        var result = await harness.Host.ApplyPropertiesAsync(draft);
        await harness.WaitForEventCountAsync(beforeEvents + 1);
        var afterState = session.CaptureState();
        var afterDocument = CaptureDocument(session);
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(result.Authoritative);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        Assert.Equal(beforeState.DocumentRevision.Increment(), afterState.DocumentRevision);
        Assert.Equal(beforeState.HistoryStatus.EntryCount + 1,
            afterState.HistoryStatus.EntryCount);
        Assert.Equal(expectedCommandTypeId, harness.Events.Events.Last().CommandTypeId);
        AssertOneFullPipeline(beforeCounters, harness.CaptureCounters());
        Assert.Equal(
            beforeDocument.VisualModel.VisualStates.AsEnumerable(),
            afterDocument.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(
            beforeDocument.Metadata.ExtensionProperties,
            afterDocument.Metadata.ExtensionProperties);
        AssertSelection(session, expectedSelection);
        Assert.True(harness.Host.CaptureState().PropertiesFormOpen);
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);
        Assert.Equal(expectedName, TextFieldValue(authoritative, NameFieldId));
        Assert.Equal(
            expectedElementNumber,
            IntegerFieldValue(authoritative, ElementNumberFieldId));
        Assert.Equal(
            expectedDescription,
            TextFieldValue(authoritative, DescriptionFieldId));
        AssertSemanticData(
            afterDocument,
            authoritative.SemanticId,
            expectedName,
            expectedElementNumber,
            expectedDescription);
        return authoritative;
    }

    private static async Task<DocumentCanvasPropertySnapshot> UndoAndAssertAsync(
        HostHarness harness,
        params VisualStateId[] expectedSelection) =>
        await ExecuteHistoryAndAssertAsync(
            harness,
            isUndo: true,
            preserveNodeLayout: false,
            expectedSelection);

    private static async Task<DocumentCanvasPropertySnapshot> RedoAndAssertAsync(
        HostHarness harness,
        params VisualStateId[] expectedSelection) =>
        await ExecuteHistoryAndAssertAsync(
            harness,
            isUndo: false,
            preserveNodeLayout: false,
            expectedSelection);

    private static async Task<DocumentCanvasPropertySnapshot>
        UndoAndAssertPreservingNodeLayoutAsync(
            HostHarness harness,
            params VisualStateId[] expectedSelection) =>
        await ExecuteHistoryAndAssertAsync(
            harness,
            isUndo: true,
            preserveNodeLayout: true,
            expectedSelection);

    private static async Task<DocumentCanvasPropertySnapshot>
        RedoAndAssertPreservingNodeLayoutAsync(
            HostHarness harness,
            params VisualStateId[] expectedSelection) =>
        await ExecuteHistoryAndAssertAsync(
            harness,
            isUndo: false,
            preserveNodeLayout: true,
            expectedSelection);

    private static async Task<DocumentCanvasPropertySnapshot> ExecuteHistoryAndAssertAsync(
        HostHarness harness,
        bool isUndo,
        bool preserveNodeLayout,
        params VisualStateId[] expectedSelection)
    {
        var session = harness.Session;
        var before = session.CaptureState();
        var beforeCounters = harness.CaptureCounters();
        var beforeEvents = harness.Events.Events.Count;

        if (isUndo)
        {
            await harness.Host.UndoAsync();
        }
        else
        {
            await harness.Host.RedoAsync();
        }

        await harness.WaitForEventCountAsync(beforeEvents + 1);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount, after.HistoryStatus.EntryCount);
        if (preserveNodeLayout)
        {
            AssertOnePipelineWithoutLayout(beforeCounters, harness.CaptureCounters());
        }
        else
        {
            AssertOneFullPipeline(beforeCounters, harness.CaptureCounters());
        }
        AssertSelection(session, expectedSelection);
        Assert.True(harness.Host.CaptureState().PropertiesFormOpen);
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);
        return CaptureProperties(session, BetaId);
    }

    private static void AssertSemanticData(
        DocumentSnapshot document,
        SemanticElementId semanticElementId,
        string expectedName,
        long expectedElementNumber,
        string expectedDescription)
    {
        Assert.True(document.SemanticModel.TryGetElement(semanticElementId, out var element));
        var semantic = Assert.IsType<
            global::Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementSnapshot>(element);
        var name = semantic.Properties[NeutralDemoPipeline.LabelPropertyKey];
        var elementNumber = semantic.Properties[NeutralDemoPipeline.ElementNumberPropertyKey];
        var description = semantic.Properties[NeutralDemoPipeline.DescriptionPropertyKey];

        Assert.Equal(PropertyValueKind.Text, name.Kind);
        Assert.Equal(expectedName, name.TextValue);
        Assert.Equal(PropertyValueKind.Integer, elementNumber.Kind);
        Assert.Equal(expectedElementNumber, elementNumber.IntegerValue);
        Assert.Equal(PropertyValueKind.Text, description.Kind);
        Assert.Equal(expectedDescription, description.TextValue);
    }

    private static void AssertDataAndBounds(
        DocumentCanvasPropertySnapshot expected,
        DocumentCanvasPropertySnapshot actual)
    {
        Assert.Equal(expected.VisualStateId, actual.VisualStateId);
        Assert.Equal(expected.SemanticId, actual.SemanticId);
        Assert.Equal(
            TextFieldValue(expected, NameFieldId),
            TextFieldValue(actual, NameFieldId));
        Assert.Equal(
            IntegerFieldValue(expected, ElementNumberFieldId),
            IntegerFieldValue(actual, ElementNumberFieldId));
        Assert.Equal(
            TextFieldValue(expected, DescriptionFieldId),
            TextFieldValue(actual, DescriptionFieldId));
        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(expected.PlacementMode, actual.PlacementMode);
    }

    private static void AssertPropertySnapshotEqual(
        DocumentCanvasPropertySnapshot expected,
        DocumentCanvasPropertySnapshot actual)
    {
        Assert.Equal(expected.DocumentId, actual.DocumentId);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.VisualStateId, actual.VisualStateId);
        Assert.Equal(expected.SemanticId, actual.SemanticId);
        Assert.Equal(expected.TypeId, actual.TypeId);
        Assert.Equal(expected.DataFields.AsEnumerable(), actual.DataFields.AsEnumerable());
        Assert.Equal(expected.PlacementMode, actual.PlacementMode);
        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(expected.IsConnector, actual.IsConnector);
        Assert.Equal(expected.SourceId, actual.SourceId);
        Assert.Equal(expected.TargetId, actual.TargetId);
        Assert.Equal(expected.LabelPlacement, actual.LabelPlacement);
    }

    private static Canvas2DScene CurrentScene(EditingSession session) =>
        Assert.IsType<Canvas2DScene>(session.CaptureState().CurrentScene);

    private static Canvas2DSceneItem MovableContent(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static Canvas2DSceneItem Label(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == visualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static Canvas2DSceneItem Connector(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == visualStateId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));

    private static Canvas2DSceneItem[] ConnectorEndpoints(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.VisualStateId == visualStateId &&
                item.Metadata.TryGetValue(
                    Canvas2DConnectorEndpointMetadata.HandleRole,
                    out var role) &&
                role.Kind == PropertyValueKind.Text)
            .OrderBy(static item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();

    private static Canvas2DSceneItem[] ConnectorAnchorHandles(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.VisualStateId == visualStateId &&
                item.Metadata.TryGetValue(
                    Canvas2DConnectorAnchorMetadata.AnchorId,
                    out var anchorId) &&
                anchorId.Kind == PropertyValueKind.Text)
            .OrderBy(static item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();

    private static ConnectorAnchorId AnchorId(Canvas2DSceneItem item) =>
        new(item.Metadata[Canvas2DConnectorAnchorMetadata.AnchorId].TextValue);

    private static Canvas2DSceneItem ConnectorTargetArrow(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == visualStateId &&
            item.Metadata.TryGetValue(
                Canvas2DConnectorArrowMetadata.TargetArrow,
                out var targetArrow) &&
            targetArrow.Kind == PropertyValueKind.Boolean &&
            targetArrow.BooleanValue);

    private static string EndpointRole(Canvas2DSceneItem item) =>
        item.Metadata[Canvas2DConnectorEndpointMetadata.HandleRole].TextValue;

    private static Canvas2DSceneItem[] RouteBendHandles(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.VisualStateId == visualStateId &&
                item.Metadata.TryGetValue(
                    Canvas2DRouteGestureMetadata.HandleRole,
                    out var role) &&
                role.Kind == PropertyValueKind.Text &&
                StringComparer.Ordinal.Equals(
                    role.TextValue,
                    Canvas2DRouteGestureMetadata.BendRole))
            .OrderBy(static item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();

    private static void AssertEndpointsMatchPath(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        PointD[] path)
    {
        Assert.True(path.Length >= 2);
        var endpoints = ConnectorEndpoints(scene, visualStateId);
        var start = Assert.Single(endpoints, item => EndpointRole(item) ==
            Canvas2DConnectorEndpointMetadata.StartEndpointRole);
        var end = Assert.Single(endpoints, item => EndpointRole(item) ==
            Canvas2DConnectorEndpointMetadata.EndEndpointRole);
        Assert.Equal(path[0], Center(start.Bounds));
        Assert.Equal(path[^1], Center(end.Bounds));
    }

    private static PointD[] DocumentPath(Canvas2DSceneItem connector) =>
        connector.Geometry.Points
            .Select(connector.Transform.TransformPoint)
            .ToArray();

    private static Canvas2DSceneItem[] LabelLines(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Label &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
                item.Origin.VisualStateId == visualStateId &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
            .OrderBy(DocumentTextAnchor, PointByYComparer.Instance)
            .ToArray();

    private static Canvas2DSceneItem ResizePreviewNode(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        SceneObjectId sourceNodeId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(sourceNodeId));

    private static Canvas2DSceneItem[] ResizePreviewLabelLines(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
                item.Origin.VisualStateId == visualStateId &&
                item.Origin.StableSourceKey?.StartsWith(
                    "resize-preview:",
                    StringComparison.Ordinal) == true)
            .OrderBy(DocumentTextAnchor, PointByYComparer.Instance)
            .ToArray();

    private static Canvas2DSceneItem ResizeInteraction(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        string role) =>
        scene.Items.Single(item =>
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                role is "north" or "east" or "south" or "west"
                    ? $"resize-edge-zone:{role}:"
                    : $"resize-handle:{role}:",
                StringComparison.Ordinal) == true);

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static double DistanceSquared(PointD left, PointD right)
    {
        var delta = left - right;
        return (delta.X * delta.X) + (delta.Y * delta.Y);
    }

    private static void AssertPointApproximately(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 10);
        Assert.Equal(expected.Y, actual.Y, precision: 10);
    }

    private static PointD DocumentTextAnchor(Canvas2DSceneItem line) =>
        line.Transform.TransformPoint(line.Geometry.TextAnchor);

    private static void AssertWrappedText(
        string expected,
        IReadOnlyCollection<Canvas2DSceneItem> lines)
    {
        Assert.NotEmpty(lines);
        Assert.Equal(
            WithoutWhitespace(expected),
            WithoutWhitespace(string.Concat(lines.Select(static line =>
                line.Geometry.Content))));
        Assert.All(lines, static line => Assert.False(string.IsNullOrEmpty(line.Geometry.Content)));
    }

    private static string WithoutWhitespace(string value) =>
        new(value.Where(static character => !char.IsWhiteSpace(character)).ToArray());

    private static void AssertCenteredBlock(
        RectD nodeBounds,
        IReadOnlyCollection<Canvas2DSceneItem> lines)
    {
        Assert.NotEmpty(lines);
        var anchors = lines.Select(DocumentTextAnchor)
            .OrderBy(static point => point.Y)
            .ToArray();
        var center = Center(nodeBounds);
        Assert.All(lines, line =>
        {
            Assert.Equal(Canvas2DTextAlignment.Center, line.Geometry.TextAlignment);
            Assert.Equal(Canvas2DTextBaseline.Middle, line.Geometry.TextBaseline);
            Assert.NotNull(line.Clip);
            Assert.Equal(center.X, DocumentTextAnchor(line).X, precision: 10);
        });
        Assert.Equal(center.Y, (anchors[0].Y + anchors[^1].Y) / 2d, precision: 10);
        for (var index = 1; index < anchors.Length; index++)
        {
            Assert.True(anchors[index].Y > anchors[index - 1].Y);
        }
    }

    private static void AssertSelection(
        EditingSession session,
        params VisualStateId[] expected) =>
        Assert.True(expected.AsSpan().SequenceEqual(
            session.CaptureState().EditorState.Selection.AsSpan()));

    private static void AssertOneFullPipeline(CounterSnapshot before, CounterSnapshot after)
    {
        Assert.Equal(before.ProjectionRules + 5, after.ProjectionRules);
        Assert.Equal(before.Layouts + 1, after.Layouts);
        Assert.Equal(before.Routings + 1, after.Routings);
        Assert.Equal(before.SceneContributions + 1, after.SceneContributions);
        Assert.Equal(before.Renders + 1, after.Renders);
    }

    private static void AssertOnePipelineWithoutLayout(
        CounterSnapshot before,
        CounterSnapshot after)
    {
        Assert.Equal(before.ProjectionRules + 5, after.ProjectionRules);
        Assert.Equal(before.Layouts, after.Layouts);
        Assert.Equal(before.Routings + 1, after.Routings);
        Assert.Equal(before.SceneContributions + 1, after.SceneContributions);
        Assert.Equal(before.Renders + 1, after.Renders);
    }

    private static void AssertSceneOnlyChange(CounterSnapshot before, CounterSnapshot after)
    {
        Assert.Equal(before.ProjectionRules, after.ProjectionRules);
        Assert.Equal(before.Layouts, after.Layouts);
        Assert.Equal(before.Routings, after.Routings);
        Assert.Equal(before.SceneContributions + 1, after.SceneContributions);
        Assert.Equal(before.Renders + 1, after.Renders);
    }

    private sealed class PointByYComparer : IComparer<PointD>
    {
        internal static PointByYComparer Instance { get; } = new();

        public int Compare(PointD left, PointD right)
        {
            var byY = left.Y.CompareTo(right.Y);
            return byY != 0 ? byY : left.X.CompareTo(right.X);
        }
    }

    private sealed class HostHarness : IAsyncDisposable
    {
        private HostHarness(
            DocumentCanvasHost host,
            RecordingPointerObserver pointer,
            RecordingSurfaceObserver surface,
            RecordingRenderExecution execution,
            RecordingSubscriber events)
        {
            Host = host;
            Pointer = pointer;
            Surface = surface;
            Execution = execution;
            Events = events;
        }

        internal DocumentCanvasHost Host { get; }

        internal RecordingPointerObserver Pointer { get; }

        internal RecordingSurfaceObserver Surface { get; }

        internal RecordingRenderExecution Execution { get; }

        internal RecordingSubscriber Events { get; }

        internal EditingSession Session => Assert.IsType<EditingSession>(
            typeof(DocumentCanvasHost)
                .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(Host));

        internal static async ValueTask<HostHarness> CreateAsync(
            double devicePixelRatio = 1.25d)
        {
            var execution = new RecordingRenderExecution();
            var renderer = new Canvas2DRenderer(
                execution,
                new Canvas2DRendererConfiguration(
                    fontResources:
                    [
                        new Canvas2DFontResource(
                            "org.dejavu.DejaVuSans",
                            "2.37",
                            "DejaVu Sans",
                            "fonts/DejaVuSans-2.37.ttf"),
                    ],
                    defaultFontFamily: "DejaVu Sans"));
            var surface = new RecordingSurfaceObserver(
                new Canvas2DSurfaceSize(800d, 600d, devicePixelRatio));
            var pointer = new RecordingPointerObserver();
            var host = new DocumentCanvasHost(
                BpmnModelerTestComposition.NeutralFactory,
                renderer,
                new RecordingSurfaceObserverFactory(surface),
                new RecordingPointerObserverFactory(pointer));

            await host.InitializeAsync("phase-l5-canvas", "phase-l5-container");
            var initialized = host.CaptureState();
            Assert.True(initialized.IsInitialized);
            Assert.Equal(
                EditingSessionStatus.Ready,
                initialized.Session?.Status);
            var events = new RecordingSubscriber();
            AttachSubscriber(CaptureSession(host), events);
            return new HostHarness(host, pointer, surface, execution, events);
        }

        internal CounterSnapshot CaptureCounters()
        {
            var state = Host.CaptureState();
            var counters = Assert.IsType<NeutralDemoPipelineCountersAdapter>(
                state.PipelineCounters).Counters;
            return new CounterSnapshot(
                counters.ProjectionRuleInvocationCount,
                counters.LayoutInvocationCount,
                counters.RoutingInvocationCount,
                counters.SceneContributionInvocationCount,
                state.SuccessfulRenderCount);
        }

        internal async Task WaitForEventCountAsync(int expected)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (Events.Events.Count < expected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.Equal(expected, Events.Events.Count);
        }

        private static EditingSession CaptureSession(DocumentCanvasHost host) =>
            Assert.IsType<EditingSession>(typeof(DocumentCanvasHost)
                .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(host));

        private static void AttachSubscriber(
            EditingSession session,
            IDocumentChangedSubscriber subscriber)
        {
            var processor = Assert.IsType<CommandProcessor>(typeof(EditingSession)
                .GetField("_commandProcessor", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(session));
            var subscribersField = typeof(CommandProcessor).GetField(
                "_subscribers",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var subscribers = Assert.IsType<ImmutableArray<IDocumentChangedSubscriber>>(
                subscribersField.GetValue(processor));
            subscribersField.SetValue(processor, subscribers.Add(subscriber));
        }

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record CounterSnapshot(
        int ProjectionRules,
        int Layouts,
        int Routings,
        int SceneContributions,
        int Renders);

    private sealed class RecordingSurfaceObserverFactory(
        RecordingSurfaceObserver observer) : ICanvasPresentationSurfaceObserverFactory
    {
        public ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
            string containerElementId,
            Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observer.Callback = onSurfaceChanged;
            return ValueTask.FromResult<ICanvasPresentationSurfaceObserver>(observer);
        }
    }

    private sealed class RecordingSurfaceObserver(Canvas2DSurfaceSize initial) :
        ICanvasPresentationSurfaceObserver
    {
        internal Func<Canvas2DSurfaceSize, Task>? Callback { get; set; }

        public ValueTask<Canvas2DSurfaceSize> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(initial);
        }

        internal Task RaiseAsync(Canvas2DSurfaceSize surfaceSize) =>
            Callback?.Invoke(surfaceSize) ?? Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingPointerObserverFactory(
        RecordingPointerObserver observer) : ICanvasPresentationPointerObserverFactory
    {
        public ValueTask<ICanvasPresentationPointerObserver> CreateAsync(
            string canvasElementId,
            Func<CanvasPointerInput, Task> onPointerInput,
            Func<CanvasWheelInput, Task> onWheelInput,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observer.Callback = onPointerInput;
            _ = onWheelInput;
            return ValueTask.FromResult<ICanvasPresentationPointerObserver>(observer);
        }
    }

    private sealed class RecordingPointerObserver : ICanvasPresentationPointerObserver
    {
        internal Func<CanvasPointerInput, Task>? Callback { get; set; }

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseCaptureAsync(
            long captureGeneration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask SetCursorAsync(string cssCursor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal Task DownDocumentPointAsync(
            Canvas2DScene scene,
            PointD documentPoint,
            long pointerId) =>
            SendDocumentPointAsync(
                scene,
                documentPoint,
                CanvasPointerEventKind.Down,
                pointerId,
                button: 0,
                buttons: 1,
                captureGeneration: pointerId);

        internal Task MoveDocumentPointAsync(
            Canvas2DScene scene,
            PointD documentPoint,
            long pointerId,
            int buttons) =>
            SendDocumentPointAsync(
                scene,
                documentPoint,
                CanvasPointerEventKind.Move,
                pointerId,
                button: -1,
                buttons,
                captureGeneration: buttons == 0 ? 0 : pointerId);

        internal Task UpDocumentPointAsync(
            Canvas2DScene scene,
            PointD documentPoint,
            long pointerId) =>
            SendDocumentPointAsync(
                scene,
                documentPoint,
                CanvasPointerEventKind.Up,
                pointerId,
                button: 0,
                buttons: 0,
                captureGeneration: pointerId);

        internal async Task ClickDocumentPointAsync(
            Canvas2DScene scene,
            PointD documentPoint,
            long pointerId)
        {
            await DownDocumentPointAsync(scene, documentPoint, pointerId);
            await UpDocumentPointAsync(scene, documentPoint, pointerId);
        }

        internal Task ContextMenuDocumentPointAsync(Canvas2DScene scene, PointD documentPoint)
            => SendDocumentPointAsync(
                scene,
                documentPoint,
                CanvasPointerEventKind.ContextMenu,
                pointerId: 1,
                button: 2,
                buttons: 0,
                captureGeneration: 0);

        private Task SendDocumentPointAsync(
            Canvas2DScene scene,
            PointD documentPoint,
            CanvasPointerEventKind kind,
            long pointerId,
            int button,
            int buttons,
            long captureGeneration)
        {
            var css = scene.ViewportTransform.TransformPoint(documentPoint);
            return (Callback ?? throw new InvalidOperationException(
                "The pointer observer has no managed callback."))(new CanvasPointerInput(
                kind,
                pointerId,
                button,
                buttons,
                IsPrimary: true,
                ClientX: css.X + 17d,
                ClientY: css.Y + 23d,
                CanvasLeft: 17d,
                CanvasTop: 23d,
                AltKey: false,
                ControlKey: false,
                MetaKey: false,
                ShiftKey: false,
                CaptureGeneration: captureGeneration));
        }
    }

    private sealed class RecordingRenderExecution : ICanvas2DRenderExecution
    {
        internal int RenderCount { get; private set; }

        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame)
        {
            RenderCount++;
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request)
        {
            var width = request.Text.Sum(character => character switch
            {
                ' ' => request.FontSize * 0.33d,
                >= 'A' and <= 'Z' => request.FontSize * 0.62d,
                >= 'a' and <= 'z' => request.FontSize * 0.54d,
                >= '0' and <= '9' => request.FontSize * 0.55d,
                _ => request.FontSize * 0.58d,
            });
            var ascent = request.FontSize * 0.75d;
            var descent = request.FontSize * 0.25d;
            return ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = width,
                Ascent = ascent,
                Descent = descent,
                LineHeight = request.LineHeight,
                BoundingX = 0d,
                BoundingY = -ascent,
                BoundingWidth = width,
                BoundingHeight = ascent + descent,
                ResolvedFontIdentity = $"{request.FontIdentity}@{request.FontVersion}",
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
