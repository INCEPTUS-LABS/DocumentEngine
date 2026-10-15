/**
 * Presentation-only CSS surface observation and scalar pointer capture. This module never
 * acquires a canvas drawing context and never receives or interprets Canvas2DScene data.
 */

export function createCanvasSurfaceObserver(containerElementId, dotNetReference) {
    const container = document.getElementById(containerElementId);
    if (!(container instanceof HTMLElement)) {
        throw new Error("The Canvas2D presentation container was not found.");
    }
    if (!dotNetReference || typeof dotNetReference.invokeMethodAsync !== "function") {
        throw new Error("A valid .NET resize callback is required.");
    }

    return new CanvasSurfaceObserver(container, dotNetReference);
}

// Storage boundary only. Validation and settings ownership remain in managed code.
export function readEditorSettings(key) {
    try { return localStorage.getItem(key); }
    catch { return null; }
}

export function writeEditorSettings(key, value) {
    try { localStorage.setItem(key, value); return true; }
    catch { return false; }
}

export function createCanvasPointerObserver(canvasElementId, dotNetReference) {
    const canvas = document.getElementById(canvasElementId);
    if (!(canvas instanceof HTMLCanvasElement)) {
        throw new Error("The Canvas2D interaction canvas was not found.");
    }
    if (!dotNetReference || typeof dotNetReference.invokeMethodAsync !== "function") {
        throw new Error("A valid .NET pointer callback is required.");
    }

    return new CanvasPointerObserver(canvas, dotNetReference);
}

export function createDomPointerCaptureObserver(elementId) {
    const element = document.getElementById(elementId);
    if (!(element instanceof HTMLElement)) {
        throw new Error("The DOM pointer-capture element was not found.");
    }

    return new DomPointerCaptureObserver(element);
}

class DomPointerCaptureObserver {
    #element;
    #capturedPointerId = null;
    #started = false;
    #disposed = false;
    #onPointerDown;
    #onPointerBoundary;
    #onLostPointerCapture;

    constructor(element) {
        this.#element = element;
        this.#onPointerDown = event => this.#capture(event);
        this.#onPointerBoundary = event => this.#release(event.pointerId);
        this.#onLostPointerCapture = event => {
            if (event.pointerId === this.#capturedPointerId) {
                this.#capturedPointerId = null;
            }
        };
    }

    start() {
        if (this.#disposed) {
            throw new Error("The DOM pointer-capture observer is disposed.");
        }
        if (this.#started) {
            throw new Error("The DOM pointer-capture observer is already started.");
        }

        this.#started = true;
        this.#element.addEventListener("pointerdown", this.#onPointerDown);
        this.#element.addEventListener("pointerup", this.#onPointerBoundary);
        this.#element.addEventListener("pointercancel", this.#onPointerBoundary);
        this.#element.addEventListener(
            "lostpointercapture",
            this.#onLostPointerCapture);
    }

    dispose() {
        if (this.#disposed) {
            return;
        }

        this.#disposed = true;
        if (this.#started) {
            this.#release(this.#capturedPointerId);
            this.#element.removeEventListener("pointerdown", this.#onPointerDown);
            this.#element.removeEventListener("pointerup", this.#onPointerBoundary);
            this.#element.removeEventListener("pointercancel", this.#onPointerBoundary);
            this.#element.removeEventListener(
                "lostpointercapture",
                this.#onLostPointerCapture);
        }

        this.#element = null;
    }

    #capture(event) {
        if (this.#disposed || !this.#started || !event?.isPrimary ||
            event.button !== 0 || this.#capturedPointerId !== null ||
            !Number.isSafeInteger(event.pointerId) || event.pointerId < 0) {
            return;
        }

        try {
            this.#element.setPointerCapture(event.pointerId);
            if (typeof this.#element.hasPointerCapture !== "function" ||
                this.#element.hasPointerCapture(event.pointerId)) {
                this.#capturedPointerId = event.pointerId;
            }
        } catch {
            // A rejected browser capture leaves the panel stationary once the pointer exits
            // the header. No document or Canvas gesture has started.
        }
    }

    #release(pointerId) {
        if (pointerId === null || pointerId !== this.#capturedPointerId) {
            return;
        }

        this.#capturedPointerId = null;
        try {
            if (typeof this.#element.hasPointerCapture !== "function" ||
                this.#element.hasPointerCapture(pointerId)) {
                this.#element.releasePointerCapture(pointerId);
            }
        } catch {
            // Native capture can disappear before a boundary or disposal callback.
        }
    }
}

class CanvasPointerObserver {
    #canvas;
    #dotNetReference;
    #started = false;
    #disposed = false;
    #callbackInFlight = false;
    #pending = [];
    #capturedPointerId = null;
    #captureGeneration = 0;
    #nextCaptureGeneration = 1;
    #lastCapturedInput = null;
    #lastSurfaceInput = null;
    #touches = new Map();
    #touchPan = null;
    #touchFrame = null;
    #touchMovePending = null;
    #touchSuppressed = false;
    #suppressTouchActivation = false;
    #onPointerDown;
    #onPointerMove;
    #onPointerUp;
    #onPointerCancel;
    #onPointerLeave;
    #onLostPointerCapture;
    #onContextMenu;
    #onDoubleClick;
    #onAuxClick;
    #onWheel;
    #onWindowBlur;

    constructor(canvas, dotNetReference) {
        this.#canvas = canvas;
        this.#dotNetReference = dotNetReference;
        this.#onPointerDown = event => this.#captureDown(event);
        this.#onPointerMove = event => this.#captureMove(event);
        this.#onPointerUp = event => this.#captureBoundary(2, event);
        this.#onPointerCancel = event => this.#captureBoundary(3, event);
        this.#onPointerLeave = event => this.#captureLeave(event);
        this.#onLostPointerCapture = event => this.#captureLostPointerCapture(event);
        this.#onContextMenu = event => this.#captureContextMenu(event);
        this.#onDoubleClick = event => this.#captureDoubleClick(event);
        this.#onAuxClick = event => this.#captureAuxClick(event);
        this.#onWheel = event => this.#captureWheel(event);
        this.#onWindowBlur = () => this.#captureFocusLoss();
    }

    start() {
        if (this.#disposed) {
            throw new Error("The Canvas2D pointer observer is disposed.");
        }
        if (this.#started) {
            throw new Error("The Canvas2D pointer observer is already started.");
        }

        this.#started = true;
        this.#canvas.addEventListener("pointerdown", this.#onPointerDown);
        this.#canvas.addEventListener("pointermove", this.#onPointerMove);
        this.#canvas.addEventListener("pointerup", this.#onPointerUp);
        this.#canvas.addEventListener("pointercancel", this.#onPointerCancel);
        this.#canvas.addEventListener("pointerleave", this.#onPointerLeave);
        this.#canvas.addEventListener("lostpointercapture", this.#onLostPointerCapture);
        this.#canvas.addEventListener("contextmenu", this.#onContextMenu);
        this.#canvas.addEventListener("dblclick", this.#onDoubleClick);
        this.#canvas.addEventListener("auxclick", this.#onAuxClick);
        this.#canvas.addEventListener("wheel", this.#onWheel, { passive: false });
        window.addEventListener("blur", this.#onWindowBlur);
    }

    releaseCapture(captureGeneration) {
        if (this.#disposed || !this.#started || this.#capturedPointerId === null ||
            !Number.isSafeInteger(captureGeneration) || captureGeneration <= 0 ||
            captureGeneration !== this.#captureGeneration) {
            return;
        }

        const last = this.#lastCapturedInput;
        const cancellation = last
            ? { ...last, kind: this.#touchPan ? 10 : 3, button: -1, buttons: 0 }
            : null;
        this.#flushTouchMove();
        this.#releaseCapture(captureGeneration);
        if (cancellation) {
            this.#enqueue(cancellation);
        }
    }

    setCursor(cssCursor) {
        if (this.#disposed) {
            return;
        }
        if (typeof cssCursor !== "string" || cssCursor.length === 0) {
            throw new Error("A non-empty CSS cursor value is required.");
        }

        // Interaction supplies the final scalar CSS value. Presentation applies it without
        // interpreting scene identity, gesture kind, or resize direction.
        this.#canvas.style.cursor = cssCursor;
    }

    dispose() {
        if (this.#disposed) {
            return;
        }

        this.#disposed = true;
        this.#canvas.style.cursor = "default";
        if (this.#started) {
            this.#releaseCapture();
            this.#canvas.removeEventListener("pointerdown", this.#onPointerDown);
            this.#canvas.removeEventListener("pointermove", this.#onPointerMove);
            this.#canvas.removeEventListener("pointerup", this.#onPointerUp);
            this.#canvas.removeEventListener("pointercancel", this.#onPointerCancel);
            this.#canvas.removeEventListener("pointerleave", this.#onPointerLeave);
            this.#canvas.removeEventListener(
                "lostpointercapture",
                this.#onLostPointerCapture);
            this.#canvas.removeEventListener("contextmenu", this.#onContextMenu);
            this.#canvas.removeEventListener("dblclick", this.#onDoubleClick);
            this.#canvas.removeEventListener("auxclick", this.#onAuxClick);
            this.#canvas.removeEventListener("wheel", this.#onWheel);
            window.removeEventListener("blur", this.#onWindowBlur);
        }
        this.#lastSurfaceInput = null;
        this.#pending = [];
        this.#dotNetReference = null;
        this.#canvas = null;
    }

    #captureDown(event) {
        if (event?.pointerType === "touch") {
            this.#captureTouchDown(event);
            return;
        }
        if (this.#touches.size > 0) return;
        this.#suppressTouchActivation = false;
        this.#captureSingleDown(event);
    }

    #captureSingleDown(event) {
        if (!this.#canCapture(event) || !event.isPrimary ||
            (event.button !== 0 && event.button !== 1) ||
            this.#capturedPointerId !== null || this.#pending.length >= 60) {
            return;
        }

        if (event.button === 1) {
            // Canvas middle drag is owned by viewport Pan. Suppress the browser's native
            // autoscroll affordance only on this interaction surface.
            event.preventDefault?.();
        }

        const input = this.#createInput(0, event);
        if (!input) {
            return;
        }

        const captureGeneration = this.#nextCaptureGeneration;
        if (!Number.isSafeInteger(captureGeneration) || captureGeneration <= 0) {
            return;
        }

        try {
            this.#canvas.setPointerCapture(event.pointerId);
        } catch {
            // Pointer capture is a presentation concern. If the browser rejects it, do not
            // begin a .NET gesture that could no longer receive its terminating boundary.
            return;
        }

        this.#nextCaptureGeneration++;
        this.#capturedPointerId = event.pointerId;
        this.#captureGeneration = captureGeneration;
        const capturedInput = { ...input, captureGeneration };
        this.#lastCapturedInput = capturedInput;
        this.#lastSurfaceInput = capturedInput;
        if (input.isTouch) this.#touches.set(input.pointerId, capturedInput);
        this.#enqueue(capturedInput);
    }

    #captureMove(event) {
        if (this.#touches.has(event?.pointerId)) {
            const input = this.#createInput(1, event);
            if (!input) return;
            this.#touches.set(event.pointerId, input);
            if (this.#touchSuppressed) return;
            if (this.#touchPan) {
                this.#emitTouchPan(8);
                return;
            }
        } else if (event?.pointerType === "touch" || this.#touches.size > 0) {
            return;
        }
        if (!this.#canCapture(event) || !event.isPrimary ||
            (this.#capturedPointerId === null && event.buttons !== 0) ||
            (this.#capturedPointerId !== null &&
                event.pointerId !== this.#capturedPointerId)) {
            return;
        }

        this.#captureInput(1, event);
    }

    #captureBoundary(kind, event) {
        if (this.#touches.has(event?.pointerId)) {
            this.#captureTouchBoundary(kind, event);
            return;
        }
        if (!this.#canCapture(event) ||
            event.pointerId !== this.#capturedPointerId) {
            return;
        }

        this.#captureInput(kind, event);
        this.#releaseCapture();
    }

    #captureLeave(event) {
        if (this.#touches.has(event?.pointerId)) {
            if (!this.#canvas.hasPointerCapture(event.pointerId)) this.#captureTouchBoundary(3, event);
            return;
        }
        if (this.#capturedPointerId !== null || !this.#canCapture(event) ||
            !event.isPrimary) {
            return;
        }

        this.#captureInput(4, event);
    }

    #captureFocusLoss() {
        if (this.#disposed || !this.#started) {
            return;
        }

        // Losing browser focus terminates surface input, including an uncaptured hover.
        // Managed interaction decides which transient operation to retire.
        const last = this.#lastCapturedInput ?? this.#lastSurfaceInput;
        this.#flushTouchMove();
        this.#lastSurfaceInput = null;
        this.#canvas.style.cursor = "default";
        this.#releaseCapture();
        if (last) {
            this.#enqueue({ ...last, kind: last.kind >= 7 ? 10 : 3, button: -1, buttons: 0 });
        }
    }

    #captureLostPointerCapture(event) {
        if (this.#touches.has(event?.pointerId)) {
            this.#captureTouchBoundary(3, event);
            return;
        }
        if (!this.#canCapture(event) ||
            event.pointerId !== this.#capturedPointerId) {
            return;
        }

        const captureGeneration = this.#captureGeneration;
        this.#capturedPointerId = null;
        this.#captureGeneration = 0;
        this.#lastCapturedInput = null;
        this.#lastSurfaceInput = null;
        this.#captureInput(3, event, captureGeneration);
    }

    #captureContextMenu(event) {
        if (!this.#canCapture(event)) {
            return;
        }

        // Native suppression is scoped to this editor canvas. Object resolution and all
        // context policy remain in the managed interaction path.
        event.preventDefault?.();
        if (this.#capturedPointerId !== null || this.#touches.size > 0 ||
            this.#suppressTouchActivation || this.#pending.length >= 60) {
            return;
        }

        const bounds = this.#canvas.getBoundingClientRect();
        const input = {
            kind: 5,
            pointerId: 0,
            button: 2,
            buttons: 0,
            isPrimary: true,
            clientX: event.clientX,
            clientY: event.clientY,
            canvasLeft: bounds.left,
            canvasTop: bounds.top,
            altKey: Boolean(event.altKey),
            controlKey: Boolean(event.ctrlKey),
            metaKey: Boolean(event.metaKey),
            shiftKey: Boolean(event.shiftKey),
            captureGeneration: 0
        };
        if (![input.clientX, input.clientY, input.canvasLeft, input.canvasTop].every(Number.isFinite)) {
            return;
        }

        this.#enqueue(input);
    }

    #captureDoubleClick(event) {
        if (!this.#canCapture(event) || event.button !== 0 ||
            this.#capturedPointerId !== null || this.#touches.size > 0 ||
            this.#suppressTouchActivation || this.#pending.length >= 60) {
            return;
        }

        const bounds = this.#canvas.getBoundingClientRect();
        const input = {
            kind: 6,
            pointerId: 0,
            button: 0,
            buttons: 0,
            isPrimary: true,
            clientX: event.clientX,
            clientY: event.clientY,
            canvasLeft: bounds.left,
            canvasTop: bounds.top,
            altKey: Boolean(event.altKey),
            controlKey: Boolean(event.ctrlKey),
            metaKey: Boolean(event.metaKey),
            shiftKey: Boolean(event.shiftKey),
            captureGeneration: 0
        };
        if (![input.clientX, input.clientY, input.canvasLeft, input.canvasTop]
            .every(Number.isFinite)) {
            return;
        }

        this.#enqueue(input);
    }

    #captureAuxClick(event) {
        if (this.#canCapture(event) && event.button === 1) {
            event.preventDefault?.();
        }
    }

    #captureWheel(event) {
        if (!this.#canCapture(event) || event.ctrlKey || event.metaKey ||
            this.#canvas.dataset?.viewportInputEnabled === "false" ||
            event.defaultPrevented || this.#capturedPointerId !== null || this.#touches.size > 0 ||
            !Number.isFinite(event.deltaX) || !Number.isFinite(event.deltaY) ||
            !Number.isSafeInteger(event.deltaMode) || event.deltaMode < 0 ||
            event.deltaMode > 2 || (event.deltaX === 0 && event.deltaY === 0)) {
            return;
        }

        const input = { wheel: true, deltaX: event.deltaX, deltaY: event.deltaY, deltaMode: event.deltaMode };
        const last = this.#pending.at(-1);
        // Do not combine reversals: the managed viewport clamps at the Document origin.
        const combines = last?.wheel && last.deltaMode === input.deltaMode &&
            last.deltaX * input.deltaX >= 0 && last.deltaY * input.deltaY >= 0 &&
            Number.isFinite(last.deltaX + input.deltaX) && Number.isFinite(last.deltaY + input.deltaY);
        if (!combines && this.#pending.length >= 60) return;
        // Only accepted canvas input owns native scrolling. Modified, conflicting and
        // backpressured input retains its browser fallback; sibling UI is never observed.
        event.preventDefault?.();
        event.stopPropagation?.();
        if (combines) {
            last.deltaX += input.deltaX;
            last.deltaY += input.deltaY;
        } else {
            this.#enqueue(input);
        }
    }

    #captureInput(kind, event, captureGeneration = this.#captureGeneration) {
        const input = this.#createInput(kind, event, captureGeneration);
        if (input) {
            this.#lastSurfaceInput = kind === 3 || kind === 4 ? null : input;
            if (input.pointerId === this.#capturedPointerId) {
                this.#lastCapturedInput = input;
            }
            this.#enqueue(input);
        }
    }

    #createInput(kind, event, captureGeneration = this.#captureGeneration) {
        const bounds = this.#canvas.getBoundingClientRect();
        const input = {
            kind,
            pointerId: event.pointerId,
            button: event.button,
            buttons: event.buttons,
            isPrimary: event.isPrimary,
            clientX: event.clientX,
            clientY: event.clientY,
            canvasLeft: bounds.left,
            canvasTop: bounds.top,
            altKey: Boolean(event.altKey),
            controlKey: Boolean(event.ctrlKey),
            metaKey: Boolean(event.metaKey),
            shiftKey: Boolean(event.shiftKey),
            captureGeneration,
            isTouch: event.pointerType === "touch"
        };
        if (![input.clientX, input.clientY, input.canvasLeft, input.canvasTop].every(Number.isFinite) ||
            !Number.isSafeInteger(input.pointerId) || input.pointerId < 0 ||
            !Number.isSafeInteger(input.button) || !Number.isSafeInteger(input.buttons) ||
            !Number.isSafeInteger(input.captureGeneration) || input.captureGeneration < 0) {
            return null;
        }

        return input;
    }

    #canCapture(event) {
        return !this.#disposed && this.#started && event;
    }

    #releaseCapture(expectedCaptureGeneration = null) {
        if (expectedCaptureGeneration !== null &&
            expectedCaptureGeneration !== this.#captureGeneration) {
            return;
        }

        const pointerId = this.#capturedPointerId;
        const pointerIds = new Set(this.#touches.keys());
        if (pointerId !== null) pointerIds.add(pointerId);
        this.#capturedPointerId = null;
        this.#captureGeneration = 0;
        this.#lastCapturedInput = null;
        this.#touches.clear();
        this.#touchPan = null;
        this.#touchSuppressed = false;
        if (this.#touchFrame !== null) cancelAnimationFrame(this.#touchFrame);
        this.#touchFrame = null;
        this.#touchMovePending = null;
        for (const id of pointerIds) this.#releaseNativeCapture(id);
    }

    #releaseNativeCapture(pointerId) {
        try {
            if (typeof this.#canvas.hasPointerCapture !== "function" ||
                this.#canvas.hasPointerCapture(pointerId)) {
                this.#canvas.releasePointerCapture(pointerId);
            }
        } catch {
            // Disposal and native capture loss can race. Local ownership is already released.
        }
    }

    #captureTouchDown(event) {
        if (!this.#canCapture(event) || event.button !== 0 || this.#touches.has(event.pointerId)) return;
        if (this.#touches.size >= 16) { this.#captureFocusLoss(); return; }
        if (this.#touches.size === 0) {
            this.#suppressTouchActivation = false;
            this.#captureSingleDown(event);
            return;
        }
        const input = this.#createInput(0, event);
        if (!input) return;
        try { this.#canvas.setPointerCapture(event.pointerId); }
        catch { this.#captureFocusLoss(); return; }
        this.#touches.set(event.pointerId, input);
        if (this.#touchSuppressed) return;
        if (this.#touches.size !== 2) {
            this.#finishTouchPan(10);
            return;
        }
        if (!Number.isSafeInteger(this.#nextCaptureGeneration)) { this.#captureFocusLoss(); return; }
        this.#captureGeneration = this.#nextCaptureGeneration++;
        this.#touchPan = { ...this.#lastCapturedInput, captureGeneration: this.#captureGeneration };
        this.#suppressTouchActivation = true;
        this.#emitTouchPan(7);
    }

    #emitTouchPan(kind, enqueue = true) {
        const [first, second] = this.#touches.values();
        const bounds = this.#canvas.getBoundingClientRect();
        const input = { ...this.#touchPan, kind,
            clientX: first.clientX / 2 + second.clientX / 2,
            clientY: first.clientY / 2 + second.clientY / 2,
            canvasLeft: bounds.left, canvasTop: bounds.top };
        this.#touchPan = input;
        this.#lastCapturedInput = input;
        this.#lastSurfaceInput = input;
        if (!enqueue) return;
        if (kind !== 8) { this.#enqueue(input); return; }
        // Both contacts may report separately in the same browser frame. Deliver their
        // final midpoint together, including when they move apart around a fixed center.
        this.#touchMovePending = input;
        if (this.#touchFrame === null) {
            this.#touchFrame = requestAnimationFrame(() => {
                this.#touchFrame = null;
                this.#flushTouchMove();
            });
        }
    }

    #flushTouchMove() {
        if (this.#touchFrame !== null) cancelAnimationFrame(this.#touchFrame);
        this.#touchFrame = null;
        const input = this.#touchMovePending;
        this.#touchMovePending = null;
        if (input) this.#enqueue(input);
    }

    #finishTouchPan(kind) {
        this.#flushTouchMove();
        if (this.#touchPan) this.#enqueue({ ...this.#touchPan, kind, button: -1, buttons: 0 });
        this.#touchPan = null;
        this.#touchSuppressed = true;
        this.#capturedPointerId = null;
        this.#captureGeneration = 0;
        this.#lastCapturedInput = null;
        this.#lastSurfaceInput = null;
    }

    #captureTouchBoundary(kind, event) {
        if (this.#touchPan) {
            if (kind === 2) {
                const input = this.#createInput(kind, event);
                if (input) this.#touches.set(event.pointerId, input);
                this.#emitTouchPan(8, false);
            }
            this.#finishTouchPan(kind === 2 ? 9 : 10);
        } else if (!this.#touchSuppressed) {
            this.#captureInput(kind, event);
            this.#releaseCapture();
            return;
        }
        this.#touches.delete(event.pointerId);
        this.#releaseNativeCapture(event.pointerId);
        if (this.#touches.size === 0) this.#touchSuppressed = false;
    }

    #enqueue(input) {
        if (this.#disposed || !this.#started) {
            return;
        }

        const last = this.#pending.at(-1);
        if ((input.kind === 1 || input.kind === 8) && last?.kind === input.kind &&
            input.pointerId === last.pointerId && input.captureGeneration === last.captureGeneration) {
            this.#pending[this.#pending.length - 1] = input;
        } else if ((input.kind === 1 || input.kind === 8) && this.#pending.length >= 64) {
            // Reserve boundary capacity. A terminal sample still carries the final position.
            return;
        } else {
            this.#pending.push(input);
        }

        void this.#drain();
    }

    async #drain() {
        if (this.#callbackInFlight) {
            return;
        }

        this.#callbackInFlight = true;
        try {
            while (!this.#disposed && this.#pending.length > 0) {
                const input = this.#pending.shift();
                const callback = this.#dotNetReference;
                if (!callback) {
                    return;
                }

                try {
                    if (input.wheel) {
                        await callback.invokeMethodAsync("OnCanvasWheelInput", input.deltaX,
                            input.deltaY, input.deltaMode, false, false);
                        continue;
                    }
                    await callback.invokeMethodAsync(
                        "OnCanvasPointerInput",
                        input.kind,
                        input.pointerId,
                        input.button,
                        input.buttons,
                        input.isPrimary,
                        input.clientX,
                        input.clientY,
                        input.canvasLeft,
                        input.canvasTop,
                        input.altKey,
                        input.controlKey,
                        input.metaKey,
                        input.shiftKey,
                        input.captureGeneration,
                        Boolean(input.isTouch));
                } catch {
                    // Component disposal may race a pointer callback already dispatched by the
                    // browser. The next ordered input remains independently deliverable.
                }
            }
        } finally {
            this.#callbackInFlight = false;
        }
    }
}

class CanvasSurfaceObserver {
    #container;
    #dotNetReference;
    #observer;
    #animationFrame = 0;
    #started = false;
    #disposed = false;
    #lastMeasurement;
    #onWindowResize;
    #callbackInFlight = false;
    #pendingMeasurement = null;
    #initialMeasurement = null;

    constructor(container, dotNetReference) {
        this.#container = container;
        this.#dotNetReference = dotNetReference;
        this.#onWindowResize = () => this.#schedule();
    }

    start() {
        if (this.#disposed) {
            throw new Error("The Canvas2D surface observer is disposed.");
        }
        if (this.#started) {
            throw new Error("The Canvas2D surface observer is already started.");
        }

        this.#started = true;
        this.#observer = new ResizeObserver(() => this.#schedule());
        this.#observer.observe(this.#container);
        window.addEventListener("resize", this.#onWindowResize);
        try {
            this.#lastMeasurement = measure(this.#container);
            return this.#lastMeasurement;
        } catch {
            // A host may intentionally start hidden. Observe before waiting, without inventing
            // dimensions or polling. Startup/Ready remains pending until the host reveals it.
            return new Promise((resolve, reject) => {
                this.#initialMeasurement = { resolve, reject };
            });
        }
    }

    dispose() {
        if (this.#disposed) {
            return;
        }

        this.#disposed = true;
        this.#initialMeasurement?.reject(new Error("The Canvas2D surface observer is disposed."));
        this.#initialMeasurement = null;
        this.#observer?.disconnect();
        this.#observer = null;
        window.removeEventListener("resize", this.#onWindowResize);
        if (this.#animationFrame !== 0) {
            cancelAnimationFrame(this.#animationFrame);
            this.#animationFrame = 0;
        }
        this.#dotNetReference = null;
        this.#container = null;
        this.#pendingMeasurement = null;
    }

    #schedule() {
        if (this.#disposed || !this.#started || this.#animationFrame !== 0) {
            return;
        }

        this.#animationFrame = requestAnimationFrame(async () => {
            this.#animationFrame = 0;
            if (this.#disposed) {
                return;
            }

            let next;
            try {
                next = measure(this.#container);
            } catch {
                // ResizeObserver may report a transient zero-sized box while layout or
                // navigation is changing. Retain the last valid surface and wait for the next
                // explicit browser observation; never dispatch invalid dimensions or retry.
                return;
            }
            if (equalMeasurement(next, this.#lastMeasurement)) {
                return;
            }

            this.#lastMeasurement = next;
            if (this.#initialMeasurement) {
                this.#initialMeasurement.resolve(next);
                this.#initialMeasurement = null;
                return;
            }
            await this.#deliverLatest(next);
        });
    }

    async #deliverLatest(measurement) {
        if (this.#callbackInFlight) {
            this.#pendingMeasurement = measurement;
            return;
        }

        this.#callbackInFlight = true;
        let next = measurement;
        try {
            while (next && !this.#disposed) {
                this.#pendingMeasurement = null;
                const callback = this.#dotNetReference;
                if (!callback) {
                    return;
                }

                try {
                    await callback.invokeMethodAsync(
                        "OnCanvasSurfaceChanged",
                        next.cssWidth,
                        next.cssHeight,
                        next.devicePixelRatio);
                } catch {
                    // Component disposal may race a callback already dispatched by the browser.
                    // Presentation state owns any active failure surface; no retry loop is started.
                }

                next = this.#pendingMeasurement;
            }
        } finally {
            this.#callbackInFlight = false;
        }
    }
}

function measure(container) {
    const bounds = container.getBoundingClientRect();
    const cssWidth = bounds.width;
    const cssHeight = bounds.height;
    const devicePixelRatio = window.devicePixelRatio || 1;
    if (![cssWidth, cssHeight, devicePixelRatio].every(
        value => Number.isFinite(value) && value > 0)) {
        throw new Error("The Canvas2D presentation surface has invalid dimensions.");
    }

    return { cssWidth, cssHeight, devicePixelRatio };
}

function equalMeasurement(left, right) {
    return right && left.cssWidth === right.cssWidth &&
        left.cssHeight === right.cssHeight &&
        left.devicePixelRatio === right.devicePixelRatio;
}
