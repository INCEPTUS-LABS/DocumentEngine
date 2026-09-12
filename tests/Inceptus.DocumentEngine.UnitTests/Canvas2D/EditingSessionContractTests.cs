using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionContractTests
{
    [Fact]
    public void ConfigurationDefensivelyCopiesSessionContributions()
    {
        var subscriber = new RecordingSubscriber();
        IDocumentChangedSubscriber[] subscribers = [subscriber];

        var configuration = new EditingSessionConfiguration(
            new ProjectionEngine(),
            new LayoutEngine(),
            new AlgorithmId("test:layout"),
            new RoutingEngine(),
            new AlgorithmId("test:routing"),
            new Canvas2DSceneBuilder(),
            documentChangedSubscribers: subscribers);
        subscribers[0] = new RecordingSubscriber();

        Assert.Same(subscriber, Assert.Single(configuration.DocumentChangedSubscribers));
        Assert.Empty(configuration.CommandHandlers);
        Assert.Empty(configuration.CommandValidators);
        Assert.Empty(configuration.HistoryPolicies);
        Assert.Same(EditorStateSnapshot.Empty, configuration.InitialEditorState);
    }

    [Fact]
    public void ConfigurationRejectsNullContributionsWithoutPartialExposure()
    {
        IDocumentChangedSubscriber[] subscribers = [null!];

        var exception = Assert.Throws<ArgumentException>(() =>
            new EditingSessionConfiguration(
                new ProjectionEngine(),
                new LayoutEngine(),
                new AlgorithmId("test:layout"),
                new RoutingEngine(),
                new AlgorithmId("test:routing"),
                new Canvas2DSceneBuilder(),
                documentChangedSubscribers: subscribers));

        Assert.Equal("documentChangedSubscribers", exception.ParamName);
    }

    [Fact]
    public void FaultedStateIsImmutableOrderedAndNeverGraphicallyInteractive()
    {
        var editorState = new EditorStateSnapshot(activeToolId: "tool:test");
        var diagnostics = new[]
        {
            new Diagnostic("Z_TEST", DiagnosticSeverity.Error, "Last"),
            new Diagnostic("A_TEST", DiagnosticSeverity.Error, "First"),
        };
        var state = new EditingSessionState(
            new DocumentId("test:session-document"),
            new DocumentRevision(4),
            EditingSessionStatus.RuntimeFaulted,
            new EditingSessionGeneration(9),
            currentScene: null,
            lastKnownGoodScene: null,
            projectedGraph: null,
            layoutResult: null,
            routingResult: null,
            editorState,
            new HistoryStatus(0, canUndo: false, canRedo: false),
            diagnostics,
            presentationDiagnostics: null,
            isClosed: false);
        diagnostics[0] = new Diagnostic("MUTATED", DiagnosticSeverity.Error, "Ignored");

        Assert.Equal(EditingSessionStatus.RuntimeFaulted, state.Status);
        Assert.Same(editorState, state.EditorState);
        Assert.Equal(["A_TEST", "Z_TEST"], state.RuntimeDiagnostics.Select(static item => item.Code));
        Assert.Null(state.CurrentScene);
        Assert.Null(state.ProjectedGraph);
        Assert.Null(state.LayoutResult);
        Assert.Null(state.RoutingResult);
        Assert.False(state.IsGraphicalInteractionEnabled);
        Assert.False(state.IsDisplayingStaleScene);
    }

    [Fact]
    public void ClosedStateExposesNoRuntimeResultsOrSceneCurrency()
    {
        var state = new EditingSessionState(
            new DocumentId("test:closed-session"),
            DocumentRevision.Zero,
            EditingSessionStatus.RuntimeFaulted,
            new EditingSessionGeneration(1),
            currentScene: null,
            lastKnownGoodScene: null,
            projectedGraph: null,
            layoutResult: null,
            routingResult: null,
            EditorStateSnapshot.Empty,
            new HistoryStatus(0, canUndo: false, canRedo: false),
            runtimeDiagnostics: null,
            presentationDiagnostics: null,
            isClosed: true);

        Assert.True(state.IsClosed);
        Assert.Null(state.CurrentScene);
        Assert.Null(state.LastKnownGoodScene);
        Assert.False(state.IsGraphicalInteractionEnabled);
        Assert.False(state.IsDisplayingStaleScene);
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change) =>
            ValueTask.CompletedTask;
    }
}
