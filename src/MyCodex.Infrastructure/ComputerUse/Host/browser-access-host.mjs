import net from 'node:net';
import { generateKeyPairSync, randomUUID, sign } from 'node:crypto';

const UUID = '[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}';
const OWNED_PIPE = new RegExp(`^\\\\\\\\\\.\\\\pipe\\\\mycodex-browser-access-${UUID}$(?![\\s\\S])`);
const MAX_FRAME = 64 * 1024;
const MAX_SOCKETS = 64;
const MAX_NONCES = 1024;
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const identity = value => typeof value === 'string' && /^[A-Za-z0-9._:-]{1,200}$(?![\s\S])/.test(value);
const requestId = value => Number.isSafeInteger(value) ||
    (typeof value === 'string' && value.length > 0 && value.length <= 200 && !/[\u0000-\u001f]/.test(value));
const exactKeys = (value, keys) => object(value) && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const failure = () => new Error('BROWSER_ACCESS_INVALID_CONTEXT');

// The owner must restrict the pipe ACL before publish and supply the current effective
// browser policy through authorize. This host never stores grants or executes browsers.
export function createBrowserAccessHost({ authorize, timeoutMs = 10000 } = {}) {
    if (!Number.isSafeInteger(timeoutMs) || timeoutMs < 1 || timeoutMs > 120000) throw failure();
    const { privateKey, publicKey } = generateKeyPairSync('ed25519');
    const verifyingIdentity = publicKey.export({ type: 'spki', format: 'der' }).toString('base64');
    const slots = new Map();
    let stopping = false;

    function slotFor(executorId) {
        const slot = slots.get(executorId);
        if (stopping || !slot || slot.closed) throw failure();
        return slot;
    }

    function invalidate(slot) {
        slot.generation++;
        slot.turn = null;
        slot.nonces.clear();
        for (const controller of slot.pending) controller.abort();
        for (const socket of slot.sockets) socket.destroy();
    }

    function validContext(slot, params, generation, publishedAtConnect) {
        return !stopping && !slot.closed && slot.published && publishedAtConnect &&
            slot.generation === generation && params.executorId === slot.executorId &&
            params.sessionId === slot.thread && params.turnId === slot.turn;
    }

    async function decide(slot, params, generation, publishedAtConnect, signal) {
        if (!validContext(slot, params, generation, publishedAtConnect) ||
            slot.nonces.has(params.nonce) || slot.nonces.size >= MAX_NONCES || typeof authorize !== 'function') return false;
        slot.nonces.add(params.nonce);
        const controller = new AbortController();
        slot.pending.add(controller);
        const abort = () => controller.abort();
        signal.addEventListener('abort', abort, { once: true });
        if (signal.aborted) controller.abort();
        let timer;
        try {
            const denied = new Promise(resolve => {
                controller.signal.addEventListener('abort', () => resolve(false), { once: true });
                if (controller.signal.aborted) resolve(false);
                timer = setTimeout(() => controller.abort(), timeoutMs);
            });
            const decision = await Promise.race([
                Promise.resolve().then(() => controller.signal.aborted ? false : authorize(params, { signal: controller.signal })).catch(() => false),
                denied
            ]);
            return decision === true && !controller.signal.aborted &&
                validContext(slot, params, generation, publishedAtConnect);
        } finally {
            clearTimeout(timer);
            signal.removeEventListener('abort', abort);
            slot.pending.delete(controller);
        }
    }

    function respond(socket, response) {
        if (socket.destroyed || !socket.writable) return;
        const bytes = Buffer.from(JSON.stringify(response));
        const header = Buffer.alloc(4);
        header.writeUInt32LE(bytes.length);
        socket.end(Buffer.concat([header, bytes]));
    }

    function serve(slot, socket) {
        if (stopping || slot.closed || slot.sockets.size >= MAX_SOCKETS) { socket.destroy(); return; }
        const publishedAtConnect = slot.published;
        slot.sockets.add(socket);
        const lifetime = new AbortController();
        let buffer = Buffer.alloc(0);
        let started = false;
        socket.on('error', () => {});
        socket.setTimeout(timeoutMs, () => socket.destroy());
        socket.on('close', () => { lifetime.abort(); slot.sockets.delete(socket); });
        socket.on('data', chunk => {
            if (started || buffer.length + chunk.length > MAX_FRAME + 4) { socket.destroy(); return; }
            buffer = Buffer.concat([buffer, chunk]);
            if (buffer.length < 4) return;
            const size = buffer.readUInt32LE();
            if (size === 0 || size > MAX_FRAME || buffer.length > size + 4) { socket.destroy(); return; }
            if (buffer.length < size + 4) return;
            started = true;
            socket.setTimeout(0);
            let message;
            try { message = JSON.parse(buffer.subarray(4).toString('utf8')); }
            catch { socket.destroy(); return; }
            buffer = null;
            if (!object(message) || !requestId(message.id)) { socket.destroy(); return; }
            const params = message.params;
            if (!exactKeys(message, ['jsonrpc', 'id', 'method', 'params']) ||
                message.jsonrpc !== '2.0' || message.method !== 'browser/access' ||
                !exactKeys(params, ['executorId', 'sessionId', 'turnId', 'nonce']) ||
                !identity(params.executorId) || !identity(params.sessionId) || !identity(params.turnId) ||
                typeof params.nonce !== 'string' || !/^[0-9a-f]{64}$(?![\s\S])/.test(params.nonce)) {
                respond(socket, { jsonrpc: '2.0', id: message.id, error: { code: -32602, message: 'Invalid browser access request' } });
                return;
            }
            Object.freeze(params);
            const generation = slot.generation;
            void decide(slot, params, generation, publishedAtConnect, lifetime.signal).then(allowed => {
                if (lifetime.signal.aborted || socket.destroyed) return;
                const payload = JSON.stringify({ domain: 'codex.browserAccess.v1', request: params, allowed });
                respond(socket, { jsonrpc: '2.0', id: message.id, result: {
                    payload, signature: sign(null, Buffer.from(payload), privateKey).toString('base64')
                } });
            }).catch(() => socket.destroy());
        });
    }

    async function allocate({ pipePath } = {}) {
        if (stopping || slots.size >= 64) throw failure();
        const executorId = randomUUID();
        pipePath ??= `\\\\.\\pipe\\mycodex-browser-access-${randomUUID()}`;
        if (typeof pipePath !== 'string' || !OWNED_PIPE.test(pipePath)) throw failure();
        if ([...slots.values()].some(slot => slot.pipePath === pipePath)) throw failure();
        const slot = { executorId, pipePath, published: false, closed: false, generation: 0,
            thread: null, turn: null, nonces: new Set(), sockets: new Set(), pending: new Set() };
        slot.server = net.createServer(socket => serve(slot, socket));
        slots.set(executorId, slot);
        try {
            await new Promise((resolve, reject) => {
                const failed = error => reject(error);
                slot.server.once('error', failed);
                slot.server.listen(pipePath, () => { slot.server.removeListener('error', failed); resolve(); });
            });
            slot.server.on('error', () => { slot.closed = true; invalidate(slot); });
            if (stopping || slot.closed) throw failure();
            return { executorId, pipePath, verifyingIdentity };
        } catch (error) {
            slots.delete(executorId);
            slot.closed = true;
            invalidate(slot);
            if (slot.server.listening) await new Promise(resolve => slot.server.close(resolve));
            throw error;
        }
    }

    async function remove(executorId) {
        const slot = slots.get(executorId);
        if (!slot) return;
        slots.delete(executorId);
        slot.closed = true;
        invalidate(slot);
        if (slot.server.listening) await new Promise(resolve => slot.server.close(resolve));
    }

    return {
        allocate,
        publish(executorId) { slotFor(executorId).published = true; },
        bind(executorId, thread) {
            if (!identity(thread)) throw failure();
            const slot = slotFor(executorId);
            if (slot.thread !== thread) { invalidate(slot); slot.thread = thread; }
        },
        begin(executorId, turn) {
            if (!identity(turn)) throw failure();
            const slot = slotFor(executorId);
            if (!slot.thread) throw failure();
            if (slot.turn !== turn) { invalidate(slot); slot.turn = turn; }
        },
        end(executorId, turn) {
            const slot = slots.get(executorId);
            if (slot && slot.turn === turn) invalidate(slot);
        },
        remove,
        async shutdown() {
            stopping = true;
            await Promise.all([...slots.keys()].map(remove));
        }
    };
}
