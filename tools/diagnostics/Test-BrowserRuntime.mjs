// Offline integration with the pinned upstream runtime and a synthetic extension pipe.
// No browser, desktop, user profile, or model request is used.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createBrowserAccessHost } from '../../src/MyCodex.Infrastructure/ComputerUse/Host/browser-access-host.mjs';
import { createComputerUseHost } from '../../src/MyCodex.Infrastructure/ComputerUse/Host/computer-use-host.mjs';

assert.equal(process.platform, 'win32', 'This integration check requires Windows named pipes.');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const pin = JSON.parse(readFileSync(path.join(root, 'tools/runtime/browser-use-bundle.json'), 'utf8'));
assert.match(pin.bundleId, /^[a-zA-Z0-9._-]+$/);
const runtime = path.join(root, 'runtime/browser', pin.bundleId);
assert.equal(createHash('sha256').update(readFileSync(path.join(runtime, 'bundle.json'))).digest('hex'),
    pin.manifestSha256.toLowerCase(), 'Runtime bundle does not match its reviewed pin.');
const node = path.join(runtime, 'bin/node.exe');
const modules = path.join(runtime, 'bin/node_modules');
const launcher = path.join(modules, '@oai/cua-repl/bin/cua-repl.mjs');
const service = path.join(modules, '@oai/browser-desktop/scripts/browser-service.mjs');
// The bundled runtime follows the latest stable Codex release, so its version folder is passed in.
const codexIndex = process.argv.indexOf('--codex');
assert.ok(codexIndex > 1 && process.argv[codexIndex + 1], 'Pass --codex <path to the prepared codex.exe>.');
const codex = path.resolve(process.argv[codexIndex + 1]);
for (const file of [node, launcher, service, codex]) assert.ok(existsSync(file), 'Prepared runtime component is missing.');
const temporary = mkdtempSync(path.join(os.tmpdir(), 'mycodex-browser-runtime-'));
const children = new Set();
const sockets = new Set();
const backendPath = `\\\\.\\pipe\\mycodex-browser-runtime-fixture-${randomUUID()}`;
let backendCalls = 0;
let authorized = 0;
const thread = 'runtime-fixture-thread';
const turn = 'runtime-fixture-turn';
const host = createBrowserAccessHost({ authorize: async () => { authorized++; return true; } });
const slot = await host.allocate();
const backend = net.createServer(socket => {
    sockets.add(socket);
    socket.on('close', () => sockets.delete(socket));
    socket.on('error', () => {});
    let buffer = Buffer.alloc(0);
    socket.on('data', chunk => {
        buffer = Buffer.concat([buffer, chunk]);
        while (buffer.length >= 4) {
            const size = buffer.readUInt32LE();
            if (size > 1024 * 1024) { socket.destroy(); return; }
            if (buffer.length < size + 4) return;
            const request = JSON.parse(buffer.subarray(4, size + 4).toString('utf8'));
            buffer = buffer.subarray(size + 4);
            backendCalls++;
            const response = request.method === 'getInfo'
                ? { jsonrpc: '2.0', id: request.id, result: { type: 'extension', family: 'edge', name: 'Synthetic Edge',
                    version: 'fixture', metadata: { extensionInstanceId: 'synthetic-edge' }, requestHeadersVersion: 1,
                    apiSupportOverrides: {}, capabilities: { browser: [], tab: [] } } }
                : { jsonrpc: '2.0', id: request.id, error: { code: -32601, message: 'Fixture supports discovery only.' } };
            const bytes = Buffer.from(JSON.stringify(response));
            const header = Buffer.alloc(4);
            header.writeUInt32LE(bytes.length);
            socket.write(Buffer.concat([header, bytes]));
        }
    });
});

function environment(home, includeAuthorization) {
    mkdirSync(home, { recursive: true });
    const env = {
        SystemRoot: process.env.SystemRoot, WINDIR: process.env.WINDIR,
        PATH: `${path.dirname(node)};${path.join(process.env.SystemRoot, 'System32')}`,
        TEMP: home, TMP: home, USERPROFILE: home, APPDATA: home, LOCALAPPDATA: home,
        CODEX_HOME: home, CODEX_CLI_PATH: codex,
        NODE_REPL_NODE_PATH: node, NODE_REPL_NODE_MODULE_DIRS: modules,
        NODE_REPL_TRUSTED_CODE_PATHS: `${home};${modules}`,
        NODE_REPL_TRUSTED_SERVICES: JSON.stringify({ browser: service }),
        BROWSER_USE_ACCESS_EXECUTOR_ID: slot.executorId,
        BROWSER_USE_BACKEND_PATHS: backendPath, BROWSER_USE_AVAILABLE_BACKENDS: 'chrome',
        BROWSER_USE_TINYSKY_ENABLED: '0', BROWSER_USE_SECURITY_MODE: '',
        CUA_REPL_NODE_REPL_PATH: path.join(runtime, 'bin/node_repl.exe'), CUA_REPL_ENABLED_SURFACES: 'browser'
    };
    if (includeAuthorization) {
        env.BROWSER_USE_ACCESS_PIPE_PATH = slot.pipePath;
        env.BROWSER_USE_ACCESS_VERIFYING_IDENTITY = slot.verifyingIdentity;
    }
    return env;
}

function client(home, includeAuthorization, desktop = null) {
    mkdirSync(home, { recursive: true });
    const child = spawn(desktop?.executable ?? node, desktop ? [] : [launcher], { cwd: home, env: desktop?.env ?? environment(home, includeAuthorization),
        stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    children.add(child);
    const pending = new Map();
    let sequence = 0;
    let buffer = '';
    child.stderr.on('data', () => {});
    child.on('error', error => { for (const entry of pending.values()) entry.reject(error); });
    child.on('exit', () => {
        for (const entry of pending.values()) entry.reject(new Error('Runtime exited before replying.'));
    });
    child.stdout.setEncoding('utf8');
    child.stdout.on('data', chunk => {
        buffer += chunk;
        for (;;) {
            const end = buffer.indexOf('\n');
            if (end < 0) return;
            const line = buffer.slice(0, end);
            buffer = buffer.slice(end + 1);
            if (!line.trim()) continue;
            let response;
            try { response = JSON.parse(line); } catch { continue; }
            if (response.method) {
                if (Object.hasOwn(response, 'id')) child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: response.id,
                    result: { action: 'cancel' } }) + '\n');
                continue;
            }
            const entry = pending.get(response.id);
            if (!entry) continue;
            pending.delete(response.id);
            clearTimeout(entry.timer);
            response.error ? entry.reject(new Error(response.error.message)) : entry.resolve(response.result);
        }
    });
    function request(method, params) {
        const id = ++sequence;
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => { pending.delete(id); reject(new Error('Runtime request timed out.')); }, 45000);
            pending.set(id, { resolve, reject, timer });
            child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
        });
    }
    const execute = (code, turnId) => request('tools/call', { name: 'js', arguments: { code, title: 'Synthetic browser check only' },
        _meta: { 'x-codex-turn-metadata': { session_id: thread, turn_id: turnId, session_context: 'live' } } });
    return {
        async initialize() {
            await request('initialize', { protocolVersion: '2025-03-26', capabilities: { elicitation: { form: {} } },
                clientInfo: { name: 'mycodex-browser-runtime-fixture', version: '1' } });
            child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
        },
        async discover(turnId) {
            return execute('await cua.listBrowsers();', turnId);
        },
        async probeSky(turnId) {
            return execute("nodeRepl.write(await nodeRepl.rpc('sky', {type:'execute', method:'list_windows', args:[]}));", turnId);
        },
        async selectEdge(turnId) {
            return execute(`var discovered = await cua.listBrowsers({emit:false});
var info = discovered.find(browser => browser.family === 'edge');
var selectedEdge = await cua.getBrowser({id:'edge'});
nodeRepl.write({edgeSelected:!!info && selectedEdge.browserId === info.id,
    name:info?.name,family:info?.family,extensionInstanceId:info?.metadata?.extensionInstanceId});`, turnId);
        },
        async resetBrowser(turnId) {
            return request('tools/call', { name: 'js_reset', arguments: {},
                _meta: { 'x-codex-turn-metadata': { session_id: thread, turn_id: turnId, session_context: 'live' } } });
        },
        async close() {
            if (child.exitCode !== null || !Number.isSafeInteger(child.pid)) { children.delete(child); return; }
            const exit = once(child, 'exit').catch(() => {});
            child.stdin.end();
            let timer;
            const timeout = new Promise(resolve => { timer = setTimeout(resolve, 5000); });
            await Promise.race([exit, timeout]);
            clearTimeout(timer);
            if (child.exitCode === null) {
                await stopOwnedTree(child);
                await exit;
            }
            children.delete(child);
            for (const entry of pending.values()) clearTimeout(entry.timer);
        }
    };
}

async function stopOwnedTree(child) {
    if (child.exitCode !== null || !Number.isSafeInteger(child.pid)) return;
    const killer = spawn(path.join(process.env.SystemRoot, 'System32/taskkill.exe'),
        ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true });
    await once(killer, 'exit');
}

async function verifyToolRouting(browser) {
    const wrongTool = await browser.probeSky(turn);
    assert.equal(wrongTool.isError, true);
    assert.ok(wrongTool.content.some(item => item.text?.includes('Trusted RPC service is not configured: sky')));
    const pin = JSON.parse(readFileSync(path.join(root, 'tools/runtime/computer-use-bundle.json'), 'utf8'));
    const bundle = path.join(root, 'runtime/cua', pin.bundleId);
    assert.equal(createHash('sha256').update(readFileSync(path.join(bundle, 'bundle.json'))).digest('hex'), pin.manifestSha256.toLowerCase());
    const nativeRoot = path.join(bundle, 'cua_node');
    const home = path.join(temporary, 'native-home');
    mkdirSync(home, { recursive: true });
    let nativeCalls = 0;
    let nativeHost;
    nativeHost = createComputerUseHost({
        emit: event => {
            if (event.event === 'acquire') void nativeHost.control({ command: 'acquireResult', requestId: event.id, granted: true });
        },
        createTransport: () => ({
            async request(method) {
                assert.equal(method, 'list_windows', 'The fixture permits no real window observations or input.');
                nativeCalls++;
                return [];
            },
            async close() {}
        })
    });
    const slot = await nativeHost.control({ command: 'allocate', helperHome: home });
    const env = {
        SystemRoot: process.env.SystemRoot, WINDIR: process.env.WINDIR,
        PATH: `${path.join(nativeRoot, 'bin')};${path.join(process.env.SystemRoot, 'System32')}`,
        TEMP: home, TMP: home, USERPROFILE: home, APPDATA: home, LOCALAPPDATA: home,
        CODEX_HOME: home, CODEX_CLI_PATH: codex,
        NODE_REPL_NODE_PATH: path.join(nativeRoot, 'bin/node.exe'),
        NODE_REPL_NODE_MODULE_DIRS: path.join(nativeRoot, 'bin/node_modules'),
        NODE_REPL_TRUSTED_CODE_PATHS: `${home};${path.join(nativeRoot, 'bin/node_modules')}`,
        NODE_REPL_TRUSTED_SERVICES: JSON.stringify({ sky: '@oai/sky/service' }),
        SKY_CUA_NATIVE_PIPE: '1', SKY_CUA_NATIVE_PIPE_DIRECTORY: slot.pipePath
    };
    let native;
    try {
        await nativeHost.control({ command: 'publish', slot: slot.slot });
        await nativeHost.control({ command: 'bind', slot: slot.slot, thread });
        await nativeHost.control({ command: 'begin', slot: slot.slot, turn });
        native = client(home, false, { executable: path.join(nativeRoot, 'bin/node_repl.exe'), env });
        await native.initialize();
        const read = await native.probeSky(turn);
        assert.notEqual(read.isError, true, read.content?.find(item => item.type === 'text')?.text?.slice(0, 300));
        assert.equal(nativeCalls, 1, 'The actual Sky service must reach only the synthetic executor.');
        await nativeHost.control({ command: 'end', slot: slot.slot, turn });
        const ended = await native.probeSky(turn);
        assert.equal(ended.isError, true);
        assert.equal(nativeCalls, 1, 'An ended native binding must not reach the executor.');
        console.log('Tool routing: browser-only cua_repl reproduces missing sky; actual node_repl/Sky reaches the synthetic executor and rejects an ended turn. No real desktop access.');
    } finally {
        if (native) await native.close();
        await nativeHost.shutdown();
    }
}

try {
    backend.listen(backendPath);
    await once(backend, 'listening');
    host.publish(slot.executorId);
    host.bind(slot.executorId, thread);
    host.begin(slot.executorId, turn);
    const absent = client(path.join(temporary, 'absent-home'), false);
    try {
        await absent.initialize();
        const result = await absent.discover(turn);
        assert.match(JSON.stringify(result), /The browser access host is unavailable/);
        assert.equal(backendCalls, 0, 'Authorization must run before backend discovery.');
    } finally { await absent.close(); }
    const owned = client(path.join(temporary, 'owned-home'), true);
    try {
        await owned.initialize();
        if (process.argv.includes('--check-tool-routing')) await verifyToolRouting(owned);
        const result = await owned.discover(turn);
        assert.notEqual(result.isError, true, 'Upstream browser discovery must succeed.');
        assert.match(JSON.stringify(result), /Synthetic Edge/);
        assert.ok(backendCalls > 0);
        assert.equal(authorized, 1);
        const selected = await owned.selectEdge(turn);
        assert.notEqual(selected.isError, true, 'Explicit Edge selection must succeed.');
        const selectedText = JSON.stringify(selected);
        assert.match(selectedText, /edgeSelected[^a-z]+true/);
        assert.match(selectedText, /Synthetic Edge/);
        assert.match(selectedText, /synthetic-edge/);
        const callsBeforeRevocation = backendCalls;
        await host.remove(slot.executorId);
        const cleanup = await owned.resetBrowser(turn);
        assert.notEqual(cleanup.isError, true, 'Browser reset must work after the feature gate is revoked.');
        for (let i = 0; i < 2; i++) {
            const sameTurn = await owned.discover(turn);
            assert.equal(sameTurn.isError, true, 'Revocation plus browser reset must block a previously authorized active turn.');
            assert.equal(backendCalls, callsBeforeRevocation, 'Revoked calls must not reach the browser.');
        }
        host.end(slot.executorId, turn);
        const revoked = await owned.discover('runtime-fixture-next-turn');
        assert.equal(revoked.isError, true);
        assert.equal(authorized, 1, 'A revoked binding must not reach the parent authorizer.');
    } finally { await owned.close(); }
    console.log('Browser runtime: 7 checks passed (missing host, owned host, Edge selection, reset after revocation, two active-turn calls blocked, next turn blocked; synthetic backend only).');
} finally {
    await Promise.all([...children].map(stopOwnedTree));
    for (const socket of sockets) socket.destroy();
    await host.shutdown();
    await new Promise(resolve => backend.close(resolve));
    const resolved = path.resolve(temporary);
    assert.ok(resolved.startsWith(path.resolve(os.tmpdir()) + path.sep));
    rmSync(resolved, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}
