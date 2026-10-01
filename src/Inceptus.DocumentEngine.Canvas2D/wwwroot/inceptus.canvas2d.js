/**
 * Private browser implementation of the single concrete Canvas2DRenderer.
 * No canvas, context, Path2D, image, or font resource crosses this module boundary.
 */

const rendererOwner = Symbol("inceptus.canvas2d.renderer-owner");

export function createRenderer(canvasElementId) {
    const canvas = document.getElementById(canvasElementId);
    if (!(canvas instanceof HTMLCanvasElement)) {
        return new UnavailableRenderer("CANVAS2D_RENDERER_CANVAS_NOT_FOUND", canvasElementId);
    }

    const context = canvas.getContext("2d");
    if (!context) {
        return new UnavailableRenderer("CANVAS2D_RENDERER_CONTEXT_UNAVAILABLE", canvasElementId);
    }

    return new Renderer(canvas, context, canvasElementId);
}

class Renderer {
    #canvas;
    #context;
    #defaultFontFamily;
    #imageUris = new Map();
    #images = new Map();
    #fontFaces = [];
    #fontAliases = new Map();
    #ownsCanvas = false;
    #initialized = false;
    #disposed = false;
    #cancelFontLoad = null;
    #content = null;
    #contentBounds = null;
    #presentationItems = [];
    #contentVersion = 0;
    #presentationVersion = 0;
    #operationEpoch = 0;
    #contentReady = false;

    constructor(canvas, context, canvasElementId) {
        this.#canvas = canvas;
        this.#context = context;
        this.canvasElementId = canvasElementId;
    }

    async initialize(surface, imageResources, fontResources, defaultFontFamily) {
        if (this.#disposed) {
            return failure("CANVAS2D_RENDERER_DISPOSED", this.canvasElementId);
        }
        if (this.#initialized || this.#ownsCanvas) {
            return failure("CANVAS2D_RENDERER_ALREADY_INITIALIZED", this.canvasElementId);
        }

        // JavaScript runs through this assignment before the first await. This reserves the
        // physical canvas without exposing a module-global mutable registry or an async race.
        if (this.#canvas[rendererOwner] && this.#canvas[rendererOwner] !== this) {
            return failure("CANVAS2D_RENDERER_ALREADY_INITIALIZED", this.canvasElementId);
        }
        this.#canvas[rendererOwner] = this;
        this.#ownsCanvas = true;

        for (const resource of imageResources) {
            this.#imageUris.set(resource.reference, resource.uri);
        }

        for (const resource of fontResources ?? []) {
            try {
                const privateFamily = privateFontFamily(resource);
                const face = new FontFace(
                    privateFamily,
                    `url(${JSON.stringify(resource.sourceUri)})`,
                    { weight: String(resource.fontWeight), style: resource.fontStyle });
                await this.#loadFont(face);
                if (this.#disposed) {
                    return failure("CANVAS2D_RENDERER_DISPOSED", this.canvasElementId);
                }
                document.fonts.add(face);
                this.#fontFaces.push(face);
                this.#fontAliases.set(fontKey(resource), privateFamily);
                this.#fontAliases.set(
                    fontFaceKey(resource.fontFamily, resource.fontWeight, resource.fontStyle),
                    privateFamily);
            } catch {
                this.#releaseFonts(true);
                this.#imageUris.clear();
                this.#fontAliases.clear();
                this.#releaseOwnership();
                return failure(
                    "CANVAS2D_RENDERER_INITIALIZATION_FAILED",
                    `${resource.fontIdentity}@${resource.fontVersion}`);
            }
        }

        this.#defaultFontFamily = defaultFontFamily;
        const resizeResult = this.resize(surface);
        if (!resizeResult.succeeded) {
            this.#releaseFonts(true);
            this.#imageUris.clear();
            this.#fontAliases.clear();
            this.#releaseOwnership();
            return resizeResult;
        }

        this.#initialized = true;
        return success();
    }

    resize(surface) {
        if (this.#disposed) {
            return failure("CANVAS2D_RENDERER_DISPOSED", "Canvas2DRenderer");
        }

        if (!validSurface(surface)) {
            return failure("CANVAS2D_RENDERER_RESIZE_FAILED", "Canvas2DSurfaceSize");
        }

        const width = Math.max(1, Math.round(surface.cssWidth * surface.devicePixelRatio));
        const height = Math.max(1, Math.round(surface.cssHeight * surface.devicePixelRatio));
        if (!Number.isSafeInteger(width) || !Number.isSafeInteger(height)) {
            return failure("CANVAS2D_RENDERER_RESIZE_FAILED", "Canvas2DSurfaceSize");
        }

        this.#operationEpoch++;
        this.#content = null;
        this.#contentBounds = null;
        this.#presentationItems = [];
        this.#contentReady = false;
        this.#canvas.style.width = `${surface.cssWidth}px`;
        this.#canvas.style.height = `${surface.cssHeight}px`;
        if (this.#canvas.width !== width) {
            this.#canvas.width = width;
        }
        if (this.#canvas.height !== height) {
            this.#canvas.height = height;
        }
        this.#canvas.dataset.inceptusDpr = String(surface.devicePixelRatio);
        return success();
    }

    async render(frame) {
        if (this.#disposed) {
            return failure("CANVAS2D_RENDERER_DISPOSED", "Canvas2DRenderer");
        }

        if (!this.#initialized || !validPresentation(frame) || !Array.isArray(frame.items) ||
            !validViewportLines(frame.viewportLines, frame.items.length) ||
            !validPresentationItems(frame.presentationItems, frame.items.length) ||
            frame.contentVersion <= this.#contentVersion ||
            frame.presentationVersion <= this.#presentationVersion) {
            return failure("CANVAS2D_RENDERER_RENDERING_FAILED", "Canvas2DRenderFrame");
        }

        // Reserve ordering before the first await. A newer upload/resize/disposal invalidates
        // this operation even if an old image decode subsequently completes successfully.
        this.#contentVersion = frame.contentVersion;
        this.#presentationVersion = frame.presentationVersion;
        const epoch = ++this.#operationEpoch;
        this.#contentReady = false;
        if (!frame.items.every(validItemPaint)) {
            return failure("CANVAS2D_RENDERER_RENDERING_FAILED", "Canvas2DSceneStyle");
        }

        const dpr = Number(this.#canvas.dataset.inceptusDpr);
        if (!finitePositive(dpr)) {
            return failure("CANVAS2D_RENDERER_RESIZE_FAILED", "Canvas2DSurfaceSize");
        }

        const resources = await this.#loadRequiredImages(frame.presentationItems?.length
            ? frame.items.concat(frame.presentationItems.map(entry => entry.item)) : frame.items);
        if (this.#disposed || epoch !== this.#operationEpoch) {
            return failure("CANVAS2D_RENDERER_RENDERING_FAILED", "Canvas2DRenderFrame");
        }
        if (!resources.succeeded) {
            return resources;
        }

        // Validation and resources complete before replacing the one complete drawing set.
        // There is no asynchronous boundary between installation and drawing.
        const bounds = frame.items.map(conservativeItemBounds);
        this.#content = frame.items;
        this.#contentBounds = bounds;
        this.#presentationItems = frame.presentationItems ?? [];
        const result = this.#draw(frame.viewportTransform, this.#content, dpr,
            frame.viewportLines, this.#presentationItems);
        this.#contentReady = result.succeeded;
        return result;
    }

    renderViewport(frame) {
        if (this.#disposed) {
            return failure("CANVAS2D_RENDERER_DISPOSED", "Canvas2DRenderer");
        }
        if (!this.#initialized || !validPresentation(frame) ||
            !this.#contentReady || !this.#content ||
            !validViewportLines(frame.viewportLines, this.#content.length) ||
            !validPresentationItems(frame.presentationItems, this.#content.length) ||
            frame.contentVersion !== this.#contentVersion ||
            frame.presentationVersion <= this.#presentationVersion) {
            return failure("CANVAS2D_RENDERER_RENDERING_FAILED", "Canvas2DViewportFrame");
        }
        this.#presentationVersion = frame.presentationVersion;
        const dpr = Number(this.#canvas.dataset.inceptusDpr);
        if (!finitePositive(dpr)) {
            this.#contentReady = false;
            return failure("CANVAS2D_RENDERER_RESIZE_FAILED", "Canvas2DSurfaceSize");
        }
        // Bounded requests may only use image resources already installed by a full frame.
        if (frame.presentationItems?.some(entry => entry.item.isVisible &&
            entry.item.geometryKind === 4 && !this.#images.has(entry.item.content))) {
            this.#contentReady = false;
            return failure("CANVAS2D_RENDERER_RENDERING_FAILED", "Canvas2DRenderFrame");
        }
        this.#presentationItems = frame.presentationItems ?? this.#presentationItems;
        const result = this.#draw(frame.viewportTransform, this.#content, dpr,
            frame.viewportLines, this.#presentationItems);
        this.#contentReady = result.succeeded;
        return result;
    }

    #draw(viewportTransform, items, dpr, viewportLines = null, presentationItems = null) {
        const context = this.#context;
        try {
            context.save();
            context.setTransform(1, 0, 0, 1, 0, 0);
            context.clearRect(0, 0, this.#canvas.width, this.#canvas.height);
            context.globalCompositeOperation = "source-over";
            context.globalAlpha = 1;
            context.lineCap = "butt";
            context.lineJoin = "miter";
            context.miterLimit = 10;
            context.textAlign = "start";
            context.textBaseline = "top";
            context.setLineDash([]);
            context.scale(dpr, dpr);
            applyTransform(context, viewportTransform);

            const visibleBounds = hasUnboundedEffects(context) ? null :
                visibleDocumentBounds(viewportTransform, this.#canvas.width, this.#canvas.height, dpr);
            let lineIndex = 0;
            let presentationIndex = 0;
            // Items arrive in the immutable Scene's canonical layer/z/identity order.
            for (let index = 0; index <= items.length; index++) {
                while (lineIndex < (viewportLines?.length ?? 0) &&
                    viewportLines[lineIndex].beforeContentIndex === index) {
                    this.#drawViewportLine(viewportLines[lineIndex++]);
                }
                while (presentationIndex < (presentationItems?.length ?? 0) &&
                    presentationItems[presentationIndex].beforeContentIndex === index) {
                    const item = presentationItems[presentationIndex++].item;
                    if (item.isVisible && !outsideViewport(conservativeItemBounds(item), visibleBounds)) {
                        this.#drawItem(item);
                    }
                }
                if (index === items.length) {
                    break;
                }
                const item = items[index];
                if (!item.isVisible) {
                    continue;
                }
                if (outsideViewport(this.#contentBounds[index], visibleBounds)) {
                    continue;
                }
                this.#drawItem(item);
            }
            while (lineIndex < (viewportLines?.length ?? 0)) {
                this.#drawViewportLine(viewportLines[lineIndex++]);
            }
            context.restore();
            return success();
        } catch {
            // Always restore a known graphics state even when one browser operation fails.
            try {
                context.restore();
                context.setTransform(1, 0, 0, 1, 0, 0);
                context.globalCompositeOperation = "source-over";
                context.globalAlpha = 1;
                context.lineCap = "butt";
                context.lineJoin = "miter";
                context.miterLimit = 10;
                context.textAlign = "start";
                context.textBaseline = "top";
                context.setLineDash([]);
            } catch {
                // The renderer result remains the sole observable failure surface.
            }
            return failure("CANVAS2D_RENDERER_RENDERING_FAILED", "Canvas2DRenderFrame");
        }
    }

    #drawViewportLine(line) {
        const context = this.#context;
        context.save();
        try {
            context.globalAlpha = line.opacity;
            context.strokeStyle = line.stroke;
            context.lineWidth = line.strokeWidth;
            context.setLineDash(line.dashPattern);
            context.beginPath();
            context.moveTo(line.start.x, line.start.y);
            context.lineTo(line.end.x, line.end.y);
            context.stroke();
        } finally {
            context.restore();
        }
    }

    measureText(request) {
        if (this.#disposed) {
            return failure("TEXT_METRICS_MEASUREMENT_FAILURE", "Canvas2DRenderer");
        }

        if (!request || request.locale !== "und" || request.writingMode !== 0 ||
            !finitePositive(request.scale)) {
            return failure("TEXT_METRICS_UNSUPPORTED_CONFIGURATION", request?.fontIdentity ?? "TextMeasurementRequest");
        }

        const privateFontFamily = this.#fontAliases.get(fontKey(request));
        if (!privateFontFamily ||
            !document.fonts ||
            !document.fonts.check(fontDeclaration(request, privateFontFamily))) {
            return failure("TEXT_METRICS_UNAVAILABLE_FONT", request.fontIdentity);
        }

        try {
            const context = this.#context;
            context.save();
            let nativeMetrics;
            try {
                context.font = fontDeclaration(request, privateFontFamily);
                context.direction = request.direction;
                context.textAlign = "start";
                context.textBaseline = "alphabetic";
                nativeMetrics = context.measureText(request.text);
            } finally {
                context.restore();
            }

            if (![nativeMetrics.width,
                nativeMetrics.actualBoundingBoxAscent,
                nativeMetrics.actualBoundingBoxDescent].every(finiteNonNegative) ||
                ![nativeMetrics.actualBoundingBoxLeft,
                    nativeMetrics.actualBoundingBoxRight].every(Number.isFinite)) {
                return failure("TEXT_METRICS_MEASUREMENT_FAILURE", request.fontIdentity);
            }

            const ascent = nativeMetrics.actualBoundingBoxAscent * request.scale;
            const descent = nativeMetrics.actualBoundingBoxDescent * request.scale;
            const left = nativeMetrics.actualBoundingBoxLeft * request.scale;
            const right = nativeMetrics.actualBoundingBoxRight * request.scale;
            const width = nativeMetrics.width * request.scale;
            if (![ascent, descent, width, left + right, ascent + descent]
                .every(finiteNonNegative) ||
                ![left, right].every(Number.isFinite)) {
                return failure("TEXT_METRICS_MEASUREMENT_FAILURE", request.fontIdentity);
            }
            return {
                succeeded: true,
                code: null,
                sourceIdentity: request.fontIdentity,
                width,
                ascent,
                descent,
                lineHeight: request.lineHeight * request.scale,
                boundingX: -left,
                boundingY: -ascent,
                boundingWidth: left + right,
                boundingHeight: ascent + descent,
                resolvedFontIdentity: `${request.fontIdentity}@${request.fontVersion}`
            };
        } catch {
            return failure("TEXT_METRICS_MEASUREMENT_FAILURE", request.fontIdentity);
        }
    }

    dispose() {
        if (this.#disposed) {
            return;
        }
        this.#disposed = true;
        this.#operationEpoch++;
        this.#content = null;
        this.#contentBounds = null;
        this.#contentReady = false;
        this.#presentationItems = [];
        this.#cancelFontLoad?.();
        const ownedCanvas = this.#ownsCanvas;
        let failed = false;
        try {
            this.#releaseOwnership();
        } catch {
            failed = true;
        }
        try {
            this.#images.clear();
            this.#imageUris.clear();
            this.#fontAliases.clear();
        } catch {
            failed = true;
        }
        try {
            this.#releaseFonts();
        } catch {
            failed = true;
        }
        try {
            if (ownedCanvas && this.#context) {
                this.#context.setTransform(1, 0, 0, 1, 0, 0);
                this.#context.globalCompositeOperation = "source-over";
                this.#context.globalAlpha = 1;
                this.#context.setLineDash([]);
            }
        } catch {
            failed = true;
        } finally {
            this.#canvas = null;
            this.#context = null;
        }
        if (failed) {
            throw new Error("Canvas2D browser-resource disposal failed.");
        }
    }

    #releaseOwnership() {
        if (this.#canvas?.[rendererOwner] === this) {
            delete this.#canvas[rendererOwner];
        }
        this.#ownsCanvas = false;
    }

    async #loadFont(face) {
        let timeout;
        try {
            await new Promise((resolve, reject) => {
                this.#cancelFontLoad = () => reject(new Error("Canvas2D font loading was cancelled."));
                // Infrastructure failure must settle startup even when the browser leaves a
                // request pending. Late completion never registers a face or touches the canvas.
                timeout = setTimeout(() => reject(new Error("Canvas2D font loading timed out.")), 30000);
                face.load().then(resolve, reject);
            });
        } finally {
            clearTimeout(timeout);
            this.#cancelFontLoad = null;
        }
    }

    #releaseFonts(suppressErrors = false) {
        let failed = false;
        if (document.fonts) {
            for (const face of this.#fontFaces) {
                try {
                    document.fonts.delete(face);
                } catch {
                    failed = true;
                }
            }
        }
        this.#fontFaces.length = 0;
        if (failed && !suppressErrors) {
            throw new Error("Canvas2D font disposal failed.");
        }
    }

    #drawItem(item) {
        const context = this.#context;
        context.save();
        try {
            context.globalCompositeOperation = "source-over";
            context.globalAlpha = item.opacity;
            context.lineCap = "butt";
            context.lineJoin = "miter";
            context.miterLimit = 10;
            context.textAlign = "start";
            context.textBaseline = "top";
            context.lineWidth = item.strokeWidth;
            context.setLineDash(item.dashPattern ?? []);
            if (item.clip) {
                context.beginPath();
                context.rect(item.clip.x, item.clip.y, item.clip.width, item.clip.height);
                context.clip();
            }
            applyTransform(context, item.transform);
            context.fillStyle = "rgba(0,0,0,0)";
            context.strokeStyle = "rgba(0,0,0,0)";
            if (item.fill) context.fillStyle = item.fill;
            if (item.stroke) context.strokeStyle = item.stroke;
            if (item.geometryKind === 3) {
                const family = item.fontFamily ?? this.#defaultFontFamily;
                const alias = this.#fontAliases.get(fontFaceKey(family, 400, "normal"));
                if (!alias) throw new Error("Configured scene font is unavailable.");
                context.font = `normal 400 ${item.fontSize}px ${quoteFont(alias)}`;
            }

            switch (item.geometryKind) {
                case 0:
                    drawRectangle(context, item);
                    break;
                case 1:
                    drawEllipse(context, item);
                    break;
                case 2:
                    drawPath(context, item);
                    break;
                case 3:
                    drawText(context, item);
                    break;
                case 4:
                    this.#drawImage(item);
                    break;
                default:
                    throw new Error("Unsupported scene geometry.");
            }
        } finally {
            context.restore();
        }
    }

    #drawImage(item) {
        const image = this.#images.get(item.content);
        if (!image) {
            throw new Error("Image resource was not preloaded.");
        }
        const bounds = item.geometryBounds;
        this.#context.drawImage(image, bounds.x, bounds.y, bounds.width, bounds.height);
    }

    async #loadRequiredImages(items) {
        const references = [...new Set(items
            .filter(item => item.isVisible && item.geometryKind === 4)
            .map(item => item.content))]
            .sort();
        for (const reference of references) {
            if (this.#images.has(reference)) {
                continue;
            }
            const uri = this.#imageUris.get(reference);
            if (!uri) {
                return failure("CANVAS2D_RENDERER_MISSING_IMAGE_RESOURCE", reference);
            }
            try {
                const image = new Image();
                image.decoding = "async";
                image.src = uri;
                await image.decode();
                if (this.#disposed) {
                    return failure("CANVAS2D_RENDERER_DISPOSED", "Canvas2DRenderer");
                }
                this.#images.set(reference, image);
            } catch {
                return failure("CANVAS2D_RENDERER_IMAGE_LOAD_FAILED", reference);
            }
        }
        return success();
    }
}

function validPresentationItems(items, contentLength) {
    if (items == null) return true;
    if (!Array.isArray(items) || items.length > 128) return false;
    let precedingIndex = -1;
    return items.every(entry => {
        if (!entry || !Number.isSafeInteger(entry.beforeContentIndex) ||
            entry.beforeContentIndex < precedingIndex || entry.beforeContentIndex > contentLength ||
            entry.beforeContentIndex < 0 || !entry.item || entry.item.layer !== 5 ||
            !validItemPaint(entry.item)) return false;
        precedingIndex = entry.beforeContentIndex;
        return true;
    });
}

class UnavailableRenderer {
    constructor(code, sourceIdentity) {
        this.code = code;
        this.sourceIdentity = sourceIdentity;
    }

    initialize() {
        return failure(this.code, this.sourceIdentity);
    }

    dispose() {}
}

function drawRectangle(context, item) {
    const b = item.geometryBounds;
    if (item.fill) context.fillRect(b.x, b.y, b.width, b.height);
    if (item.stroke) context.strokeRect(b.x, b.y, b.width, b.height);
}

function drawEllipse(context, item) {
    const b = item.geometryBounds;
    context.beginPath();
    context.ellipse(b.x + b.width / 2, b.y + b.height / 2, b.width / 2, b.height / 2, 0, 0, Math.PI * 2);
    if (item.fill) context.fill();
    if (item.stroke) context.stroke();
}

function drawPath(context, item) {
    context.beginPath();
    context.moveTo(item.points[0].x, item.points[0].y);
    for (let index = 1; index < item.points.length; index++) {
        context.lineTo(item.points[index].x, item.points[index].y);
    }
    if (item.isClosed) context.closePath();
    if (item.fill) context.fill();
    if (item.stroke) context.stroke();
}

function drawText(context, item) {
    const textAlignments = ["start", "center", "end"];
    const textBaselines = ["top", "middle", "bottom"];
    const anchor = item.textAnchor;
    context.textAlign = textAlignments[item.textAlignment];
    context.textBaseline = textBaselines[item.textBaseline];
    if (item.fill) context.fillText(item.content ?? "", anchor.x, anchor.y);
    if (item.stroke) context.strokeText(item.content ?? "", anchor.x, anchor.y);
}

function applyTransform(context, matrix) {
    context.transform(matrix.m11, matrix.m12, matrix.m21, matrix.m22, matrix.offsetX, matrix.offsetY);
}

function fontDeclaration(request, privateFontFamily) {
    return `${request.fontStyle} ${request.fontWeight} ${request.fontSize}px ${quoteFont(privateFontFamily)}`;
}

function fontKey(resource) {
    return `identity:${resource.fontIdentity.length}:${resource.fontIdentity}` +
        `${resource.fontVersion.length}:${resource.fontVersion}` +
        `${resource.fontWeight}:${resource.fontStyle}`;
}

function fontFaceKey(family, weight, style) {
    return `face:${family.length}:${family}${weight}:${style}`;
}

function privateFontFamily(resource) {
    return `Inceptus-${utf8Hex(resource.fontIdentity)}-${utf8Hex(resource.fontVersion)}-` +
        `${resource.fontWeight}-${resource.fontStyle}`;
}

function utf8Hex(value) {
    return Array.from(
        new TextEncoder().encode(String(value)),
        byte => byte.toString(16).padStart(2, "0"))
        .join("");
}

function quoteFont(fontFamily) {
    return `"${String(fontFamily).replaceAll("\\", "\\\\").replaceAll('"', '\\"')}"`;
}

function validSurface(surface) {
    return surface && finitePositive(surface.cssWidth) && finitePositive(surface.cssHeight) &&
        finitePositive(surface.devicePixelRatio);
}

function validPresentation(frame) {
    const matrix = frame?.viewportTransform;
    return frame && Number.isSafeInteger(frame.contentVersion) && frame.contentVersion > 0 &&
        Number.isSafeInteger(frame.presentationVersion) && frame.presentationVersion > 0 &&
        matrix && [matrix.m11, matrix.m12, matrix.m21, matrix.m22, matrix.offsetX, matrix.offsetY]
            .every(Number.isFinite);
}

// Managed presentation supplies line presence, geometry, style and canonical insertion
// position. This validates a bounded drawing command, never Document boundary policy.
function validViewportLines(lines, contentLength) {
    if (lines == null) return true;
    if (!Array.isArray(lines) || lines.length > 2) return false;
    let previousIndex = 0;
    return lines.every(line => {
        if (!line || !Number.isSafeInteger(line.beforeContentIndex) ||
            line.beforeContentIndex < previousIndex || line.beforeContentIndex > contentLength ||
            !line.start || !line.end ||
            ![line.start.x, line.start.y, line.end.x, line.end.y].every(Number.isFinite) ||
            typeof line.stroke !== "string" || !line.stroke || !validPaint(line.stroke) ||
            !finitePositive(line.strokeWidth) || !Number.isFinite(line.opacity) ||
            line.opacity < 0 || line.opacity > 1 ||
            !Array.isArray(line.dashPattern) || line.dashPattern.length > 2 ||
            !line.dashPattern.every(finiteNonNegative)) return false;
        previousIndex = line.beforeContentIndex;
        return true;
    });
}

function finitePositive(value) {
    return Number.isFinite(value) && value > 0;
}

function finiteNonNegative(value) {
    return Number.isFinite(value) && value >= 0;
}

// Rendering metadata only. Unknown/invalid data must reach the ordinary draw/failure path,
// never disappear because an optimistic bound happened to be outside the surface.
function conservativeItemBounds(item) {
    if (!item?.isVisible || ![0, 1, 2, 4].includes(item.geometryKind) ||
        !safeMatrix(item.transform) || !rectBounds(item.geometryBounds) ||
        (item.clip != null && !rectBounds(item.clip)) ||
        !finiteNonNegative(item.opacity) || item.opacity > 1 ||
        !finiteNonNegative(item.strokeWidth) ||
        !Array.isArray(item.dashPattern) || !item.dashPattern.every(finiteNonNegative)) {
        return null;
    }

    let bounds = rectBounds(item.geometryBounds);
    if (item.geometryKind === 2) {
        // Complete path, including intermediate segments/jumps/closed-path decorations.
        if (!Array.isArray(item.points) || item.points.length < (item.isClosed ? 3 : 2)) return null;
        let left = Infinity, top = Infinity, right = -Infinity, bottom = -Infinity;
        for (const point of item.points) {
            if (!point || !safeCoordinate(point.x) || !safeCoordinate(point.y)) return null;
            left = Math.min(left, point.x);
            top = Math.min(top, point.y);
            right = Math.max(right, point.x);
            bottom = Math.max(bottom, point.y);
        }
        bounds = { left, top, right, bottom };
    }

    if (item.stroke && item.geometryKind !== 4) {
        // Canvas ignores lineWidth=0, retaining the inherited width. Do not guess it.
        if (item.strokeWidth === 0) return null;
        // Butt caps, miter joins, miterLimit=10; this also encloses every dashed segment.
        const padding = 10 * item.strokeWidth;
        bounds = { left: bounds.left - padding, top: bounds.top - padding,
            right: bounds.right + padding, bottom: bounds.bottom + padding };
    }
    // Clip is already in document space. Ignoring it only produces false positives.
    return transformBounds(bounds, item.transform);
}

function rectBounds(rect) {
    if (!rect || ![rect.x, rect.y, rect.width, rect.height].every(safeCoordinate) ||
        rect.width < 0 || rect.height < 0) return null;
    const right = rect.x + rect.width, bottom = rect.y + rect.height;
    return safeCoordinate(right) && safeCoordinate(bottom) ?
        { left: rect.x, top: rect.y, right, bottom } : null;
}

// Extreme coordinates remain supported by drawing conservatively. Limit only the
// optimization's arithmetic. Outward guards also allow for native Canvas float precision.
function safeCoordinate(value) {
    return Number.isFinite(value) && Math.abs(value) <= 1e12;
}

function safeMatrix(matrix) {
    return matrix && [matrix.m11, matrix.m12, matrix.m21, matrix.m22,
        matrix.offsetX, matrix.offsetY].every(safeCoordinate);
}

function transformBounds(bounds, matrix) {
    let left = Infinity, top = Infinity, right = -Infinity, bottom = -Infinity;
    let magnitude = 1;
    for (const x of [bounds.left, bounds.right]) {
        for (const y of [bounds.top, bounds.bottom]) {
            const ax = matrix.m11 * x, cy = matrix.m21 * y;
            const bx = matrix.m12 * x, dy = matrix.m22 * y;
            const px = ax + cy + matrix.offsetX, py = bx + dy + matrix.offsetY;
            magnitude = Math.max(magnitude, Math.abs(ax) + Math.abs(cy) + Math.abs(matrix.offsetX),
                Math.abs(bx) + Math.abs(dy) + Math.abs(matrix.offsetY));
            if (!safeCoordinate(px) || !safeCoordinate(py) || !safeCoordinate(magnitude)) return null;
            left = Math.min(left, px); top = Math.min(top, py);
            right = Math.max(right, px); bottom = Math.max(bottom, py);
        }
    }
    const guard = magnitude * 1e-6;
    return { left: left - guard, top: top - guard, right: right + guard, bottom: bottom + guard };
}

function visibleDocumentBounds(matrix, width, height, dpr) {
    if (!safeMatrix(matrix)) return null;
    const scale = Math.max(Math.abs(matrix.m11), Math.abs(matrix.m12),
        Math.abs(matrix.m21), Math.abs(matrix.m22));
    if (scale === 0) return null;
    const a = matrix.m11 / scale, b = matrix.m12 / scale;
    const c = matrix.m21 / scale, d = matrix.m22 / scale;
    const determinant = a * d - b * c;
    // Ill-conditioned or singular views draw everything; the accepted contract is unchanged.
    if (!Number.isFinite(determinant) || Math.abs(determinant) <= 1e-8) return null;
    const factor = (1 / scale) / determinant;
    const inverse = { m11: d * factor, m12: -b * factor, m21: -c * factor, m22: a * factor,
        offsetX: 0, offsetY: 0 };
    if (!safeMatrix(inverse)) return null;
    // Subtract translation before inversion to avoid an extra cancellation-prone offset sum.
    // Two device pixels cover raster-edge filtering; the relative guard covers inversion error.
    const guard = 2 / dpr + 1e-6 / Math.abs(determinant) *
        Math.max(1, width / dpr, height / dpr, Math.abs(matrix.offsetX), Math.abs(matrix.offsetY));
    const surface = { left: -matrix.offsetX - guard, top: -matrix.offsetY - guard,
        right: width / dpr - matrix.offsetX + guard,
        bottom: height / dpr - matrix.offsetY + guard };
    return transformBounds(surface, inverse);
}

function outsideViewport(bounds, visibleBounds) {
    return bounds !== null && visibleBounds !== null &&
        (bounds.right < visibleBounds.left || bounds.left > visibleBounds.right ||
            bounds.bottom < visibleBounds.top || bounds.top > visibleBounds.bottom);
}

function hasUnboundedEffects(context) {
    return (context.filter != null && context.filter !== "none") ||
        (context.shadowBlur ?? 0) !== 0 || (context.shadowOffsetX ?? 0) !== 0 ||
        (context.shadowOffsetY ?? 0) !== 0;
}

function validItemPaint(item) {
    if (!item || !item.isVisible) return true;
    return validPaint(item.fill) && validPaint(item.stroke);
}

function validPaint(value) {
    return value == null || (typeof value === "string" && CSS.supports("color", value));
}

function success() {
    return { succeeded: true, code: null, sourceIdentity: null };
}

function failure(code, sourceIdentity) {
    return { succeeded: false, code, sourceIdentity };
}
