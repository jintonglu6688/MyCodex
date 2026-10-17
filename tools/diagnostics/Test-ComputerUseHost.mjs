// Offline executor substitute: no official component, desktop access, network or grants.
// Run on Windows: node tools/diagnostics/Test-ComputerUseHost.mjs
import assert from 'node:assert/strict';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { createComputerUseHost } from '../../src/MyCodex.Infrastructure/ComputerUse/Host/computer-use-host.mjs';

assert.equal(process.platform, 'win32', 'These are Windows named-pipe integration tests.');
const events = [];
const calls = [];
const transports = [];
let grant = true;
let deferAcquire = false;
let host;
let assertions = 0;
const check = (value, message) => { assert.ok(value, message); assertions++; };
host = createComputerUseHost({
    emit: event => {
        events.push(event);
        if (event.event === 'acquire' && !deferAcquire)
            void host.control({ command: 'acquireResult', requestId: event.id, granted: grant });
    },
    createTransport: helperHome => {
        const transport = {
            closed: false,
            pending: null,
            async request(method, params, context) {
                calls.push({ method, params, metadata: context.codexTurnMetadata, helperHome });
                let approved;
                if (params.approval) {
                    const choice = await context.createElicitation({
                        message: 'Allow fixture?', meta: { connector_id: 'computer-use', persist: ['session', 'always'] }
                    });
                    if (choice.action !== 'accept') throw new Error('private payload must not escape');
                    check(!this.closed, 'An approval cannot execute after close');
                    approved = choice;
                }
                if (params.hang) return new Promise((_, reject) => { this.pending = reject; });
                if (params.deferredError) return new Promise((_, reject) => { this.rejectLate = reject; });
                if (params.helperError) throw Object.assign(new Error(params.helperError), {
                    name: params.errorName ?? 'Error', code: params.errorCode
                });
                if (params.failure) throw new Error('secret input or helper stderr');
                return approved ? { approved } : { method };
            },
            async close() {
                this.closed = true;
                this.pending?.(new Error('closed'));
            }
        };
        transports.push(transport);
        return transport;
    }
});

function peer(pipePath) {
    const socket = net.createConnection(pipePath);
    const messages = [];
    const waiters = new Map();
    let buffer = Buffer.alloc(0);
    let next = 0;
    socket.on('error', () => {});
    socket.on('data', chunk => {
        buffer = Buffer.concat([buffer, chunk]);
        while (buffer.length >= 4 && buffer.length >= buffer.readUInt32LE(0) + 4) {
            const size = buffer.readUInt32LE(0);
            const message = JSON.parse(buffer.subarray(4, size + 4));
            buffer = buffer.subarray(size + 4);
            messages.push(message);
            if (message.method) continue;
            waiters.get(message.id)?.(message);
            waiters.delete(message.id);
        }
    });
    socket.on('close', () => {
        for (const resolve of waiters.values()) resolve({ disconnected: true });
        waiters.clear();
    });
    const send = message => {
        const bytes = Buffer.from(JSON.stringify(message));
        const header = Buffer.alloc(4);
        header.writeUInt32LE(bytes.length);
        socket.write(Buffer.concat([header, bytes]));
    };
    return { socket, messages, send,
        async ready() {
            if (socket.connecting) await new Promise((resolve, reject) => {
                socket.once('connect', resolve); socket.once('error', reject);
            });
        },
        request(slot, method = 'list_windows', params = {}, metadata = null) {
            const id = ++next;
            const result = new Promise(resolve => waiters.set(id, resolve));
            send({ id, jsonrpc: '2.0', method: 'request', params: { method, params,
                codexTurnMetadata: metadata ?? { session_id: slot.thread, turn_id: slot.turn } } });
            return result;
        }
    };
}

async function until(test, message) {
    const deadline = Date.now() + 3000;
    while (!test()) {
        if (Date.now() > deadline) throw new Error(message);
        await new Promise(resolve => setTimeout(resolve, 5));
    }
}
async function slot(name) {
    const result = await host.control({ command: 'allocate', helperHome: os.tmpdir() });
    await host.control({ command: 'publish', slot: result.slot });
    await host.control({ command: 'bind', slot: result.slot, thread: name });
    await host.control({ command: 'begin', slot: result.slot, turn: `${name}-turn` });
    return { ...result, thread: name, turn: `${name}-turn` };
}
const end = value => host.control({ command: 'end', slot: value.slot, turn: value.turn });

try {
    const unpublished = await host.control({ command: 'allocate', helperHome: os.tmpdir() });
    const closed = peer(unpublished.pipePath);
    await closed.ready();
    const prePublish = closed.request({ thread: 'x', turn: 'y' });
    await host.control({ command: 'publish', slot: unpublished.slot });
    check((await prePublish).disconnected, 'Pre-ACL connections are discarded, never promoted');
    check(calls.length === 0, 'No executor before publish');
    await host.control({ command: 'remove', slot: unpublished.slot });

    const a = await slot('thread-a');
    const b = await slot('thread-b');
    const pa = peer(a.pipePath); const pb = peer(b.pipePath);
    await Promise.all([pa.ready(), pb.ready()]);
    check(events.length === 0, 'Allocating sessions must not occupy desktop');
    const child = await pa.request(a, 'list_windows', {}, { session_id: 'child', turn_id: a.turn });
    check(child.error.message === 'CUA_TURN_NOT_ACTIVE', 'Unregistered child must be denied');
    check(calls.length === 0, 'Unregistered child cannot reach executor');
    check((await pa.request(a)).result.method === 'list_windows', 'Read path works');
    check((await pb.request(b)).error.message === 'CUA_DESKTOP_BUSY', 'Contention is immediate');
    await end(b);
    check((await pa.request(a, 'type_text', { text: '中文' })).result.method === 'type_text', 'Nonowner stop does not stop input');
    check(!(await host.control({ command: 'end', slot: a.slot, turn: 'old-turn' })), 'Stale stop ignored');
    const request = pa.request(a, 'get_window_state', { approval: true });
    await until(() => pa.messages.some(m => m.method === 'requestComputerUseApproval'), 'Expected approval');
    const approval = pa.messages.find(m => m.method === 'requestComputerUseApproval');
    const busy = await pa.request(a);
    check(busy.error.message === 'CUA_REQUEST_BUSY', 'No queue behind pending approval');
    const foreign = peer(a.pipePath); await foreign.ready();
    foreign.send({ jsonrpc: '2.0', id: approval.id, result: { action: 'accept' } });
    await new Promise(resolve => setImmediate(resolve));
    check(!pa.messages.some(m => m.result?.approved), 'Foreign connection cannot answer approval');
    foreign.socket.destroy();
    pa.send({ jsonrpc: '2.0', id: approval.id, result: { action: 'accept', _meta: { persist: 'session' } } });
    check((await request).result.approved._meta.persist === 'session', 'Grant scope passes unchanged');
    const pending = pa.request(a, 'get_window_state', { approval: true });
    await until(() => pa.messages.filter(m => m.method).length === 2, 'Expected second approval');
    await end(a);
    check((await pending).disconnected, 'Stop proactively cancels pending approvals');
    check(transports.every(t => t.closed), 'Stop closes owned executor');
    const released = events.filter(e => e.event === 'released');
    check(released.length === 1 && released[0].thread === a.thread, 'Only desktop owner releases lease');

    a.turn = 'next-turn';
    await host.control({ command: 'begin', slot: a.slot, turn: a.turn });
    const next = peer(a.pipePath); await next.ready();
    const old = await next.request(a, 'list_apps', {}, { session_id: a.thread, turn_id: 'thread-a-turn' });
    check(old.error.message === 'CUA_TURN_NOT_ACTIVE', 'Old turn cannot act after restart');
    const unknown = await next.request(a, 'arbitrary_method');
    check(unknown.error.message === 'CUA_UNSUPPORTED_REQUEST', 'Unknown methods do not reach executor');
    const response = await next.request(a, 'click', { failure: true });
    check(response.error?.message === 'CUA_EXECUTION_FAILED' || response.disconnected, 'No private error leaks');
    await until(() => transports.every(t => t.closed), 'Failure must close executor');
    check(!JSON.stringify(events).includes('secret'), 'Diagnostics omit input and stderr');

    b.turn = 'denied-turn';
    await host.control({ command: 'begin', slot: b.slot, turn: b.turn });
    const denied = peer(b.pipePath); await denied.ready();
    grant = false;
    check((await denied.request(b)).error.message === 'CUA_DESKTOP_BUSY', 'Cross-instance busy result honored');
    grant = true;
    const count = calls.length;
    deferAcquire = true;
    const acquiring = denied.request(b);
    await until(() => events.filter(e => e.event === 'acquire').length === 4, 'Expected deferred acquisition');
    const lease = events.filter(e => e.event === 'acquire').at(-1);
    await end(b);
    await host.control({ command: 'acquireResult', requestId: lease.id, granted: true });
    check((await acquiring).disconnected && calls.length === count, 'Cancelled acquisition cannot execute');
    check(events.some(e => e.event === 'released' && e.leaseId === lease.id), 'Late OS grant is released by exact lease');
    deferAcquire = false;

    b.turn = 'persistent-turn';
    await host.control({ command: 'begin', slot: b.slot, turn: b.turn });
    const persistent = peer(b.pipePath); await persistent.ready();
    const grantResult = persistent.request(b, 'click_element', { approval: true });
    await until(() => persistent.messages.some(m => m.method), 'Expected persistent approval');
    persistent.send({ jsonrpc: '2.0', id: persistent.messages.find(m => m.method).id,
        result: { action: 'accept', _meta: { persist: 'always' } } });
    check((await grantResult).result.approved._meta.persist === 'always', 'Official persistent choice passes unchanged');
    await end(b);

    b.turn = 'decline-turn';
    await host.control({ command: 'begin', slot: b.slot, turn: b.turn });
    const decline = peer(b.pipePath); await decline.ready();
    const declineResult = decline.request(b, 'set_value', { approval: true });
    await until(() => decline.messages.some(m => m.method), 'Expected decline approval');
    decline.send({ jsonrpc: '2.0', id: decline.messages.find(m => m.method).id, result: { action: 'decline' } });
    const rejected = await declineResult;
    check(rejected.error?.message === 'CUA_EXECUTION_FAILED' || rejected.disconnected, 'Declined approval never succeeds');
    await until(() => transports.at(-1).closed, 'Decline closes executor');

    const invalid = await slot('invalid-frame');
    const oversized = peer(invalid.pipePath); await oversized.ready();
    const frame = Buffer.alloc(4); frame.writeUInt32LE(8 * 1024 * 1024 + 1);
    oversized.socket.write(frame);
    await until(() => oversized.socket.destroyed, 'Oversized frame rejected before allocation');
    check(true, 'Oversized frame rejected');
    const replay = peer(invalid.pipePath); await replay.ready();
    replay.send({ id: 1, jsonrpc: '2.0', method: 'ping' });
    await until(() => replay.messages.length === 1, 'Expected first ping');
    replay.send({ id: 1, jsonrpc: '2.0', method: 'ping' });
    await until(() => replay.socket.destroyed, 'Duplicate request ID rejected');
    check(true, 'Duplicate request ID rejected');
    const bad = peer(invalid.pipePath); await bad.ready();
    bad.send({ id: {}, jsonrpc: '2.0', method: 'ping' });
    await until(() => bad.socket.destroyed, 'Invalid request ID rejected');
    check(true, 'Invalid request ID rejected');

    b.turn = 'disconnect-turn';
    await host.control({ command: 'begin', slot: b.slot, turn: b.turn });
    const disconnect = peer(b.pipePath); await disconnect.ready();
    const inflight = disconnect.request(b, 'drag', { hang: true });
    await until(() => transports.at(-1).pending, 'Expected in-flight input');
    disconnect.socket.destroy();
    await inflight;
    await until(() => transports.at(-1).closed, 'Disconnect closes executor');

    const competitor = await slot('known-failure-competitor');
    const competing = peer(competitor.pipePath); await competing.ready();
    const obscuredError = 'point (824, -494) is over Fixture.exe "private overlay title", not target window target.exe "private target title"; activate the target or take a fresh screenshot before retrying';
    for (const [method, helperError, expectedCode, guidance] of [
        ['click_element', 'coordinate input geometry is unavailable', 'CUA_GEOMETRY_UNAVAILABLE', 'screenshot'],
        ['launch_app', 'accessibility window-opened handler did not become ready', 'CUA_WINDOW_OBSERVER_NOT_READY', 'inspect'],
        ['click_element', 'user input was detected in this window; call get_window_state before continuing', 'CUA_OBSERVATION_STALE', 'get_window_state'],
        ['click', obscuredError, 'CUA_TARGET_OBSCURED', 'activate_window']
    ]) {
        const recovering = await slot('recoverable-' + method);
        const client = peer(recovering.pipePath); await client.ready();
        const before = calls.length;
        const failed = await client.request(recovering, method, { helperError });
        check(failed.error?.message.startsWith(expectedCode + ':'), 'Known action failure has a safe specific reason');
        check(failed.error.message.includes(guidance) && !failed.error.message.includes(helperError), 'Guidance is static and does not forward helper errors');
        check(!/private|824|494|Fixture\.exe|target\.exe/.test(failed.error.message), 'Action guidance excludes titles, paths and coordinates');
        check(events.some(event => event.event === 'actionFailed' && event.code === expectedCode && event.thread === recovering.thread), 'Known failure reports its category');
        check(events.find(event => event.event === 'actionFailed' && event.thread === recovering.thread).diagnostic.category === 'known_action_failure',
            'Recoverable failures also carry diagnostics without changing recovery');
        check(calls.length === before + 1, 'Failed action is never replayed automatically');
        check((await client.request(recovering, 'get_window_state')).result.method === 'get_window_state', 'Same turn can observe after a known action failure');
        check(!transports.at(-1).closed, 'Known action failure retains its executor');
        check((await client.request(recovering, 'list_windows', {}, { session_id: recovering.thread, turn_id: 'foreign-turn' })).error.message === 'CUA_TURN_NOT_ACTIVE', 'Known failure does not authorize another turn');
        check((await competing.request(competitor)).error.message === 'CUA_DESKTOP_BUSY', 'Known failure retains desktop ownership');
        check(calls.length === before + 2, 'Only the requested action and new observation reached the executor');
        await end(recovering);
    }
    const unrecognized = await slot('unrecognized-helper-error');
    const unrecognizedClient = peer(unrecognized.pipePath); await unrecognizedClient.ready();
    const privateError = 'coordinate input geometry is unavailable: secret input or helper stderr';
    const hidden = await unrecognizedClient.request(unrecognized, 'click_element', { helperError: privateError });
    check(hidden.error?.message === 'CUA_EXECUTION_FAILED' || hidden.disconnected, 'Whitelist matching is exact, unknown content stays hidden');
    await until(() => transports.at(-1).closed, 'Unknown helper error must close executor');
    const afterUnknown = peer(unrecognized.pipePath); await afterUnknown.ready();
    check((await afterUnknown.request(unrecognized, 'get_window_state')).error.message === 'CUA_TURN_NOT_ACTIVE', 'Unknown helper failure still revokes the turn');
    check(!JSON.stringify(events).includes('secret'), 'Unknown helper diagnostics contain no sensitive raw error');
    for (const [index, helperError, errorCode, category, helperExitCode] of [
        [0, 'computer-use request timed out: launch_app', undefined, 'request_timeout', null],
        [1, 'computer-use helper exited with exit code 5: secret stderr', undefined, 'helper_exit', 5],
        [2, 'computer-use helper emitted invalid JSON: secret response', undefined, 'invalid_json', null],
        [3, 'spawn secret executable', 'ENOENT', 'system_error', null],
        [4, 'secret application error HRESULT: 0x80070005', undefined, 'unknown', null],
        [5, 'secret application error', 'secret-error-code', 'unknown', null],
        [6, 'secret numeric protocol error', -32000, 'unknown', null]
    ]) {
        const failedSlot = await slot('diagnostic-' + index);
        const client = peer(failedSlot.pipePath); await client.ready();
        const before = calls.length;
        const response = client.request(failedSlot, 'launch_app', {
            approval: true, helperError, errorCode, errorName: index === 5 ? 'secret-error-name' : 'Error',
            app: 'secret-app', text: 'secret-input'
        });
        await until(() => client.messages.some(message => message.method), 'Diagnostic launch requests approval');
        client.send({ jsonrpc: '2.0', id: client.messages.find(message => message.method).id, result: { action: 'accept' } });
        check((await response).error?.message === 'CUA_EXECUTION_FAILED', 'Diagnostics preserve the generic tool error');
        const failure = events.find(event => event.event === 'fault' && event.thread === failedSlot.thread);
        check(failure?.diagnostic?.stage === 'executor_request', 'Failure identifies the executor request boundary');
        const diagnostic = failure.diagnostic;
        check(diagnostic.method === 'launch_app' && diagnostic.approval === 'accept', 'Failure records method and completed approval');
        check(diagnostic.category === category && diagnostic.helperExitCode === helperExitCode, 'Failure classifies safe transport evidence');
        check(diagnostic.errorCode === (errorCode === 'ENOENT' ? 'ENOENT' : 'unknown'), 'Only known system error codes survive');
        check(diagnostic.errorType === (index === 5 ? 'unknown' : 'Error'), 'Custom exception names stay private');
        check(diagnostic.hresult === (index === 4 ? '0x80070005' : 'unknown'), 'Only a labeled numeric HRESULT survives');
        check(Number.isSafeInteger(diagnostic.elapsedMs) && diagnostic.elapsedMs >= 0, 'Failure includes elapsed time');
        check(!JSON.stringify(failure).includes('secret'), 'Diagnostics omit raw errors, names, paths, input and stderr');
        await until(() => transports.at(-1).closed, 'Diagnosed failure still closes executor');
        const after = peer(failedSlot.pipePath); await after.ready();
        check((await after.request(failedSlot)).error.message === 'CUA_TURN_NOT_ACTIVE' && calls.length === before + 1,
            'Diagnostics neither replay the action nor reactivate its turn');
    }
    for (const [index, unexpected] of [
        'prefix ' + obscuredError,
        obscuredError + '\n',
        obscuredError + '\nprivate suffix',
        obscuredError.replace('private overlay title', 'private\nmultiline title'),
        obscuredError.replace('private overlay title', 'private\0title'),
        obscuredError.replace('(824, -494)', '(824.5, -494)'),
        obscuredError.replace('private overlay title', 'x'.repeat(513)),
        'user input was detected in this window; call get_window_state before continuing: private suffix'
    ].entries()) {
        const malformed = await slot('unrecognized-observation-' + index);
        const client = peer(malformed.pipePath); await client.ready();
        const result = await client.request(malformed, 'click', { helperError: unexpected });
        check(result.error?.message === 'CUA_EXECUTION_FAILED' || result.disconnected, 'Unverified observation error stays on the conservative path');
        await until(() => transports.at(-1).closed, 'Unverified observation error closes executor');
        check(!events.some(event => event.event === 'actionFailed' && event.thread === malformed.thread), 'Malformed error cannot enter a known recovery category');
    }

    for (const failedStage of ['transport_prepare', 'transport_create']) {
        const observed = [];
        let diagnosticHost;
        diagnosticHost = createComputerUseHost({
            emit: event => {
                observed.push(event);
                if (event.event === 'acquire') void diagnosticHost.control({ command: 'acquireResult', requestId: event.id, granted: true });
            },
            prepareTransport: async () => { if (failedStage === 'transport_prepare') throw new Error('secret preparation'); },
            createTransport: () => { throw new Error('secret construction'); }
        });
        try {
            const diagnosticSlot = await diagnosticHost.control({ command: 'allocate', helperHome: os.tmpdir() });
            await diagnosticHost.control({ command: 'publish', slot: diagnosticSlot.slot });
            await diagnosticHost.control({ command: 'bind', slot: diagnosticSlot.slot, thread: 'diagnostic-thread' });
            await diagnosticHost.control({ command: 'begin', slot: diagnosticSlot.slot, turn: 'diagnostic-turn' });
            const client = peer(diagnosticSlot.pipePath); await client.ready();
            await client.request({ thread: 'diagnostic-thread', turn: 'diagnostic-turn' });
            const failure = observed.find(event => event.event === 'fault');
            check(failure?.diagnostic.stage === failedStage && failure.diagnostic.approval === 'not_requested',
                'Preparation and construction failures identify their own boundaries');
            check(!JSON.stringify(observed).includes('secret'), 'Pre-execution errors remain private');
        } finally { await diagnosticHost.shutdown(); }
    }

    const late = await slot('late-known-failure');
    const lateClient = peer(late.pipePath); await lateClient.ready();
    const lateResult = lateClient.request(late, 'click_element', { deferredError: true });
    await until(() => transports.at(-1).rejectLate, 'Expected held executor failure');
    const oldTransport = transports.at(-1);
    await end(late);
    check((await lateResult).disconnected, 'Stop cancels a request before its known failure arrives');
    const successor = await slot('after-late-failure');
    const successorClient = peer(successor.pipePath); await successorClient.ready();
    check((await successorClient.request(successor)).result.method === 'list_windows', 'Another turn can acquire after stop');
    oldTransport.rejectLate(new Error('coordinate input geometry is unavailable'));
    await new Promise(resolve => setImmediate(resolve));
    check(!events.some(event => event.event === 'actionFailed' && event.thread === late.thread), 'Late known failure is not treated as an active recoverable action');
    const stopped = peer(late.pipePath); await stopped.ready();
    check((await stopped.request(late)).error.message === 'CUA_TURN_NOT_ACTIVE', 'Late known failure cannot revive the stopped turn');
    check((await successorClient.request(successor, 'get_window_state')).result.method === 'get_window_state', 'Late old failure cannot close the new owner');
    await end(successor);
    const callCount = calls.length;
    await host.shutdown();
    check(calls.length === callCount, 'No unknown input replay');
    check(!events.some(e => e.event === 'fatal'), 'Normal teardown requires no forced termination');

    let recoveredCalls = 0;
    const recovered = createComputerUseHost({
        emit: event => {
            if (event.event === 'acquire') void recovered.control({ command: 'acquireResult', requestId: event.id, granted: true });
        },
        createTransport: () => ({
            async request() { recoveredCalls++; return 'recovered'; },
            async close() {}
        })
    });
    try {
        await assert.rejects(recovered.control({ command: 'allocate', helperHome: os.tmpdir(), pipePath: '\\\\.\\pipe\\foreign' }),
            failure => failure.code === 'CUA_INVALID_PIPE');
        const restored = await recovered.control({ command: 'allocate', helperHome: os.tmpdir(), pipePath: a.pipePath });
        check(restored.pipePath === a.pipePath && restored.slot !== a.slot, 'Restart retains address and rotates private slot');
        await recovered.control({ command: 'publish', slot: restored.slot });
        await recovered.control({ command: 'bind', slot: restored.slot, thread: a.thread });
        await recovered.control({ command: 'begin', slot: restored.slot, turn: 'recovered-turn' });
        const reconnect = peer(restored.pipePath); await reconnect.ready();
        check((await reconnect.request(a)).error.message === 'CUA_TURN_NOT_ACTIVE', 'Old turn rejected by restarted host');
        check(recoveredCalls === 0, 'Restart does not replay an old request');
        check((await reconnect.request({ thread: a.thread, turn: 'recovered-turn' })).result === 'recovered', 'New turn reconnects to original pipe');
    } finally { await recovered.shutdown(); }
    // Production request gate: prepare policy before acquiring any desktop ownership.
    const preparationEvents = [];
    let preparedCalls = 0;
    let compatibilityCalls = 0;
    let lazy;
    lazy = createComputerUseHost({
        prepareExecution: true,
        prepareTransport: async () => { compatibilityCalls++; },
        emit: event => {
            preparationEvents.push(event);
            if (event.event === 'acquire') void lazy.control({ command: 'acquireResult', requestId: event.id, granted: true });
        },
        createTransport: () => ({ async request() { preparedCalls++; return {}; }, async close() {} })
    });
    let lazyPeer;
    try {
        const lazySlot = await lazy.control({ command: 'allocate', helperHome: os.tmpdir() });
        lazySlot.thread = 'lazy-thread';
        lazySlot.turn = 'denied-turn';
        await lazy.control({ command: 'publish', slot: lazySlot.slot });
        await lazy.control({ command: 'bind', slot: lazySlot.slot, thread: lazySlot.thread });
        await lazy.control({ command: 'begin', slot: lazySlot.slot, turn: lazySlot.turn });
        check(preparationEvents.length === 0 && compatibilityCalls === 0, 'Ordinary turn does not prepare desktop execution');
        lazyPeer = peer(lazySlot.pipePath);
        await lazyPeer.ready();
        const denied = lazyPeer.request(lazySlot);
        await until(() => preparationEvents.some(e => e.event === 'prepare'), 'Tool call requests policy preparation');
        const deniedId = preparationEvents.at(-1).id;
        check(!preparationEvents.some(e => e.event === 'acquire') && preparedCalls === 0, 'No desktop ownership before preparation');
        check(await lazy.control({ command: 'acquireResult', requestId: deniedId, granted: true }) === false, 'Acquisition cannot bypass preparation');
        await lazy.control({ command: 'prepareResult', requestId: deniedId, prepared: false });
        check((await denied).error.message === 'CUA_PREPARATION_FAILED', 'Preparation failure reaches caller without execution');
        check(preparationEvents.find(event => event.event === 'fault').diagnostic.stage === 'desktop_acquire',
            'Policy or desktop lease preparation failure is distinct from an executor request');
        lazyPeer.socket.destroy();
        lazySlot.turn = 'cancelled-turn';
        await lazy.control({ command: 'begin', slot: lazySlot.slot, turn: lazySlot.turn });
        lazyPeer = peer(lazySlot.pipePath);
        await lazyPeer.ready();
        const cancelled = lazyPeer.request(lazySlot);
        await until(() => preparationEvents.filter(e => e.event === 'prepare').length === 2, 'Second preparation requested');
        const cancelledId = preparationEvents.at(-1).id;
        await lazy.control({ command: 'end', slot: lazySlot.slot, turn: lazySlot.turn });
        await cancelled;
        check(await lazy.control({ command: 'prepareResult', requestId: cancelledId, prepared: true }) === false, 'Late preparation cannot reactivate a cancelled turn');
        check(preparedCalls === 0 && compatibilityCalls === 0, 'Cancellation starts no executor');
        check(preparationEvents.filter(e => e.event === 'fault').length === 1, 'Turn cancellation does not announce an executor failure');
        lazySlot.turn = 'ready-turn';
        await lazy.control({ command: 'begin', slot: lazySlot.slot, turn: lazySlot.turn });
        lazyPeer = peer(lazySlot.pipePath);
        await lazyPeer.ready();
        const accepted = lazyPeer.request(lazySlot);
        await until(() => preparationEvents.filter(e => e.event === 'prepare').length === 3, 'Recovery preparation requested');
        await lazy.control({ command: 'prepareResult', requestId: preparationEvents.at(-1).id, prepared: true });
        check(!(await accepted).error && preparedCalls === 1 && compatibilityCalls === 1, 'Validated first tool invokes the executor exactly once');
        check(preparationEvents.some(e => e.event === 'available'), 'Successful execution reports recovery');
    } finally {
        lazyPeer?.socket.destroy();
        await lazy.shutdown();
    }

    // Exercise the production startup gate with inert substitute transports.
    const fixtureRoot = mkdtempSync(path.join(os.tmpdir(), 'mycodex-cua-contract-'));
    try {
        const sky = path.join(fixtureRoot, 'bin/node_modules/@oai/sky');
        const transport = path.join(sky, 'dist/project/cua/sky_js/src/targets/windows/internal/helper_transport.js');
        mkdirSync(path.dirname(transport), { recursive: true });
        writeFileSync(path.join(sky, 'package.json'), JSON.stringify({ type: 'module' }));
        for (const [body, ready] of [
            ['async request(method) { if (method !== "end_turn") throw Error(); } async close() {}', true],
            ['async close() {}', false],
            ['constructor() { throw Error("must remain lazy"); } async request() {} async close() {}', true],
            ['async request() { throw Error("private-error-data"); } async close() {}', true]
        ]) {
            writeFileSync(transport, `export class WindowsHelperTransport { ${body} }`);
            const child = spawnSync(process.execPath, [fileURLToPath(new URL(
                '../../src/MyCodex.Infrastructure/ComputerUse/Host/main.mjs', import.meta.url))], {
                cwd: fixtureRoot, env: { ...process.env, MYCODEX_CUA_RUNTIME: fixtureRoot, CODEX_CLI_PATH: process.execPath },
                input: '', encoding: 'utf8', timeout: 10000, windowsHide: true
            });
            check(!child.error, 'Startup probe exits within its deadline');
            const messages = child.stdout.trim().split('\n').map(line => JSON.parse(line));
            check(messages.some(value => value.ready === true) === ready, 'Registration validates the interface without executing the helper');
            check(ready || messages.some(value => value.code === 'CUA_COMPONENT_CONTRACT'), 'Incompatible transport reports a stable code');
            check(!child.stdout.includes('private-error-data'), 'Startup failures do not expose component error data');
        }
    } finally { rmSync(fixtureRoot, { recursive: true, force: true }); }
    console.log(`Computer Use host: ${assertions} checks passed (fake executor, no desktop actions).`);
} finally {
    await host.shutdown();
}
