// Synthetic parent/native control with real framed authorization pipes and signatures.
// No browser, desktop actions, profiles, or user policy changes.
// Run on Windows: node tools/diagnostics/Test-BrowserUseControl.mjs
import assert from 'node:assert/strict';
import net from 'node:net';
import { randomUUID, randomBytes, createPublicKey, verify } from 'node:crypto';
import { EventEmitter, once } from 'node:events';
import { test } from 'node:test';
import { createBrowserUseControl } from '../../src/MyCodex.Infrastructure/ComputerUse/Host/browser-use-control.mjs';

assert.equal(process.platform, 'win32', 'These tests require Windows named pipes.');
const PIPE_PREFIX = String.raw`\\.\pipe\mycodex-browser-access-`;

function framed(value) {
    const bytes = Buffer.from(JSON.stringify(value));
    const header = Buffer.alloc(4);
    header.writeUInt32LE(bytes.length);
    return Buffer.concat([header, bytes]);
}

async function request(access, overrides = {}) {
    const params = { executorId: access.executorId, sessionId: 'thread-1', turnId: 'turn-1',
        nonce: randomBytes(32).toString('hex'), ...overrides };
    const socket = net.createConnection(access.pipePath);
    socket.on('error', () => {});
    await once(socket, 'connect');
    return new Promise((resolve, reject) => {
        let buffer = Buffer.alloc(0);
        const timer = setTimeout(() => { socket.destroy(); reject(new Error('Bridge response timeout')); }, 2500);
        socket.on('close', () => { clearTimeout(timer); resolve({ disconnected: true }); });
        socket.on('data', chunk => {
            buffer = Buffer.concat([buffer, chunk]);
            if (buffer.length < 4 || buffer.length < buffer.readUInt32LE() + 4) return;
            clearTimeout(timer);
            socket.destroy();
            try {
                const response = JSON.parse(buffer.subarray(4).toString('utf8'));
                assert.equal(response.id, 'browser-policy');
                assert.equal(response.jsonrpc, '2.0');
                const { payload, signature } = response.result;
                const key = createPublicKey({ key: Buffer.from(access.verifyingIdentity, 'base64'), type: 'spki', format: 'der' });
                assert.equal(verify(null, Buffer.from(payload), key, Buffer.from(signature, 'base64')), true);
                const decision = JSON.parse(payload);
                assert.equal(payload, JSON.stringify({ domain: 'codex.browserAccess.v1', request: params, allowed: decision.allowed }));
                resolve({ allowed: decision.allowed });
            } catch (error) { reject(error); }
        });
        socket.write(framed({ jsonrpc: '2.0', id: 'browser-policy', method: 'browser/access', params }));
    });
}

function fixture(t) {
    const events = [];
    const calls = [];
    const observed = new EventEmitter();
    const live = new Set();
    let nativeHook;
    const bridge = createBrowserUseControl({
        native: { async control(message) {
            calls.push(message);
            await nativeHook?.(message);
            if (message.command === 'allocate') {
                const slot = randomUUID(); live.add(slot); return { slot };
            }
            if (message.command === 'shutdown') { live.clear(); return null; }
            if (!live.has(message.slot)) throw new Error('Unknown native slot');
            if (message.command === 'remove') live.delete(message.slot);
            return null;
        } },
        emit(event) { events.push(event); observed.emit(event.event, event); }
    });
    t.after(() => bridge.shutdown());
    return { bridge, events, calls, observed, setNativeHook: hook => { nativeHook = hook; } };
}

async function allocate(bridge, { publish = true, bind = true } = {}) {
    const { slot } = await bridge.control({ command: 'allocate' });
    const access = await bridge.control({ command: 'allocateBrowser', slot });
    assert.equal(access.pipePath.slice(0, PIPE_PREFIX.length), PIPE_PREFIX);
    assert.deepEqual([...access.pipePath.slice(0, 9)].map(character => character.charCodeAt(0)), [92, 92, 46, 92, 112, 105, 112, 101, 92]);
    if (publish) await bridge.control({ command: 'publishBrowser', slot });
    if (bind) {
        await bridge.control({ command: 'bind', slot, thread: 'thread-1' });
        await bridge.control({ command: 'begin', slot, turn: 'turn-1' });
    }
    return { slot, access };
}

test('real signed access waits for parent preparation and reports exact live context', async t => {
    const { bridge, observed, calls, events } = fixture(t);
    const { access } = await allocate(bridge);
    const notification = once(observed, 'browserPrepare');
    const response = request(access);
    const [event] = await notification;
    assert.deepEqual(Object.keys(event).sort(), ['event', 'executorId', 'id', 'thread', 'turn']);
    assert.equal(event.executorId, access.executorId);
    assert.equal(event.thread, 'thread-1');
    assert.equal(event.turn, 'turn-1');
    await bridge.control({ command: 'browserPrepareResult', requestId: event.id, prepared: true });
    assert.deepEqual(await response, { allowed: true });
    assert.equal(events.length, 1);
    assert.deepEqual(calls.map(call => call.command), ['allocate', 'bind', 'begin']);
});

test('the host cannot authorize before explicit ACL publication or for a foreign context', async t => {
    const { bridge, observed, events } = fixture(t);
    const { slot, access } = await allocate(bridge, { publish: false });
    assert.deepEqual(await request(access), { allowed: false });
    assert.equal(events.length, 0);
    await bridge.control({ command: 'publishBrowser', slot });
    for (const overrides of [{ sessionId: 'foreign' }, { turnId: 'foreign' }, { executorId: randomUUID() }])
        assert.deepEqual(await request(access, overrides), { allowed: false });
    assert.equal(events.length, 0);
    const eventPromise = once(observed, 'browserPrepare');
    const response = request(access);
    const [event] = await eventPromise;
    await bridge.control({ command: 'browserPrepareResult', requestId: event.id, prepared: false });
    assert.deepEqual(await response, { allowed: false });
});

test('unknown, duplicate and nonboolean approval results cannot approve another request', async t => {
    const { bridge, observed } = fixture(t);
    const { access } = await allocate(bridge);
    let eventPromise = once(observed, 'browserPrepare');
    let response = request(access);
    let [event] = await eventPromise;
    await bridge.control({ command: 'browserPrepareResult', requestId: randomUUID(), prepared: true });
    await bridge.control({ command: 'browserPrepareResult', requestId: event.id, prepared: 'true' });
    assert.deepEqual(await response, { allowed: false });
    eventPromise = once(observed, 'browserPrepare');
    response = request(access);
    const [next] = await eventPromise;
    await bridge.control({ command: 'browserPrepareResult', requestId: event.id, prepared: true });
    await bridge.control({ command: 'browserPrepareResult', requestId: next.id, prepared: false });
    assert.deepEqual(await response, { allowed: false });
});

test('end revokes pending authorization before native cleanup and ignores a late approval', async t => {
    const { bridge, observed, setNativeHook } = fixture(t);
    const { slot, access } = await allocate(bridge);
    const notification = once(observed, 'browserPrepare');
    const response = request(access);
    const [event] = await notification;
    setNativeHook(async message => { if (message.command === 'end') throw new Error('Synthetic native end failure'); });
    await assert.rejects(bridge.control({ command: 'end', slot, turn: 'turn-1' }));
    await bridge.control({ command: 'browserPrepareResult', requestId: event.id, prepared: true });
    assert.deepEqual(await response, { disconnected: true });
    assert.deepEqual(await request(access), { allowed: false });
});

for (const transition of ['bind', 'begin']) test(`${transition} revokes the previous authorization before awaiting native transition`, async t => {
    const { bridge, observed, setNativeHook } = fixture(t);
    const { slot, access } = await allocate(bridge);
    const notification = once(observed, 'browserPrepare');
    const response = request(access);
    const [event] = await notification;
    let entered;
    const enteredNative = new Promise(resolve => { entered = resolve; });
    let release;
    const nativeWait = new Promise(resolve => { release = resolve; });
    setNativeHook(async message => {
        if (message.command === transition) { entered(); await nativeWait; }
    });
    const changing = bridge.control({ command: transition, slot, thread: 'thread-2', turn: 'turn-2' });
    await enteredNative;
    try {
        await bridge.control({ command: 'browserPrepareResult', requestId: event.id, prepared: true });
        assert.deepEqual(await response, { disconnected: true });
    } finally { release(); await changing; }
});

test('rejects foreign native slots and concurrent browser allocations for one slot', async t => {
    const { bridge } = fixture(t);
    const foreign = randomUUID();
    await assert.rejects(bridge.control({ command: 'allocateBrowser', slot: foreign }));
    await assert.rejects(bridge.control({ command: 'publishBrowser', slot: foreign }));
    const { slot } = await bridge.control({ command: 'allocate' });
    const results = await Promise.allSettled([
        bridge.control({ command: 'allocateBrowser', slot }), bridge.control({ command: 'allocateBrowser', slot })
    ]);
    assert.equal(results.filter(result => result.status === 'fulfilled').length, 1);
    assert.equal(results.filter(result => result.status === 'rejected').length, 1);
});

test('end during a pending native begin prevents late browser reactivation', async t => {
    const { bridge, events, setNativeHook } = fixture(t);
    const { slot, access } = await allocate(bridge);
    let entered;
    const enteredNative = new Promise(resolve => { entered = resolve; });
    let release;
    const waiting = new Promise(resolve => { release = resolve; });
    setNativeHook(async message => { if (message.command === 'begin') { entered(); await waiting; } });
    const beginning = bridge.control({ command: 'begin', slot, turn: 'turn-2' });
    await enteredNative;
    await bridge.control({ command: 'end', slot, turn: 'turn-2' });
    release();
    await beginning;
    assert.deepEqual(await request(access, { turnId: 'turn-2' }), { allowed: false });
    assert.equal(events.length, 0);
});

test('cancelling browser allocation closes its late pipe without reviving the binding', async t => {
    const { bridge } = fixture(t);
    const { slot } = await bridge.control({ command: 'allocate' });
    const allocating = bridge.control({ command: 'allocateBrowser', slot });
    const rejected = assert.rejects(allocating);
    await bridge.control({ command: 'removeBrowser', slot });
    await rejected;
    await assert.rejects(bridge.control({ command: 'publishBrowser', slot }));
    const replacement = await bridge.control({ command: 'allocateBrowser', slot });
    assert.ok(replacement.executorId);
});

test('remove and shutdown revoke live policy requests and close every authorization pipe', async t => {
    const { bridge, observed } = fixture(t);
    const { slot, access } = await allocate(bridge);
    const notification = once(observed, 'browserPrepare');
    const response = request(access);
    const [event] = await notification;
    await bridge.control({ command: 'remove', slot });
    assert.deepEqual(await response, { disconnected: true });
    await bridge.control({ command: 'browserPrepareResult', requestId: event.id, prepared: true });
    await assert.rejects(request(access));
    const second = await allocate(bridge);
    await bridge.control({ command: 'shutdown' });
    await assert.rejects(request(second.access));
    await assert.rejects(bridge.control({ command: 'allocate' }));
    await bridge.shutdown();
});
