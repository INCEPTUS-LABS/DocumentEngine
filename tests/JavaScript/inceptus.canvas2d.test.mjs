import assert from "node:assert/strict";
import test from "node:test";
import { pathToFileURL } from "node:url";
import path from "node:path";

const operations = [];
const loadedImages = [];
const fontFaces = [];
let nativeTextMetrics = {
    width: 40,
    actualBoundingBoxAscent: 8,
    actualBoundingBoxDescent: 2,
    actualBoundingBoxLeft: 1,
    actualBoundingBoxRight: 39
};
let rejectedImageUri = null;
let rejectNextFontLoad = false;
let pendingFontLoad = null;
let rejectNextFill = false;

class FakeContext {
    save() { operations.push(["save"]); }
    restore() { operations.push(["restore"]); }
    setTransform(...args) { operations.push(["setTransform", ...args]); }
    clearRect(...args) { operations.push(["clearRect", ...args]); }
    scale(...args) { operations.push(["scale", ...args]); }
    transform(...args) { operations.push(["transform", ...args]); }
    setLineDash(value) { operations.push(["setLineDash", ...value]); }
    beginPath() { operations.push(["beginPath"]); }
    rect(...args) { operations.push(["rect", ...args]); }
    clip() { operations.push(["clip"]); }
    fillRect(...args) {
        operations.push(["fillRect", ...args]);
        if (rejectNextFill) {
            rejectNextFill = false;
            throw new Error("draw failed");
        }
    }
    strokeRect(...args) { operations.push(["strokeRect", ...args]); }
    ellipse(...args) { operations.push(["ellipse", ...args]); }
    fill() { operations.push(["fill"]); }
    stroke() { operations.push(["stroke"]); }
    moveTo(...args) { operations.push(["moveTo", ...args]); }
    lineTo(...args) { operations.push(["lineTo", ...args]); }
    closePath() { operations.push(["closePath"]); }
    fillText(...args) { operations.push(["fillText", ...args]); }
    strokeText(...args) { operations.push(["strokeText", ...args]); }
    drawImage(image, ...args) { operations.push(["drawImage", image.src, ...args]); }
    measureText(text) {
        operations.push(["measureText", text]);
        return nativeTextMetrics;
    }
}

class FakeCanvas {
    constructor(context) {
        this.context = context;
        this.style = {};
        this.dataset = {};
        this.width = 0;
        this.height = 0;
    }
    getContext(kind) { return kind === "2d" ? this.context : null; }
}

globalThis.HTMLCanvasElement = FakeCanvas;
globalThis.CSS = { supports: (kind, value) => kind === "color" && value !== "invalid" };
globalThis.Image = class {
    set src(value) { this._src = value; }
    get src() { return this._src; }
    async decode() {
        if (this._src === rejectedImageUri) throw new Error("image decode failed");
        loadedImages.push(this._src);
    }
};
globalThis.FontFace = class {
    constructor(family, source, descriptor) {
        this.family = family;
        this.source = source;
        this.descriptor = descriptor;
    }
    async load() {
        if (pendingFontLoad) await pendingFontLoad;
        if (rejectNextFontLoad) {
            rejectNextFontLoad = false;
            throw new Error("font load failed");
        }
        fontFaces.push(this);
        return this;
    }
};

const context = new FakeContext();
const canvas = new FakeCanvas(context);
const fonts = {
    added: [],
    deleted: [],
    add(face) { this.added.push(face); },
    delete(face) { this.deleted.push(face); },
    check() { return true; }
};
globalThis.document = {
    getElementById: id => id === "canvas" ? canvas : null,
    fonts
};

const modulePath = path.resolve(
    "src/Inceptus.DocumentEngine.Canvas2D/wwwroot/inceptus.canvas2d.js");
const { createRenderer } = await import(pathToFileURL(modulePath));

const delayedFont = {
    fontIdentity: "font:delayed", fontVersion: "1", fontFamily: "Delayed",
    sourceUri: "_content/example/font.ttf", fontWeight: 400, fontStyle: "normal"
};

test("disposing during font load promptly settles startup without late registration", async () => {
    let complete;
    pendingFontLoad = new Promise(resolve => { complete = resolve; });
    const renderer = createRenderer("canvas");
    const addedBefore = fonts.added.length;
    const startup = renderer.initialize(
        { cssWidth: 640, cssHeight: 420, devicePixelRatio: 1 }, [], [delayedFont], "Delayed");
    renderer.dispose();
    // Startup must settle even if the browser never completes the resource request.
    const result = await startup;
    assert.equal(result.succeeded, false);
    complete();
    pendingFontLoad = null;
    await Promise.resolve();
    await Promise.resolve();
    assert.equal(fonts.added.length, addedBefore);
    const next = createRenderer("canvas");
    assert.equal((await next.initialize(
        { cssWidth: 640, cssHeight: 420, devicePixelRatio: 1 }, [], [delayedFont], "Delayed")).succeeded, true);
    next.dispose();
});

test("stalled font loading has a bounded failure and cannot register after timeout", async t => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    let complete;
    pendingFontLoad = new Promise(resolve => { complete = resolve; });
    const renderer = createRenderer("canvas");
    const addedBefore = fonts.added.length;
    const startup = renderer.initialize(
        { cssWidth: 640, cssHeight: 420, devicePixelRatio: 1 }, [], [delayedFont], "Delayed");
    t.mock.timers.tick(30000);
    const result = await startup;
    assert.equal(result.code, "CANVAS2D_RENDERER_INITIALIZATION_FAILED");
    complete();
    pendingFontLoad = null;
    await Promise.resolve();
    await Promise.resolve();
    assert.equal(fonts.added.length, addedBefore);
    renderer.dispose();
});

test("delayed font registration precedes successful startup and rejects concurrent initialize", async () => {
    let complete;
    pendingFontLoad = new Promise(resolve => { complete = resolve; });
    const renderer = createRenderer("canvas");
    const addedBefore = fonts.added.length;
    let ready = false;
    const startup = renderer.initialize(
        { cssWidth: 640, cssHeight: 420, devicePixelRatio: 1 }, [], [delayedFont], "Delayed")
        .then(result => { ready = result.succeeded; return result; });
    await Promise.resolve();
    assert.equal(ready, false);
    assert.equal(fonts.added.length, addedBefore);
    assert.equal((await renderer.initialize(
        { cssWidth: 640, cssHeight: 420, devicePixelRatio: 1 }, [], [], null)).code,
        "CANVAS2D_RENDERER_ALREADY_INITIALIZED");
    complete();
    pendingFontLoad = null;
    assert.equal((await startup).succeeded, true);
    assert.equal(fonts.added.length, addedBefore + 1);
    renderer.dispose();
});

test("two renderers own distinct font faces and disposal never deletes the other face", async () => {
    const getElement = document.getElementById;
    const secondCanvas = new FakeCanvas(new FakeContext());
    document.getElementById = id => id === "second" ? secondCanvas : getElement(id);
    const first = createRenderer("canvas");
    const second = createRenderer("second");
    try {
        const surface = { cssWidth: 640, cssHeight: 420, devicePixelRatio: 1 };
        assert.equal((await first.initialize(surface, [], [delayedFont], "Delayed")).succeeded, true);
        const faceA = fonts.added.at(-1);
        assert.equal((await second.initialize(surface, [], [delayedFont], "Delayed")).succeeded, true);
        const faceB = fonts.added.at(-1);
        assert.notEqual(faceA, faceB);
        assert.equal(faceA.family, faceB.family);
        first.dispose();
        assert.equal(fonts.deleted.includes(faceA), true);
        assert.equal(fonts.deleted.includes(faceB), false);
        assert.equal(second.measureText({
            ...delayedFont, text: "Still independent", fontSize: 12, lineHeight: 16,
            locale: "und", direction: "ltr", writingMode: 0, scale: 1
        }).succeeded, true);
    } finally {
        first.dispose();
        second.dispose();
        document.getElementById = getElement;
    }
});

const matrix = (offsetX = 0, offsetY = 0) => ({
    m11: 1, m12: 0, m21: 0, m22: 1, offsetX, offsetY
});
const rectangle = (id, layer, options = {}) => ({
    id,
    layer,
    zIndex: 0,
    geometryKind: 0,
    geometryBounds: { x: 0, y: 0, width: 10, height: 5 },
    points: [],
    content: null,
    isClosed: true,
    textAnchor: options.textAnchor ?? { x: 0, y: 0 },
    textAlignment: options.textAlignment ?? 0,
    textBaseline: options.textBaseline ?? 0,
    transform: matrix(options.offsetX ?? 0, options.offsetY ?? 0),
    clip: options.clip ?? null,
    fill: options.fill ?? "#123456",
    stroke: options.stroke ?? "#654321",
    strokeWidth: 2,
    dashPattern: [3, 1],
    opacity: 0.5,
    fontFamily: options.fontFamily ?? null,
    fontSize: 12,
    isVisible: options.isVisible ?? true
});

test("initialization owns one canvas and computes DPR backing size", async () => {
    operations.length = 0;
    const addedBefore = fonts.added.length;
    const renderer = createRenderer("canvas");
    const result = await renderer.initialize(
        { cssWidth: 100.25, cssHeight: 50.25, devicePixelRatio: 2 },
        [],
        [{
            fontIdentity: "font:test",
            fontVersion: "1",
            fontFamily: "Inter",
            sourceUri: "/inter.woff2",
            fontWeight: 400,
            fontStyle: "normal"
        }],
        "Inter");

    assert.equal(result.succeeded, true);
    assert.equal(canvas.width, 201);
    assert.equal(canvas.height, 101);
    assert.equal(canvas.style.width, "100.25px");
    assert.equal(canvas.dataset.inceptusDpr, "2");
    assert.equal(fonts.added.length, addedBefore + 1);
    const contender = createRenderer("canvas");
    const rejected = await contender.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 }, [], [], null);
    assert.equal(rejected.code, "CANVAS2D_RENDERER_ALREADY_INITIALIZED");
    contender.dispose();
    assert.equal((await renderer.resize(
        { cssWidth: 0.25, cssHeight: 0.5, devicePixelRatio: 1.5 })).succeeded, true);
    assert.equal(canvas.width, 1);
    assert.equal(canvas.height, 1);
    assert.equal(canvas.style.width, "0.25px");
    renderer.dispose();
});

test("complete frame clears at identity, scales DPR, applies viewport, preserves order and clip-before-item-transform", async () => {
    operations.length = 0;
    const renderer = createRenderer("canvas");
    await renderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 2 }, [], [], null);
    const frame = {
        viewportTransform: { m11: 2, m12: 0, m21: 0, m22: 2, offsetX: 20, offsetY: 10 },
        items: [
            rectangle("background", 0),
            rectangle("content", 1, {
                offsetX: 100,
                offsetY: 50,
                clip: { x: 102, y: 52, width: 6, height: 2 }
            }),
            rectangle("hidden", 5, { isVisible: false })
        ]
    };

    const result = await renderer.render(frame);
    assert.equal(result.succeeded, true);
    assert.deepEqual(operations.slice(0, 5), [
        ["save"],
        ["setTransform", 1, 0, 0, 1, 0, 0],
        ["clearRect", 0, 0, 200, 100],
        ["setLineDash"],
        ["scale", 2, 2]
    ]);
    assert.deepEqual(operations[5], ["transform", 2, 0, 0, 2, 20, 10]);
    const clipIndex = operations.findIndex(entry => entry[0] === "clip");
    const itemTransformIndex = operations.findIndex(
        entry => entry[0] === "transform" && entry[5] === 100 && entry[6] === 50);
    assert.ok(clipIndex >= 0 && clipIndex < itemTransformIndex);
    assert.equal(operations.filter(entry => entry[0] === "fillRect").length, 2);
    assert.equal(operations.filter(entry => entry[0] === "save").length,
        operations.filter(entry => entry[0] === "restore").length);
    assert.equal(context.globalCompositeOperation, "source-over");

    const first = structuredClone(operations);
    operations.length = 0;
    await renderer.render(frame);
    assert.deepEqual(operations, first);
    renderer.dispose();
});

test("geometry, text, image cache, measurement and disposal remain renderer-owned", async () => {
    operations.length = 0;
    loadedImages.length = 0;
    const renderer = createRenderer("canvas");
    await renderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 },
        [{ reference: "image:test", uri: "/image.png" }],
        [{
            fontIdentity: "font:test",
            fontVersion: "1",
            fontFamily: "Inter",
            sourceUri: "/inter.woff2",
            fontWeight: 400,
            fontStyle: "normal"
        }],
        "Inter");
    const items = [
        rectangle("rectangle", 1),
        { ...rectangle("ellipse", 1), geometryKind: 1 },
        {
            ...rectangle("path", 2), geometryKind: 2,
            points: [{ x: 0, y: 0 }, { x: 5, y: 5 }, { x: 10, y: 0 }], isClosed: true
        },
        { ...rectangle("image", 4), geometryKind: 4, content: "image:test" },
        {
            ...rectangle("text", 3, {
                textAnchor: { x: 5, y: 2.5 }, textAlignment: 1, textBaseline: 1
            }),
            geometryKind: 3, content: "Neutral", fontFamily: "Inter"
        }
    ];
    const frame = { viewportTransform: matrix(), items };

    assert.equal((await renderer.render(frame)).succeeded, true);
    assert.equal((await renderer.render(frame)).succeeded, true);
    assert.deepEqual(loadedImages, ["/image.png"]);
    assert.ok(operations.some(entry => entry[0] === "ellipse"));
    assert.ok(operations.some(entry => entry[0] === "closePath"));
    assert.deepEqual(
        operations.filter(entry => entry[0] === "fillText").at(-1),
        ["fillText", "Neutral", 5, 2.5]);
    assert.equal(context.textAlign, "center");
    assert.equal(context.textBaseline, "middle");
    assert.ok(operations.some(entry => entry[0] === "drawImage"));

    const measured = renderer.measureText({
        text: "Neutral", fontFamily: "Inter", fontIdentity: "font:test", fontVersion: "1",
        fontSize: 12, lineHeight: 16, fontWeight: 400, fontStyle: "normal",
        locale: "und", direction: "ltr", writingMode: 0, scale: 2
    });
    assert.equal(measured.succeeded, true);
    assert.equal(measured.width, 80);
    assert.equal(measured.resolvedFontIdentity, "font:test@1");
    assert.match(context.font, /Inceptus-/);

    renderer.dispose();
    renderer.dispose();
    assert.equal(fonts.deleted.length >= 1, true);
    assert.equal(context.globalCompositeOperation, "source-over");
    assert.deepEqual(context._lineDash ?? [], []);
});

test("mid-draw browser failure restores balanced graphics state and remains diagnostic", async () => {
    operations.length = 0;
    const renderer = createRenderer("canvas");
    await renderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 }, [], [], null);
    rejectNextFill = true;

    const result = await renderer.render({
        viewportTransform: matrix(),
        items: [rectangle("fails", 1)]
    });

    assert.equal(result.succeeded, false);
    assert.equal(result.code, "CANVAS2D_RENDERER_RENDERING_FAILED");
    assert.equal(
        operations.filter(entry => entry[0] === "save").length,
        operations.filter(entry => entry[0] === "restore").length);
    assert.deepEqual(operations.at(-1), ["setLineDash"]);
    renderer.dispose();
});

test("invalid paint and image loading failures are diagnostic and isolated", async () => {
    operations.length = 0;
    const renderer = createRenderer("canvas");
    await renderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 }, [], [], null);

    const invalidPaint = await renderer.render({
        viewportTransform: matrix(),
        items: [rectangle("invalid", 1, { fill: "invalid" })]
    });
    assert.equal(invalidPaint.code, "CANVAS2D_RENDERER_RENDERING_FAILED");
    assert.equal(operations.some(entry => entry[0] === "clearRect"), false);

    const missingImage = await renderer.render({
        viewportTransform: matrix(),
        items: [{ ...rectangle("image", 4), geometryKind: 4, content: "missing" }]
    });
    assert.equal(missingImage.code, "CANVAS2D_RENDERER_MISSING_IMAGE_RESOURCE");
    renderer.dispose();
});

test("text measurement rejects missing or non-finite native bounding metrics without fallback", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 },
        [],
        [{
            fontIdentity: "font:test", fontVersion: "1", fontFamily: "Inter",
            sourceUri: "/inter.woff2", fontWeight: 400, fontStyle: "normal"
        }],
        "Inter");
    const request = {
        text: "Neutral", fontFamily: "Inter", fontIdentity: "font:test", fontVersion: "1",
        fontSize: 12, lineHeight: 16, fontWeight: 400, fontStyle: "normal",
        locale: "und", direction: "ltr", writingMode: 0, scale: 1
    };
    const original = nativeTextMetrics;

    nativeTextMetrics = { width: 40 };
    const missing = renderer.measureText(request);
    nativeTextMetrics = { ...original, actualBoundingBoxAscent: Number.NaN };
    const nonFinite = renderer.measureText(request);
    nativeTextMetrics = original;

    assert.equal(missing.succeeded, false);
    assert.equal(missing.code, "TEXT_METRICS_MEASUREMENT_FAILURE");
    assert.equal("width" in missing, false);
    assert.equal(nonFinite.succeeded, false);
    assert.equal(nonFinite.code, "TEXT_METRICS_MEASUREMENT_FAILURE");
    renderer.dispose();
});

test("text measurement preserves signed native horizontal bounds when their extent is valid", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 },
        [],
        [{
            fontIdentity: "font:test", fontVersion: "1", fontFamily: "Inter",
            sourceUri: "/inter.woff2", fontWeight: 400, fontStyle: "normal"
        }],
        "Inter");
    const original = nativeTextMetrics;
    const request = {
        text: "Message Boundary Event 1",
        fontFamily: "Inter",
        fontIdentity: "font:test",
        fontVersion: "1",
        fontSize: 12,
        lineHeight: 16,
        fontWeight: 400,
        fontStyle: "normal",
        locale: "und",
        direction: "ltr",
        writingMode: 0,
        scale: 2
    };
    nativeTextMetrics = {
        ...original,
        actualBoundingBoxLeft: -0.25,
        actualBoundingBoxRight: 40.25
    };
    const leftBearing = renderer.measureText(request);
    nativeTextMetrics = {
        ...original,
        actualBoundingBoxLeft: 40.25,
        actualBoundingBoxRight: -0.25
    };
    const rightBearing = renderer.measureText({ ...request, direction: "rtl" });
    nativeTextMetrics = original;

    assert.equal(leftBearing.succeeded, true);
    assert.equal(leftBearing.boundingX, 0.5);
    assert.equal(leftBearing.boundingWidth, 80);
    assert.equal(rightBearing.succeeded, true);
    assert.equal(rightBearing.boundingX, -80.5);
    assert.equal(rightBearing.boundingWidth, 80);
    renderer.dispose();
});

test("text measurement rejects an unsupported locale instead of silently ignoring it", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 },
        [],
        [{
            fontIdentity: "font:test", fontVersion: "1", fontFamily: "Inter",
            sourceUri: "/inter.woff2", fontWeight: 400, fontStyle: "normal"
        }],
        "Inter");

    const measurementsBefore = operations.filter(entry => entry[0] === "measureText").length;
    const result = renderer.measureText({
        text: "Neutral", fontFamily: "Inter", fontIdentity: "font:test", fontVersion: "1",
        fontSize: 12, lineHeight: 16, fontWeight: 400, fontStyle: "normal",
        locale: "en-US", direction: "ltr", writingMode: 0, scale: 1
    });

    assert.equal(result.succeeded, false);
    assert.equal(result.code, "TEXT_METRICS_UNSUPPORTED_CONFIGURATION");
    assert.equal(
        operations.filter(entry => entry[0] === "measureText").length,
        measurementsBefore);
    renderer.dispose();
});

test("font load failure identifies the font, releases canvas ownership, and permits retry", async () => {
    rejectNextFontLoad = true;
    const failedRenderer = createRenderer("canvas");
    const failed = await failedRenderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 },
        [],
        [{
            fontIdentity: "font:broken", fontVersion: "1", fontFamily: "Broken",
            sourceUri: "/broken.woff2", fontWeight: 400, fontStyle: "normal"
        }],
        "Broken");

    assert.equal(failed.succeeded, false);
    assert.equal(failed.code, "CANVAS2D_RENDERER_INITIALIZATION_FAILED");
    assert.equal(failed.sourceIdentity, "font:broken@1");
    const retry = createRenderer("canvas");
    assert.equal((await retry.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 }, [], [], null)).succeeded, true);
    retry.dispose();
});

test("image decode failure is isolated and identifies the configured resource", async () => {
    rejectedImageUri = "/broken.png";
    const renderer = createRenderer("canvas");
    await renderer.initialize(
        { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1 },
        [{ reference: "image:broken", uri: rejectedImageUri }], [], null);

    const failed = await renderer.render({
        viewportTransform: matrix(),
        items: [{ ...rectangle("image", 4), geometryKind: 4, content: "image:broken" }]
    });

    assert.equal(failed.code, "CANVAS2D_RENDERER_IMAGE_LOAD_FAILED");
    assert.equal(failed.sourceIdentity, "image:broken");
    rejectedImageUri = null;
    renderer.dispose();
});
