using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM31BpmnPropertiesIntegrationTests
{
    private const string InitialDescription =
        "Review the incoming customer order.\nCheck completeness before approval.";

    [Fact]
    public async Task TaskBodyAndLabelResolveTheExactM31SchemaWhileEventsKeepInternalDataAndHaveNoProperties()
    {
        await using var harness = await HostHarness.CreateAsync();
        var initialDocument = harness.Composition.Document.CaptureSnapshot();
        var initialState = harness.State;

        var body = NodeBody(harness.Scene, BpmnDemoPipeline.TaskVisualId);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            new PointD(
                body.Bounds.Left + (body.Bounds.Width * 0.25d),
                body.Bounds.Top + (body.Bounds.Height * 0.8d)));
        Assert.Equal(
            BpmnDemoPipeline.TaskVisualId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        var bodyProperties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BpmnDemoPipeline.TaskVisualId));

        AssertTaskIdentityAndFields(bodyProperties);
        CloseProperties(harness.Host);

        var label = Assert.Single(TaskLabelLines(harness.Scene));
        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            DocumentTextAnchor(label));
        Assert.Equal(
            BpmnDemoPipeline.TaskVisualId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        var labelProperties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BpmnDemoPipeline.TaskVisualId));
        AssertTaskIdentityAndFields(labelProperties);
        Assert.Equal(bodyProperties.SemanticId, labelProperties.SemanticId);
        Assert.Equal(bodyProperties.VisualStateId, labelProperties.VisualStateId);
        CloseProperties(harness.Host);

        foreach (var (visualId, semanticId, typeId) in new[]
                 {
                     (BpmnDemoPipeline.StartEventVisualId, BpmnDemoPipeline.StartEventId, BpmnSemanticTypes.StartEvent),
                     (BpmnDemoPipeline.EndEventVisualId, BpmnDemoPipeline.EndEventId, BpmnSemanticTypes.EndEvent),
                 })
        {
            await harness.Pointer.ContextMenuDocumentPointAsync(harness.Scene,
                Center(NodeBody(harness.Scene, visualId).Bounds));
            Assert.False(harness.Host.CanOpenContextProperties());
            Assert.Null(await harness.Host.OpenPropertiesAsync(visualId));
            Assert.False(harness.Host.CaptureState().PropertiesFormOpen);
            Assert.True(DocumentCanvasPropertySnapshot.TryCreate(initialDocument, visualId,
                harness.Composition.PropertiesSchemaCatalog, out var internalSnapshot));
            Assert.Equal(semanticId, internalSnapshot!.SemanticId);
            Assert.Equal(typeId, internalSnapshot.TypeId);
            Assert.Empty(internalSnapshot.DataFields);
        }

        var finalState = harness.State;
        Assert.Same(initialDocument, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(initialState.DocumentRevision, finalState.DocumentRevision);
        Assert.Equal(initialState.HistoryStatus, finalState.HistoryStatus);
    }

    [Fact]
    public async Task CodeEditIsIndependentAndUndoRedoRefreshTheOpenPropertiesSnapshot()
    {
        await using var harness = await HostHarness.CreateAsync();
        var original = await harness.OpenNodePropertiesAsync(BpmnDemoPipeline.TaskVisualId);
        var visualsBefore = CaptureTaskVisuals(harness.Scene);
        var stateBefore = harness.State;
        var renderCountBefore = harness.Execution.RenderCount;

        var committed = await CommitFieldAsync(
            harness,
            original,
            "code",
            "REVIEW_CUSTOMER_ORDER");

        Assert.Equal(
            "REVIEW_CUSTOMER_ORDER",
            TaskProperty(harness, BpmnSemanticProperties.Code).TextValue);
        Assert.Equal("Review order", TaskProperty(harness, BpmnSemanticProperties.Name).TextValue);
        AssertField(committed, "code", "REVIEW_CUSTOMER_ORDER", PropertyValueKind.Text);
        AssertTaskVisualsEqual(visualsBefore, CaptureTaskVisuals(harness.Scene));
        AssertOneCommit(stateBefore, harness.State);
        Assert.Equal(renderCountBefore + 1, harness.Execution.RenderCount);

        await harness.Host.UndoAsync();
        Assert.Equal("REVIEW_ORDER", TaskProperty(harness, BpmnSemanticProperties.Code).TextValue);
        AssertCurrentField(harness, "code", "REVIEW_ORDER", PropertyValueKind.Text);
        AssertTaskVisualsEqual(visualsBefore, CaptureTaskVisuals(harness.Scene));

        await harness.Host.RedoAsync();
        Assert.Equal(
            "REVIEW_CUSTOMER_ORDER",
            TaskProperty(harness, BpmnSemanticProperties.Code).TextValue);
        AssertCurrentField(
            harness,
            "code",
            "REVIEW_CUSTOMER_ORDER",
            PropertyValueKind.Text);
        AssertTaskVisualsEqual(visualsBefore, CaptureTaskVisuals(harness.Scene));
    }

    [Fact]
    public async Task NameEditRebuildsTheProjectedAndSceneLabelWithoutChangingGeometryOrRoutes()
    {
        const string renamed = "Review customer order";
        await using var harness = await HostHarness.CreateAsync();
        var original = await harness.OpenNodePropertiesAsync(BpmnDemoPipeline.TaskVisualId);
        var visualsBefore = CaptureTaskVisuals(harness.Scene);

        var committed = await CommitFieldAsync(harness, original, "name", renamed);

        Assert.Equal(renamed, TaskProperty(harness, BpmnSemanticProperties.Name).TextValue);
        Assert.Equal("REVIEW_ORDER", TaskProperty(harness, BpmnSemanticProperties.Code).TextValue);
        AssertField(committed, "name", renamed, PropertyValueKind.Text);
        var renamedVisuals = CaptureTaskVisuals(harness.Scene);
        AssertGeometryAndRoutesEqual(visualsBefore, renamedVisuals);
        Assert.Equal(renamed, renamedVisuals.Label);
        Assert.Equal(
            renamed,
            Assert.Single(
                harness.State.ProjectedGraph!.Labels,
                label => label.Source.SemanticElementId == BpmnDemoPipeline.TaskId).Text);

        await harness.Host.UndoAsync();
        Assert.Equal("Review order", CaptureTaskVisuals(harness.Scene).Label);
        AssertCurrentField(harness, "name", "Review order", PropertyValueKind.Text);

        await harness.Host.RedoAsync();
        Assert.Equal(renamed, CaptureTaskVisuals(harness.Scene).Label);
        AssertCurrentField(harness, "name", renamed, PropertyValueKind.Text);
    }

    [Fact]
    public async Task ExternalAuthoritativeNameUpdateRefreshesTheOpenCleanFormAndScene()
    {
        const string renamed = "Externally updated review";
        await using var harness = await HostHarness.CreateAsync();
        var original = await harness.OpenNodePropertiesAsync(BpmnDemoPipeline.TaskVisualId);
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);

        var result = await harness.Session.ExecuteAsync(new UpdateSemanticElementNameCommand(
            original.DocumentId,
            harness.State.DocumentRevision,
            original.SemanticId,
            BpmnSemanticProperties.Name,
            renamed));

        Assert.True(result.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        AssertCurrentField(harness, "name", renamed, PropertyValueKind.Text);
        Assert.Equal(renamed, CaptureTaskVisuals(harness.Scene).Label);
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);

        CloseProperties(harness.Host);
        var refreshed = await harness.OpenNodePropertiesAsync(
            BpmnDemoPipeline.TaskVisualId);
        AssertField(refreshed, "name", renamed, PropertyValueKind.Text);
    }

    [Fact]
    public async Task ElementNumberIsTypedAndInvalidOrNoOpInputPerformsNoPersistentWork()
    {
        await using var harness = await HostHarness.CreateAsync();
        var original = await harness.OpenNodePropertiesAsync(BpmnDemoPipeline.TaskVisualId);
        var invalidDraft = new DocumentCanvasPropertiesDraft(original);
        DraftField(invalidDraft, "element-number").EditorValue = "20.5";
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.TaskVisualId,
            isDirty: invalidDraft.IsDirty));
        var documentBeforeInvalid = harness.Composition.Document.CaptureSnapshot();
        var stateBeforeInvalid = harness.State;
        var renderCountBeforeInvalid = harness.Execution.RenderCount;

        var invalid = await harness.Host.ApplyPropertiesAsync(invalidDraft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.ValidationFailed, invalid.Status);
        Assert.Contains("Element number", invalid.Message, StringComparison.Ordinal);
        Assert.Same(documentBeforeInvalid, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(stateBeforeInvalid.DocumentRevision, harness.State.DocumentRevision);
        Assert.Equal(stateBeforeInvalid.HistoryStatus, harness.State.HistoryStatus);
        Assert.Same(stateBeforeInvalid.CurrentScene, harness.State.CurrentScene);
        Assert.Equal(renderCountBeforeInvalid, harness.Execution.RenderCount);

        CloseProperties(harness.Host);
        var reopened = await harness.OpenNodePropertiesAsync(BpmnDemoPipeline.TaskVisualId);
        var exactNoOpDraft = new DocumentCanvasPropertiesDraft(reopened);
        var documentBeforeNoOp = harness.Composition.Document.CaptureSnapshot();
        var stateBeforeNoOp = harness.State;
        var renderCountBeforeNoOp = harness.Execution.RenderCount;

        var noOp = await harness.Host.ApplyPropertiesAsync(exactNoOpDraft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.NoChange, noOp.Status);
        Assert.Same(documentBeforeNoOp, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(stateBeforeNoOp.DocumentRevision, harness.State.DocumentRevision);
        Assert.Equal(stateBeforeNoOp.HistoryStatus, harness.State.HistoryStatus);
        Assert.Same(stateBeforeNoOp.CurrentScene, harness.State.CurrentScene);
        Assert.Equal(renderCountBeforeNoOp, harness.Execution.RenderCount);

        var visualsBefore = CaptureTaskVisuals(harness.Scene);
        var committed = await CommitFieldAsync(
            harness,
            Assert.IsType<DocumentCanvasPropertySnapshot>(noOp.Authoritative),
            "element-number",
            "21");
        var elementNumber = TaskProperty(harness, BpmnSemanticProperties.ElementNumber);
        Assert.Equal(PropertyValueKind.Integer, elementNumber.Kind);
        Assert.Equal(21L, elementNumber.IntegerValue);
        AssertField(committed, "element-number", "21", PropertyValueKind.Integer);
        AssertTaskVisualsEqual(visualsBefore, CaptureTaskVisuals(harness.Scene));

        await harness.Host.UndoAsync();
        Assert.Equal(20L, TaskProperty(
            harness,
            BpmnSemanticProperties.ElementNumber).IntegerValue);
        AssertCurrentField(harness, "element-number", "20", PropertyValueKind.Integer);

        await harness.Host.RedoAsync();
        Assert.Equal(21L, TaskProperty(
            harness,
            BpmnSemanticProperties.ElementNumber).IntegerValue);
        AssertCurrentField(harness, "element-number", "21", PropertyValueKind.Integer);
        AssertTaskVisualsEqual(visualsBefore, CaptureTaskVisuals(harness.Scene));
    }

    [Fact]
    public async Task MultilineDescriptionPersistsExactlyAndRemainsVisuallyNeutralAcrossHistory()
    {
        const string description =
            "Review customer details.\nCheck approval limits.\r\nRecord the decision.";
        await using var harness = await HostHarness.CreateAsync();
        var original = await harness.OpenNodePropertiesAsync(BpmnDemoPipeline.TaskVisualId);
        var visualsBefore = CaptureTaskVisuals(harness.Scene);

        var committed = await CommitFieldAsync(
            harness,
            original,
            "description",
            description);

        Assert.Equal(
            description,
            TaskProperty(harness, BpmnSemanticProperties.Description).TextValue);
        AssertField(committed, "description", description, PropertyValueKind.Text);
        AssertTaskVisualsEqual(visualsBefore, CaptureTaskVisuals(harness.Scene));

        await harness.Host.UndoAsync();
        Assert.Equal(
            InitialDescription,
            TaskProperty(harness, BpmnSemanticProperties.Description).TextValue);
        AssertCurrentField(
            harness,
            "description",
            InitialDescription,
            PropertyValueKind.Text);

        await harness.Host.RedoAsync();
        Assert.Equal(
            description,
            TaskProperty(harness, BpmnSemanticProperties.Description).TextValue);
        AssertCurrentField(harness, "description", description, PropertyValueKind.Text);
        AssertTaskVisualsEqual(visualsBefore, CaptureTaskVisuals(harness.Scene));
    }

    private static void AssertTaskIdentityAndFields(DocumentCanvasPropertySnapshot properties)
    {
        Assert.Equal(BpmnDemoPipeline.DemoDocumentId, properties.DocumentId);
        Assert.Equal(BpmnDemoPipeline.TaskVisualId, properties.VisualStateId);
        Assert.Equal(BpmnDemoPipeline.TaskId, properties.SemanticId);
        Assert.Equal(BpmnSemanticTypes.UserTask, properties.TypeId);
        Assert.False(properties.IsConnector);
        Assert.Collection(
            properties.DataFields,
            field => AssertDefinition(
                field,
                "code",
                "Code",
                BpmnSemanticProperties.Code,
                ElementPropertyEditorKind.SingleLineText,
                SemanticPropertyMutationKind.Property,
                "REVIEW_ORDER",
                PropertyValueKind.Text),
            field => AssertDefinition(
                field,
                "name",
                "Name",
                BpmnSemanticProperties.Name,
                ElementPropertyEditorKind.SingleLineText,
                SemanticPropertyMutationKind.Name,
                "Review order",
                PropertyValueKind.Text),
            field => AssertDefinition(
                field,
                "element-number",
                "Element number",
                BpmnSemanticProperties.ElementNumber,
                ElementPropertyEditorKind.Integer,
                SemanticPropertyMutationKind.Property,
                "20",
                PropertyValueKind.Integer),
            field => AssertDefinition(
                field,
                "description",
                "Description",
                BpmnSemanticProperties.Description,
                ElementPropertyEditorKind.MultilineText,
                SemanticPropertyMutationKind.Property,
                InitialDescription,
                PropertyValueKind.Text));
    }

    private static void AssertDefinition(
        DocumentCanvasDataPropertySnapshot field,
        string fieldId,
        string displayName,
        string propertyKey,
        ElementPropertyEditorKind editorKind,
        SemanticPropertyMutationKind mutationKind,
        string editorValue,
        PropertyValueKind valueKind)
    {
        Assert.Equal(fieldId, field.FieldId.Value);
        Assert.Equal(displayName, field.Definition.DisplayName);
        Assert.Equal(propertyKey, field.Definition.SemanticPropertyKey);
        Assert.Equal(editorKind, field.Definition.EditorKind);
        Assert.Equal(mutationKind, field.Definition.MutationKind);
        Assert.True(field.Definition.IsEditable);
        Assert.True(field.IsAvailable);
        Assert.True(field.CanEdit);
        Assert.Equal(editorValue, field.EditorValue);
        Assert.Equal(valueKind, Assert.IsType<PropertyValue>(field.Value).Kind);
    }

    private static async Task<DocumentCanvasPropertySnapshot> CommitFieldAsync(
        HostHarness harness,
        DocumentCanvasPropertySnapshot authoritative,
        string fieldId,
        string editorValue)
    {
        var draft = new DocumentCanvasPropertiesDraft(authoritative);
        DraftField(draft, fieldId).EditorValue = editorValue;
        Assert.True(draft.IsDirty);
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.TaskVisualId,
            isDirty: draft.IsDirty));

        var result = await harness.Host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        Assert.False(harness.Host.CaptureState().PropertiesFormDirty);
        return Assert.IsType<DocumentCanvasPropertySnapshot>(result.Authoritative);
    }

    private static DocumentCanvasDataPropertyDraft DraftField(
        DocumentCanvasPropertiesDraft draft,
        string fieldId)
    {
        Assert.True(draft.TryGetDataField(
            new ElementPropertyFieldId(fieldId),
            out var field));
        return Assert.IsType<DocumentCanvasDataPropertyDraft>(field);
    }

    private static void AssertField(
        DocumentCanvasPropertySnapshot properties,
        string fieldId,
        string editorValue,
        PropertyValueKind kind)
    {
        Assert.True(properties.TryGetDataField(
            new ElementPropertyFieldId(fieldId),
            out var field));
        var resolved = Assert.IsType<DocumentCanvasDataPropertySnapshot>(field);
        Assert.Equal(editorValue, resolved.EditorValue);
        Assert.Equal(kind, Assert.IsType<PropertyValue>(resolved.Value).Kind);
    }

    private static void AssertCurrentField(
        HostHarness harness,
        string fieldId,
        string editorValue,
        PropertyValueKind kind)
    {
        Assert.True(harness.Host.TryCaptureCurrentPropertiesForm(out var current));
        AssertField(
            Assert.IsType<DocumentCanvasPropertySnapshot>(current),
            fieldId,
            editorValue,
            kind);
    }

    private static PropertyValue TaskProperty(HostHarness harness, string propertyKey)
    {
        var document = harness.Composition.Document.CaptureSnapshot();
        Assert.True(document.SemanticModel.TryGetElement(
            BpmnDemoPipeline.TaskId,
            out var task));
        return Assert.IsType<PropertyValue>(task!.Properties[propertyKey]);
    }

    private static void AssertOneCommit(
        EditingSessionState before,
        EditingSessionState after)
    {
        Assert.Equal(before.DocumentRevision.Value + 1, after.DocumentRevision.Value);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.True(after.HistoryStatus.CanUndo);
        Assert.False(after.HistoryStatus.CanRedo);
    }

    private static TaskVisualSnapshot CaptureTaskVisuals(Canvas2DScene scene)
    {
        var body = NodeBody(scene, BpmnDemoPipeline.TaskVisualId);
        return new TaskVisualSnapshot(
            body.Id,
            body.Bounds,
            body.Transform,
            body.Geometry,
            body.Style,
            ConnectorPath(scene, BpmnDemoPipeline.FirstSequenceFlowVisualId),
            ConnectorPath(scene, BpmnDemoPipeline.SecondSequenceFlowVisualId),
            TaskLabelText(scene));
    }

    private static void AssertGeometryAndRoutesEqual(
        TaskVisualSnapshot expected,
        TaskVisualSnapshot actual)
    {
        Assert.Equal(expected.BodyId, actual.BodyId);
        Assert.Equal(expected.BodyBounds, actual.BodyBounds);
        Assert.Equal(expected.BodyTransform, actual.BodyTransform);
        Assert.Equal(expected.BodyGeometry, actual.BodyGeometry);
        Assert.Equal(expected.BodyStyle, actual.BodyStyle);
        Assert.Equal(expected.FirstRoute.AsEnumerable(), actual.FirstRoute.AsEnumerable());
        Assert.Equal(expected.SecondRoute.AsEnumerable(), actual.SecondRoute.AsEnumerable());
    }

    private static void AssertTaskVisualsEqual(
        TaskVisualSnapshot expected,
        TaskVisualSnapshot actual)
    {
        AssertGeometryAndRoutesEqual(expected, actual);
        Assert.Equal(expected.Label, actual.Label);
    }

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.IsVisible &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static ImmutableArray<PointD> ConnectorPath(
        Canvas2DScene scene,
        VisualStateId visualStateId)
    {
        var connector = Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == visualStateId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow) &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        return connector.Geometry.Points
            .Select(point => connector.Transform.TransformPoint(point))
            .ToImmutableArray();
    }

    private static Canvas2DSceneItem[] TaskLabelLines(Canvas2DScene scene) =>
        scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Label &&
                item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Text)
            .OrderBy(item => DocumentTextAnchor(item).Y)
            .ToArray();

    private static string TaskLabelText(Canvas2DScene scene) =>
        string.Join(" ", TaskLabelLines(scene)
            .Select(static item => item.Geometry.Content));

    private static PointD DocumentTextAnchor(Canvas2DSceneItem item) =>
        item.Transform.TransformPoint(item.Geometry.TextAnchor);

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));

    private static void CloseProperties(DocumentCanvasHost host)
    {
        Assert.True(host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false));
    }

    private sealed record TaskVisualSnapshot(
        SceneObjectId BodyId,
        RectD BodyBounds,
        Matrix2D BodyTransform,
        Canvas2DSceneGeometry BodyGeometry,
        Canvas2DSceneStyle BodyStyle,
        ImmutableArray<PointD> FirstRoute,
        ImmutableArray<PointD> SecondRoute,
        string Label);

    internal sealed class HostHarness : IAsyncDisposable
    {
        private HostHarness(
            DocumentCanvasHost host,
            DocumentCanvasComposition composition,
            RecordingPointerObserver pointer,
            RecordingRenderExecution execution)
        {
            Host = host;
            Composition = composition;
            Pointer = pointer;
            Execution = execution;
        }

        internal DocumentCanvasHost Host { get; }

        internal DocumentCanvasComposition Composition { get; }

        internal RecordingPointerObserver Pointer { get; }

        internal RecordingRenderExecution Execution { get; }

        internal EditingSession Session => Assert.IsType<EditingSession>(
            typeof(DocumentCanvasHost)
                .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(Host));

        internal EditingSessionState State => Assert.IsType<EditingSessionState>(
            Host.CaptureState().Session);

        internal Canvas2DScene Scene => Assert.IsType<Canvas2DScene>(State.CurrentScene);

        internal static async ValueTask<HostHarness> CreateAsync()
        {
            var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
            return await CreateAsync(composition);
        }

        internal static async ValueTask<HostHarness> CreateAsync(
            DocumentCanvasComposition composition)
        {
            ArgumentNullException.ThrowIfNull(composition);
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
                new Canvas2DSurfaceSize(900d, 600d, 1.25d));
            var pointer = new RecordingPointerObserver();
            var host = new DocumentCanvasHost(
                new FixedCompositionFactory(composition),
                renderer,
                new RecordingSurfaceObserverFactory(surface),
                new RecordingPointerObserverFactory(pointer));

            await host.InitializeAsync("phase-m31-canvas", "phase-m31-container");
            var state = host.CaptureState();
            Assert.True(state.IsInitialized);
            Assert.Equal(EditingSessionStatus.Ready, state.Session?.Status);
            return new HostHarness(host, composition, pointer, execution);
        }

        internal async Task<DocumentCanvasPropertySnapshot> OpenNodePropertiesAsync(
            VisualStateId visualStateId)
        {
            var scene = Scene;
            var bodyCenter = Center(NodeBody(scene, visualStateId).Bounds);
            await Pointer.ContextMenuDocumentPointAsync(
                scene,
                bodyCenter);
            Assert.Equal(
                visualStateId,
                Host.CaptureState().ContextMenu?.TargetVisualStateId);
            return Assert.IsType<DocumentCanvasPropertySnapshot>(
                await Host.OpenPropertiesAsync(visualStateId));
        }

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private sealed class FixedCompositionFactory(DocumentCanvasComposition composition) :
        IDocumentCanvasCompositionFactory
    {
        public ValueTask<DocumentCanvasComposition> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(composition);
        }
    }

    private sealed class RecordingSurfaceObserverFactory(
        RecordingSurfaceObserver observer) : ICanvasPresentationSurfaceObserverFactory
    {
        public ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
            string containerElementId,
            Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICanvasPresentationSurfaceObserver>(observer);
        }
    }

    private sealed class RecordingSurfaceObserver(Canvas2DSurfaceSize initial) :
        ICanvasPresentationSurfaceObserver
    {
        public ValueTask<Canvas2DSurfaceSize> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(initial);
        }

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

    internal sealed class RecordingPointerObserver : ICanvasPresentationPointerObserver
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

        internal Task ContextMenuDocumentPointAsync(
            Canvas2DScene scene,
            PointD documentPoint)
        {
            var css = scene.ViewportTransform.TransformPoint(documentPoint);
            return (Callback ?? throw new InvalidOperationException(
                "The pointer observer has no managed callback."))(new CanvasPointerInput(
                CanvasPointerEventKind.ContextMenu,
                PointerId: 1,
                Button: 2,
                Buttons: 0,
                IsPrimary: true,
                ClientX: css.X + 17d,
                ClientY: css.Y + 23d,
                CanvasLeft: 17d,
                CanvasTop: 23d,
                AltKey: false,
                ControlKey: false,
                MetaKey: false,
                ShiftKey: false,
                CaptureGeneration: 0));
        }
    }

    internal sealed class RecordingRenderExecution : ICanvas2DRenderExecution
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
