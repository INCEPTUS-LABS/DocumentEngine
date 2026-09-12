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

        if (!frame || !frame.viewportTransform || !Array.isArray(frame.items)) {
            return failure("CANVAS2D_RENDERER_RENDERING_FAILED", "Canvas2DRenderFrame");
        }

        if (!frame.items.every(validItemPaint)) {
            return failure("CANVAS2D_RENDERER_RENDERING_FAILED", "Canvas2DSceneStyle");
        }

        const dpr = Number(this.#canvas.dataset.inceptusDpr);
        if (!finitePositive(dpr)) {
            return failure("CANVAS2D_RENDERER_RESIZE_FAILED", "Canvas2DSurfaceSize");
        }

        const resources = await this.#loadRequiredImages(frame.items);
        if (!resources.succeeded) {
            return resources;
        }

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
            applyTransform(context, frame.viewportTransform);

            // Items arrive in the immutable Scene's canonical layer/z/identity order.
            for (const item of frame.items) {
                if (!item.isVisible) {
                    continue;
                }
                this.#drawItem(item);
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
                this.#images.set(reference, image);
            } catch {
                return failure("CANVAS2D_RENDERER_IMAGE_LOAD_FAILED", reference);
            }
        }
        return success();
    }
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

function finitePositive(value) {
    return Number.isFinite(value) && value > 0;
}

function finiteNonNegative(value) {
    return Number.isFinite(value) && value >= 0;
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
