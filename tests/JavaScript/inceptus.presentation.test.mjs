import assert from "node:assert/strict";
import test, { beforeEach } from "node:test";
import { pathToFileURL } from "node:url";
import path from "node:path";

class FakeElement {
    constructor(width, height, left = 0, top = 0) {
        this.width = width;
        this.height = height;
        this.left = left;
        this.top = top;
        this.listeners = new Map();
        this.listenerOptions = new Map();
        this.capturedPointers = new Set();
        this.captureOperations = [];
        this.rejectCapture = false;
        this.style = { cursor: "" };
    }

    getBoundingClientRect() {
        return {
            width: this.width,
            height: this.height,
            left: this.left,
            top: this.top
        };
    }

    addEventListener(name, callback, options) {
        const callbacks = this.listeners.get(name) ?? [];
        callbacks.push(callback);
        this.listeners.set(name, callbacks);
        this.listenerOptions.set(name, options);
    }

    removeEventListener(name, callback) {
        const callbacks = this.listeners.get(name) ?? [];
        this.listeners.set(name, callbacks.filter(candidate => candidate !== callback));
    }

    dispatch(name, event = {}) {
        for (const callback of [...(this.listeners.get(name) ?? [])]) {
            callback(event);
        }
    }

    setPointerCapture(pointerId) {
        this.captureOperations.push(["set", pointerId]);
        if (this.rejectCapture) {
            throw new Error("capture rejected");
        }

        this.capturedPointers.add(pointerId);
    }

    hasPointerCapture(pointerId) {
        return this.capturedPointers.has(pointerId);
    }

    releasePointerCapture(pointerId) {
        this.captureOperations.push(["release", pointerId]);
        this.capturedPointers.delete(pointerId);
    }
}

class FakeResizeObserver {
    static instances = [];

    constructor(callback) {
        this.callback = callback;
        this.observed = [];
        this.disconnectCount = 0;
        FakeResizeObserver.instances.push(this);
    }

    observe(element) {
        this.observed.push(element);
    }

    disconnect() {
        this.disconnectCount++;
    }

    notify() {
        this.callback();
    }
}

let container;
let canvas;
let panelHeader;
let windowListeners;
let animationFrames;
let cancelledAnimationFrames;
let nextAnimationFrameId;

globalThis.HTMLElement = FakeElement;
globalThis.HTMLCanvasElement = FakeElement;
globalThis.ResizeObserver = FakeResizeObserver;
globalThis.document = {
    getElementById: id => id === "surface"
        ? container
        : id === "canvas"
            ? canvas
            : id === "panel-header"
                ? panelHeader
                : null
};
globalThis.window = {
    devicePixelRatio: 1,
    addEventListener(name, callback) {
        windowListeners.set(name, callback);
    },
    removeEventListener(name, callback) {
        if (windowListeners.get(name) === callback) {
            windowListeners.delete(name);
        }
    }
};
globalThis.requestAnimationFrame = callback => {
    const id = nextAnimationFrameId++;
    animationFrames.set(id, callback);
    return id;
};
globalThis.cancelAnimationFrame = id => {
    cancelledAnimationFrames.push(id);
    animationFrames.delete(id);
};

const modulePath = path.resolve(
    "src/Inceptus.DocumentEngine.Bpmn.Blazor/wwwroot/inceptus.presentation.js");
const {
    createCanvasPointerObserver,
    createCanvasSurfaceObserver,
    createDomPointerCaptureObserver,
    downloadFile
} = await import(pathToFileURL(modulePath));

beforeEach(() => {
    container = new FakeElement(640, 480);
    canvas = new FakeElement(640, 480, 15, 25);
    panelHeader = new FakeElement(480, 48);
    window.devicePixelRatio = 1;
    windowListeners = new Map();
    animationFrames = new Map();
    cancelledAnimationFrames = [];
    nextAnimationFrameId = 1;
    FakeResizeObserver.instances = [];
});

async function flushAnimationFrames() {
    const scheduled = [...animationFrames.entries()];
    animationFrames.clear();
    for (const [, callback] of scheduled) {
        await callback();
    }
}

function pointer(overrides = {}) {
    return {
        pointerId: 7,
        button: -1,
        buttons: 0,
        isPrimary: true,
        clientX: 115,
        clientY: 225,
        altKey: false,
        ctrlKey: false,
        metaKey: false,
        shiftKey: false,
        ...overrides
    };
}

function wheel(overrides = {}) {
    return {
        deltaX: 0,
        deltaY: 0,
        deltaMode: 0,
        ctrlKey: false,
        metaKey: false,
        preventDefault() {},
        ...overrides
    };
}

test("generic DOM observer captures only primary header drags and releases boundaries", () => {
    const observer = createDomPointerCaptureObserver("panel-header");
    observer.start();

    panelHeader.dispatch("pointerdown", pointer({
        pointerId: 17,
        button: 0,
        buttons: 1
    }));
    assert.equal(panelHeader.hasPointerCapture(17), true);
    panelHeader.dispatch("pointerup", pointer({ pointerId: 17, button: 0 }));
    assert.equal(panelHeader.hasPointerCapture(17), false);

    panelHeader.dispatch("pointerdown", pointer({
        pointerId: 18,
        button: 1,
        buttons: 4
    }));
    panelHeader.dispatch("pointerdown", pointer({
        pointerId: 19,
        button: 0,
        buttons: 1,
        isPrimary: false
    }));
    assert.deepEqual(panelHeader.captureOperations, [["set", 17], ["release", 17]]);

    panelHeader.dispatch("pointerdown", pointer({
        pointerId: 20,
        button: 0,
        buttons: 1
    }));
    observer.dispose();
    observer.dispose();
    assert.deepEqual(panelHeader.captureOperations, [
        ["set", 17],
        ["release", 17],
        ["set", 20],
        ["release", 20]
    ]);
    assert.equal(panelHeader.listeners.get("pointerdown").length, 0);
    assert.throws(() => observer.start(), /disposed/);
});

test("download bridge preserves exact bytes, MIME type, and filename", async () => {
    const appended = [];
    const removed = [];
    const revoked = [];
    const anchors = [];
    let capturedBlob;
    const originalCreateElement = document.createElement;
    const originalBody = document.body;
    const originalCreateObjectUrl = URL.createObjectURL;
    const originalRevokeObjectUrl = URL.revokeObjectURL;
    document.createElement = tagName => {
        assert.equal(tagName, "a");
        const anchor = {
            href: "",
            download: "",
            hidden: false,
            clickCount: 0,
            click() { this.clickCount++; },
            remove() { removed.push(this); }
        };
        anchors.push(anchor);
        return anchor;
    };
    document.body = { appendChild(anchor) { appended.push(anchor); } };
    URL.createObjectURL = blob => {
        capturedBlob = blob;
        return "blob:test-native-document";
    };
    URL.revokeObjectURL = objectUrl => revoked.push(objectUrl);

    try {
        const bytes = Uint8Array.from([0, 1, 2, 127, 128, 254, 255]);
        downloadFile(bytes, "application/json", "document.inceptus.json");

        assert.equal(capturedBlob.type, "application/json");
        assert.deepEqual(
            new Uint8Array(await capturedBlob.arrayBuffer()),
            bytes);
        assert.equal(anchors.length, 1);
        assert.equal(anchors[0].href, "blob:test-native-document");
        assert.equal(anchors[0].download, "document.inceptus.json");
        assert.equal(anchors[0].hidden, true);
        assert.equal(anchors[0].clickCount, 1);
        assert.deepEqual(appended, anchors);
        assert.deepEqual(removed, anchors);
        assert.deepEqual(revoked, ["blob:test-native-document"]);
    } finally {
        document.createElement = originalCreateElement;
        document.body = originalBody;
        URL.createObjectURL = originalCreateObjectUrl;
        URL.revokeObjectURL = originalRevokeObjectUrl;
    }
});

test("download bridge rejects invalid browser-boundary inputs", () => {
    assert.throws(
        () => downloadFile([1, 2, 3], "application/json", "document.inceptus.json"),
        /Uint8Array/);
    assert.throws(
        () => downloadFile(new Uint8Array(), "", "document.inceptus.json"),
        /content type/);
    assert.throws(
        () => downloadFile(new Uint8Array(), "application/json", ""),
        /file name/);
});

test("DOM pointer observer validates construction and isolates capture rejection", () => {
    assert.throws(
        () => createDomPointerCaptureObserver("missing"),
        /element was not found/);

    panelHeader.rejectCapture = true;
    const observer = createDomPointerCaptureObserver("panel-header");
    observer.start();
    assert.throws(() => observer.start(), /already started/);
    panelHeader.dispatch("pointerdown", pointer({
        pointerId: 21,
        button: 0,
        buttons: 1
    }));
    panelHeader.dispatch("pointercancel", pointer({ pointerId: 21 }));
    assert.equal(panelHeader.capturedPointers.size, 0);
    observer.dispose();
});

test("start returns the initial numeric measurement and installs one observer", () => {
    const invocations = [];
    const callback = {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    };

    const observer = createCanvasSurfaceObserver("surface", callback);
    const initial = observer.start();

    assert.deepEqual(initial, {
        cssWidth: 640,
        cssHeight: 480,
        devicePixelRatio: 1
    });
    assert.equal(FakeResizeObserver.instances.length, 1);
    assert.deepEqual(FakeResizeObserver.instances[0].observed, [container]);
    assert.equal(typeof windowListeners.get("resize"), "function");
    assert.deepEqual(invocations, []);
});

test("resize and DPR changes coalesce, deduplicate, and emit only scalar values", async () => {
    const invocations = [];
    const callback = {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    };
    const observer = createCanvasSurfaceObserver("surface", callback);
    observer.start();
    const resizeObserver = FakeResizeObserver.instances[0];

    container.width = 800;
    container.height = 600;
    resizeObserver.notify();
    resizeObserver.notify();
    windowListeners.get("resize")();
    assert.equal(animationFrames.size, 1);
    await flushAnimationFrames();

    assert.deepEqual(invocations, [
        ["OnCanvasSurfaceChanged", 800, 600, 1]
    ]);

    resizeObserver.notify();
    await flushAnimationFrames();
    assert.equal(invocations.length, 1);

    window.devicePixelRatio = 2;
    windowListeners.get("resize")();
    await flushAnimationFrames();
    assert.deepEqual(invocations[1], ["OnCanvasSurfaceChanged", 800, 600, 2]);
    assert.equal(invocations[1].slice(1).every(Number.isFinite), true);

    observer.dispose();
});

test("dispose disconnects, cancels queued work, and prevents future callbacks", async () => {
    const invocations = [];
    const callback = {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    };
    const observer = createCanvasSurfaceObserver("surface", callback);
    observer.start();
    const resizeObserver = FakeResizeObserver.instances[0];
    const removedWindowCallback = windowListeners.get("resize");

    container.width = 900;
    resizeObserver.notify();
    assert.equal(animationFrames.size, 1);
    observer.dispose();
    observer.dispose();

    assert.equal(resizeObserver.disconnectCount, 1);
    assert.equal(windowListeners.has("resize"), false);
    assert.deepEqual(cancelledAnimationFrames, [1]);
    assert.equal(animationFrames.size, 0);

    resizeObserver.notify();
    removedWindowCallback();
    await flushAnimationFrames();
    assert.deepEqual(invocations, []);
});

test("a rejected .NET callback is isolated and later measurements remain deliverable", async () => {
    const invocations = [];
    let reject = true;
    const observer = createCanvasSurfaceObserver("surface", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            if (reject) {
                reject = false;
                return Promise.reject(new Error("component is disposing"));
            }

            return Promise.resolve();
        }
    });
    observer.start();

    container.width = 700;
    FakeResizeObserver.instances[0].notify();
    const [[, firstFrame]] = [...animationFrames.entries()];
    animationFrames.clear();
    const firstDelivery = firstFrame();
    await Promise.resolve();
    container.width = 710;
    FakeResizeObserver.instances[0].notify();
    await flushAnimationFrames();

    assert.equal(invocations.length, 2);
    assert.deepEqual(invocations[1], ["OnCanvasSurfaceChanged", 710, 480, 1]);
    observer.dispose();
});

test("a transient invalid measurement is ignored and the next valid resize is delivered", async () => {
    const invocations = [];
    const observer = createCanvasSurfaceObserver("surface", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    container.width = 0;
    FakeResizeObserver.instances[0].notify();
    await flushAnimationFrames();
    assert.deepEqual(invocations, []);

    container.width = 660;
    FakeResizeObserver.instances[0].notify();
    await flushAnimationFrames();
    assert.deepEqual(invocations, [
        ["OnCanvasSurfaceChanged", 660, 480, 1]
    ]);
    observer.dispose();
});

test("only the latest measurement waits behind one in-flight .NET callback", async () => {
    const invocations = [];
    let releaseFirst;
    const first = new Promise(resolve => { releaseFirst = resolve; });
    const observer = createCanvasSurfaceObserver("surface", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return invocations.length === 1 ? first : Promise.resolve();
        }
    });
    observer.start();

    container.width = 700;
    FakeResizeObserver.instances[0].notify();
    const firstFlush = flushAnimationFrames();
    await Promise.resolve();
    container.width = 710;
    FakeResizeObserver.instances[0].notify();
    await flushAnimationFrames();
    container.width = 720;
    window.devicePixelRatio = 2;
    windowListeners.get("resize")();
    await flushAnimationFrames();

    assert.equal(invocations.length, 1);
    releaseFirst();
    await firstFlush;
    assert.deepEqual(invocations, [
        ["OnCanvasSurfaceChanged", 700, 480, 1],
        ["OnCanvasSurfaceChanged", 720, 480, 2]
    ]);
    observer.dispose();
});

test("hidden startup waits for a usable surface and resolves once on reveal", async () => {
    container.width = 0;
    container.height = 0;
    const invocations = [];
    const observer = createCanvasSurfaceObserver("surface", {
        invokeMethodAsync(...args) { invocations.push(args); return Promise.resolve(); }
    });
    let completed = false;
    const initial = Promise.resolve(observer.start()).then(value => { completed = true; return value; });
    await Promise.resolve();
    assert.equal(completed, false);
    FakeResizeObserver.instances[0].notify();
    await flushAnimationFrames();
    assert.equal(completed, false);
    assert.equal(animationFrames.size, 0);
    container.width = 640;
    container.height = 420;
    FakeResizeObserver.instances[0].notify();
    await flushAnimationFrames();
    assert.deepEqual(await initial, { cssWidth: 640, cssHeight: 420, devicePixelRatio: 1 });
    assert.deepEqual(invocations, []);
    container.width = 960;
    FakeResizeObserver.instances[0].notify();
    await flushAnimationFrames();
    assert.deepEqual(invocations, [["OnCanvasSurfaceChanged", 960, 420, 1]]);
    observer.dispose();
});

test("disposing hidden startup settles its wait and ignores late observation", async () => {
    container.height = 0;
    const observer = createCanvasSurfaceObserver("surface", {
        invokeMethodAsync() { assert.fail("Disposed startup must not dispatch callbacks."); }
    });
    const pending = observer.start();
    const rejected = assert.rejects(pending, /disposed/);
    const resizeObserver = FakeResizeObserver.instances[0];
    observer.dispose();
    await rejected;
    container.height = 420;
    resizeObserver.notify();
    await flushAnimationFrames();
    assert.equal(resizeObserver.disconnectCount, 1);
    assert.equal(animationFrames.size, 0);
});

test("invalid construction and lifecycle inputs fail without installing observation", () => {
    assert.throws(
        () => createCanvasSurfaceObserver("missing", { invokeMethodAsync() {} }),
        /container was not found/);
    assert.throws(
        () => createCanvasSurfaceObserver("surface", {}),
        /valid \.NET resize callback/);

    const observer = createCanvasSurfaceObserver(
        "surface",
        { invokeMethodAsync() { return Promise.resolve(); } });
    observer.start();
    assert.throws(() => observer.start(), /already started/);
    observer.dispose();
    assert.throws(() => observer.start(), /disposed/);
});

test("pointer observer emits one normalized scalar stream with fresh bounds and no DPR", async () => {
    const invocations = [];
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    window.devicePixelRatio = 3;
    canvas.dispatch("pointermove", pointer({ altKey: true }));
    canvas.left = 30;
    canvas.top = 40;
    canvas.dispatch("pointerdown", pointer({
        button: 0,
        buttons: 1,
        clientX: 180,
        clientY: 260,
        ctrlKey: true,
        shiftKey: true
    }));
    canvas.dispatch("pointerup", pointer({
        button: 0,
        clientX: 182,
        clientY: 262
    }));
    canvas.dispatch("pointerleave", pointer({ clientX: 190, clientY: 270 }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations, [
        ["OnCanvasPointerInput", 1, 7, -1, 0, true,
            115, 225, 15, 25, true, false, false, false, 0],
        ["OnCanvasPointerInput", 0, 7, 0, 1, true,
            180, 260, 30, 40, false, true, false, true, 1],
        ["OnCanvasPointerInput", 2, 7, 0, 0, true,
            182, 262, 30, 40, false, false, false, false, 1],
        ["OnCanvasPointerInput", 4, 7, -1, 0, true,
            190, 270, 30, 40, false, false, false, false, 0]
    ]);
    assert.equal(invocations.flat().includes(window.devicePixelRatio), false);
    assert.deepEqual(canvas.captureOperations, [["set", 7], ["release", 7]]);
    assert.equal(canvas.capturedPointers.size, 0);
    observer.dispose();
});

test("ordinary Canvas wheel is non-passive, prevented, scalar, and fractional", async () => {
    const invocations = [];
    let releaseFirst;
    const first = new Promise(resolve => { releaseFirst = resolve; });
    let prevented = 0;
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return invocations.length === 1 ? first : Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("wheel", wheel({
        deltaX: 2.4,
        deltaY: 7.8,
        preventDefault() { prevented++; }
    }));
    canvas.dispatch("wheel", wheel({
        deltaX: 3.1,
        deltaY: -1.2,
        preventDefault() { prevented++; }
    }));
    canvas.dispatch("wheel", wheel({
        deltaX: 0.5,
        deltaY: 4.4,
        preventDefault() { prevented++; }
    }));
    assert.equal(invocations.length, 3);

    releaseFirst();
    await first;
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations, [
        ["OnCanvasWheelInput", 2.4, 7.8, 0, false, false],
        ["OnCanvasWheelInput", 3.1, -1.2, 0, false, false],
        ["OnCanvasWheelInput", 0.5, 4.4, 0, false, false]
    ]);
    assert.equal(prevented, 3);
    assert.deepEqual(canvas.listenerOptions.get("wheel"), { passive: false });
    observer.dispose();
});

test("modified, zero, and nonfinite wheel input is not consumed or forwarded", async () => {
    const invocations = [];
    let prevented = 0;
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("wheel", wheel({
        deltaY: 10,
        ctrlKey: true,
        preventDefault() { prevented++; }
    }));
    canvas.dispatch("wheel", wheel({
        deltaY: 10,
        metaKey: true,
        preventDefault() { prevented++; }
    }));
    canvas.dispatch("wheel", wheel({ preventDefault() { prevented++; } }));
    canvas.dispatch("wheel", wheel({
        deltaX: Number.NaN,
        deltaY: 10,
        preventDefault() { prevented++; }
    }));
    canvas.dispatch("wheel", wheel({
        deltaY: 10,
        deltaMode: 99,
        preventDefault() { prevented++; }
    }));
    container.dispatch("wheel", wheel({
        deltaY: 10,
        preventDefault() { prevented++; }
    }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations, []);
    assert.equal(prevented, 0);
    observer.dispose();
});

test("middle pointer capture suppresses Canvas autoscroll and forwards its full lifecycle", async () => {
    const invocations = [];
    let prevented = 0;
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({
        pointerId: 12,
        button: 1,
        buttons: 4,
        preventDefault() { prevented++; }
    }));
    canvas.dispatch("pointermove", pointer({
        pointerId: 12,
        button: -1,
        buttons: 4,
        clientX: 215,
        clientY: 275
    }));
    canvas.dispatch("pointerup", pointer({
        pointerId: 12,
        button: 1,
        buttons: 0,
        clientX: 215,
        clientY: 275
    }));
    canvas.dispatch("auxclick", pointer({
        pointerId: 12,
        button: 1,
        preventDefault() { prevented++; }
    }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations.map(invocation => [invocation[1], invocation[2], invocation[3]]), [
        [0, 12, 1],
        [1, 12, -1],
        [2, 12, 1]
    ]);
    assert.deepEqual(canvas.captureOperations, [["set", 12], ["release", 12]]);
    assert.equal(prevented, 2);
    observer.dispose();
});

test("canvas context menu is suppressed and forwards only ordered scalar input without capture", async () => {
    const invocations = [];
    let preventDefaultCount = 0;
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    window.devicePixelRatio = 2.5;
    canvas.left = 30;
    canvas.top = 40;
    canvas.dispatch("contextmenu", pointer({
        pointerId: 91,
        button: 2,
        clientX: 410.5,
        clientY: 260.25,
        altKey: true,
        metaKey: true,
        shiftKey: true,
        preventDefault() { preventDefaultCount++; }
    }));
    canvas.dispatch("contextmenu", pointer({
        clientX: Number.NaN,
        preventDefault() { preventDefaultCount++; }
    }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations, [[
        "OnCanvasPointerInput",
        5,
        0,
        2,
        0,
        true,
        410.5,
        260.25,
        30,
        40,
        true,
        false,
        true,
        true,
        0
    ]]);
    assert.equal(invocations.flat().includes(window.devicePixelRatio), false);
    assert.equal(preventDefaultCount, 2);
    assert.deepEqual(canvas.captureOperations, []);
    assert.equal(canvas.capturedPointers.size, 0);
    observer.dispose();
});

test("context menu remains suppressed but is not forwarded during pointer capture", async () => {
    const invocations = [];
    let preventDefaultCount = 0;
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ button: 0, buttons: 1 }));
    canvas.dispatch("contextmenu", pointer({
        button: 2,
        preventDefault() { preventDefaultCount++; }
    }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations.map(invocation => invocation[1]), [0]);
    assert.equal(preventDefaultCount, 1);
    assert.deepEqual(canvas.captureOperations, [["set", 7]]);
    observer.dispose();
});

test("double-click forwards scalar activation with fresh bounds only after capture ends", async () => {
    const invocations = [];
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ button: 0, buttons: 1 }));
    canvas.dispatch("dblclick", pointer({ button: 0, clientX: 310, clientY: 240 }));
    canvas.dispatch("pointerup", pointer({ button: 0 }));
    canvas.left = 42;
    canvas.top = 53;
    canvas.dispatch("dblclick", pointer({
        button: 0,
        clientX: 310.5,
        clientY: 240.25,
        altKey: true,
        shiftKey: true
    }));
    canvas.dispatch("dblclick", pointer({
        button: 1,
        clientX: 310.5,
        clientY: 240.25
    }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations.map(invocation => invocation[1]), [0, 2, 6]);
    assert.deepEqual(invocations[2], [
        "OnCanvasPointerInput",
        6,
        0,
        0,
        0,
        true,
        310.5,
        240.25,
        42,
        53,
        true,
        false,
        false,
        true,
        0
    ]);
    observer.dispose();
});

test("pointer observer applies only the final scalar CSS cursor and resets it on disposal", () => {
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync() { return Promise.resolve(); }
    });

    observer.setCursor("nwse-resize");
    assert.equal(canvas.style.cursor, "nwse-resize");
    assert.throws(() => observer.setCursor(""), /non-empty CSS cursor/);

    observer.start();
    observer.setCursor("ew-resize");
    assert.equal(canvas.style.cursor, "ew-resize");
    observer.dispose();
    assert.equal(canvas.style.cursor, "default");

    observer.setCursor("ns-resize");
    assert.equal(canvas.style.cursor, "default");
});

test("captured pointer continues outside while secondary input and leave are ignored", async () => {
    const invocations = [];
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ pointerId: 3, button: 0, buttons: 1 }));
    canvas.dispatch("pointerleave", pointer({ pointerId: 3 }));
    canvas.dispatch("pointermove", pointer({ pointerId: 9, isPrimary: false }));
    canvas.dispatch("pointermove", pointer({ pointerId: 3, buttons: 1, clientX: 900 }));
    canvas.dispatch("pointerup", pointer({ pointerId: 3, button: 0, clientX: 910 }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations.map(invocation => [invocation[1], invocation[2]]), [
        [0, 3],
        [1, 3],
        [2, 3]
    ]);
    assert.equal(canvas.capturedPointers.size, 0);
    observer.dispose();
});

test("managed gesture invalidation releases current capture and emits one ordered cancel", async () => {
    const invocations = [];
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({
        pointerId: 23,
        button: 0,
        buttons: 1,
        clientX: 140,
        clientY: 155
    }));
    await new Promise(resolve => setImmediate(resolve));
    const captureGeneration = invocations[0][14];
    observer.releaseCapture(captureGeneration);
    observer.releaseCapture(captureGeneration);
    canvas.dispatch("pointerup", pointer({ pointerId: 23, button: 0 }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations.map(invocation => [
        invocation[1],
        invocation[2],
        invocation[3],
        invocation[4],
        invocation[6],
        invocation[7]
    ]), [
        [0, 23, 0, 1, 140, 155],
        [3, 23, -1, 0, 140, 155]
    ]);
    assert.deepEqual(canvas.captureOperations, [["set", 23], ["release", 23]]);
    assert.equal(canvas.capturedPointers.size, 0);
    observer.dispose();
});

test("stale managed release cannot release a newer browser capture generation", async () => {
    const invocations = [];
    let releaseOldUp;
    const oldUpBlocked = new Promise(resolve => { releaseOldUp = resolve; });
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return args[1] === 2 && args[2] === 23
                ? oldUpBlocked
                : Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ pointerId: 23, button: 0, buttons: 1 }));
    await new Promise(resolve => setImmediate(resolve));
    const oldGeneration = invocations[0][14];
    canvas.dispatch("pointerup", pointer({ pointerId: 23, button: 0 }));
    canvas.dispatch("pointerdown", pointer({ pointerId: 24, button: 0, buttons: 1 }));

    assert.equal(canvas.hasPointerCapture(24), true);
    observer.releaseCapture(oldGeneration);
    assert.equal(canvas.hasPointerCapture(24), true);
    assert.deepEqual(canvas.captureOperations, [
        ["set", 23],
        ["release", 23],
        ["set", 24]
    ]);

    releaseOldUp();
    await oldUpBlocked;
    await new Promise(resolve => setImmediate(resolve));
    const newDown = invocations.find(invocation =>
        invocation[1] === 0 && invocation[2] === 24);
    assert.ok(newDown);
    assert.notEqual(newDown[14], oldGeneration);

    observer.dispose();
});

test("pointer delivery preserves boundaries and coalesces only adjacent same-pointer moves", async () => {
    const invocations = [];
    let releaseFirst;
    const first = new Promise(resolve => { releaseFirst = resolve; });
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return invocations.length === 1 ? first : Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ button: 0, buttons: 1 }));
    canvas.dispatch("pointermove", pointer({ buttons: 1, clientX: 110, clientY: 110 }));
    canvas.dispatch("pointermove", pointer({ buttons: 1, clientX: 120, clientY: 120 }));
    canvas.dispatch("pointerup", pointer({ button: 0, clientX: 125, clientY: 125 }));
    canvas.dispatch("pointermove", pointer({ clientX: 130, clientY: 130 }));
    canvas.dispatch("pointerleave", pointer({ clientX: 135, clientY: 135 }));
    assert.equal(invocations.length, 1);

    releaseFirst();
    await first;
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations.map(invocation => [invocation[1], invocation[2], invocation[6]]), [
        [0, 7, 115],
        [1, 7, 120],
        [2, 7, 125],
        [1, 7, 130],
        [4, 7, 135]
    ]);
    observer.dispose();
});

test("a rejected pointer callback is isolated from later ordered input", async () => {
    const invocations = [];
    let reject = true;
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            if (reject) {
                reject = false;
                return Promise.reject(new Error("component is disposing"));
            }

            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ button: 0, buttons: 1 }));
    canvas.dispatch("pointermove", pointer({ buttons: 1, clientX: 110 }));
    canvas.dispatch("pointerup", pointer({ button: 0, clientX: 120 }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations.map(invocation => invocation[1]), [0, 1, 2]);
    observer.dispose();
});

test("pointer disposal removes listeners, drops queued input, and is idempotent", async () => {
    const invocations = [];
    let releaseFirst;
    const first = new Promise(resolve => { releaseFirst = resolve; });
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return first;
        }
    });
    observer.start();
    canvas.dispatch("pointerdown", pointer({ button: 0, buttons: 1 }));
    canvas.dispatch("pointermove", pointer({ buttons: 1, clientX: 110 }));

    observer.dispose();
    observer.dispose();
    canvas.dispatch("pointermove", pointer({ clientX: 120 }));
    canvas.dispatch("pointerup", pointer({ button: 0, clientX: 130 }));
    canvas.dispatch("pointerleave", pointer());
    releaseFirst();
    await first;
    await Promise.resolve();

    assert.equal(invocations.length, 1);
    assert.deepEqual(canvas.captureOperations, [["set", 7], ["release", 7]]);
    assert.equal(canvas.capturedPointers.size, 0);
    assert.equal(canvas.listeners.get("pointerdown").length, 0);
    assert.equal(canvas.listeners.get("pointermove").length, 0);
    assert.equal(canvas.listeners.get("pointerup").length, 0);
    assert.equal(canvas.listeners.get("pointercancel").length, 0);
    assert.equal(canvas.listeners.get("pointerleave").length, 0);
    assert.equal(canvas.listeners.get("lostpointercapture").length, 0);
    assert.equal(canvas.listeners.get("contextmenu").length, 0);
    assert.equal(canvas.listeners.get("dblclick").length, 0);
    assert.equal(canvas.listeners.get("auxclick").length, 0);
    assert.equal(canvas.listeners.get("wheel").length, 0);
});

test("pointer cancel and unexpected capture loss terminate exactly one active capture", async () => {
    const invocations = [];
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ button: 0, buttons: 1 }));
    canvas.dispatch("pointercancel", pointer({ button: 0 }));
    canvas.dispatch("pointerup", pointer({ button: 0 }));
    canvas.dispatch("pointerdown", pointer({ pointerId: 8, button: 0, buttons: 1 }));
    canvas.capturedPointers.delete(8);
    canvas.dispatch("lostpointercapture", pointer({ pointerId: 8, button: 0 }));
    canvas.dispatch("pointerup", pointer({ pointerId: 8, button: 0 }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations.map(invocation => [invocation[1], invocation[2]]), [
        [0, 7],
        [3, 7],
        [0, 8],
        [3, 8]
    ]);
    observer.dispose();
});

test("capture rejection never begins a managed gesture or leaves browser capture", async () => {
    const invocations = [];
    canvas.rejectCapture = true;
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ button: 0, buttons: 1 }));
    canvas.dispatch("pointermove", pointer({ buttons: 1 }));
    canvas.dispatch("pointerup", pointer({ button: 0 }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations, []);
    assert.equal(canvas.capturedPointers.size, 0);
    observer.dispose();
});

test("right pointerdown never acquires editing capture", async () => {
    const invocations = [];
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();

    canvas.dispatch("pointerdown", pointer({ button: 2, buttons: 2 }));
    canvas.dispatch("pointerup", pointer({ button: 2, buttons: 0 }));
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(invocations, []);
    assert.deepEqual(canvas.captureOperations, []);
    assert.equal(canvas.capturedPointers.size, 0);
    observer.dispose();
});

test("pointer observer rejects invalid construction/lifecycle and ignores nonfinite input", async () => {
    assert.throws(
        () => createCanvasPointerObserver("missing", { invokeMethodAsync() {} }),
        /interaction canvas was not found/);
    assert.throws(
        () => createCanvasPointerObserver("canvas", {}),
        /valid \.NET pointer callback/);

    const invocations = [];
    const observer = createCanvasPointerObserver("canvas", {
        invokeMethodAsync(...args) {
            invocations.push(args);
            return Promise.resolve();
        }
    });
    observer.start();
    assert.throws(() => observer.start(), /already started/);
    canvas.dispatch("pointermove", pointer({ clientX: Number.NaN }));
    canvas.left = Number.POSITIVE_INFINITY;
    canvas.dispatch("pointerdown", pointer({ button: 0, buttons: 1 }));
    canvas.left = 15;
    canvas.dispatch("pointerdown", pointer({
        pointerId: Number.NaN,
        button: 0,
        buttons: 1
    }));
    canvas.dispatch("pointerdown", pointer({
        isPrimary: false,
        button: 0,
        buttons: 1
    }));
    await Promise.resolve();
    assert.deepEqual(invocations, []);
    observer.dispose();
    assert.throws(() => observer.start(), /disposed/);
});
