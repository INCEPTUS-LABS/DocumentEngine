(() => {
"use strict";

const FORMAT = "Inceptus.PublishedProcess";
const FORMAT_VERSION = 1;
const MIN_SCALE = 0.08;
const MAX_SCALE = 16;
const VIEW_PADDING = 48;
const TOKEN_RADIUS = 7;
const START_GUIDANCE = "Click a Start element in the diagram.";

function routeLength(points) {
    requireRoute(points);
    let length = 0;
    for (let index = 1; index < points.length; index++) {
        length += Math.hypot(
            points[index].x - points[index - 1].x,
            points[index].y - points[index - 1].y);
    }
    return length;
}

function interpolateRoute(points, distance) {
    requireRoute(points);
    if (!Number.isFinite(distance)) throw new TypeError("Route distance must be finite.");
    if (distance <= 0) return { ...points[0] };
    let remaining = distance;
    for (let index = 1; index < points.length; index++) {
        const start = points[index - 1];
        const end = points[index];
        const length = Math.hypot(end.x - start.x, end.y - start.y);
        if (length === 0) continue;
        if (remaining <= length) {
            const progress = remaining / length;
            return {
                x: start.x + ((end.x - start.x) * progress),
                y: start.y + ((end.y - start.y) * progress)
            };
        }
        remaining -= length;
    }
    return { ...points[points.length - 1] };
}

function connectorDurationMs(points, speedPxPerSecond, minimumDurationMs) {
    if (!Number.isFinite(speedPxPerSecond) || speedPxPerSecond <= 0 ||
        !Number.isFinite(minimumDurationMs) || minimumDurationMs < 0) {
        throw new TypeError("Published runtime timing must be finite and valid.");
    }
    const length = routeLength(points);
    return length === 0 ? 0 : Math.max(minimumDurationMs, (length / speedPxPerSecond) * 1000);
}

class PublishedViewport {
    constructor(contentBounds, width, height, padding = VIEW_PADDING, initialView = null) {
        validateRect(contentBounds);
        this.contentBounds = Object.freeze({ ...contentBounds });
        this.initialRegion = initialView == null ? null : Object.freeze({ ...validateInitialRegion(initialView) });
        this.userAdjusted = false;
        this.width = width;
        this.height = height;
        this.scale = 1;
        this.offsetX = 0;
        this.offsetY = 0;
        this.resize(width, height, padding);
    }

    fit(width, height, padding = VIEW_PADDING) {
        this.frameRegion(this.contentBounds, width, height, padding, 1);
    }

    frameRegion(region, width, height, padding, minimumDimension = 0) {
        requireSurface(width, height);
        const usableWidth = Math.max(1, width - (padding * 2));
        const usableHeight = Math.max(1, height - (padding * 2));
        this.scale = clamp(
            Math.min(
                usableWidth / Math.max(minimumDimension, region.width),
                usableHeight / Math.max(minimumDimension, region.height)),
            MIN_SCALE,
            MAX_SCALE);
        this.offsetX = (width / 2) -
            ((region.x + (region.width / 2)) * this.scale);
        this.offsetY = (height / 2) -
            ((region.y + (region.height / 2)) * this.scale);
    }

    resize(width, height, padding = VIEW_PADDING) {
        requireSurface(width, height);
        if (this.userAdjusted) {
            const center = this.toDocument(this.width / 2, this.height / 2);
            this.offsetX = width / 2 - center.x * this.scale;
            this.offsetY = height / 2 - center.y * this.scale;
        } else if (this.initialRegion) {
            this.frameRegion(this.initialRegion, width, height, 0);
        } else {
            this.fit(width, height, padding);
        }
        this.width = width;
        this.height = height;
    }

    pan(deltaX, deltaY) {
        if (![deltaX, deltaY].every(Number.isFinite)) {
            throw new TypeError("Pan distance must be finite.");
        }
        this.offsetX += deltaX;
        this.offsetY += deltaY;
        if (deltaX !== 0 || deltaY !== 0) this.userAdjusted = true;
    }

    zoomAt(factor, screenX, screenY) {
        if (![factor, screenX, screenY].every(Number.isFinite) || factor <= 0) {
            throw new TypeError("Zoom input must be finite and positive.");
        }
        const documentPoint = this.toDocument(screenX, screenY);
        this.scale = clamp(this.scale * factor, MIN_SCALE, MAX_SCALE);
        this.offsetX = screenX - (documentPoint.x * this.scale);
        this.offsetY = screenY - (documentPoint.y * this.scale);
        if (factor !== 1) this.userAdjusted = true;
    }

    toDocument(screenX, screenY) {
        return {
            x: (screenX - this.offsetX) / this.scale,
            y: (screenY - this.offsetY) / this.scale
        };
    }

    capture() {
        return {
            scale: this.scale,
            offsetX: this.offsetX,
            offsetY: this.offsetY,
            contentBounds: { ...this.contentBounds }
        };
    }
}

class PublishedTokenRuntime {
    constructor(process, options = {}) {
        validateProcess(process);
        this.process = process;
        this.random = options.random ?? Math.random;
        this.now = options.now ?? (() => performance.now());
        if (typeof this.random !== "function" || typeof this.now !== "function") {
            throw new TypeError("Published runtime providers must be functions.");
        }
        this.nodes = new Map(process.tokenGraph.nodes.map(node => [node.id, node]));
        this.presentationNodes = new Map(process.presentation.nodes.map(node => [node.id, node]));
        this.connectors = new Map(process.presentation.connectors.map(edge => [edge.id, edge]));
        this.startNodeIds = Object.freeze([...this.nodes.values()]
            .filter(node => node.role === "start")
            .map(node => node.id));
        if (this.startNodeIds.length === 0) {
            throw new Error("Published process requires at least one Start.");
        }
        this.tokens = new Map();
        this.mergeQueues = new Map();
        this.parallelQueues = new Map();
        this.nextTokenId = 1;
        this.startedTokenCount = 0;
        this.execution = process.execution ?? null;
        this.senders = new Set(this.execution?.senderIds ?? []);
        this.catches = new Set(this.execution?.catchIds ?? []);
        this.communication = new Map((this.execution?.communicationRoutes ?? []).map(route => [route.connectorId, route]));
        this.triggers = this.execution?.attachedTriggers ?? [];
        this.receivers = new Map((this.execution?.receiverIds ?? []).map(id => [id, this.#emptyReceiver()]));
        this.activityOutputs = {};
        this.boundaryActivations = {};
        this.deliveredSignals = 0;
        this.clock = -Infinity;
    }

    start(atMs = this.now()) {
        requireTime(atMs);
        if (this.startNodeIds.length !== 1) {
            throw new Error("Targetless activation requires exactly one Start.");
        }
        return this.activateStart(this.startNodeIds[0], atMs);
    }

    activateStart(startId, atMs = this.now()) {
        requireTime(atMs);
        const start = this.nodes.get(startId);
        if (!start || start.role !== "start" || !this.presentationNodes.has(startId)) {
            throw new Error("The selected node is not a published Start.");
        }
        if (this.execution) this.#advance(atMs, false);
        const token = this.#newToken(startId, atMs);
        this.startedTokenCount++;
        this.#enterNode(token, startId, null, atMs, 0);
        return token.id;
    }

    tick(atMs = this.now()) {
        requireTime(atMs);
        if (this.execution) {
            // An input event may run after a frame timestamp was sampled but before its
            // callback. Render that frame at the latest logical time; never rewind input.
            this.#advance(Math.max(atMs, this.clock), true);
            return this.captureState();
        }
        for (let pass = 0; pass < 128; pass++) {
            let changed = false;
            for (const token of [...this.tokens.values()]) {
                if (!this.tokens.has(token.id)) continue;
                if (token.state === "travelling") {
                    const edge = this.connectors.get(token.connectorId);
                    const elapsed = Math.max(0, atMs - token.travelStartedAt);
                    const progress = token.travelDurationMs === 0
                        ? 1
                        : Math.min(1, elapsed / token.travelDurationMs);
                    token.position = interpolateRoute(
                        edge.points,
                        token.routeLength * progress);
                    if (progress >= 1) {
                        const arrival = token.travelStartedAt + token.travelDurationMs;
                        this.#enterNode(token, edge.targetElementId, edge.id, arrival, 0);
                        changed = true;
                    }
                } else if (token.state === "activity-delay" &&
                    atMs - token.enteredAt >= this.process.runtime.activityDelayMs) {
                    this.#takeOnlyOutgoing(token, token.nodeId,
                        token.enteredAt + this.process.runtime.activityDelayMs, 0);
                    changed = true;
                }
            }
            if (!changed) break;
        }
        return this.captureState();
    }

    captureState() {
        const waiting = {};
        for (const [nodeId, queues] of this.mergeQueues) {
            waiting[nodeId] = {};
            for (const [connectorId, tokenIds] of queues) {
                waiting[nodeId][connectorId] = tokenIds.length;
            }
        }
        const parallelWaiting = {};
        for (const [nodeId, queues] of this.parallelQueues) {
            parallelWaiting[nodeId] = {};
            for (const [connectorId, tokenIds] of queues) {
                parallelWaiting[nodeId][connectorId] = tokenIds.length;
            }
        }
        return {
            startedTokenCount: this.startedTokenCount,
            activeTokenCount: this.tokens.size,
            tokens: [...this.tokens.values()]
                .sort((left, right) => left.sequence - right.sequence)
                .map(token => ({
                    id: token.id,
                    ...(this.execution ? { kind: token.kind } : {}),
                    state: token.state,
                    nodeId: token.nodeId,
                    connectorId: token.connectorId,
                    incomingConnectorId: token.incomingConnectorId,
                    enteredAt: token.enteredAt,
                    travelStartedAt: token.travelStartedAt,
                    travelDurationMs: token.travelDurationMs,
                    position: { ...token.position }
                })),
            mergeWaiting: waiting,
            parallelWaiting,
            ...(this.execution ? {
                deliveredSignalCount: this.deliveredSignals,
                activityOutputCounts: { ...this.activityOutputs },
                boundaryActivationCounts: { ...this.boundaryActivations },
                receivers: Object.fromEntries([...this.receivers].map(([id, state]) => [id, {
                    enabled: state.enabled, pendingCount: state.pending.length,
                    processingCount: state.batch?.ids.length ?? 0, idleCycle: state.cycle,
                    timerDeadline: state.timer?.at ?? null
                }]))
            } : {})
        };
    }

    reset() {
        this.tokens.clear();
        this.mergeQueues.clear();
        this.parallelQueues.clear();
        this.nextTokenId = 1;
        this.startedTokenCount = 0;
        for (const id of this.receivers.keys()) this.receivers.set(id, this.#emptyReceiver());
        this.activityOutputs = {};
        this.boundaryActivations = {};
        this.deliveredSignals = 0;
        this.clock = -Infinity;
    }

    #newToken(nodeId, atMs) {
        if (this.execution && this.tokens.size >= 10000) throw new Error("Published animation capacity exceeded.");
        const token = {
            id: `token-${this.nextTokenId}`,
            sequence: this.nextTokenId++,
            state: "node",
            kind: "sequence",
            nodeId,
            connectorId: null,
            incomingConnectorId: null,
            enteredAt: atMs,
            travelStartedAt: 0,
            travelDurationMs: 0,
            routeLength: 0,
            position: center(this.presentationNodes.get(nodeId).bounds)
        };
        this.tokens.set(token.id, token);
        return token;
    }

    #enterNode(token, nodeId, incomingConnectorId, atMs, depth) {
        if (depth > 128) throw new Error("Published token graph contains an immediate cycle.");
        const node = this.nodes.get(nodeId);
        const presentationNode = this.presentationNodes.get(nodeId);
        if (!node || !presentationNode) throw new Error(`Published node '${nodeId}' is missing.`);
        token.nodeId = nodeId;
        token.connectorId = null;
        token.incomingConnectorId = incomingConnectorId;
        token.enteredAt = atMs;
        token.position = center(presentationNode.bounds);
        if (this.receivers.has(nodeId)) {
            const receiver = this.receivers.get(nodeId);
            this.tokens.delete(token.id);
            if (!receiver.enabled || receiver.boundariesClosed) {
                receiver.enabled = true;
                receiver.boundariesClosed = false;
                receiver.idle = false;
                receiver.settleAt = atMs;
            }
            return;
        }
        if (this.senders.has(nodeId) && node.role === "passThrough") {
            this.#sendMessages(nodeId, 1, atMs, depth);
            if (node.outgoingConnectorIds.length === 0) {
                this.tokens.delete(token.id);
                return;
            }
        }
        switch (node.role) {
            case "start":
            case "passThrough":
                this.#takeOnlyOutgoing(token, nodeId, atMs, depth + 1);
                break;
            case "activityDelay":
                token.state = "activity-delay";
                break;
            case "splitInvariant": {
                const value = Number(this.random());
                const normalized = Number.isFinite(value) ? clamp(value, 0, 0.9999999999999999) : 0;
                const index = Math.floor(normalized * node.outgoingConnectorIds.length);
                this.#beginConnector(token, node.outgoingConnectorIds[index], atMs, depth + 1);
                break;
            }
            case "mergeInvariant":
                this.#waitAtMerge(token, node, incomingConnectorId, atMs, depth + 1);
                break;
            case "parallelSynchronize":
                this.#waitAtParallel(token, node, incomingConnectorId, atMs, depth + 1);
                break;
            case "end":
                this.tokens.delete(token.id);
                break;
            default:
                throw new Error(`Published token role '${node.role}' is unsupported.`);
        }
    }

    #takeOnlyOutgoing(token, nodeId, atMs, depth) {
        const node = this.nodes.get(nodeId);
        if (!node || node.outgoingConnectorIds.length !== 1) {
            throw new Error(`Published node '${nodeId}' has ambiguous continuation.`);
        }
        this.#beginConnector(token, node.outgoingConnectorIds[0], atMs, depth + 1);
    }

    #beginConnector(token, connectorId, atMs, depth) {
        const edge = this.connectors.get(connectorId);
        if (!edge) throw new Error(`Published connector '${connectorId}' is missing.`);
        if (this.execution && (edge.sourceElementId !== token.nodeId ||
            (token.kind === "communication") !== (this.communication.get(connectorId)?.canDeliver === true))) {
            throw new Error("Published connector does not match its activation source or kind.");
        }
        const length = routeLength(edge.points);
        const duration = connectorDurationMs(
            edge.points,
            this.process.runtime.tokenSpeedPxPerSecond,
            this.process.runtime.minimumConnectorDurationMs);
        token.state = "travelling";
        token.connectorId = connectorId;
        token.incomingConnectorId = null;
        token.travelStartedAt = atMs;
        token.travelDurationMs = duration;
        token.routeLength = length;
        token.position = { ...edge.points[0] };
        if (duration === 0) {
            this.#arrive(token, edge, atMs, depth + 1);
        }
    }

    #arrive(token, edge, atMs, depth) {
        if (token.kind !== "communication") {
            this.#enterNode(token, edge.targetElementId, edge.id, atMs, depth);
            return;
        }
        if (!this.communication.get(edge.id)?.canDeliver || !this.senders.has(edge.sourceElementId)) {
            throw new Error("Published communication delivery is unsupported.");
        }
        this.deliveredSignals++;
        token.kind = "sequence";
        const receiver = this.receivers.get(edge.targetElementId);
        if (receiver) {
            token.state = "receiver-pending";
            token.nodeId = edge.targetElementId;
            token.connectorId = null;
            token.position = center(this.presentationNodes.get(token.nodeId).bounds);
            receiver.pending.push(token.id);
            receiver.timer = null;
            receiver.settleAt = atMs;
        } else if (this.catches.has(edge.targetElementId)) {
            this.#enterNode(token, edge.targetElementId, null, atMs, depth);
        } else throw new Error("Published communication receiver is missing.");
    }

    #emptyReceiver() {
        return { enabled: false, pending: [], batch: null, timer: null, idle: false, cycle: 0, settleAt: null, boundariesClosed: false };
    }

    #due(token) {
        return token.state === "travelling" ? token.travelStartedAt + token.travelDurationMs :
            token.state === "activity-delay" ? token.enteredAt + this.process.runtime.activityDelayMs : Infinity;
    }

    #advance(atMs, inclusive) {
        if (atMs < this.clock) throw new Error("Published animation time cannot move backwards.");
        let iterations = 0;
        while (true) {
            let due = Infinity;
            for (const token of this.tokens.values()) due = Math.min(due, this.#due(token));
            for (const state of this.receivers.values()) {
                due = Math.min(due, state.settleAt ?? Infinity, state.batch?.at ?? Infinity, state.timer?.at ?? Infinity);
            }
            if (!Number.isFinite(due) || due > atMs || (!inclusive && due === atMs)) break;
            if (++iterations > 10000) throw new Error("Published animation contains an unbounded immediate cycle.");
            // Deliver all arrivals at this logical instant before evaluating idle deadlines.
            // Zero-length continuations are resolved synchronously by the existing route path.
            for (const token of [...this.tokens.values()]) {
                if (this.tokens.has(token.id) && token.state === "travelling" && this.#due(token) <= due) {
                    this.#arrive(token, this.connectors.get(token.connectorId), due, 0);
                }
            }
            for (const token of [...this.tokens.values()]) {
                if (this.tokens.has(token.id) && token.state === "activity-delay" && this.#due(token) <= due) {
                    const owner = token.nodeId;
                    this.#completed(owner, [token], due);
                }
            }
            for (const [id, state] of this.receivers) {
                if (state.batch && state.batch.at <= due) {
                    const ids = state.batch.ids;
                    state.batch = null;
                    this.#completed(id, ids.map(tokenId => this.tokens.get(tokenId)), due);
                    state.settleAt = due;
                }
            }
            for (const [id, state] of this.receivers) {
                if (state.settleAt !== null && state.settleAt <= due) {
                    state.settleAt = null;
                    this.#settle(id, state, due);
                }
            }
            for (const [id, state] of this.receivers) {
                if (state.timer && state.timer.at <= due && state.enabled && !state.boundariesClosed && state.idle &&
                    !state.batch && state.pending.length === 0) {
                    const trigger = state.timer.trigger;
                    state.timer = null;
                    this.#fireTrigger(trigger, due);
                }
            }
        }
        this.clock = atMs;
        for (const token of this.tokens.values()) {
            if (token.state === "travelling") {
                const progress = Math.min(1, Math.max(0, (atMs - token.travelStartedAt) / token.travelDurationMs));
                token.position = interpolateRoute(this.connectors.get(token.connectorId).points, token.routeLength * progress);
            }
        }
    }

    #settle(id, state, atMs) {
        if (!state.enabled || state.batch) return;
        if (state.pending.length) {
            state.idle = false;
            state.boundariesClosed = false;
            state.timer = null;
            const ids = state.pending.splice(0);
            state.batch = { ids, at: atMs + this.process.runtime.activityDelayMs };
            for (const tokenId of ids) {
                const token = this.tokens.get(tokenId);
                token.state = "receiver-processing";
                token.enteredAt = atMs;
            }
        } else if (!state.idle && !state.boundariesClosed) {
            state.idle = true;
            state.cycle++;
            const signal = this.triggers.find(trigger => trigger.ownerId === id && trigger.kind === "idleSignal");
            const timer = this.triggers.find(trigger => trigger.ownerId === id && trigger.kind === "idleTimer");
            if (signal) {
                state.timer = null;
                this.#fireTrigger(signal, atMs);
            } else if (timer) state.timer = { trigger: timer, at: atMs + timer.delayMs };
        }
    }

    #completed(id, tokens, atMs) {
        const count = tokens.length;
        if (!count) return;
        // Close the completed cycle before following even zero-length outputs. A new
        // Sequence activation may then open a fresh cycle without reviving the old one.
        const receiver = this.receivers.get(id);
        if (receiver) {
            receiver.boundariesClosed = true;
            receiver.timer = null;
            receiver.idle = false;
        }
        this.activityOutputs[id] = (this.activityOutputs[id] ?? 0) + count;
        for (const token of tokens) this.#takeOnlyOutgoing(token, id, atMs, 0);
        this.#sendMessages(id, count, atMs, 0);
    }

    #sendMessages(nodeId, count, atMs, depth) {
        if (!this.senders.has(nodeId)) return;
        for (const [id, route] of this.communication) {
            if (!route.canDeliver || this.connectors.get(id).sourceElementId !== nodeId) continue;
            for (let index = 0; index < count; index++) {
                const signal = this.#newToken(nodeId, atMs);
                signal.kind = "communication";
                this.#beginConnector(signal, id, atMs, depth + 1);
            }
        }
    }

    #fireTrigger(trigger, atMs) {
        if (this.receivers.get(trigger.ownerId)?.boundariesClosed) return;
        this.boundaryActivations[trigger.nodeId] = (this.boundaryActivations[trigger.nodeId] ?? 0) + 1;
        // Apply interruption before following a route which might explicitly re-enable its owner.
        if (trigger.interrupting) {
            const state = this.receivers.get(trigger.ownerId);
            if (state) {
                state.enabled = false;
                state.timer = null;
                state.idle = false;
                state.settleAt = null;
            }
            for (const token of this.tokens.values()) {
                if (token.nodeId === trigger.ownerId && token.state === "activity-delay") this.tokens.delete(token.id);
            }
        }
        const token = this.#newToken(trigger.nodeId, atMs);
        this.#takeOnlyOutgoing(token, trigger.nodeId, atMs, 0);
    }

    #waitAtMerge(token, node, incomingConnectorId, atMs, depth) {
        if (!incomingConnectorId || !node.incomingConnectorIds.includes(incomingConnectorId)) {
            throw new Error(`Merge '${node.id}' received a token from an unknown connector.`);
        }
        token.state = "merge-wait";
        let queues = this.mergeQueues.get(node.id);
        if (!queues) {
            queues = new Map(node.incomingConnectorIds.map(id => [id, []]));
            this.mergeQueues.set(node.id, queues);
        }
        queues.get(incomingConnectorId).push(token.id);
        this.#fireReadyMerges(node, queues, atMs, depth + 1);
    }

    #fireReadyMerges(node, queues, atMs, depth) {
        while (true) {
            const ready = node.incomingConnectorIds.filter(id => queues.get(id).length > 0);
            if (ready.length < 2) return;
            const firstId = queues.get(ready[0]).shift();
            const secondId = queues.get(ready[1]).shift();
            const first = this.tokens.get(firstId);
            const second = this.tokens.get(secondId);
            if (!first || !second) throw new Error("Merge waiting state is inconsistent.");
            this.tokens.delete(second.id);
            first.incomingConnectorId = null;
            this.#takeOnlyOutgoing(first, node.id, atMs, depth + 1);
        }
    }

    #waitAtParallel(token, node, incomingConnectorId, atMs, depth) {
        if (!incomingConnectorId || !node.incomingConnectorIds.includes(incomingConnectorId)) {
            throw new Error(
                `Parallel synchronization '${node.id}' received a token from an unknown connector.`);
        }
        token.state = "parallel-wait";
        let queues = this.parallelQueues.get(node.id);
        if (!queues) {
            queues = new Map(node.incomingConnectorIds.map(id => [id, []]));
            this.parallelQueues.set(node.id, queues);
        }
        queues.get(incomingConnectorId).push(token.id);
        this.#fireReadyParallels(node, queues, atMs, depth + 1);
    }

    #fireReadyParallels(node, queues, atMs, depth) {
        while (node.incomingConnectorIds.every(id => queues.get(id).length > 0)) {
            const consumedIds = node.incomingConnectorIds.map(id => queues.get(id).shift());
            const primary = this.tokens.get(consumedIds[0]);
            const consumed = consumedIds.map(id => this.tokens.get(id));
            if (!primary || consumed.some(token => !token)) {
                throw new Error("Parallel waiting state is inconsistent.");
            }
            for (const token of consumed.slice(1)) this.tokens.delete(token.id);
            primary.incomingConnectorId = null;
            this.#fanOut(primary, node, atMs, depth + 1);
        }
    }

    #fanOut(token, node, atMs, depth) {
        const branchTokens = [token];
        for (let index = 1; index < node.outgoingConnectorIds.length; index++) {
            branchTokens.push(this.#newToken(node.id, atMs));
        }
        for (let index = 0; index < node.outgoingConnectorIds.length; index++) {
            this.#beginConnector(
                branchTokens[index],
                node.outgoingConnectorIds[index],
                atMs,
                depth + 1);
        }
    }
}

class PublishedProcessViewer {
    constructor(canvas, process, controls = {}, options = {}) {
        if (!(canvas instanceof HTMLCanvasElement)) throw new TypeError("A Canvas is required.");
        validateProcess(process);
        this.canvas = canvas;
        this.context = canvas.getContext("2d");
        if (!this.context) throw new Error("Canvas2D is unavailable.");
        this.process = process;
        this.runtime = new PublishedTokenRuntime(process, options);
        this.controls = controls;
        this.viewport = new PublishedViewport(process.presentation.contentBounds, 1, 1, VIEW_PADDING, process.initialView);
        this.pointerId = null;
        this.pointerX = 0;
        this.pointerY = 0;
        this.pointerStartId = null;
        this.disposed = false;
        this.onResize = () => this.resize();
        this.onStart = () => {
            if (!this.disposed && this.runtime.startNodeIds.length === 1) this.runtime.start();
        };
        this.onZoomIn = () => this.zoomFromCenter(1.2);
        this.onZoomOut = () => this.zoomFromCenter(1 / 1.2);
        this.onPointerDown = event => this.pointerDown(event);
        this.onPointerMove = event => this.pointerMove(event);
        this.onPointerUp = event => this.pointerUp(event);
        this.onPointerCancel = event => this.pointerUp(event, false);
        this.onWheel = event => this.wheel(event);
        const singletonStart = this.runtime.startNodeIds.length === 1;
        if (controls.startButton) {
            controls.startButton.disabled = !singletonStart;
            controls.startButton.title = singletonStart ? "Start" : START_GUIDANCE;
        }
        if (controls.startGuidance) {
            controls.startGuidance.textContent = START_GUIDANCE;
            controls.startGuidance.hidden = singletonStart;
        }
        controls.startButton?.addEventListener("click", this.onStart);
        controls.zoomInButton?.addEventListener("click", this.onZoomIn);
        controls.zoomOutButton?.addEventListener("click", this.onZoomOut);
        canvas.addEventListener("pointerdown", this.onPointerDown);
        canvas.addEventListener("pointermove", this.onPointerMove);
        canvas.addEventListener("pointerup", this.onPointerUp);
        canvas.addEventListener("pointercancel", this.onPointerCancel);
        canvas.addEventListener("lostpointercapture", this.onPointerCancel);
        canvas.addEventListener("wheel", this.onWheel, { passive: false });
        window.addEventListener("resize", this.onResize);
        this.resize();
        this.frame = requestAnimationFrame(timestamp => this.drawFrame(timestamp));
    }

    resize() {
        this.pointerStartId = null;
        const bounds = this.canvas.getBoundingClientRect();
        const width = Math.max(1, bounds.width);
        const height = Math.max(1, bounds.height);
        const dpr = Number.isFinite(window.devicePixelRatio) && window.devicePixelRatio > 0
            ? window.devicePixelRatio : 1;
        this.canvas.width = Math.max(1, Math.ceil(width * dpr));
        this.canvas.height = Math.max(1, Math.ceil(height * dpr));
        this.cssWidth = width;
        this.cssHeight = height;
        this.dpr = dpr;
        this.viewport.resize(width, height);
    }

    zoomFromCenter(factor) {
        this.pointerStartId = null;
        this.viewport.zoomAt(factor, this.cssWidth / 2, this.cssHeight / 2);
    }

    startAt(clientX, clientY) {
        const surface = this.canvas.getBoundingClientRect();
        const position = this.viewport.toDocument(clientX - surface.left, clientY - surface.top);
        let startId = null;
        for (const node of this.process.presentation.nodes) {
            if (this.runtime.nodes.get(node.id)?.role !== "start" ||
                !containsPublishedNode(node, position)) continue;
            if (startId !== null) return null;
            startId = node.id;
        }
        return startId;
    }

    pointerDown(event) {
        if (this.disposed || this.pointerId !== null) return;
        this.pointerId = event.pointerId;
        this.pointerX = event.clientX;
        this.pointerY = event.clientY;
        this.pointerStartId = event.button === 0
            ? this.startAt(event.clientX, event.clientY)
            : null;
        this.canvas.dataset.panning = "true";
        try { this.canvas.setPointerCapture(event.pointerId); } catch { /* capture is optional */ }
    }

    pointerMove(event) {
        if (event.pointerId !== this.pointerId) return;
        const deltaX = event.clientX - this.pointerX;
        const deltaY = event.clientY - this.pointerY;
        if (deltaX !== 0 || deltaY !== 0) this.pointerStartId = null;
        this.viewport.pan(deltaX, deltaY);
        this.pointerX = event.clientX;
        this.pointerY = event.clientY;
    }

    pointerUp(event, allowActivation = true) {
        if (event.pointerId !== this.pointerId) return;
        const startId = allowActivation && !this.disposed &&
            event.clientX === this.pointerX && event.clientY === this.pointerY &&
            this.pointerStartId === this.startAt(event.clientX, event.clientY)
            ? this.pointerStartId
            : null;
        this.pointerId = null;
        this.pointerStartId = null;
        this.canvas.dataset.panning = "false";
        try {
            if (this.canvas.hasPointerCapture?.(event.pointerId)) {
                this.canvas.releasePointerCapture(event.pointerId);
            }
        } catch { /* release is best effort */ }
        if (startId !== null) this.runtime.activateStart(startId);
    }

    wheel(event) {
        const deltaX = Number(event.deltaX);
        const deltaY = Number(event.deltaY);
        if (![deltaX, deltaY].every(Number.isFinite)) return;
        this.pointerStartId = null;
        const horizontalMultiplier = event.deltaMode === 2
            ? this.cssWidth
            : event.deltaMode === 1 ? 16 : 1;
        const verticalMultiplier = event.deltaMode === 2
            ? this.cssHeight
            : event.deltaMode === 1 ? 16 : 1;
        event.preventDefault();
        this.viewport.pan(
            -deltaX * horizontalMultiplier,
            -deltaY * verticalMultiplier);
    }

    drawFrame(timestamp) {
        if (this.disposed) return;
        this.runtime.tick(timestamp);
        drawPublishedProcess(
            this.context,
            this.process,
            this.runtime.captureState(),
            this.viewport,
            this.cssWidth,
            this.cssHeight,
            this.dpr);
        this.frame = requestAnimationFrame(next => this.drawFrame(next));
    }

    dispose() {
        if (this.disposed) return;
        this.disposed = true;
        if (this.pointerId !== null) this.pointerUp({ pointerId: this.pointerId }, false);
        cancelAnimationFrame(this.frame);
        window.removeEventListener("resize", this.onResize);
        this.controls.startButton?.removeEventListener("click", this.onStart);
        this.controls.zoomInButton?.removeEventListener("click", this.onZoomIn);
        this.controls.zoomOutButton?.removeEventListener("click", this.onZoomOut);
        this.canvas.removeEventListener("pointerdown", this.onPointerDown);
        this.canvas.removeEventListener("pointermove", this.onPointerMove);
        this.canvas.removeEventListener("pointerup", this.onPointerUp);
        this.canvas.removeEventListener("pointercancel", this.onPointerCancel);
        this.canvas.removeEventListener("lostpointercapture", this.onPointerCancel);
        this.canvas.removeEventListener("wheel", this.onWheel);
    }
}

function drawPublishedProcess(context, process, runtimeState, viewport, width, height, dpr = 1) {
    context.save();
    try {
        context.setTransform(1, 0, 0, 1, 0, 0);
        context.clearRect(0, 0, Math.ceil(width * dpr), Math.ceil(height * dpr));
        context.scale(dpr, dpr);
        context.transform(
            viewport.scale, 0, 0, viewport.scale,
            viewport.offsetX, viewport.offsetY);
        for (const item of process.presentation.items) drawItem(context, item);
        for (const token of runtimeState.tokens) drawToken(context, token.position, viewport.scale, token.kind);
    } finally {
        context.restore();
    }
}

function startPublishedViewer() {
    const canvas = document.getElementById("process-canvas");
    const status = document.getElementById("viewer-status");
    // Clear the optional banner in older HTML shells as well. Authoring diagnostics
    // must not become publication content, including when bootstrap validation fails.
    if (status) {
        status.textContent = "";
        status.hidden = true;
    }
    try {
        const process = globalThis.__INCEPTUS_PUBLISHED_PROCESS__;
        const viewer = new PublishedProcessViewer(canvas, process, {
            startButton: document.getElementById("start-process"),
            startGuidance: document.getElementById("start-guidance"),
            zoomInButton: document.getElementById("zoom-in"),
            zoomOutButton: document.getElementById("zoom-out")
        });
        return viewer;
    } catch {
        return null;
    }
}

function drawItem(context, item) {
    context.save();
    try {
        context.globalAlpha = item.opacity;
        context.lineWidth = item.strokeWidth;
        context.lineCap = "butt";
        context.lineJoin = "miter";
        context.setLineDash(item.dashPattern ?? []);
        if (item.clip) {
            context.beginPath();
            context.rect(item.clip.x, item.clip.y, item.clip.width, item.clip.height);
            context.clip();
        }
        const m = item.transform;
        context.transform(m.m11, m.m12, m.m21, m.m22, m.offsetX, m.offsetY);
        context.fillStyle = item.fill ?? "rgba(0,0,0,0)";
        context.strokeStyle = item.stroke ?? "rgba(0,0,0,0)";
        const bounds = item.geometryBounds;
        switch (item.geometryKind) {
            case 0:
                if (item.fill) context.fillRect(bounds.x, bounds.y, bounds.width, bounds.height);
                if (item.stroke) context.strokeRect(bounds.x, bounds.y, bounds.width, bounds.height);
                break;
            case 1:
                context.beginPath();
                context.ellipse(bounds.x + (bounds.width / 2), bounds.y + (bounds.height / 2),
                    bounds.width / 2, bounds.height / 2, 0, 0, Math.PI * 2);
                if (item.fill) context.fill();
                if (item.stroke) context.stroke();
                break;
            case 2:
                context.beginPath();
                context.moveTo(item.points[0].x, item.points[0].y);
                for (let index = 1; index < item.points.length; index++) {
                    context.lineTo(item.points[index].x, item.points[index].y);
                }
                if (item.isClosed) context.closePath();
                if (item.fill) context.fill();
                if (item.stroke) context.stroke();
                break;
            case 3: {
                const alignments = ["start", "center", "end"];
                const baselines = ["top", "middle", "bottom"];
                context.font = `400 ${item.fontSize}px ${quoteFont(item.fontFamily ?? "sans-serif")}`;
                context.textAlign = alignments[item.textAlignment];
                context.textBaseline = baselines[item.textBaseline];
                if (item.fill) context.fillText(item.content ?? "", item.textAnchor.x, item.textAnchor.y);
                if (item.stroke) context.strokeText(item.content ?? "", item.textAnchor.x, item.textAnchor.y);
                break;
            }
            default:
                throw new Error("Published presentation contains unsupported geometry.");
        }
    } finally {
        context.restore();
    }
}

function drawToken(context, position, scale, kind) {
    context.save();
    try {
        context.beginPath();
        if (kind === "communication") {
            const radius = (TOKEN_RADIUS + 2) / scale;
            context.moveTo(position.x, position.y - radius);
            context.lineTo(position.x + radius, position.y);
            context.lineTo(position.x, position.y + radius);
            context.lineTo(position.x - radius, position.y);
            context.closePath();
        } else context.arc(position.x, position.y, TOKEN_RADIUS / scale, 0, Math.PI * 2);
        context.fillStyle = kind === "communication" ? "#0284c7" : "#f97316";
        context.strokeStyle = kind === "communication" ? "#0c4a6e" : "#7c2d12";
        context.lineWidth = 2 / scale;
        context.shadowColor = "rgba(15, 23, 42, .3)";
        context.shadowBlur = 5 / scale;
        context.fill();
        context.stroke();
    } finally {
        context.restore();
    }
}

function validateProcess(process) {
    if (!process || process.format !== FORMAT || process.formatVersion !== FORMAT_VERSION ||
        !process.presentation || !Array.isArray(process.presentation.items) ||
        !Array.isArray(process.presentation.nodes) ||
        !Array.isArray(process.presentation.connectors) ||
        !process.tokenGraph || !Array.isArray(process.tokenGraph.nodes) ||
        !process.runtime) {
        throw new TypeError("Unsupported published process data.");
    }
    if (process.initialView != null) validateInitialRegion(process.initialView);
    if (process.execution != null) validateExecution(process);
}

function validateExecution(process) {
    const execution = process.execution;
    for (const key of ["senderIds", "catchIds", "receiverIds", "communicationRoutes", "attachedTriggers", "warnings"]) {
        if (!Array.isArray(execution[key])) throw new TypeError("Incomplete published execution metadata.");
    }
    if (!execution.warnings.every(value => typeof value === "string")) throw new TypeError("Invalid publication warnings.");
    const uniqueMap = (items, key) => {
        const map = new Map();
        for (const item of items) {
            const id = key(item);
            if (typeof id !== "string" || !id || map.has(id)) throw new TypeError("Duplicate or invalid published identity.");
            map.set(id, item);
        }
        return map;
    };
    const nodes = uniqueMap(process.tokenGraph.nodes, node => node.id);
    const visible = uniqueMap(process.presentation.nodes, node => node.id);
    const edges = uniqueMap(process.presentation.connectors, edge => edge.id);
    const routes = uniqueMap(execution.communicationRoutes, route => route.connectorId);
    for (const [key, roles] of [["senderIds", ["passThrough", "activityDelay"]], ["catchIds", ["passThrough"]], ["receiverIds", ["activityDelay"]]]) {
        uniqueMap(execution[key], id => id);
        for (const id of execution[key]) if (!roles.includes(nodes.get(id)?.role)) throw new TypeError("Invalid published activation role.");
    }
    for (const edge of edges.values()) requireRoute(edge.points);
    for (const node of nodes.values()) {
        if (!visible.has(node.id)) throw new TypeError("Execution node has no presentation.");
        for (const [list, endpoint] of [[node.incomingConnectorIds, "targetElementId"], [node.outgoingConnectorIds, "sourceElementId"]]) {
            if (!Array.isArray(list) || new Set(list).size !== list.length) throw new TypeError("Invalid token adjacency.");
            for (const id of list) if (!edges.has(id) || routes.has(id) || edges.get(id)[endpoint] !== node.id) {
                throw new TypeError("Communication routes cannot enter token adjacency.");
            }
        }
    }
    for (const route of routes.values()) {
        const edge = edges.get(route.connectorId);
        if (!edge || typeof route.canDeliver !== "boolean" || (route.canDeliver &&
            (!execution.senderIds.includes(edge.sourceElementId) ||
                (!execution.catchIds.includes(edge.targetElementId) && !execution.receiverIds.includes(edge.targetElementId))))) {
            throw new TypeError("Invalid communication endpoints.");
        }
    }
    const triggerKeys = new Set();
    uniqueMap(execution.attachedTriggers, trigger => trigger.nodeId);
    for (const trigger of execution.attachedTriggers) {
        const key = `${trigger.ownerId}/${trigger.kind}`;
        const node = nodes.get(trigger.nodeId);
        if (triggerKeys.has(key) || nodes.get(trigger.ownerId)?.role !== "activityDelay" ||
            node?.role !== "passThrough" || node.incomingConnectorIds.length !== 0 || node.outgoingConnectorIds.length !== 1 ||
            typeof trigger.interrupting !== "boolean" || !["idleTimer", "idleSignal", "completion"].includes(trigger.kind) ||
            (trigger.kind === "idleTimer" && (!Number.isFinite(trigger.delayMs) || trigger.delayMs <= 0 || trigger.delayMs > Number.MAX_SAFE_INTEGER))) {
            throw new TypeError("Invalid attached activation definition.");
        }
        triggerKeys.add(key);
    }
    if (!Number.isFinite(process.runtime.activityDelayMs) || process.runtime.activityDelayMs < 0 ||
        !Number.isFinite(process.runtime.tokenSpeedPxPerSecond) || process.runtime.tokenSpeedPxPerSecond <= 0 ||
        !Number.isFinite(process.runtime.minimumConnectorDurationMs) || process.runtime.minimumConnectorDurationMs < 0) {
        throw new TypeError("Invalid published timing configuration.");
    }
}

function validateInitialRegion(initialView) {
    const region = initialView?.visibleRegion;
    validateRect(region);
    if (region.width <= 0 || region.height <= 0 ||
        ![region.x + region.width, region.y + region.height,
            (region.x + region.width / 2) * MAX_SCALE, (region.y + region.height / 2) * MAX_SCALE].every(Number.isFinite)) {
        throw new TypeError("The initial visible region must be positive and finite within supported zoom limits.");
    }
    return region;
}

function validateRect(value) {
    if (!value || ![value.x, value.y, value.width, value.height].every(Number.isFinite) ||
        value.width < 0 || value.height < 0) {
        throw new TypeError("Published bounds must be finite and non-negative.");
    }
}

function requireRoute(points) {
    if (!Array.isArray(points) || points.length < 2 ||
        !points.every(point => point && [point.x, point.y].every(Number.isFinite))) {
        throw new TypeError("A frozen route requires at least two finite points.");
    }
}

function requireSurface(width, height) {
    if (![width, height].every(value => Number.isFinite(value) && value > 0)) {
        throw new TypeError("Viewer surface dimensions must be positive.");
    }
}

function requireTime(value) {
    if (!Number.isFinite(value)) throw new TypeError("Runtime time must be finite.");
}

function center(bounds) {
    return { x: bounds.x + (bounds.width / 2), y: bounds.y + (bounds.height / 2) };
}

function containsPublishedNode(node, position) {
    const bounds = node.bounds;
    if (position.x < bounds.x || position.x > bounds.x + bounds.width ||
        position.y < bounds.y || position.y > bounds.y + bounds.height ||
        ![position.x, position.y].every(Number.isFinite)) return false;
    if (node.descriptor !== "ellipse") return true;
    if (bounds.width === 0 || bounds.height === 0) return false;
    const middle = center(bounds);
    return (((position.x - middle.x) / (bounds.width / 2)) ** 2) +
        (((position.y - middle.y) / (bounds.height / 2)) ** 2) <= 1;
}

function clamp(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value));
}

function quoteFont(value) {
    return `"${String(value).replaceAll("\\", "\\\\").replaceAll('"', '\\"')}"`;
}

globalThis.InceptusPublishedViewer = Object.freeze({
    PublishedProcessViewer,
    PublishedTokenRuntime,
    PublishedViewport,
    connectorDurationMs,
    drawPublishedProcess,
    interpolateRoute,
    routeLength,
    startPublishedViewer
});

if (typeof window !== "undefined" && typeof document !== "undefined") {
    window.addEventListener("DOMContentLoaded", () => { startPublishedViewer(); });
}
})();
