import assert from "node:assert/strict";
import test from "node:test";
import { pathToFileURL } from "node:url";
import path from "node:path";
import { readFile } from "node:fs/promises";

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
let rejectNextStroke = false;
let version = 0;
const versions = () => ({ contentVersion: ++version, presentationVersion: version });
let pendingImageLoad = null;

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
    stroke() {
        operations.push(["stroke"]);
        if (rejectNextStroke) {
            rejectNextStroke = false;
            throw new Error("stroke failed");
        }
    }
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
        if (pendingImageLoad) await pendingImageLoad;
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
                offsetX: 10,
                offsetY: 5,
                clip: { x: 12, y: 7, width: 6, height: 2 }
            }),
            rectangle("hidden", 5, { isVisible: false })
        ]
    };

    const result = await renderer.render({ ...frame, ...versions() });
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
        entry => entry[0] === "transform" && entry[5] === 10 && entry[6] === 5);
    assert.ok(clipIndex >= 0 && clipIndex < itemTransformIndex);
    assert.equal(operations.filter(entry => entry[0] === "fillRect").length, 2);
    assert.equal(operations.filter(entry => entry[0] === "save").length,
        operations.filter(entry => entry[0] === "restore").length);
    assert.equal(context.globalCompositeOperation, "source-over");

    const first = structuredClone(operations);
    operations.length = 0;
    await renderer.render({ ...frame, ...versions() });
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

    assert.equal((await renderer.render({ ...frame, ...versions() })).succeeded, true);
    assert.equal((await renderer.render({ ...frame, ...versions() })).succeeded, true);
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
        ...versions(),
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
        ...versions(),
        viewportTransform: matrix(),
        items: [rectangle("invalid", 1, { fill: "invalid" })]
    });
    assert.equal(invalidPaint.code, "CANVAS2D_RENDERER_RENDERING_FAILED");
    assert.equal(operations.some(entry => entry[0] === "clearRect"), false);

    const missingImage = await renderer.render({
        ...versions(),
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
        ...versions(),
        viewportTransform: matrix(),
        items: [{ ...rectangle("image", 4), geometryKind: 4, content: "image:broken" }]
    });

    assert.equal(failed.code, "CANVAS2D_RENDERER_IMAGE_LOAD_FAILED");
    assert.equal(failed.sourceIdentity, "image:broken");
    rejectedImageUri = null;
    renderer.dispose();
});

const cacheSurface = { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1.25 };
const viewport = (contentVersion, presentationVersion, offsetX = 0) => ({
    contentVersion, presentationVersion, viewportTransform: { ...matrix(), offsetX }
});
const content = (contentVersion, presentationVersion, items = [rectangle("body", 1)]) => ({
    ...viewport(contentVersion, presentationVersion), items
});

test("bounded primitives preserve full drawing and style order including guides and stable overlays", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cacheSurface, [{ reference: "image:test", uri: "/image.png" }], [delayedFont], "Delayed");
    const base = [rectangle("pool", 0), rectangle("node", 1),
        { ...rectangle("stable-overlay", 5), zIndex: 4000 }];
    const bounded = [
        { ...rectangle("selection", 5), zIndex: 0 },
        { ...rectangle("ghost", 5, { offsetX: 7, clip: { x: 3, y: 2, width: 50, height: 40 } }), zIndex: 3000, opacity: 0.72 },
        { ...rectangle("label", 5), zIndex: 3001, geometryKind: 3, content: "Preview", fontFamily: "Delayed" },
        { ...rectangle("hit-region", 5), zIndex: 5000, opacity: 0 },
        { ...rectangle("anchor", 5), zIndex: 5050, geometryKind: 1 },
    ];
    const presentationItems = bounded.map((item, index) => ({ beforeContentIndex: index < 3 ? 2 : 3, item }));
    const line = { beforeContentIndex: 1, start: { x: 0, y: 0 }, end: { x: 70, y: 0 },
        stroke: "#94a3b8", strokeWidth: 1, dashPattern: [4, 4], opacity: 0.8 };
    const complete = [base[0], base[1], ...bounded.slice(0, 3), base[2], ...bounded.slice(3)];
    const properties = ["globalAlpha", "fillStyle", "strokeStyle", "lineWidth", "font", "textAlign", "textBaseline"];
    for (const name of properties) Object.defineProperty(context, name, {
        configurable: true, set(value) { operations.push([name, value]); }
    });
    try {
        operations.length = 0;
        assert.equal((await renderer.render({ ...content(1, 1, complete), viewportLines: [line] })).succeeded, true);
        const expected = structuredClone(operations);
        operations.length = 0;
        assert.equal((await renderer.render({ ...content(2, 2, base), viewportLines: [line], presentationItems })).succeeded, true);
        assert.deepEqual(operations, expected);
        operations.length = 0;
        assert.equal(renderer.renderViewport({ ...viewport(2, 3), viewportLines: [line], presentationItems }).succeeded, true);
        assert.deepEqual(operations, expected);
        operations.length = 0;
        assert.equal(renderer.renderViewport({ ...viewport(2, 4), viewportLines: [line], presentationItems: null }).succeeded, true);
        assert.deepEqual(operations, expected);
        operations.length = 0;
        assert.equal(renderer.renderViewport({ ...viewport(2, 5), viewportLines: [line], presentationItems: [] }).succeeded, true);
        const cleared = structuredClone(operations);
        operations.length = 0;
        assert.equal((await renderer.render({ ...content(3, 6, base), viewportLines: [line] })).succeeded, true);
        assert.deepEqual(operations, cleared);
    } finally {
        for (const name of properties) delete context[name];
        renderer.dispose();
    }
});

test("bounded requests reject bad indices, stale tokens, unknown resources and surface invalidation", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cacheSurface, [], [], null);
    const item = rectangle("ghost", 5);
    await renderer.render(content(1, 1));
    for (const presentationItems of [
        [{ beforeContentIndex: -1, item }], [{ beforeContentIndex: 2, item }],
        [{ beforeContentIndex: 0.5, item }], [{ beforeContentIndex: 0, item: { ...item, layer: 1 } }],
        [{ beforeContentIndex: 1, item }, { beforeContentIndex: 0, item }],
        Array.from({ length: 129 }, () => ({ beforeContentIndex: 0, item })),
        [{ beforeContentIndex: 0, item: { ...item, fill: "invalid" } }],
    ]) {
        operations.length = 0;
        assert.equal(renderer.renderViewport({ ...viewport(1, 2), presentationItems }).succeeded, false);
        assert.equal(operations.length, 0);
    }
    const presentationItems = [{ beforeContentIndex: 1, item }];
    assert.equal(renderer.renderViewport({ ...viewport(1, 2), presentationItems }).succeeded, true);
    operations.length = 0;
    assert.equal(renderer.renderViewport({ ...viewport(1, 2), presentationItems }).succeeded, false);
    assert.equal(renderer.renderViewport({ ...viewport(2, 3), presentationItems }).succeeded, false);
    assert.equal(operations.length, 0);
    assert.equal(renderer.renderViewport({ ...viewport(1, 3), presentationItems:
        [{ beforeContentIndex: 0, item: { ...item, geometryKind: 4, content: "missing" } }] }).succeeded, false);
    assert.equal(operations.length, 0);
    await renderer.render(content(2, 4));
    renderer.resize({ ...cacheSurface, devicePixelRatio: 2 });
    assert.equal(renderer.renderViewport({ ...viewport(2, 5), presentationItems }).succeeded, false);
    renderer.dispose();
    assert.equal(renderer.renderViewport({ ...viewport(2, 6), presentationItems }).succeeded, false);
});

test("cache installation and scalar redraw preserve exact drawing operations for every primitive", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cacheSurface, [{ reference: "image:test", uri: "/image.png" }],
        [delayedFont], "Delayed");
    const items = [
        rectangle("first", 0, { clip: { x: 1, y: 2, width: 9, height: 8 }, offsetX: 13 }),
        { ...rectangle("ellipse", 1), geometryKind: 1, opacity: 0.3 },
        { ...rectangle("path", 2), geometryKind: 2,
            points: [{ x: 0, y: 2 }, { x: 8, y: 7 }], isClosed: true },
        { ...rectangle("text", 3), geometryKind: 3, content: "Cached text", fontFamily: "Delayed" },
        { ...rectangle("image", 4), geometryKind: 4, content: "image:test" },
        rectangle("hidden", 5, { isVisible: false })
    ];
    // Record state assignments as well as calls: styles/opacity/font must also be identical.
    const names = ["globalAlpha", "fillStyle", "strokeStyle", "lineWidth", "font", "textAlign",
        "textBaseline", "globalCompositeOperation", "lineCap", "lineJoin", "miterLimit"];
    for (const name of names) Object.defineProperty(context, name, {
        configurable: true, set(value) { operations.push([name, value]); }
    });
    try {
        const full = { ...content(1, 1, items), viewportTransform: { ...matrix(), offsetX: 17, offsetY: -23 } };
        operations.length = 0;
        assert.equal((await renderer.render(full)).succeeded, true);
        const reference = structuredClone(operations);
        operations.length = 0;
        assert.equal(renderer.renderViewport({ ...viewport(1, 2), viewportTransform: full.viewportTransform }).succeeded, true);
        assert.deepEqual(operations, reference);
        operations.length = 0;
        assert.equal(renderer.renderViewport({ ...viewport(1, 3), viewportTransform: full.viewportTransform }).succeeded, true);
        assert.deepEqual(operations, reference);
    } finally {
        for (const name of names) delete context[name];
        renderer.dispose();
    }
});

test("absent, mismatched, stale and malformed viewport requests never draw", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cacheSurface, [], [], null);
    assert.equal(renderer.renderViewport(viewport(1, 1)).succeeded, false);
    assert.equal((await renderer.render(content(1, 2))).succeeded, true);
    const count = operations.length;
    for (const request of [viewport(2, 3), viewport(1, 1), viewport(1, 2),
        { ...viewport(1, 4), viewportTransform: { ...matrix(), offsetX: NaN } }]) {
        assert.equal(renderer.renderViewport(request).succeeded, false);
    }
    assert.equal(operations.length, count);
    assert.equal(renderer.renderViewport(viewport(1, 5, 45)).succeeded, true);
    assert.equal((await renderer.render(content(2, 6))).succeeded, true);
    const after = operations.length;
    assert.equal(renderer.renderViewport(viewport(1, 7)).succeeded, false);
    assert.equal((await renderer.render(content(1, 8))).succeeded, false);
    assert.equal(operations.length, after);
    renderer.dispose();
});

test("failed content validation preserves pixels and requires a new full replacement", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cacheSurface, [], [], null);
    assert.equal((await renderer.render(content(1, 1))).succeeded, true);
    const count = operations.length;
    assert.equal((await renderer.render(content(2, 2, [rectangle("bad", 0, { fill: "invalid" })]))).succeeded, false);
    assert.equal(operations.length, count);
    assert.equal(renderer.renderViewport(viewport(2, 3)).succeeded, false);
    assert.equal(renderer.renderViewport(viewport(1, 3)).succeeded, false);
    assert.equal((await renderer.render(content(3, 4))).succeeded, true);
    assert.equal(renderer.renderViewport(viewport(3, 5)).succeeded, true);
    renderer.dispose();
});

for (const superseding of ["content", "resize", "dpr", "dispose"]) {
    test(`late full upload cannot install or draw after ${superseding}`, async () => {
        const renderer = createRenderer("canvas");
        await renderer.initialize(cacheSurface, [{ reference: "slow", uri: "/slow.png" }], [], null);
        let release;
        pendingImageLoad = new Promise(resolve => { release = resolve; });
        const delayed = renderer.render(content(1, 1,
            [{ ...rectangle("slow", 0), geometryKind: 4, content: "slow" }]));
        assert.equal(renderer.renderViewport(viewport(1, 2)).succeeded, false);
        if (superseding === "content") {
            assert.equal((await renderer.render(content(2, 3))).succeeded, true);
            assert.equal(renderer.renderViewport(viewport(2, 4, 52)).succeeded, true);
        } else if (superseding === "dispose") {
            renderer.dispose();
        } else {
            assert.equal(renderer.resize({ ...cacheSurface,
                cssWidth: superseding === "resize" ? 200 : 100,
                devicePixelRatio: superseding === "dpr" ? 2 : 1.25 }).succeeded, true);
        }
        const count = operations.length;
        release();
        pendingImageLoad = null;
        assert.equal((await delayed).succeeded, false);
        assert.equal(operations.length, count);
        if (superseding !== "dispose") {
            assert.equal((await renderer.render(content(3, 5))).succeeded, true);
            assert.equal(renderer.renderViewport(viewport(3, 6)).succeeded, true);
        }
        renderer.dispose();
    });
}

test("resize and disposal clear cached content and fresh canvas owner must upload", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cacheSurface, [], [], null);
    await renderer.render(content(1, 1));
    renderer.resize({ ...cacheSurface, devicePixelRatio: 2 });
    assert.equal(canvas.width, 200);
    assert.equal(renderer.renderViewport(viewport(1, 2)).succeeded, false);
    assert.equal((await renderer.render(content(2, 3))).succeeded, true);
    renderer.dispose();
    assert.equal(renderer.renderViewport(viewport(2, 4)).succeeded, false);
    const next = createRenderer("canvas");
    await next.initialize(cacheSurface, [], [], null);
    assert.equal(next.renderViewport(viewport(2, 5)).succeeded, false);
    assert.equal((await next.render(content(1, 1))).succeeded, true);
    next.dispose();
});

test("renderer caches cannot cross canvases even with identical numeric tokens", async () => {
    const original = document.getElementById;
    const secondCanvas = new FakeCanvas(new FakeContext());
    document.getElementById = id => id === "second" ? secondCanvas : original(id);
    const first = createRenderer("canvas");
    const second = createRenderer("second");
    try {
        await first.initialize(cacheSurface, [], [], null);
        await second.initialize(cacheSurface, [], [], null);
        await first.render(content(1, 1, [rectangle("first", 0, { offsetX: 11 })]));
        assert.equal(second.renderViewport(viewport(1, 2)).succeeded, false);
        await second.render(content(1, 1, [rectangle("second", 0, { offsetX: 99 })]));
        operations.length = 0;
        first.renderViewport(viewport(1, 2));
        assert.ok(operations.some(op => op[0] === "transform" && op[5] === 11));
        assert.ok(!operations.some(op => op[0] === "transform" && op[5] === 99));
    } finally {
        first.dispose(); second.dispose(); document.getElementById = original;
    }
});

test("cached draw execution failure disallows reuse until full recovery", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cacheSurface, [], [], null);
    await renderer.render(content(1, 1));
    rejectNextFill = true;
    assert.equal(renderer.renderViewport(viewport(1, 2)).succeeded, false);
    assert.equal(renderer.renderViewport(viewport(1, 3)).succeeded, false);
    assert.equal((await renderer.render(content(2, 4))).succeeded, true);
    assert.equal(renderer.renderViewport(viewport(2, 5)).succeeded, true);
    renderer.dispose();
});

// Test-only uncullled reference uses the identical drawing code. No production flag/export.
const cullingSource = await readFile(modulePath, "utf8");
const predicateSignature = "function outsideViewport(bounds, visibleBounds) {";
assert.equal(cullingSource.split(predicateSignature).length, 2);
const uncullledSource = cullingSource.replace(predicateSignature,
    `${predicateSignature}\n    return false; // test reference\n`);
const { createRenderer: createUnculledRenderer } = await import(
    `data:text/javascript;base64,${Buffer.from(uncullledSource).toString("base64")}`);

const cullRect = (x, y, width = 10, height = 10, extra = {}) => ({
    ...rectangle("culling", 1), geometryBounds: { x, y, width, height },
    stroke: null, dashPattern: [], opacity: 1, ...extra
});
const cullPath = (points, extra = {}) => cullRect(500, 500, 1, 1, {
    geometryKind: 2, points: points.map(([x, y]) => ({ x, y })),
    isClosed: false, fill: null, stroke: "#123456", strokeWidth: 1, ...extra
});
const cullText = (x, y, extra = {}) => cullRect(x, y, 1, 1, {
    geometryKind: 3, content: "Overhanging glyphs / wrapped label",
    textAnchor: { x, y }, fontFamily: "Delayed", ...extra
});
const cullSurface = { cssWidth: 100, cssHeight: 50, devicePixelRatio: 1.25 };
const viewportLine = (beforeContentIndex = 0, end = { x: 100, y: 0 }) => ({
    beforeContentIndex, start: { x: 0, y: 0 }, end,
    stroke: "#94a3b8", strokeWidth: 1, dashPattern: [4, 4], opacity: 0.8
});

test("bounded lines interleave at canonical indices and appear and disappear without content rescans", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cullSurface, [], [], null);
    const offscreen = cullRect(500, 500);
    const items = [cullRect(10, 10), offscreen, cullRect(30, 10)];
    assert.equal((await renderer.render(content(1, 1, items))).succeeded, true);
    Object.defineProperty(offscreen, "geometryBounds", { get() { throw new Error("bounds rescanned"); } });
    Object.defineProperty(offscreen, "points", { get() { throw new Error("geometry rescanned"); } });
    let presentation = 1;
    try {
        for (const lines of [[], [viewportLine(1)], [viewportLine(1), viewportLine(3, { x: 0, y: 50 })], []]) {
            operations.length = 0;
            assert.equal(renderer.renderViewport({ ...viewport(1, ++presentation), viewportLines: lines }).succeeded, true);
            const drawn = operations.filter(op => ["fillRect", "moveTo"].includes(op[0]));
            assert.deepEqual(drawn.map(op => op[0]), lines.length === 0 ? ["fillRect", "fillRect"] :
                lines.length === 1 ? ["fillRect", "moveTo", "fillRect"] : ["fillRect", "moveTo", "fillRect", "moveTo"]);
            assert.equal(operations.filter(op => op[0] === "stroke").length, lines.length);
            assert.equal(operations.filter(op => op[0] === "save").length,
                operations.filter(op => op[0] === "restore").length);
        }
    } finally { renderer.dispose(); }
});

for (const zoom of [0.25, 1.1, 4]) {
    for (const dpr of [1, 1.25, 2]) {
        test(`bounded line paint agrees with legacy guide paths at zoom ${zoom} DPR ${dpr}`, async () => {
            const renderer = createRenderer("canvas");
            await renderer.initialize({ ...cullSurface, devicePixelRatio: dpr }, [], [], null);
            const line = { ...viewportLine(), strokeWidth: 1 / zoom, dashPattern: [4 / zoom, 4 / zoom] };
            const legacy = cullPath([[0, 0], [100, 0]], {
                geometryBounds: { x: 0, y: 0, width: 100, height: 0 }, ...line
            });
            const transform = { ...matrix(), m11: zoom, m22: zoom };
            const ink = [];
            const originalStroke = context.stroke;
            context.stroke = function () {
                ink.push([this.strokeStyle, this.lineWidth, this.globalAlpha, this.lineCap, this.lineJoin]);
                originalStroke.call(this);
            };
            try {
                operations.length = 0;
                assert.equal((await renderer.render({ ...content(1, 1, [legacy]), viewportTransform: transform })).succeeded, true);
                const paths = operations.filter(op => ["beginPath", "moveTo", "lineTo", "stroke", "setLineDash", "scale"].includes(op[0]));
                const style = ink.pop();
                operations.length = 0;
                assert.equal((await renderer.render({ ...content(2, 2, []), viewportTransform: transform, viewportLines: [line] })).succeeded, true);
                assert.deepEqual(operations.filter(op => ["beginPath", "moveTo", "lineTo", "stroke", "setLineDash", "scale"].includes(op[0])), paths);
                assert.deepEqual(ink.pop(), style);
            } finally { context.stroke = originalStroke; renderer.dispose(); }
        });
    }
}

test("malformed bounded presentations cannot draw or replace acknowledged content", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cullSurface, [], [], null);
    await renderer.render(content(1, 1));
    try {
        for (const lines of [[viewportLine(), viewportLine(), viewportLine()], [viewportLine(-1)],
            [viewportLine(2)], [viewportLine(1), viewportLine(0)], [{ ...viewportLine(), stroke: "invalid" }],
            [{ ...viewportLine(), end: { x: NaN, y: 0 } }], [{ ...viewportLine(), opacity: 2 }]]) {
            operations.length = 0;
            assert.equal(renderer.renderViewport({ ...viewport(1, 2), viewportLines: lines }).succeeded, false);
            assert.equal((await renderer.render({ ...content(2, 2), viewportLines: lines })).succeeded, false);
            assert.equal(operations.length, 0);
        }
        assert.equal(renderer.renderViewport({ ...viewport(1, 2), viewportLines: [viewportLine()] }).succeeded, true);
    } finally { renderer.dispose(); }
});

test("bounded line failure restores Canvas state and requires a fresh full upload", async () => {
    const renderer = createRenderer("canvas");
    await renderer.initialize(cullSurface, [], [], null);
    await renderer.render(content(1, 1));
    operations.length = 0;
    rejectNextStroke = true;
    try {
        assert.equal(renderer.renderViewport({ ...viewport(1, 2), viewportLines: [viewportLine()] }).succeeded, false);
        assert.equal(operations.filter(op => op[0] === "save").length,
            operations.filter(op => op[0] === "restore").length);
        assert.equal(context.globalAlpha, 1);
        assert.equal(renderer.renderViewport({ ...viewport(1, 3), viewportLines: [] }).succeeded, false);
        assert.equal((await renderer.render({ ...content(2, 4), viewportLines: [viewportLine()] })).succeeded, true);
        assert.equal(renderer.renderViewport({ ...viewport(1, 5), viewportLines: [] }).succeeded, false);
        assert.equal(renderer.renderViewport({ ...viewport(2, 4), viewportLines: [] }).succeeded, false);
        renderer.resize({ ...cullSurface, devicePixelRatio: 2 });
        assert.equal(renderer.renderViewport({ ...viewport(2, 5), viewportLines: [] }).succeeded, false);
        assert.equal((await renderer.render({ ...content(3, 6), viewportLines: [viewportLine()] })).succeeded, true);
    } finally { renderer.dispose(); }
    assert.equal(renderer.renderViewport({ ...viewport(3, 7), viewportLines: [] }).succeeded, false);
});

const cullCases = [
    ["offscreen rectangle", cullRect(500, 500), false],
    ["partially visible rectangle", cullRect(-5, 20), true],
    ["fully visible rectangle", cullRect(20, 20), true],
    ["left edge touch", cullRect(-10, 20), true],
    ["right edge touch", cullRect(100, 20), true],
    ["top edge touch", cullRect(20, -10), true],
    ["bottom edge touch", cullRect(20, 50), true],
    ["corner touch", cullRect(100, 50), true],
    ["raster fringe", cullRect(100.5, 20), true],
    ["translated local rectangle", cullRect(500, 500, 10, 10, { transform: matrix(-490, -480) }), true],
    ["rotated local rectangle", cullRect(0, 0, 20, 10, {
        transform: { m11: 0, m12: 1, m21: -1, m22: 0, offsetX: 105, offsetY: 10 }
    }), true],
    ["scaled sheared reflected local rectangle", cullRect(10, 10, 20, 10, {
        transform: { m11: -2, m12: 0.5, m21: 1, m22: 2, offsetX: 60, offsetY: -20 }
    }), true],
    ["offscreen transformed rectangle", cullRect(10, 10, 20, 10, { transform: matrix(500, 500) }), false],
    ["stroke overlaps edge", cullRect(102, 20, 10, 10, { stroke: "#123456", strokeWidth: 8 }), true],
    ["zero stroke width retains inherited ink", cullRect(500, 500, 10, 10, { stroke: "#123456", strokeWidth: 0 }), true],
    ["ellipse intersects edge", cullRect(95, 20, 20, 20, { geometryKind: 1 }), true],
    ["ellipse offscreen", cullRect(500, 500, 20, 20, { geometryKind: 1 }), false],
    ["connector crosses with both endpoints outside", cullPath([[-100, 20], [200, 20]]), true],
    ["intermediate route segment crosses", cullPath([[-100, -100], [50, -100], [50, 100], [200, 100]]), true],
    ["diagonal connector crosses", cullPath([[-100, -100], [200, 100]]), true],
    ["connector fully outside", cullPath([[400, 400], [500, 400], [500, 500]]), false],
    ["miter and dashed path near edge", cullPath([[110, 5], [100, 25], [110, 24]], {
        strokeWidth: 4, dashPattern: [3, 2]
    }), true],
    ["closed arrowhead at edge", cullPath([[95, 20], [110, 15], [110, 25]], {
        isClosed: true, fill: "#123456"
    }), true],
    ["partially visible text", cullText(-5, 20), true],
    ["offscreen text remains conservative", cullText(500, 500), true],
    ["text bounds cannot override anchor or alignment", cullText(500, 500, {
        textAnchor: { x: 100, y: 50 }, textAlignment: 2, textBaseline: 2,
        transform: { ...matrix(), m11: 2, m22: 2 }, stroke: "#123456", strokeWidth: 5
    }), true],
    ["image offscreen", cullRect(500, 500, 20, 20, { geometryKind: 4, content: "image:test" }), false],
    ["image partial", cullRect(95, 20, 20, 20, { geometryKind: 4, content: "image:test" }), true],
    ["document clip ignored conservatively", cullRect(0, 0, 20, 20, {
        clip: { x: 500, y: 500, width: 10, height: 10 }
    }), true],
    ["document clip before local transform", cullRect(500, 500, 20, 20, {
        transform: matrix(-490, -480), clip: { x: 10, y: 20, width: 10, height: 10 }
    }), true],
    ["nonfinite bounds draw conservatively", cullRect(NaN, 500), true],
    ["negative extent draw conservatively", cullRect(500, 500, -10), true],
    ["extreme coordinates draw conservatively", cullRect(1e100, 1e100), true],
    ["uncertain transform draw conservatively", cullRect(500, 500, 10, 10, { transform: { ...matrix(), m11: NaN } }), true],
    ["invalid clip draw conservatively", cullRect(500, 500, 10, 10, { clip: { x: NaN, y: 0, width: 10, height: 10 } }), true],
    ["invalid path point draw conservatively", cullPath([[500, 500], [NaN, 500]]), true],
    ["invalid dash draw conservatively", cullRect(500, 500, 10, 10, { dashPattern: [NaN] }), true],
    ["zero opacity preserves conservative text path", cullText(500, 500, { opacity: 0 }), true]
];

async function cullingRenderer(factory = createRenderer, surface = cullSurface) {
    const renderer = factory("canvas");
    assert.equal((await renderer.initialize(surface, [{ reference: "image:test", uri: "/image.png" }],
        [delayedFont], "Delayed")).succeeded, true);
    return renderer;
}

function itemCommandGroups(log) {
    let depth = 0, current;
    const groups = [];
    for (const operation of log) {
        if (operation[0] === "save") {
            if (depth === 1) { current = []; groups.push(current); }
            depth++;
        }
        if (depth >= 2) current.push(operation);
        if (operation[0] === "restore") depth--;
    }
    return groups;
}

for (const [name, item, drawn] of cullCases) {
    test(`viewport culling: ${name}; identical retained Canvas commands`, async () => {
        const names = ["globalAlpha", "fillStyle", "strokeStyle", "lineWidth", "font", "textAlign",
            "textBaseline", "globalCompositeOperation", "lineCap", "lineJoin", "miterLimit"];
        for (const property of names) Object.defineProperty(context, property, {
            configurable: true, set(value) { operations.push([property, value]); }
        });
        let renderer;
        try {
            renderer = await cullingRenderer(createUnculledRenderer);
            operations.length = 0;
            assert.equal((await renderer.render(content(1, 1, [item]))).succeeded, true);
            const reference = itemCommandGroups(operations);
            assert.equal(reference.length, 1);
            renderer.dispose();
            renderer = await cullingRenderer();
            operations.length = 0;
            assert.equal((await renderer.render(content(1, 1, [item]))).succeeded, true);
            assert.deepEqual(itemCommandGroups(operations), drawn ? reference : []);
            operations.length = 0;
            assert.equal(renderer.renderViewport(viewport(1, 2)).succeeded, true);
            assert.deepEqual(itemCommandGroups(operations), drawn ? reference : []);
        } finally {
            renderer?.dispose();
            for (const property of names) delete context[property];
        }
    });
}

test("culling preserves original mixed-primitive order and invisible-item behavior", async () => {
    const items = cullCases.map((entry, index) => ({ ...entry[1], id: String(index), layer: index % 6 }));
    items.splice(4, 0, cullRect(20, 20, 10, 10, { isVisible: false }));
    let renderer = await cullingRenderer(createUnculledRenderer);
    operations.length = 0;
    assert.equal((await renderer.render(content(1, 1, items))).succeeded, true);
    const reference = itemCommandGroups(operations).filter((_, index) => cullCases[index][2]);
    renderer.dispose();
    renderer = await cullingRenderer();
    try {
        operations.length = 0;
        assert.equal((await renderer.render(content(1, 1, items))).succeeded, true);
        assert.deepEqual(itemCommandGroups(operations), reference);
    } finally { renderer.dispose(); }
});

test("cached Pan repeatedly enters, crosses and leaves the surface without upload or geometry scans", async () => {
    const renderer = await cullingRenderer();
    const item = cullPath([[500, 20], [520, 20]]);
    try {
        assert.equal((await renderer.render(content(1, 1, [item]))).succeeded, true);
        // Detect an accidental per-Pan bounds/path scan; drawItem still needs its path when visible.
        const original = item.geometryBounds;
        Object.defineProperty(item, "geometryBounds", { get() { throw new Error("rescanned bounds"); } });
        let presentation = 1;
        for (const [x, drawn] of [[0, false], [-405, true], [-480, true], [-600, false],
            [-480, true], [0, false], [-405, true], [0, false]]) {
            operations.length = 0;
            assert.equal(renderer.renderViewport(viewport(1, ++presentation, x)).succeeded, true);
            assert.equal(operations.some(op => op[0] === "stroke"), drawn);
        }
        assert.ok(original);
    } finally { renderer.dispose(); }
});

for (const [name, transform, drawn] of [
    ["rotation", { m11: 0, m12: 1, m21: -1, m22: 0, offsetX: 120, offsetY: 0 }, true],
    ["shear and reflection", { m11: -1, m12: 0.2, m21: 1, m22: 1, offsetX: 30, offsetY: 0 }, true],
    ["scaled outside", { ...matrix(500, 500), m11: 2, m22: 3 }, false],
    ["singular fallback", { ...matrix(500, 500), m11: 0, m22: 0 }, true],
    ["ill-conditioned fallback", { ...matrix(500, 500), m11: 1e-12 }, true],
    ["extreme fallback", matrix(1e100, 1e100), true]
]) {
    test(`viewport affine culling: ${name}`, async () => {
        const renderer = await cullingRenderer();
        try {
            operations.length = 0;
            assert.equal((await renderer.render({ ...content(1, 1, [cullRect(20, 20)]),
                viewportTransform: transform })).succeeded, true);
            assert.equal(operations.some(op => op[0] === "fillRect"), drawn);
        } finally { renderer.dispose(); }
    });
}

test("resize and DPR use actual rounded backing bounds and invalidate content metadata", async () => {
    const renderer = await cullingRenderer();
    try {
        let token = 0;
        for (const [surface, x, drawn] of [
            [cullSurface, 150, false],
            [{ ...cullSurface, cssWidth: 200 }, 150, true],
            [{ ...cullSurface, devicePixelRatio: 2 }, 150, false],
            [{ cssWidth: 0.25, cssHeight: 0.25, devicePixelRatio: 0.5 }, 1.5, true]
        ]) {
            assert.equal(renderer.resize(surface).succeeded, true);
            assert.equal(renderer.renderViewport(viewport(token, token + 1)).succeeded, false);
            operations.length = 0;
            assert.equal((await renderer.render(content(++token, token, [cullRect(x, 0, 0.1, 0.1)]))).succeeded, true);
            assert.equal(operations.some(op => op[0] === "fillRect"), drawn);
        }
    } finally { renderer.dispose(); }
});

test("full replacement, disposal and a fresh owner cannot reuse stale bounds", async () => {
    let renderer = await cullingRenderer();
    for (const [token, x, drawn] of [[1, 500, false], [2, 20, true], [3, 500, false]]) {
        operations.length = 0;
        assert.equal((await renderer.render(content(token, token, [cullRect(x, 20)]))).succeeded, true);
        assert.equal(operations.some(op => op[0] === "fillRect"), drawn);
    }
    renderer.dispose();
    assert.equal(renderer.renderViewport(viewport(3, 4)).succeeded, false);
    renderer = await cullingRenderer();
    try {
        assert.equal(renderer.renderViewport(viewport(3, 4)).succeeded, false);
        operations.length = 0;
        assert.equal((await renderer.render(content(1, 1, [cullRect(20, 20)]))).succeeded, true);
        assert.equal(operations.some(op => op[0] === "fillRect"), true);
    } finally { renderer.dispose(); }
});

test("independent canvases keep independent culling metadata and viewports", async () => {
    const original = document.getElementById;
    const secondCanvas = new FakeCanvas(new FakeContext());
    document.getElementById = id => id === "second" ? secondCanvas : original(id);
    const first = await cullingRenderer();
    const second = createRenderer("second");
    try {
        await second.initialize(cullSurface, [], [], null);
        await first.render(content(1, 1, [cullRect(500, 20)]));
        await second.render(content(1, 1, [cullRect(20, 20)]));
        for (const [renderer, drawn] of [[first, false], [second, true]]) {
            operations.length = 0;
            assert.equal(renderer.renderViewport(viewport(1, 2)).succeeded, true);
            assert.equal(operations.some(op => op[0] === "fillRect"), drawn);
        }
        first.dispose();
        assert.equal(second.renderViewport(viewport(1, 3)).succeeded, true);
    } finally { first.dispose(); second.dispose(); document.getElementById = original; }
});

test("offscreen invalid paint, missing resources and unsupported geometry retain failures", async () => {
    const renderer = await cullingRenderer();
    try {
        for (const [item, code] of [
            [cullRect(500, 500, 10, 10, { fill: "invalid" }), "CANVAS2D_RENDERER_RENDERING_FAILED"],
            [cullRect(500, 500, 10, 10, { geometryKind: 4, content: "missing" }), "CANVAS2D_RENDERER_MISSING_IMAGE_RESOURCE"],
            [cullText(500, 500, { fontFamily: "missing" }), "CANVAS2D_RENDERER_RENDERING_FAILED"],
            [cullRect(500, 500, 10, 10, { geometryKind: 99 }), "CANVAS2D_RENDERER_RENDERING_FAILED"],
            [cullRect(500, 500, 10, 10, { geometryBounds: null }), "CANVAS2D_RENDERER_RENDERING_FAILED"]
        ]) {
            const tokens = versions();
            assert.equal((await renderer.render({ ...tokens, viewportTransform: matrix(), items: [item] })).code, code);
            assert.equal(renderer.renderViewport(viewport(tokens.contentVersion, tokens.presentationVersion + 1)).succeeded, false);
        }
    } finally { renderer.dispose(); }
});

test("unexpected Canvas shadows or filters disable culling without changing drawing state", async () => {
    const renderer = await cullingRenderer();
    try {
        for (const [property, value] of [["filter", "blur(500px)"], ["shadowBlur", 1000],
            ["shadowOffsetX", -500], ["shadowOffsetY", -500]]) {
            context[property] = value;
            operations.length = 0;
            assert.equal((await renderer.render({ ...versions(), viewportTransform: matrix(),
                items: [cullRect(500, 500)] })).succeeded, true);
            assert.equal(operations.some(op => op[0] === "fillRect"), true);
            assert.equal(context[property], value);
            delete context[property];
        }
    } finally { renderer.dispose(); }
});
