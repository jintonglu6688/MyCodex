import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { mkdtempSync, rmSync } from 'node:fs';
import { createComputerUseHost } from './computer-use-host.mjs';
import { createBrowserUseControl } from './browser-use-control.mjs';

const write = value => process.stdout.write(JSON.stringify(value) + '\n');
const runtime = process.env.MYCODEX_CUA_RUNTIME;
const cli = process.env.CODEX_CLI_PATH;
if (process.platform !== 'win32' || process.arch !== 'x64' || !runtime || !cli ||
    !path.isAbsolute(runtime) || !path.isAbsolute(cli)) {
    write({ event: 'fatal', code: 'CUA_INVALID_ENVIRONMENT' });
    process.exit(1);
}

try {
    const sky = path.join(runtime, 'bin', 'node_modules', '@oai', 'sky');
    const modulePath = path.join(sky, 'dist', 'project', 'cua', 'sky_js', 'src',
        'targets', 'windows', 'internal', 'helper_transport.js');
    const { WindowsHelperTransport } = await import(pathToFileURL(modulePath).href);
    if (typeof WindowsHelperTransport !== 'function' ||
        typeof WindowsHelperTransport.prototype.request !== 'function' ||
        typeof WindowsHelperTransport.prototype.close !== 'function') throw new Error('CUA_COMPONENT_CONTRACT');
    let compatibility;
    async function checkCompatibility() {
        const probeHome = mkdtempSync(path.join(process.cwd(), 'compatibility-'));
        let probe;
        try {
            probe = new WindowsHelperTransport({
                helperCommand: path.join(sky, 'bin', 'windows', 'codex-computer-use.exe'),
                helperArgs: ['--parent-pid', String(process.pid)],
                helperEnv: { CODEX_CLI_PATH: cli, CODEX_HOME: probeHome }, timeoutMs: 3000
            });
            await probe.request('end_turn', {}, { skipTurnTransition: true });
        } catch { throw Object.assign(new Error('CUA_COMPONENT_CONTRACT'), { code: 'CUA_COMPONENT_CONTRACT' }); }
        finally {
            try { await probe?.close(); }
            finally { rmSync(probeHome, { recursive: true, force: true }); }
        }
    }
    const host = createComputerUseHost({
        emit: write,
        prepareExecution: true,
        prepareTransport: () => {
            compatibility ??= checkCompatibility().catch(error => { compatibility = null; throw error; });
            return compatibility;
        },
        createTransport: helperHome => new WindowsHelperTransport({
            helperCommand: path.join(sky, 'bin', 'windows', 'codex-computer-use.exe'),
            helperArgs: ['--parent-pid', String(process.pid)],
            helperEnv: { CODEX_CLI_PATH: cli, CODEX_HOME: helperHome }
        })
    });
    const surfaces = createBrowserUseControl({ native: host, emit: write });
    let closing = false;
    async function stop() {
        if (closing) return;
        closing = true;
        try { await surfaces.shutdown(); await host.shutdown(); process.exit(0); }
        catch { write({ event: 'fatal', code: 'CUA_CLOSE_TIMEOUT' }); }
    }
    let buffer = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', chunk => {
        buffer += chunk;
        if (Buffer.byteLength(buffer, 'utf8') > 1024 * 1024) {
            write({ event: 'fatal', code: 'CUA_CONTROL_TOO_LARGE' });
            void stop();
            return;
        }
        for (;;) {
            const end = buffer.indexOf('\n');
            if (end < 0) break;
            const line = buffer.slice(0, end);
            buffer = buffer.slice(end + 1);
            let request;
            try {
                request = JSON.parse(line);
                if (request === null || typeof request !== 'object' ||
                    !(Number.isSafeInteger(request.id) || typeof request.id === 'string')) throw new Error();
            } catch { write({ error: { code: 'CUA_INVALID_CONTROL' } }); continue; }
            // Do not serialize this queue: an end must cancel a waiting approval/acquisition.
            surfaces.control(request).then(result => {
                write({ id: request.id, result });
                if (request.command === 'shutdown') void stop();
            }, failure => {
                write({ id: request.id, error: { code: /^CUA_[A-Z_]+$/.test(failure?.code) ?
                    failure.code : 'CUA_CONTROL_FAILED' } });
            });
        }
    });
    process.stdin.on('end', () => { void stop(); });
    process.on('SIGTERM', () => { void stop(); });
    // Version 2 requires parent policy preparation before acquiring desktop ownership.
    write({ ready: true, protocolVersion: 2, pid: process.pid });
} catch {
    write({ event: 'fatal', code: 'CUA_COMPONENT_CONTRACT' });
    process.exit(1);
}
