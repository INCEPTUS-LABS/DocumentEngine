using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.EditorState;

public sealed class EditorStateStoreTests
{
    [Fact]
    public void StoreStartsFromEmptyOrExplicitImmutableSnapshot()
    {
        var emptyStore = new EditorStateStore();
        var supplied = new EditorStateSnapshot(activeToolId: "tool:select");
        var suppliedStore = new EditorStateStore(supplied);

        Assert.Same(EditorStateSnapshot.Empty, emptyStore.CaptureSnapshot());
        Assert.Same(supplied, suppliedStore.CaptureSnapshot());
    }

    [Fact]
    public void CompareAndSwapReplacementIsControlledAndAtomic()
    {
        var initial = new EditorStateSnapshot(activeToolId: "tool:select");
        var stale = new EditorStateSnapshot(activeToolId: "tool:stale");
        var replacement = new EditorStateSnapshot(
            selection: [new VisualStateId("visual:selected")],
            activeToolId: "tool:move");
        var ignored = new EditorStateSnapshot(activeToolId: "tool:ignored");
        var store = new EditorStateStore(initial);

        Assert.False(store.TryUpdate(stale, ignored));
        Assert.Same(initial, store.CaptureSnapshot());
        Assert.True(store.TryUpdate(initial, replacement));
        Assert.Same(replacement, store.CaptureSnapshot());
        Assert.False(store.TryUpdate(initial, ignored));
        Assert.Same(replacement, store.CaptureSnapshot());
    }

    [Fact]
    public async Task ConcurrentWritersCannotBothReplaceTheSameCapturedSnapshot()
    {
        var initial = new EditorStateSnapshot();
        var store = new EditorStateStore(initial);
        using var start = new ManualResetEventSlim();

        var first = Task.Run(() =>
        {
            start.Wait();
            return store.TryUpdate(initial, new EditorStateSnapshot(activeToolId: "tool:first"));
        });
        var second = Task.Run(() =>
        {
            start.Wait();
            return store.TryUpdate(initial, new EditorStateSnapshot(activeToolId: "tool:second"));
        });

        start.Set();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, static result => result);
        Assert.True(store.CaptureSnapshot().ActiveToolId is "tool:first" or "tool:second");
    }

    [Fact]
    public void StoreRejectsNullReplacementInputs()
    {
        var store = new EditorStateStore();
        var snapshot = store.CaptureSnapshot();

        Assert.Throws<ArgumentNullException>(() => store.TryUpdate(null!, snapshot));
        Assert.Throws<ArgumentNullException>(() => store.TryUpdate(snapshot, null!));
        Assert.Same(snapshot, store.CaptureSnapshot());
    }

    [Fact]
    public void StoreSequentiallyInstallsEveryEditorStateUpdateCategory()
    {
        var store = new EditorStateStore();

        var selection = new EditorStateSnapshot(
            selection: [new VisualStateId("visual:selected")]);
        Assert.True(store.TryUpdate(EditorStateSnapshot.Empty, selection));
        Assert.Same(selection, store.CaptureSnapshot());
        Assert.Equal("visual:selected", Assert.Single(store.CaptureSnapshot().Selection).Value);

        var hover = new EditorStateSnapshot(
            selection: selection.Selection,
            hoveredObjectId: new SceneObjectId("scene:hovered"));
        Assert.True(store.TryUpdate(selection, hover));
        Assert.Same(hover, store.CaptureSnapshot());
        Assert.Equal("scene:hovered", store.CaptureSnapshot().HoveredObjectId?.Value);

        var activeTool = new EditorStateSnapshot(
            selection: hover.Selection,
            hoveredObjectId: hover.HoveredObjectId,
            activeToolId: "tool:move");
        Assert.True(store.TryUpdate(hover, activeTool));
        Assert.Same(activeTool, store.CaptureSnapshot());
        Assert.Equal("tool:move", store.CaptureSnapshot().ActiveToolId);

        var focus = new EditorStateSnapshot(
            selection: activeTool.Selection,
            hoveredObjectId: activeTool.HoveredObjectId,
            activeToolId: activeTool.ActiveToolId,
            focusTargetId: "focus:canvas");
        Assert.True(store.TryUpdate(activeTool, focus));
        Assert.Same(focus, store.CaptureSnapshot());
        Assert.Equal("focus:canvas", store.CaptureSnapshot().FocusTargetId);

        var viewport = new ViewportSnapshot(2d, new VectorD(25d, -10d));
        var withViewport = new EditorStateSnapshot(
            selection: focus.Selection,
            hoveredObjectId: focus.HoveredObjectId,
            activeToolId: focus.ActiveToolId,
            focusTargetId: focus.FocusTargetId,
            viewport: viewport);
        Assert.True(store.TryUpdate(focus, withViewport));
        Assert.Same(withViewport, store.CaptureSnapshot());
        Assert.Same(viewport, store.CaptureSnapshot().Viewport);

        var gesture = new EditorGestureSnapshot(
            "gesture:drag",
            "drag",
            new PointD(10d, 20d),
            new PointD(30d, 40d));
        var feedback = new EditorFeedbackSnapshot(
            "feedback:guide",
            "snap-guide",
            points: [new PointD(30d, 40d)]);
        var interaction = new EditorStateSnapshot(
            selection: withViewport.Selection,
            hoveredObjectId: withViewport.HoveredObjectId,
            activeToolId: withViewport.ActiveToolId,
            focusTargetId: withViewport.FocusTargetId,
            viewport: withViewport.Viewport,
            activeGesture: gesture,
            temporaryFeedback: [feedback]);
        Assert.True(store.TryUpdate(withViewport, interaction));
        Assert.Same(interaction, store.CaptureSnapshot());
        Assert.Same(gesture, store.CaptureSnapshot().ActiveGesture);
        Assert.Same(feedback, Assert.Single(store.CaptureSnapshot().TemporaryFeedback));
    }

    [Fact]
    public void EditorStateReplacementDoesNotTouchDocumentHistoryOrCommittedEvents()
    {
        var document = DocumentFactory.CreateEmpty(new DocumentId("document:editor-isolation")).Document!;
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        _ = new CommandProcessor(subscribers: [subscriber]);
        var store = new EditorStateStore();
        var beforeDocument = document.CaptureSnapshot();
        var beforeRevision = document.Revision;
        var beforeHistory = history.CaptureStatus();
        var replacement = new EditorStateSnapshot(
            selection: [new VisualStateId("visual:selected")],
            hoveredObjectId: new SceneObjectId("scene:hovered"),
            activeToolId: "tool:move",
            focusTargetId: "focus:canvas");

        Assert.True(store.TryUpdate(EditorStateSnapshot.Empty, replacement));

        Assert.Same(replacement, store.CaptureSnapshot());
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(beforeRevision, document.Revision);
        Assert.Equal(beforeHistory, history.CaptureStatus());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public void EditorStateTypesOwnNoDocumentRevisionCommandEventOrHistoryState()
    {
        Type[] editorStateTypes =
        [
            typeof(EditorStateSnapshot),
            typeof(ViewportSnapshot),
            typeof(EditorGestureSnapshot),
            typeof(EditorFeedbackSnapshot),
            typeof(EditorStateStore),
        ];
        var forbiddenTypes = new HashSet<Type>
        {
            typeof(Document),
            typeof(DocumentId),
            typeof(DocumentRevision),
            typeof(ICommand),
            typeof(DocumentChangedEvent),
        };

        foreach (var type in editorStateTypes)
        {
            Assert.Empty(type.GetEvents(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));

            var signatureAndFieldTypes = type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(field => field.FieldType)
                .Concat(type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance |
                    BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(property => property.PropertyType))
                .Concat(type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance |
                    BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetParameters())
                    .Select(parameter => parameter.ParameterType));

            Assert.DoesNotContain(signatureAndFieldTypes, forbiddenTypes.Contains);
            Assert.DoesNotContain(
                type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly),
                member => member.Name.Contains("History", StringComparison.OrdinalIgnoreCase));
        }
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
}
