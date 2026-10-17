import net from 'node:net';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const MAX_REQUEST = 8 * 1024 * 1024;
const MAX_RESPONSE = 64 * 1024 * 1024;
const OWNED_PIPE = /^\\\\\.\\pipe\\mycodex-cua-[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const METHODS = new Set([
    'list_apps', 'list_windows', 'get_window', 'get_window_state', 'activate_window',
    'click', 'click_element', 'scroll', 'drag', 'press_key', 'type_text', 'launch_app',
    'perform_secondary_action', 'set_value', 'end_turn'
]);
// Known categories only select recovery guidance; they never bypass the official
// helper's checks or repeat an action. Unrecognized failures remain conservative.
const RECOVERABLE_ACTION_FAILURES = new Map([
    ['coordinate input geometry is unavailable', {
        code: 'CUA_GEOMETRY_UNAVAILABLE',
        guidance: 'Refresh the window state with include_screenshot:true before choosing further input. Do not blindly repeat the failed input.'
    }],
    ['accessibility window-opened handler did not become ready', {
        code: 'CUA_WINDOW_OBSERVER_NOT_READY',
        guidance: 'The application launch could not be observed; inspect existing windows before choosing another action. Do not blindly repeat the launch.'
    }],
    ['user input was detected in this window; call get_window_state before continuing', {
        code: 'CUA_OBSERVATION_STALE',
        guidance: 'The window observation is stale. Call get_window_state with include_screenshot:true before choosing further input. Do not blindly repeat the failed action.'
    }]
]);
// Exact pinned-helper sentence shape, bounded dynamic fields, and an absolute end
// assertion (plain JS $ also accepts a trailing newline). Never expose its captures.
const TARGET_OBSCURED_PATTERN = /^point \(-?\d{1,10}, -?\d{1,10}\) is over [^\r\n\0]{1,512}, not target window [^\r\n\0]{1,512}; activate the target or take a fresh screenshot before retrying$(?![\s\S])/;
const TARGET_OBSCURED_FAILURE = {
    code: 'CUA_TARGET_OBSCURED',
    guidance: 'The target is obscured. Call activate_window for the intended target, then get_window_state with include_screenshot:true before choosing further input. Do not blindly repeat the failed action.'
};
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const identity = value => typeof value === 'string' && /^[A-Za-z0-9._:-]{1,200}$/.test(value);
const requestId = value => identity(value) || (Number.isSafeInteger(value) && value >= 0);
const error = code => Object.assign(new Error(code), { code });
const SYSTEM_ERRORS = new Set(['ENOENT', 'EACCES', 'EPERM', 'EPIPE', 'ECONNRESET', 'ECONNREFUSED',
    'ETIMEDOUT', 'ESRCH', 'EBADF', 'ERR_STREAM_DESTROYED', 'ERR_STREAM_WRITE_AFTER_END']);
const ERROR_TYPES = new Set(['Error', 'TypeError', 'RangeError', 'SyntaxError', 'ReferenceError', 'AbortError']);

function failureDiagnostic(failure, method, stage, approval, started, recoverable) {
    const message = typeof failure?.message === 'string' ? failure.message : '';
    // These shapes come from the pinned WindowsHelperTransport. Never copy a suffix:
    // helper exit/JSON errors can include stderr, application data or credentials.
    const exit = /^computer-use helper exited with exit code (-?\d{1,10})(?::|$)/.exec(message);
    const exitCode = exit ? Number(exit[1]) : null;
    const hresult = /\bHRESULT[=: ]+(0x[0-9a-fA-F]{8})\b/i.exec(message);
    const timeoutMethod = message.startsWith('computer-use request timed out: ')
        ? message.slice('computer-use request timed out: '.length) : '';
    return {
        method, stage, approval, elapsedMs: Math.max(0, Math.round(performance.now() - started)),
        category: recoverable ? 'known_action_failure' :
            approval === 'decline' || approval === 'cancel' ? 'approval_declined' :
            METHODS.has(timeoutMethod) ? 'request_timeout' :
            message.startsWith('computer-use helper exited with ') ? 'helper_exit' :
            message.startsWith('computer-use helper emitted invalid JSON: ') ? 'invalid_json' :
            SYSTEM_ERRORS.has(failure?.code) ? 'system_error' : 'unknown',
        errorType: ERROR_TYPES.has(failure?.name) ? failure.name : 'unknown',
        errorCode: SYSTEM_ERRORS.has(failure?.code) ? failure.code : 'unknown',
        helperExitCode: exitCode !== null && exitCode >= -2147483648 && exitCode <= 4294967295 ? exitCode : null,
        hresult: hresult ? hresult[1].toLowerCase() : 'unknown'
    };
}

// The only executor boundary is the original WindowsHelperTransport. This module never
// captures a window, injects input, stores grants, or retries an executor request.
export function createComputerUseHost({ createTransport, emit, prepareExecution = false, prepareTransport = async () => {} }) {
    const slots = new Map();
    const acquisitions = new Map();
    let owner = null;
    let stopping = false;
    let fatal = false;
    let shutdownPromise = null;

    function alive(slot, generation) {
        return !stopping && !fatal && slot.turn !== null && slot.generation === generation;
    }

    function send(socket, message) {
        if (socket.destroyed) return;
        const bytes = Buffer.from(JSON.stringify(message));
        if (bytes.length > MAX_RESPONSE || socket.writableLength > MAX_RESPONSE) {
            socket.destroy();
            throw error('CUA_RESPONSE_TOO_LARGE');
        }
        const header = Buffer.alloc(4);
        header.writeUInt32LE(bytes.length);
        socket.write(Buffer.concat([header, bytes]));
    }

    function released(lease) {
        emit({ event: 'released', leaseId: lease.id, slot: lease.slot.id,
            thread: lease.thread, turn: lease.turn, generation: lease.generation });
    }

    function failHost(code) {
        if (fatal) return;
        fatal = true;
        // The parent kills its owned process tree BEFORE releasing the OS mutex.
        emit({ event: 'fatal', code });
    }

    async function release(slot, reason) {
        if (slot.closing) return slot.closing;
        slot.turn = null;
        slot.generation++;
        for (const pending of slot.approvals.values()) {
            clearTimeout(pending.timer);
            pending.reject(error(reason));
        }
        slot.approvals.clear();
        for (const socket of slot.unpublishedSockets) socket.destroy();
        const lease = owner?.slot === slot ? owner : null;
        if (lease?.acquiring) {
            lease.cancelled = true;
            if (lease.preparing) {
                clearTimeout(lease.timer);
                acquisitions.delete(lease.id);
            }
            lease.reject(error(reason));
        }
        // Old connections cannot close or approve operations in the next generation.
        for (const socket of slot.sockets) socket.destroy();
        const transport = slot.transport;
        slot.transport = null;
        slot.closing = (async () => {
            if (transport) {
                let timer;
                try {
                    await Promise.race([
                        Promise.resolve().then(() => transport.close()),
                        new Promise((_, reject) => {
                            timer = setTimeout(() => reject(error('CUA_CLOSE_TIMEOUT')), 1500);
                        })
                    ]);
                } catch {
                    failHost('CUA_CLOSE_TIMEOUT');
                    throw error('CUA_CLOSE_TIMEOUT');
                } finally {
                    clearTimeout(timer);
                }
            }
            if (lease && owner === lease) {
                owner = null;
                if (!lease.acquiring) released(lease);
            }
        })();
        try { await slot.closing; }
        finally { slot.closing = null; }
    }

    function requestDesktopLease(lease) {
        lease.timer = setTimeout(() => {
            acquisitions.delete(lease.id);
            lease.cancelled = true;
            lease.reject(error('CUA_ACQUIRE_TIMEOUT'));
            failHost('CUA_ACQUIRE_TIMEOUT');
        }, 5000);
        emit({ event: 'acquire', id: lease.id, leaseId: lease.id, slot: lease.slot.id,
            thread: lease.thread, turn: lease.turn, generation: lease.generation });
    }

    async function acquire(slot, generation) {
        if (owner) {
            if (owner.slot === slot && !owner.acquiring) return;
            throw error('CUA_DESKTOP_BUSY');
        }
        const lease = { id: randomUUID(), slot, thread: slot.thread,
            turn: slot.turn, generation, acquiring: true, cancelled: false };
        owner = lease;
        try {
            await new Promise((resolve, reject) => {
                lease.resolve = resolve;
                lease.reject = reject;
                acquisitions.set(lease.id, lease);
                lease.preparing = prepareExecution;
                if (prepareExecution) {
                    lease.timer = setTimeout(() => failHost('CUA_PREPARATION_TIMEOUT'), 60000);
                    emit({ event: 'prepare', id: lease.id, thread: slot.thread, turn: slot.turn });
                } else requestDesktopLease(lease);
            });
            if (!alive(slot, generation)) throw error('CUA_TURN_ENDED');
        } catch (failure) {
            if (owner === lease && !fatal) owner = null;
            throw failure;
        }
    }

    function approval(slot, socket, generation, request) {
        if (!alive(slot, generation)) return Promise.reject(error('CUA_TURN_ENDED'));
        const id = `computer-use-approval:${randomUUID()}`;
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                slot.approvals.delete(id);
                reject(error('CUA_APPROVAL_TIMEOUT'));
            }, 10 * 60 * 1000);
            slot.approvals.set(id, { socket, generation, timer, resolve, reject });
            try { send(socket, { jsonrpc: '2.0', id, method: 'requestComputerUseApproval', params: request }); }
            catch (failure) {
                clearTimeout(timer);
                slot.approvals.delete(id);
                reject(failure);
            }
        });
    }

    async function handle(slot, socket, message) {
        if (!object(message) || message.jsonrpc !== '2.0' || !requestId(message.id)) {
            socket.destroy();
            return;
        }
        if (!Object.hasOwn(message, 'method')) {
            const pending = slot.approvals.get(message.id);
            if (!pending || pending.socket !== socket) return;
            slot.approvals.delete(message.id);
            clearTimeout(pending.timer);
            if (!alive(slot, pending.generation) || message.error || !object(message.result))
                pending.reject(error('CUA_APPROVAL_CANCELLED'));
            else pending.resolve(message.result);
            return;
        }
        const fail = (code, detail = code) => {
            if (code === 'CUA_DESKTOP_BUSY') emit({ event: 'busy', thread: slot.thread });
            send(socket, { jsonrpc: '2.0', id: message.id, error: { code: -32000, message: detail } });
        };
        const reply = result => send(socket, { jsonrpc: '2.0', id: message.id, result });
        // ponytail: cap replay memory per connection; reconnect if a long session reaches 4096 calls.
        if (socket.requestIds.has(message.id) || socket.requestIds.size >= 4096) {
            socket.destroy();
            return;
        }
        socket.requestIds.add(message.id);
        if (message.method === 'ping') return reply('pong');
        if (message.method === 'close') {
            reply(null);
            if (socket.generation === slot.generation) await release(slot, 'CUA_CLIENT_CLOSED');
            return;
        }
        const request = message.params;
        const metadata = request?.codexTurnMetadata;
        if (message.method !== 'request' || !object(request) || !METHODS.has(request.method) ||
            !object(request.params) || !object(metadata)) return fail('CUA_UNSUPPORTED_REQUEST');
        if (!alive(slot, slot.generation) || metadata.session_id !== slot.thread || metadata.turn_id !== slot.turn)
            return fail('CUA_TURN_NOT_ACTIVE');
        if (slot.closing || slot.busy) return fail('CUA_REQUEST_BUSY');
        if (owner && owner.slot !== slot) return fail('CUA_DESKTOP_BUSY');
        if (request.method === 'end_turn') {
            reply(null);
            await release(slot, 'CUA_TURN_ENDED');
            return;
        }
        slot.busy = true;
        const generation = slot.generation;
        socket.generation = generation;
        const started = performance.now();
        let stage = 'desktop_acquire';
        let approvalState = 'not_requested';
        try {
            await acquire(slot, generation);
            if (!alive(slot, generation)) throw error('CUA_TURN_ENDED');
            stage = 'transport_prepare';
            await prepareTransport();
            if (!alive(slot, generation)) throw error('CUA_TURN_ENDED');
            stage = 'transport_create';
            slot.transport ??= createTransport(slot.helperHome);
            stage = 'executor_request';
            const result = await slot.transport.request(request.method, request.params, {
                codexTurnMetadata: metadata,
                createElicitation: async value => {
                    stage = 'approval';
                    approvalState = 'pending';
                    const result = await approval(slot, socket, generation, value);
                    approvalState = ['accept', 'decline', 'cancel'].includes(result.action) ? result.action : 'invalid';
                    stage = 'executor_request';
                    return result;
                }
            });
            if (!alive(slot, generation)) throw error('CUA_TURN_ENDED');
            stage = 'response_write';
            reply(result);
            emit({ event: 'available', thread: slot.thread });
        } catch (failure) {
            if (!alive(slot, generation)) return;
            const helperMessage = failure?.message;
            const recoverable = RECOVERABLE_ACTION_FAILURES.get(helperMessage) ??
                (typeof helperMessage === 'string' && helperMessage.length <= 1200 && TARGET_OBSCURED_PATTERN.test(helperMessage)
                    ? TARGET_OBSCURED_FAILURE : null);
            const diagnostic = failureDiagnostic(failure, request.method, stage, approvalState, started, recoverable);
            if (recoverable && alive(slot, generation)) {
                fail(recoverable.code, `${recoverable.code}: ${recoverable.guidance}`);
                emit({ event: 'actionFailed', code: recoverable.code, slot: slot.id, generation, thread: slot.thread, diagnostic });
                return;
            }
            const code = typeof failure?.code === 'string' && failure.code.startsWith('CUA_') ? failure.code : 'CUA_EXECUTION_FAILED';
            fail(code);
            if (code !== 'CUA_DESKTOP_BUSY') {
                emit({ event: 'fault', code, slot: slot.id, thread: slot.thread, turn: slot.turn, generation, diagnostic });
                if (alive(slot, generation)) await release(slot, code);
            }
        } finally {
            slot.busy = false;
        }
    }

    function connect(slot, socket) {
        socket.on('error', () => {});
        if (stopping || fatal || slot.sockets.size + slot.unpublishedSockets.size >= 8) return socket.destroy();
        if (!slot.published) {
            // The parent opens a security-only handle to tighten this pipe's DACL.
            // Hold that connection without reading; destroying it immediately races SetSecurityInfo.
            socket.pause();
            slot.unpublishedSockets.add(socket);
            const timer = setTimeout(() => socket.destroy(), 5000);
            socket.on('close', () => { clearTimeout(timer); slot.unpublishedSockets.delete(socket); });
            return;
        }
        slot.sockets.add(socket);
        socket.requestIds = new Set();
        let buffer = Buffer.alloc(0);
        socket.on('data', chunk => {
            buffer = Buffer.concat([buffer, chunk]);
            while (buffer.length >= 4) {
                const size = buffer.readUInt32LE(0);
                if (size === 0 || size > MAX_REQUEST) return socket.destroy();
                if (buffer.length < size + 4) return;
                const bytes = buffer.subarray(4, size + 4);
                buffer = buffer.subarray(size + 4);
                try {
                    const message = JSON.parse(bytes.toString('utf8'));
                    handle(slot, socket, message).catch(() => socket.destroy());
                } catch { socket.destroy(); return; }
            }
        });
        socket.on('close', () => {
            slot.sockets.delete(socket);
            for (const [id, pending] of slot.approvals) {
                if (pending.socket !== socket) continue;
                slot.approvals.delete(id);
                clearTimeout(pending.timer);
                pending.reject(error('CUA_CLIENT_DISCONNECTED'));
            }
            // A disconnected active request has an unknown outcome. Never replay it.
            if (socket.generation === slot.generation)
                release(slot, 'CUA_CLIENT_DISCONNECTED').catch(() => {});
        });
    }

    async function control(request) {
        if (!object(request)) throw error('CUA_INVALID_CONTROL');
        if (request.command === 'prepareResult') {
            const lease = acquisitions.get(request.requestId);
            if (!lease || !lease.preparing) return false;
            clearTimeout(lease.timer);
            lease.preparing = false;
            if (request.prepared !== true || lease.cancelled || !alive(lease.slot, lease.generation)) {
                acquisitions.delete(lease.id);
                lease.acquiring = false;
                lease.reject(error('CUA_PREPARATION_FAILED'));
            } else requestDesktopLease(lease);
            return true;
        }
        if (request.command === 'acquireResult') {
            const lease = acquisitions.get(request.requestId);
            if (!lease || lease.preparing) return false;
            acquisitions.delete(lease.id);
            clearTimeout(lease.timer);
            lease.acquiring = false;
            if (lease.cancelled || !alive(lease.slot, lease.generation)) {
                if (request.granted === true) released(lease);
                lease.reject(error('CUA_TURN_ENDED'));
            } else if (request.granted === true) lease.resolve();
            else lease.reject(error('CUA_DESKTOP_BUSY'));
            return true;
        }
        if (request.command === 'shutdown') return shutdown();
        if (stopping || fatal) throw error('CUA_HOST_STOPPED');
        if (request.command === 'allocate') {
            if (typeof request.helperHome !== 'string' || !path.isAbsolute(request.helperHome) ||
                request.helperHome.includes('\0')) throw error('CUA_INVALID_HELPER_HOME');
            if (request.pipePath !== undefined && (typeof request.pipePath !== 'string' ||
                !OWNED_PIPE.test(request.pipePath))) throw error('CUA_INVALID_PIPE');
            const id = randomUUID();
            // Only the private parent channel may restore a formerly owned address, after
            // terminating its previous Job. NativePipeTransport reconnects without replay.
            const pipePath = request.pipePath ?? `\\\\.\\pipe\\mycodex-cua-${id}`;
            const slot = { id, pipePath, helperHome: request.helperHome, published: false,
                thread: null, turn: null, generation: 0, approvals: new Map(),
                sockets: new Set(), unpublishedSockets: new Set(), transport: null, closing: null, busy: false };
            slot.server = net.createServer(socket => connect(slot, socket));
            await new Promise((resolve, reject) => {
                slot.server.once('error', reject);
                slot.server.listen(pipePath, resolve);
            });
            slot.server.on('error', () => failHost('CUA_PIPE_FAILED'));
            slots.set(id, slot);
            return { slot: id, pipePath };
        }
        const slot = slots.get(request.slot);
        if (!slot) throw error('CUA_UNKNOWN_SLOT');
        switch (request.command) {
            case 'publish':
                // Drain already accepted security-only connections before opening the gate.
                await new Promise(resolve => setImmediate(resolve));
                if (stopping || fatal || !slots.has(slot.id)) throw error('CUA_HOST_STOPPED');
                for (const socket of slot.unpublishedSockets) socket.destroy();
                slot.published = true;
                return true;
            case 'bind':
                if (slot.thread || !identity(request.thread)) throw error('CUA_INVALID_BINDING');
                slot.thread = request.thread;
                return true;
            case 'begin':
                if (!slot.thread || !slot.published || slot.turn || slot.closing || slot.busy || !identity(request.turn))
                    throw error('CUA_INVALID_TURN');
                slot.turn = request.turn;
                return ++slot.generation;
            case 'end':
                if (request.turn !== slot.turn || slot.turn === null) return false;
                await release(slot, 'CUA_TURN_ENDED');
                return true;
            case 'remove':
                await release(slot, 'CUA_SLOT_REMOVED');
                slots.delete(slot.id);
                await new Promise(resolve => slot.server.close(resolve));
                return true;
            default:
                throw error('CUA_INVALID_CONTROL');
        }
    }

    function shutdown() {
        if (shutdownPromise) return shutdownPromise;
        stopping = true;
        shutdownPromise = (async () => {
            await Promise.all([...slots.values()].map(async slot => {
                await release(slot, 'CUA_HOST_STOPPED');
                await new Promise(resolve => slot.server.close(resolve));
            }));
            for (const lease of acquisitions.values()) clearTimeout(lease.timer);
            acquisitions.clear();
            slots.clear();
            return true;
        })();
        return shutdownPromise;
    }

    return { control, shutdown };
}
