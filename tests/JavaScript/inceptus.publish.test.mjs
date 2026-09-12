import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { runInThisContext } from "node:vm";

const publishRuntimeUrl = new URL(
    "../../src/Inceptus.DocumentEngine.Canvas2D/Publishing/Assets/inceptus.publish.js",
    import.meta.url);
runInThisContext(readFileSync(publishRuntimeUrl, "utf8"), {
    filename: fileURLToPath(publishRuntimeUrl)
});

const {
    PublishedProcessViewer,
    PublishedTokenRuntime,
    PublishedViewport,
    connectorDurationMs,
    interpolateRoute,
    routeLength,
    startPublishedViewer
} = globalThis.InceptusPublishedViewer;

const point = (x, y) => ({ x, y });

function connector(id, sourceElementId, targetElementId, points) {
    return { id, sourceElementId, targetElementId, points };
}

function tokenNode(id, role, incomingConnectorIds = [], outgoingConnectorIds = []) {
    return { id, role, incomingConnectorIds, outgoingConnectorIds };
}

function publishedProcess(nodes, connectors, runtime = {}) {
    return {
        format: "Inceptus.PublishedProcess",
        formatVersion: 1,
        source: { documentId: "test:published", revision: 7, scopeId: "test:scope" },
        presentation: {
            nodes: nodes.map((node, index) => ({
                id: node.id,
                descriptor: "rectangle",
                bounds: { x: index * 100, y: index * 40, width: 40, height: 30 },
                label: node.id,
                description: node.description ?? ""
            })),
            connectors,
            items: [],
            contentBounds: { x: 0, y: 0, width: 600, height: 300 }
        },
        tokenGraph: { nodes },
        runtime: {
            activityDelayMs: 2000,
            tokenSpeedPxPerSecond: 300,
            minimumConnectorDurationMs: 150,
            ...runtime
        }
    };
}

function activityProcess() {
    const nodes = [
        tokenNode("start", "start", [], ["start-activity"]),
        tokenNode("activity", "activityDelay", ["start-activity"], ["activity-end"]),
        tokenNode("end", "end", ["activity-end"], [])
    ];
    const connectors = [
        connector("start-activity", "start", "activity", [point(20, 15), point(20, 15)]),
        connector("activity-end", "activity", "end", [point(120, 55), point(420, 55)])
    ];
    return publishedProcess(nodes, connectors);
}

function multipleStartProcess() {
    const nodes = [
        tokenNode("entry:a", "start", [], ["a-activity"]),
        tokenNode("entry:b", "start", [], ["b-activity"]),
        tokenNode("activity:a", "activityDelay", ["a-activity"], ["a-end"]),
        tokenNode("activity:b", "activityDelay", ["b-activity"], ["b-end"]),
        tokenNode("finish", "end", ["a-end", "b-end"], [])
    ];
    const process = publishedProcess(nodes, [
        connector("a-activity", "entry:a", "activity:a", [point(76, 58), point(180, 58)]),
        connector("b-activity", "entry:b", "activity:b", [point(76, 168), point(180, 168)]),
        connector("a-end", "activity:a", "finish", [point(220, 58), point(520, 58)]),
        connector("b-end", "activity:b", "finish", [point(220, 168), point(520, 168)])
    ]);
    for (const [id, x, y] of [["entry:a", 40, 40], ["entry:b", 40, 150]]) {
        Object.assign(process.presentation.nodes.find(node => node.id === id), {
            descriptor: "ellipse",
            bounds: { x, y, width: 36, height: 36 },
            label: "Same visible name"
        });
    }
    return process;
}

function nodePointer(viewer, nodeId, options = {}) {
    const bounds = viewer.process.presentation.nodes.find(node => node.id === nodeId).bounds;
    const surface = viewer.canvas.getBoundingClientRect();
    return {
        pointerId: 7,
        button: 0,
        clientX: surface.left + viewer.viewport.offsetX +
            ((bounds.x + (bounds.width / 2)) * viewer.viewport.scale),
        clientY: surface.top + viewer.viewport.offsetY +
            ((bounds.y + (bounds.height / 2)) * viewer.viewport.scale),
        ...options
    };
}

function clickPublishedNode(viewer, nodeId) {
    const event = nodePointer(viewer, nodeId);
    viewer.canvas.dispatch("pointerdown", event);
    viewer.canvas.dispatch("pointerup", event);
    viewer.canvas.dispatch("click", event);
}

function mergeProcess(randomValues) {
    const nodes = [
        tokenNode("start", "start", [], ["start-split"]),
        tokenNode("split", "splitInvariant", ["start-split"], ["a", "b", "c"]),
        tokenNode("merge", "mergeInvariant", ["a", "b", "c"], ["merge-end"]),
        tokenNode("end", "end", ["merge-end"], [])
    ];
    const connectors = [
        connector("start-split", "start", "split", [point(0, 0), point(0, 0)]),
        connector("a", "split", "merge", [point(10, 0), point(10, 0)]),
        connector("b", "split", "merge", [point(10, 10), point(10, 10)]),
        connector("c", "split", "merge", [point(10, 20), point(10, 20)]),
        connector("merge-end", "merge", "end", [point(20, 10), point(320, 10)])
    ];
    let index = 0;
    return new PublishedTokenRuntime(publishedProcess(nodes, connectors), {
        random: () => randomValues[index++] ?? 0
    });
}

function parallelRuntime(incomingIds, outgoingIds, randomValues = []) {
    const selectorRole = incomingIds.length === 1 ? "passThrough" : "splitInvariant";
    const nodes = [
        tokenNode("start", "start", [], ["start-selector"]),
        tokenNode("selector", selectorRole, ["start-selector"], incomingIds),
        tokenNode("parallel", "parallelSynchronize", incomingIds, outgoingIds),
        ...outgoingIds.map(id => tokenNode(`${id}-end`, "end", [id], []))
    ];
    const connectors = [
        connector("start-selector", "start", "selector", [point(0, 0), point(0, 0)]),
        ...incomingIds.map((id, index) => connector(
            id,
            "selector",
            "parallel",
            [point(10, index * 10), point(10, index * 10)])),
        ...outgoingIds.map((id, index) => connector(
            id,
            "parallel",
            `${id}-end`,
            [point(20, index * 10), point(320, index * 10)]))
    ];
    let index = 0;
    return new PublishedTokenRuntime(publishedProcess(nodes, connectors), {
        random: () => randomValues[index++] ?? 0
    });
}

function wheelInput(deltaX, deltaY, deltaMode = 0, options = {}) {
    return {
        deltaX,
        deltaY,
        deltaMode,
        defaultPrevented: false,
        ...options,
        preventDefault() { this.defaultPrevented = true; }
    };
}

function withViewerStartupEnvironment(process, action) {
    const originalWindow = globalThis.window;
    const originalDocument = globalThis.document;
    const originalCanvas = globalThis.HTMLCanvasElement;
    const originalRequest = globalThis.requestAnimationFrame;
    const originalCancel = globalThis.cancelAnimationFrame;
    const hadBootstrap = Object.hasOwn(
        globalThis,
        "__INCEPTUS_PUBLISHED_PROCESS__");
    const originalBootstrap = globalThis.__INCEPTUS_PUBLISHED_PROCESS__;
    class FakeCanvas {
        constructor() {
            this.dataset = {};
            this.listeners = new Map();
            this.bounds = { left: 37, top: 29, width: 800, height: 600 };
        }
        getContext() { return {}; }
        getBoundingClientRect() { return this.bounds; }
        addEventListener(name, listener) { this.listeners.set(name, listener); }
        removeEventListener(name) { this.listeners.delete(name); }
        dispatch(name, event) { this.listeners.get(name)?.(event); }
        setPointerCapture() {}
        hasPointerCapture() { return false; }
        releasePointerCapture() {}
    }
    const canvas = new FakeCanvas();
    const status = { hidden: false, textContent: "Loading process…" };
    const createControl = () => ({
        listeners: new Map(),
        addEventListener(name, listener) { this.listeners.set(name, listener); },
        removeEventListener(name) { this.listeners.delete(name); },
        click() { this.listeners.get("click")?.(); }
    });
    const startButton = createControl();
    const startGuidance = { hidden: true, textContent: "" };
    const zoomInButton = createControl();
    const zoomOutButton = createControl();
    const elements = new Map([
        ["process-canvas", canvas],
        ["viewer-status", status],
        ["start-process", startButton],
        ["start-guidance", startGuidance],
        ["zoom-in", zoomInButton],
        ["zoom-out", zoomOutButton]
    ]);
    try {
        globalThis.window = {
            devicePixelRatio: 1,
            addEventListener() {},
            removeEventListener() {}
        };
        globalThis.document = { getElementById: id => elements.get(id) ?? null };
        globalThis.HTMLCanvasElement = FakeCanvas;
        globalThis.requestAnimationFrame = () => 11;
        globalThis.cancelAnimationFrame = () => {};
        if (process === undefined) {
            delete globalThis.__INCEPTUS_PUBLISHED_PROCESS__;
        } else {
            globalThis.__INCEPTUS_PUBLISHED_PROCESS__ = process;
        }
        return action({ canvas, startButton, startGuidance, status, zoomInButton, zoomOutButton });
    } finally {
        globalThis.window = originalWindow;
        globalThis.document = originalDocument;
        globalThis.HTMLCanvasElement = originalCanvas;
        globalThis.requestAnimationFrame = originalRequest;
        globalThis.cancelAnimationFrame = originalCancel;
        if (hadBootstrap) {
            globalThis.__INCEPTUS_PUBLISHED_PROCESS__ = originalBootstrap;
        } else {
            delete globalThis.__INCEPTUS_PUBLISHED_PROCESS__;
        }
    }
}

test("Start creates one independent runtime token per activation", () => {
    const runtime = new PublishedTokenRuntime(activityProcess());

    assert.equal(runtime.start(0), "token-1");
    assert.equal(runtime.captureState().activeTokenCount, 1);
    runtime.start(100);
    runtime.start(200);

    const state = runtime.captureState();
    assert.equal(state.startedTokenCount, 3);
    assert.equal(state.activeTokenCount, 3);
    assert.deepEqual(state.tokens.map(token => token.id), ["token-1", "token-2", "token-3"]);
});

test("Multiple Starts activate the requested identity without choosing the first or every Start", () => {
    const process = multipleStartProcess();
    const before = structuredClone(process);
    const runtime = new PublishedTokenRuntime(process);

    assert.equal(runtime.activateStart("entry:b", 0), "token-1");
    assert.deepEqual(runtime.captureState().tokens.map(token => token.connectorId), ["b-activity"]);
    runtime.activateStart("entry:a", 100);
    runtime.activateStart("entry:b", 200);
    runtime.activateStart("entry:a", 300);

    const state = runtime.captureState();
    assert.equal(state.startedTokenCount, 4);
    assert.equal(state.activeTokenCount, 4);
    assert.deepEqual(state.tokens.map(token => token.id), ["token-1", "token-2", "token-3", "token-4"]);
    assert.deepEqual(state.tokens.map(token => token.connectorId),
        ["b-activity", "a-activity", "b-activity", "a-activity"]);
    assert.deepEqual(state.tokens.map(token => token.travelStartedAt), [0, 100, 200, 300]);
    assert.deepEqual(process, before);
});

test("Explicit Start activation rejects unknown and non-Start identities without runtime changes", () => {
    const runtime = new PublishedTokenRuntime(multipleStartProcess());
    runtime.activateStart("entry:b", 10);
    const before = runtime.captureState();

    for (const id of ["missing", "activity:a", "finish", "", null]) {
        assert.throws(() => runtime.activateStart(id, 20), /Start/);
        assert.deepEqual(runtime.captureState(), before);
    }
    assert.throws(() => runtime.activateStart("entry:a", NaN), /time/i);
    assert.deepEqual(runtime.captureState(), before);
});

test("Targetless Start is a singleton convenience and never selects among multiple Starts", () => {
    const multiple = new PublishedTokenRuntime(multipleStartProcess());
    const before = multiple.captureState();
    assert.throws(() => multiple.start(0), /one Start/);
    assert.deepEqual(multiple.captureState(), before);

    const single = new PublishedTokenRuntime(activityProcess());
    assert.equal(single.activateStart("start", 0), "token-1");
    assert.equal(single.start(100), "token-2");
    assert.equal(single.captureState().startedTokenCount, 2);
});

test("Zero-Start published input fails during startup with a bounded diagnostic", () => {
    const process = activityProcess();
    process.tokenGraph.nodes.find(node => node.id === "start").role = "passThrough";
    withViewerStartupEnvironment(process, ({ status, canvas }) => {
        assert.equal(startPublishedViewer(), null);
        assert.equal(status.hidden, false);
        assert.equal(status.textContent, "This published process could not be displayed.");
        assert.equal(canvas.listeners.size, 0);
    });
});

test("Multi-Start viewer disables the targetless button and gives direct activation guidance", () => {
    withViewerStartupEnvironment(multipleStartProcess(), ({ startButton, startGuidance, status }) => {
        const viewer = startPublishedViewer();
        assert.ok(viewer instanceof PublishedProcessViewer);
        assert.equal(status.hidden, true);
        assert.equal(startButton.disabled, true);
        assert.equal(startGuidance.hidden, false);
        assert.equal(startGuidance.textContent, "Click a Start element in the diagram.");
        assert.doesNotThrow(() => startButton.click());
        assert.equal(viewer.runtime.captureState().startedTokenCount, 0);
        viewer.dispose();
    });
});

test("Singleton viewer keeps global Start and direct activation independently usable", () => {
    withViewerStartupEnvironment(activityProcess(), ({ startButton, startGuidance }) => {
        const viewer = startPublishedViewer();
        assert.equal(startButton.disabled, false);
        assert.equal(startGuidance.hidden, true);
        startButton.click();
        clickPublishedNode(viewer, "start");
        assert.equal(viewer.runtime.captureState().startedTokenCount, 2);
        viewer.dispose();
    });
});

test("Clicking Start B then A B A creates one token per gesture at the chosen Start", () => {
    const process = multipleStartProcess();
    const before = structuredClone(process);
    withViewerStartupEnvironment(process, () => {
        const viewer = startPublishedViewer();
        clickPublishedNode(viewer, "entry:b");
        assert.deepEqual(viewer.runtime.captureState().tokens.map(token => token.connectorId), ["b-activity"]);
        for (const id of ["entry:a", "entry:b", "entry:a"]) clickPublishedNode(viewer, id);
        assert.equal(viewer.runtime.captureState().startedTokenCount, 4);
        assert.deepEqual(viewer.runtime.captureState().tokens.map(token => token.connectorId),
            ["b-activity", "a-activity", "b-activity", "a-activity"]);
        assert.deepEqual(process, before);
        viewer.dispose();
    });
});

test("Start identity hit testing survives pointer Pan wheel Pan and plus minus Zoom", () => {
    withViewerStartupEnvironment(multipleStartProcess(), ({ canvas, zoomInButton, zoomOutButton }) => {
        const viewer = startPublishedViewer();
        const frozen = structuredClone(viewer.process);
        canvas.dispatch("pointerdown", { pointerId: 2, button: 0, clientX: 760, clientY: 500 });
        canvas.dispatch("pointermove", { pointerId: 2, clientX: 811, clientY: 478 });
        canvas.dispatch("pointerup", { pointerId: 2, clientX: 811, clientY: 478 });
        canvas.dispatch("wheel", wheelInput(-13, 23));
        zoomInButton.click();
        clickPublishedNode(viewer, "entry:b");
        zoomOutButton.click();
        clickPublishedNode(viewer, "entry:a");
        assert.deepEqual(viewer.runtime.captureState().tokens.map(token => token.connectorId),
            ["b-activity", "a-activity"]);
        assert.deepEqual(viewer.process, frozen);
        viewer.dispose();
    });
});

test("Dragging from a Start pans without activation even after returning to its press position", () => {
    withViewerStartupEnvironment(multipleStartProcess(), ({ canvas }) => {
        const viewer = startPublishedViewer();
        const event = nodePointer(viewer, "entry:b");
        const before = viewer.viewport.capture();
        canvas.dispatch("pointerdown", event);
        canvas.dispatch("pointermove", { ...event, clientX: event.clientX + 35, clientY: event.clientY + 20 });
        assert.equal(viewer.viewport.offsetX, before.offsetX + 35);
        assert.equal(viewer.viewport.offsetY, before.offsetY + 20);
        canvas.dispatch("pointermove", event);
        canvas.dispatch("pointerup", event);
        canvas.dispatch("click", event);
        assert.equal(viewer.runtime.captureState().startedTokenCount, 0);
        viewer.dispose();
    });
});

test("Cancelled or interrupted Start gestures never activate on a later pointer release", () => {
    withViewerStartupEnvironment(multipleStartProcess(), ({ canvas, zoomInButton }) => {
        const viewer = startPublishedViewer();
        for (const interrupt of ["pointercancel", "lostpointercapture", "wheel", "zoom"]) {
            const event = nodePointer(viewer, "entry:a");
            canvas.dispatch("pointerdown", event);
            if (interrupt === "wheel") canvas.dispatch("wheel", wheelInput(0, 10));
            else if (interrupt === "zoom") zoomInButton.click();
            else canvas.dispatch(interrupt, event);
            canvas.dispatch("pointerup", nodePointer(viewer, "entry:a"));
        }
        assert.equal(viewer.runtime.captureState().startedTokenCount, 0);
        clickPublishedNode(viewer, "entry:b");
        assert.equal(viewer.runtime.captureState().startedTokenCount, 1);
        viewer.dispose();
        clickPublishedNode(viewer, "entry:a");
        assert.equal(viewer.runtime.captureState().startedTokenCount, 1);
    });
});

test("Only a primary click on the same unambiguous Start shape activates it", () => {
    withViewerStartupEnvironment(multipleStartProcess(), ({ canvas }) => {
        const viewer = startPublishedViewer();
        clickPublishedNode(viewer, "activity:a");
        const secondary = nodePointer(viewer, "entry:a", { button: 2 });
        canvas.dispatch("pointerdown", secondary);
        canvas.dispatch("pointerup", secondary);
        canvas.dispatch("pointerdown", nodePointer(viewer, "entry:a"));
        canvas.dispatch("pointerup", nodePointer(viewer, "entry:b"));
        const corner = nodePointer(viewer, "entry:a");
        corner.clientX -= 17 * viewer.viewport.scale;
        corner.clientY -= 17 * viewer.viewport.scale;
        canvas.dispatch("pointerdown", corner);
        canvas.dispatch("pointerup", corner);
        assert.equal(viewer.runtime.captureState().startedTokenCount, 0);
        viewer.dispose();
    });
    const overlap = multipleStartProcess();
    overlap.presentation.nodes.find(node => node.id === "entry:b").bounds =
        { ...overlap.presentation.nodes.find(node => node.id === "entry:a").bounds };
    withViewerStartupEnvironment(overlap, () => {
        const viewer = startPublishedViewer();
        clickPublishedNode(viewer, "entry:a");
        assert.equal(viewer.runtime.captureState().startedTokenCount, 0);
        viewer.dispose();
    });
});

test("Different Start activations preserve independent Activity clocks and End consumption", () => {
    const process = multipleStartProcess();
    for (const id of ["a-activity", "b-activity"]) {
        const edge = process.presentation.connectors.find(item => item.id === id);
        edge.points = [edge.points[0], { ...edge.points[0] }];
    }
    const runtime = new PublishedTokenRuntime(process);
    runtime.activateStart("entry:b", 0);
    runtime.activateStart("entry:a", 500);
    assert.deepEqual(runtime.tick(1999).tokens.map(token => token.state),
        ["activity-delay", "activity-delay"]);
    assert.deepEqual(runtime.tick(2000).tokens.map(token => token.state),
        ["travelling", "activity-delay"]);
    assert.deepEqual(runtime.tick(2500).tokens.map(token => token.travelStartedAt), [2000, 2500]);
    assert.equal(runtime.tick(3000).activeTokenCount, 1);
    assert.equal(runtime.tick(3500).activeTokenCount, 0);
});

test("Frozen route interpolation follows every polyline segment", () => {
    const route = [point(0, 0), point(30, 0), point(30, 40)];

    assert.equal(routeLength(route), 70);
    assert.deepEqual(interpolateRoute(route, 15), point(15, 0));
    assert.deepEqual(interpolateRoute(route, 50), point(30, 20));
    assert.deepEqual(interpolateRoute(route, 500), point(30, 40));
});

test("Connector duration uses configured speed and nonzero minimum", () => {
    assert.equal(connectorDurationMs([point(0, 0), point(300, 0)], 300, 150), 1000);
    assert.equal(connectorDurationMs([point(0, 0), point(3, 0)], 300, 150), 150);
    assert.equal(connectorDurationMs([point(0, 0), point(0, 0)], 300, 150), 0);
});

test("Activity waits exactly 2000 milliseconds before continuing", () => {
    const runtime = new PublishedTokenRuntime(activityProcess());
    runtime.start(0);

    assert.equal(runtime.tick(1999).tokens[0].state, "activity-delay");
    const released = runtime.tick(2000).tokens[0];
    assert.equal(released.state, "travelling");
    assert.equal(released.connectorId, "activity-end");
    assert.equal(released.travelStartedAt, 2000);
});

test("Concurrent Activity tokens keep independent delay clocks", () => {
    const runtime = new PublishedTokenRuntime(activityProcess());
    runtime.start(0);
    runtime.start(500);

    let state = runtime.tick(2000);
    assert.deepEqual(state.tokens.map(token => token.state), ["travelling", "activity-delay"]);
    state = runtime.tick(2500);
    assert.deepEqual(state.tokens.map(token => token.state), ["travelling", "travelling"]);
    assert.deepEqual(state.tokens.map(token => token.travelStartedAt), [2000, 2500]);
});

test("Split Invariant emits on exactly one deterministic random branch", () => {
    const nodes = [
        tokenNode("start", "start", [], ["start-split"]),
        tokenNode("split", "splitInvariant", ["start-split"], ["left", "right"]),
        tokenNode("left-end", "end", ["left"], []),
        tokenNode("right-end", "end", ["right"], [])
    ];
    const connectors = [
        connector("start-split", "start", "split", [point(0, 0), point(0, 0)]),
        connector("left", "split", "left-end", [point(0, 0), point(300, 0)]),
        connector("right", "split", "right-end", [point(0, 0), point(0, 300)])
    ];
    const runtime = new PublishedTokenRuntime(publishedProcess(nodes, connectors), {
        random: () => 0.75
    });

    runtime.start(0);

    const token = runtime.captureState().tokens[0];
    assert.equal(token.connectorId, "right");
    assert.equal(runtime.captureState().activeTokenCount, 1);
});

test("Merge does not fire for two tokens from the same incoming connector", () => {
    const runtime = mergeProcess([0, 0]);
    runtime.start(0);
    runtime.start(0);

    const state = runtime.captureState();
    assert.equal(state.activeTokenCount, 2);
    assert.equal(state.mergeWaiting.merge.a, 2);
    assert.equal(state.mergeWaiting.merge.b, 0);
    assert.equal(state.tokens.every(token => token.state === "merge-wait"), true);
});

test("Merge consumes two distinct inputs and produces one output", () => {
    const runtime = mergeProcess([0, 0.4]);
    runtime.start(0);
    runtime.start(0);

    const state = runtime.captureState();
    assert.equal(state.activeTokenCount, 1);
    assert.equal(state.tokens[0].connectorId, "merge-end");
    assert.equal(state.mergeWaiting.merge.a, 0);
    assert.equal(state.mergeWaiting.merge.b, 0);
});

test("Merge repeatedly fires using stable incoming connector order", () => {
    const runtime = mergeProcess([0, 0, 0.4, 0.8]);
    runtime.start(0);
    runtime.start(0);
    runtime.start(0);
    runtime.start(0);

    const state = runtime.captureState();
    assert.equal(state.activeTokenCount, 2);
    assert.deepEqual(state.tokens.map(token => token.connectorId), ["merge-end", "merge-end"]);
    assert.deepEqual(state.tokens.map(token => token.id), ["token-1", "token-2"]);
    assert.deepEqual(state.mergeWaiting.merge, { a: 0, b: 0, c: 0 });
});

test("Parallel Split emits exactly one token on every outgoing connector", () => {
    const runtime = parallelRuntime(["in"], ["x", "y", "z"]);

    runtime.start(0);

    const state = runtime.captureState();
    assert.equal(state.activeTokenCount, 3);
    assert.deepEqual(state.tokens.map(token => token.connectorId), ["x", "y", "z"]);
    assert.deepEqual(state.parallelWaiting.parallel, { in: 0 });
});

test("Parallel Join waits until every distinct incoming connector has a token", () => {
    const runtime = parallelRuntime(["a", "b", "c"], ["out"], [0, 0.34, 0.67]);

    runtime.start(0);
    runtime.start(0);
    let state = runtime.captureState();
    assert.equal(state.tokens.some(token => token.connectorId === "out"), false);
    assert.deepEqual(state.parallelWaiting.parallel, { a: 1, b: 1, c: 0 });

    runtime.start(0);
    state = runtime.captureState();
    assert.deepEqual(state.tokens.map(token => token.connectorId), ["out"]);
    assert.deepEqual(state.parallelWaiting.parallel, { a: 0, b: 0, c: 0 });
});

test("Parallel synchronization ignores same-input duplicates when other inputs are empty", () => {
    const runtime = parallelRuntime(["a", "b", "c"], ["out"], [0, 0, 0]);

    runtime.start(0);
    runtime.start(0);
    runtime.start(0);

    const state = runtime.captureState();
    assert.equal(state.tokens.some(token => token.connectorId === "out"), false);
    assert.deepEqual(state.parallelWaiting.parallel, { a: 3, b: 0, c: 0 });
});

test("Parallel Join consumes one waiting token from every input", () => {
    const runtime = parallelRuntime(["a", "b", "c"], ["out"], [0, 0, 0.34, 0.67]);

    runtime.start(0);
    runtime.start(0);
    runtime.start(0);
    runtime.start(0);

    const state = runtime.captureState();
    assert.deepEqual(state.parallelWaiting.parallel, { a: 1, b: 0, c: 0 });
    assert.equal(state.tokens.filter(token => token.connectorId === "out").length, 1);
});

test("Parallel synchronization repeatedly fires complete N-of-N input sets", () => {
    const runtime = parallelRuntime(
        ["a", "b", "c"],
        ["out"],
        [0, 0.34, 0.67, 0, 0.34, 0.67]);

    for (let index = 0; index < 6; index++) runtime.start(0);

    const state = runtime.captureState();
    assert.equal(state.tokens.filter(token => token.connectorId === "out").length, 2);
    assert.deepEqual(state.parallelWaiting.parallel, { a: 0, b: 0, c: 0 });
});

test("Combined Parallel Join and Split consumes all inputs and fans out to all outputs", () => {
    const runtime = parallelRuntime(["a", "b", "c"], ["x", "y"], [0, 0.34, 0.67]);

    runtime.start(0);
    runtime.start(0);
    runtime.start(0);

    const state = runtime.captureState();
    assert.deepEqual(state.tokens.map(token => token.connectorId), ["x", "y"]);
    assert.deepEqual(state.parallelWaiting.parallel, { a: 0, b: 0, c: 0 });
});

test("Parallel fan-out creates independent runtime token identities", () => {
    const runtime = parallelRuntime(["in"], ["x", "y", "z"]);

    runtime.start(0);

    const tokens = runtime.captureState().tokens;
    assert.equal(new Set(tokens.map(token => token.id)).size, 3);
    assert.deepEqual(tokens.map(token => token.id), ["token-1", "token-2", "token-3"]);
    assert.deepEqual(tokens.map(token => token.connectorId), ["x", "y", "z"]);
});

test("End removes an arriving token", () => {
    const nodes = [
        tokenNode("start", "start", [], ["finish"]),
        tokenNode("end", "end", ["finish"], [])
    ];
    const runtime = new PublishedTokenRuntime(publishedProcess(nodes, [
        connector("finish", "start", "end", [point(0, 0), point(0, 0)])
    ]));

    runtime.start(0);

    assert.equal(runtime.captureState().startedTokenCount, 1);
    assert.equal(runtime.captureState().activeTokenCount, 0);
});

test("Pan changes only viewport state and leaves frozen geometry intact", () => {
    const process = activityProcess();
    const before = structuredClone(process.presentation);
    const viewport = new PublishedViewport(process.presentation.contentBounds, 800, 600);
    const original = viewport.capture();

    viewport.pan(27, -13);

    const after = viewport.capture();
    assert.equal(after.offsetX, original.offsetX + 27);
    assert.equal(after.offsetY, original.offsetY - 13);
    assert.deepEqual(process.presentation, before);
});

test("Pointer-correct zoom changes only viewport state", () => {
    const process = activityProcess();
    const before = structuredClone(process.presentation);
    const viewport = new PublishedViewport(process.presentation.contentBounds, 800, 600);
    const anchorBefore = viewport.toDocument(320, 210);

    viewport.zoomAt(1.5, 320, 210);

    assert.deepEqual(viewport.toDocument(320, 210), anchorBefore);
    assert.deepEqual(process.presentation, before);
});

test("Dragging over a node pans the viewport and cannot edit element geometry", () => {
    const originalWindow = globalThis.window;
    const originalCanvas = globalThis.HTMLCanvasElement;
    const originalRequest = globalThis.requestAnimationFrame;
    const originalCancel = globalThis.cancelAnimationFrame;
    class FakeCanvas {
        constructor() {
            this.dataset = {};
            this.listeners = new Map();
        }
        getContext() { return {}; }
        getBoundingClientRect() { return { left: 0, top: 0, width: 800, height: 600 }; }
        addEventListener(name, listener) { this.listeners.set(name, listener); }
        removeEventListener(name) { this.listeners.delete(name); }
        setPointerCapture() {}
        hasPointerCapture() { return true; }
        releasePointerCapture() {}
    }
    try {
        globalThis.window = {
            devicePixelRatio: 1,
            addEventListener() {},
            removeEventListener() {}
        };
        globalThis.HTMLCanvasElement = FakeCanvas;
        globalThis.requestAnimationFrame = () => 11;
        globalThis.cancelAnimationFrame = () => {};
        const process = activityProcess();
        const before = structuredClone(process.presentation.nodes);
        const viewer = new PublishedProcessViewer(new FakeCanvas(), process);
        const viewportBefore = viewer.viewport.capture();

        viewer.pointerDown({ pointerId: 4, clientX: 120, clientY: 80 });
        viewer.pointerMove({ pointerId: 4, clientX: 155, clientY: 102 });
        viewer.pointerUp({ pointerId: 4 });

        assert.equal(viewer.viewport.offsetX, viewportBefore.offsetX + 35);
        assert.equal(viewer.viewport.offsetY, viewportBefore.offsetY + 22);
        assert.deepEqual(process.presentation.nodes, before);
        viewer.dispose();
    } finally {
        globalThis.window = originalWindow;
        globalThis.HTMLCanvasElement = originalCanvas;
        globalThis.requestAnimationFrame = originalRequest;
        globalThis.cancelAnimationFrame = originalCancel;
    }
});

test("Compact plus and minus controls are the viewer Zoom authority", () => {
    withViewerStartupEnvironment(
        activityProcess(),
        ({ zoomInButton, zoomOutButton }) => {
            const viewer = startPublishedViewer();
            const initialScale = viewer.viewport.scale;

            zoomInButton.click();
            const increasedScale = viewer.viewport.scale;
            zoomOutButton.click();

            assert.ok(increasedScale > initialScale);
            assert.ok(viewer.viewport.scale < increasedScale);
            assert.ok(Math.abs(viewer.viewport.scale - initialScale) < 1e-12);
            viewer.dispose();
        });
});

test("Vertical wheel input pans vertically, preserves scale, and prevents page scroll", () => {
    withViewerStartupEnvironment(activityProcess(), ({ canvas }) => {
        const viewer = startPublishedViewer();
        const before = viewer.viewport.capture();
        const event = wheelInput(0, 25);

        canvas.dispatch("wheel", event);

        assert.equal(viewer.viewport.offsetX, before.offsetX);
        assert.equal(viewer.viewport.offsetY, before.offsetY - 25);
        assert.equal(viewer.viewport.scale, before.scale);
        assert.equal(event.defaultPrevented, true);
        viewer.dispose();
    });
});

test("Horizontal wheel input pans horizontally and preserves scale", () => {
    withViewerStartupEnvironment(activityProcess(), ({ canvas }) => {
        const viewer = startPublishedViewer();
        const before = viewer.viewport.capture();
        const event = wheelInput(-18, 0);

        canvas.dispatch("wheel", event);

        assert.equal(viewer.viewport.offsetX, before.offsetX + 18);
        assert.equal(viewer.viewport.offsetY, before.offsetY);
        assert.equal(viewer.viewport.scale, before.scale);
        assert.equal(event.defaultPrevented, true);
        viewer.dispose();
    });
});

test("Combined touchpad-like line deltas pan both axes without Zoom", () => {
    withViewerStartupEnvironment(activityProcess(), ({ canvas }) => {
        const viewer = startPublishedViewer();
        const before = viewer.viewport.capture();
        const event = wheelInput(2, -3, 1);

        canvas.dispatch("wheel", event);

        assert.equal(viewer.viewport.offsetX, before.offsetX - 32);
        assert.equal(viewer.viewport.offsetY, before.offsetY + 48);
        assert.equal(viewer.viewport.scale, before.scale);
        assert.equal(event.defaultPrevented, true);
        viewer.dispose();
    });
});

test("Repeated wheel Pan accumulates deterministically without mutating geometry", () => {
    withViewerStartupEnvironment(activityProcess(), ({ canvas }) => {
        const viewer = startPublishedViewer();
        const beforeViewport = viewer.viewport.capture();
        const beforePresentation = structuredClone(viewer.process.presentation);
        const events = [
            wheelInput(4, 5),
            wheelInput(-2, 7),
            wheelInput(3, -4)
        ];

        for (const event of events) canvas.dispatch("wheel", event);

        assert.equal(viewer.viewport.offsetX, beforeViewport.offsetX - 5);
        assert.equal(viewer.viewport.offsetY, beforeViewport.offsetY - 8);
        assert.equal(viewer.viewport.scale, beforeViewport.scale);
        assert.equal(events.every(event => event.defaultPrevented), true);
        assert.deepEqual(viewer.process.presentation, beforePresentation);
        viewer.dispose();
    });
});

test("Modified wheel input cannot silently re-enable model Zoom", () => {
    withViewerStartupEnvironment(activityProcess(), ({ canvas }) => {
        const viewer = startPublishedViewer();
        const before = viewer.viewport.capture();
        const event = wheelInput(0, -40, 0, { ctrlKey: true });

        canvas.dispatch("wheel", event);

        assert.equal(viewer.viewport.offsetY, before.offsetY + 40);
        assert.equal(viewer.viewport.scale, before.scale);
        assert.equal(event.defaultPrevented, true);
        viewer.dispose();
    });
});

test("Standalone startup uses the static published-process bootstrap", () => {
    withViewerStartupEnvironment(activityProcess(), ({ status }) => {
        const viewer = startPublishedViewer();

        assert.ok(viewer instanceof PublishedProcessViewer);
        assert.equal(status.hidden, true);
        assert.equal(status.textContent, "Loading process…");
        viewer.dispose();
    });
});

test("Description data is inert and does not create viewer UI", () => {
    const process = activityProcess();
    const description =
        "</script><script>globalThis.__n107DescriptionExecuted = true</script> \"quoted\" \\\nDruga linia";
    process.presentation.nodes.find(node => node.id === "activity").description = description;
    globalThis.__n107DescriptionExecuted = false;
    try {
        withViewerStartupEnvironment(process, ({ startButton, status }) => {
            const viewer = startPublishedViewer();

            assert.ok(viewer instanceof PublishedProcessViewer);
            assert.equal(status.hidden, true);
            assert.equal(
                viewer.process.presentation.nodes.find(node => node.id === "activity").description,
                description);
            assert.equal(globalThis.__n107DescriptionExecuted, false);
            assert.equal(globalThis.document.createElement, undefined);
            startButton.click();
            assert.equal(viewer.runtime.captureState().startedTokenCount, 1);
            viewer.dispose();
        });
    } finally {
        delete globalThis.__n107DescriptionExecuted;
    }
});

test("Missing bootstrap reports bounded startup failure", () => {
    withViewerStartupEnvironment(undefined, ({ status }) => {
        assert.equal(startPublishedViewer(), null);
        assert.equal(status.hidden, false);
        assert.equal(status.textContent, "This published process could not be displayed.");
    });
});

test("Invalid bootstrap reports bounded startup failure", () => {
    withViewerStartupEnvironment({ format: "unsupported" }, ({ status }) => {
        assert.equal(startPublishedViewer(), null);
        assert.equal(status.hidden, false);
        assert.equal(status.textContent, "This published process could not be displayed.");
    });
});

test("Reset clears runtime tokens, Merge waiting state, and runtime identities", () => {
    const runtime = mergeProcess([0, 0]);
    runtime.start(0);
    runtime.start(0);
    assert.equal(runtime.captureState().mergeWaiting.merge.a, 2);

    runtime.reset();

    assert.deepEqual(runtime.captureState(), {
        startedTokenCount: 0,
        activeTokenCount: 0,
        tokens: [],
        mergeWaiting: {},
        parallelWaiting: {}
    });
    assert.equal(runtime.start(500), "token-1");
});

test("Reset clears Parallel synchronization queues and runtime identities", () => {
    const runtime = parallelRuntime(["a", "b"], ["out"], [0, 0]);
    runtime.start(0);
    runtime.start(0);
    assert.deepEqual(runtime.captureState().parallelWaiting.parallel, { a: 2, b: 0 });

    runtime.reset();

    assert.deepEqual(runtime.captureState(), {
        startedTokenCount: 0,
        activeTokenCount: 0,
        tokens: [],
        mergeWaiting: {},
        parallelWaiting: {}
    });
    assert.equal(runtime.start(500), "token-1");
});
