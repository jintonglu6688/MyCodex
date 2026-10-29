// Local named-pipe/crypto tests only; no browser, desktop, profiles, or real grants.
// Run on Windows: node tools/diagnostics/Test-BrowserAccessHost.mjs
import assert from 'node:assert/strict';
import net from 'node:net';
import { randomBytes, createPublicKey, verify } from 'node:crypto';
import { once } from 'node:events';
import { test } from 'node:test';
import { createBrowserAccessHost } from '../../src/MyCodex.Infrastructure/ComputerUse/Host/browser-access-host.mjs';

assert.equal(process.platform, 'win32', 'These tests require Windows named pipes.');
const PIPE_PREFIX = String.raw`\\.\pipe\mycodex-browser-access-`;
const nonce = () => randomBytes(32).toString('hex');
const frame = value => {
    const bytes = Buffer.from(JSON.stringify(value));
    const header = Buffer.alloc(4);
    header.writeUInt32LE(bytes.length);
    return Buffer.concat([header, bytes]);
};
const params = (slot, overrides = {}) => ({ executorId: slot.executorId, sessionId: 'thread-1', turnId: 'turn-1', nonce: nonce(), ...overrides });

async function connect(slot) {
    const socket = net.createConnection(slot.pipePath);
    socket.on('error', () => {});
    await once(socket, 'connect');
    return socket;
}

async function exchange(slot, message, fragmented = false) {
    const socket = await connect(slot);
    return new Promise((resolve, reject) => {
        let data = Buffer.alloc(0);
        const timer = setTimeout(() => { socket.destroy(); reject(new Error('Test response timeout')); }, 3000);
        socket.on('data', chunk => {
            data = Buffer.concat([data, chunk]);
            if (data.length >= 4 && data.length >= 4 + data.readUInt32LE()) {
                clearTimeout(timer);
                socket.destroy();
                resolve(JSON.parse(data.subarray(4, 4 + data.readUInt32LE()).toString('utf8')));
            }
        });
        socket.on('close', () => { clearTimeout(timer); resolve({ disconnected: true }); });
        const bytes = frame(message);
        if (fragmented) {
            socket.write(bytes.subarray(0, 2));
            setImmediate(() => socket.write(bytes.subarray(2, 9)));
            setImmediate(() => socket.write(bytes.subarray(9)));
        } else socket.write(bytes);
    });
}

async function request(slot, value, fragmented = false) {
    const response = await exchange(slot, { jsonrpc: '2.0', id: 7, method: 'browser/access', params: value }, fragmented);
    if (response.disconnected) return response;
    assert.equal(response.id, 7);
    assert.equal(response.jsonrpc, '2.0');
    return response;
}

function allowed(slot, response, value) {
    assert.ok(response.result, JSON.stringify(response));
    const key = createPublicKey({ key: Buffer.from(slot.verifyingIdentity, 'base64'), type: 'spki', format: 'der' });
    assert.equal(key.asymmetricKeyType, 'ed25519');
    assert.equal(verify(null, Buffer.from(response.result.payload), key, Buffer.from(response.result.signature, 'base64')), true);
    const payload = JSON.parse(response.result.payload);
    assert.equal(response.result.payload, JSON.stringify({ domain: 'codex.browserAccess.v1', request: value, allowed: payload.allowed }));
    return payload.allowed;
}

async function fixture(t, authorize, timeoutMs = 1000) {
    const host = createBrowserAccessHost({ authorize, timeoutMs });
    t.after(() => host.shutdown());
    const slot = await host.allocate();
    host.publish(slot.executorId);
    host.bind(slot.executorId, 'thread-1');
    host.begin(slot.executorId, 'turn-1');
    return { host, slot };
}

test('publishes an isolated authenticated pipe and preserves exact signed request bytes', async t => {
    let calls = 0;
    const { slot } = await fixture(t, async () => { calls++; return true; });
    assert.match(slot.executorId, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    assert.equal(slot.pipePath.slice(0, PIPE_PREFIX.length), PIPE_PREFIX);
    assert.deepEqual([...slot.pipePath.slice(0, 9)].map(character => character.charCodeAt(0)), [92, 92, 46, 92, 112, 105, 112, 101, 92]);
    const value = params(slot);
    assert.equal(allowed(slot, await request(slot, value, true), value), true);
    const reordered = { nonce: nonce(), turnId: 'turn-1', sessionId: 'thread-1', executorId: slot.executorId };
    assert.equal(allowed(slot, await request(slot, reordered), reordered), true);
    assert.equal(calls, 2);
    const key = createPublicKey({ key: Buffer.from(slot.verifyingIdentity, 'base64'), type: 'spki', format: 'der' });
    assert.equal(verify(null, Buffer.from('altered payload'), key, Buffer.alloc(64)), false);
});

test('denies before publication, absent/foreign bindings and nonce replay', async t => {
    let calls = 0;
    const host = createBrowserAccessHost({ authorize: async () => { calls++; return true; } });
    t.after(() => host.shutdown());
    const slot = await host.allocate();
    let value = params(slot);
    assert.equal(allowed(slot, await request(slot, value), value), false);
    host.publish(slot.executorId);
    value = params(slot);
    assert.equal(allowed(slot, await request(slot, value), value), false);
    host.bind(slot.executorId, 'thread-1');
    host.begin(slot.executorId, 'turn-1');
    for (const override of [{ sessionId: 'foreign' }, { turnId: 'foreign' }, { executorId: 'foreign' }]) {
        value = params(slot, override);
        assert.equal(allowed(slot, await request(slot, value), value), false);
    }
    value = params(slot);
    assert.equal(allowed(slot, await request(slot, value), value), true);
    assert.equal(allowed(slot, await request(slot, value), value), false);
    assert.equal(calls, 1);
});

test('rejects invalid request shapes, remote pipes, oversized and malformed frames', async t => {
    let calls = 0;
    const { host, slot } = await fixture(t, async () => { calls++; return true; });
    await assert.rejects(host.allocate({ pipePath: '\\\\remote\\pipe\\mycodex-browser-access-test' }));
    await assert.rejects(host.allocate({ pipePath: slot.pipePath.replaceAll('\\', '\\\\') }));
    for (const value of [params(slot, { nonce: 'bad' }), { ...params(slot), extra: true }, params(slot, { turnId: '' })]) {
        assert.equal((await request(slot, value)).error.code, -32602);
    }
    assert.equal((await exchange(slot, { jsonrpc: '2.0', id: {}, method: 'browser/access', params: params(slot) })).disconnected, true);
    const socket = await connect(slot);
    const closed = once(socket, 'close');
    const header = Buffer.alloc(4);
    header.writeUInt32LE(65537);
    socket.write(header);
    await closed;
    const malformed = await connect(slot);
    const malformedClosed = once(malformed, 'close');
    malformed.end(Buffer.from([1, 0, 0, 0, 123]));
    await malformedClosed;
    assert.equal(calls, 0);
});

test('denies false, absent authorizers, exceptions and timeouts without leaking details', async t => {
    for (const authorize of [undefined, async () => false, async () => ({ allowed: true }), async () => { throw new Error('private detail'); }, () => new Promise(() => {})]) {
        const { slot } = await fixture(t, authorize, 30);
        const value = params(slot);
        const response = await request(slot, value);
        assert.equal(allowed(slot, response, value), false);
        assert.equal(JSON.stringify(response).includes('private detail'), false);
    }
});

test('end cancels pending authorization and a late decision cannot authorize a replacement turn', async t => {
    let release;
    let started;
    let observedSignal;
    const entered = new Promise(resolve => { started = resolve; });
    const { host, slot } = await fixture(t, (value, context) => {
        observedSignal = context.signal;
        started();
        return new Promise(resolve => { release = resolve; });
    });
    const response = request(slot, params(slot));
    await entered;
    host.end(slot.executorId, 'wrong-turn');
    assert.equal(observedSignal.aborted, false);
    host.end(slot.executorId, 'turn-1');
    assert.equal(observedSignal.aborted, true);
    host.begin(slot.executorId, 'turn-2');
    release(true);
    assert.equal((await response).disconnected, true);
    const old = params(slot);
    assert.equal(allowed(slot, await request(slot, old), old), false);
});

test('session replacement and removal revoke pending requests and close the pipe', async t => {
    let started;
    const entered = new Promise(resolve => { started = resolve; });
    const { host, slot } = await fixture(t, async () => { started(); return new Promise(() => {}); });
    const pending = request(slot, params(slot));
    await entered;
    host.bind(slot.executorId, 'thread-2');
    assert.equal((await pending).disconnected, true);
    await host.remove(slot.executorId);
    await assert.rejects(connect(slot));
    await host.remove(slot.executorId);
    await host.shutdown();
    await host.shutdown();
    await assert.rejects(host.allocate());
});

test('simultaneous slots cannot authorize through each other and consume nonces separately', async t => {
    const { host, slot } = await fixture(t, async () => true);
    const second = await host.allocate();
    host.publish(second.executorId);
    host.bind(second.executorId, 'thread-1');
    host.begin(second.executorId, 'turn-1');
    const firstValue = params(slot);
    const secondValue = params(second, { nonce: firstValue.nonce });
    assert.equal(allowed(slot, await request(slot, secondValue), secondValue), false);
    const [firstResponse, secondResponse] = await Promise.all([request(slot, firstValue), request(second, secondValue)]);
    assert.equal(allowed(slot, firstResponse, firstValue), true);
    assert.equal(allowed(second, secondResponse, secondValue), true);
});

test('nonce budget fails closed until the next turn instead of evicting replay history', async t => {
    let calls = 0;
    const { host, slot } = await fixture(t, async () => { calls++; return true; });
    const first = params(slot);
    assert.equal(allowed(slot, await request(slot, first), first), true);
    for (let index = 1; index < 1024; index++) {
        const value = params(slot);
        assert.equal(allowed(slot, await request(slot, value), value), true);
    }
    const exhausted = params(slot);
    assert.equal(allowed(slot, await request(slot, exhausted), exhausted), false);
    assert.equal(allowed(slot, await request(slot, first), first), false);
    assert.equal(calls, 1024);
    host.begin(slot.executorId, 'turn-2');
    const next = params(slot, { turnId: 'turn-2' });
    assert.equal(allowed(slot, await request(slot, next), next), true);
});
