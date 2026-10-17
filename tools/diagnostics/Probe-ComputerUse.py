"""Optional, real official MCP/host probe, scoped to a newly created fixture.

No model API calls. Requires Windows x64, Python 3.11+, the net48 compiler,
an explicitly selected compatible codex.exe, and MyCodex's prepared CUA bundle.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import stat
import subprocess
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]


class Rpc:
    def __init__(self, process, handler=lambda *_: None, on_close=lambda: None):
        self.process, self.handler, self.on_close = process, handler, on_close
        self.lock = threading.Lock()
        self.pending = {}
        self.sequence = 0
        self.ready = queue.Queue()
        self.reader = threading.Thread(target=self.read, daemon=True)
        self.reader.start()

    def send(self, value):
        with self.lock:
            self.process.stdin.write(json.dumps(value) + '\n')
            self.process.stdin.flush()

    def read(self):
        try:
            for line in self.process.stdout:
                value = json.loads(line)
                if value.get('ready'):
                    self.ready.put(value)
                elif value.get('event') or 'method' in value:
                    self.handler(self, value)
                else:
                    reply = self.pending.get(value.get('id'))
                    if reply is not None:
                        reply.put(value)
        finally:
            self.on_close()
            for reply in list(self.pending.values()):
                reply.put({'error': {'code': 'PROBE_PROCESS_EXITED'}})

    def request(self, method, params=None, control=False):
        with self.lock:
            self.sequence += 1
            identifier = str(self.sequence)
            reply = queue.Queue()
            self.pending[identifier] = reply
        message = {'id': identifier, 'command': method, **(params or {})} if control else {
            'jsonrpc': '2.0', 'id': identifier, 'method': method, 'params': params or {}}
        self.send(message)
        try:
            result = reply.get(timeout=45)
            if 'error' in result:
                raise RuntimeError('PROBE_RPC_FAILED: ' + method)
            return result.get('result')
        finally:
            self.pending.pop(identifier, None)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None


def runtime_root(explicit):
    pin = json.loads((ROOT / 'tools/runtime/computer-use-bundle.json').read_text(encoding='utf-8'))
    candidates = [Path(explicit)] if explicit else [
        ROOT / 'runtime/cua' / pin['bundleId'] / 'cua_node']
    verified = []
    for candidate in candidates:
        try:
            manifest = json.loads((candidate / 'manifest.json').read_text(encoding='utf-8'))
            sky = json.loads((candidate / 'bin/node_modules/@oai/sky/package.json').read_text(encoding='utf-8'))
            if (manifest.get('platform'), manifest.get('arch'), manifest.get('target'),
                manifest.get('node_path'), manifest.get('node_repl_path'), manifest.get('node_modules'), sky.get('name')) != (
                    'windows', 'x64', 'windows-x64', 'bin/node.exe',
                    'bin/node_repl.exe', 'bin/node_modules', '@oai/sky'):
                continue
            version = manifest.get('runtime_archive_version', '').split('/')[0]
            if not re.fullmatch(r'\d+\.\d+(?:\.\d+){0,2}', version) or not sky.get('version'):
                continue
            for file in ('manifest.json', 'bin/node.exe', 'bin/node_repl.exe',
                         'bin/node_modules/@oai/sky/package.json',
                         'bin/node_modules/@oai/sky/bin/windows/codex-computer-use.exe',
                         'bin/node_modules/@oai/sky/dist/project/cua/sky_js/src/index.js',
                         'bin/node_modules/@oai/sky/dist/project/cua/sky_js/src/service.js',
                         'bin/node_modules/@oai/sky/dist/project/cua/sky_js/src/targets/windows/internal/helper_transport.js'):
                if not (candidate / file).is_file():
                    raise ValueError('Incomplete runtime')
                entry = candidate / file
                while entry != candidate.parent:
                    if entry.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                        raise ValueError('Linked component entry')
                    entry = entry.parent
            verify = subprocess.run([str(Path(os.environ['WINDIR']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'),
                '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File',
                str(ROOT / 'src/MyCodex.Infrastructure/ComputerUse/Host/verify-components.ps1')],
                env={**os.environ, 'MYCODEX_CUA_VERIFY_ROOT': str(candidate)}, capture_output=True, text=True, timeout=15)
            if verify.returncode != 0 or verify.stdout.strip() != 'OPENAI_COMPONENTS_VERIFIED':
                raise RuntimeError('Official component signature validation failed')
            verified.append((tuple(map(int, version.split('.'))), manifest['runtime_archive_version'], candidate.resolve()))
        except (OSError, ValueError):
            continue
    if verified:
        return max(verified, key=lambda entry: entry[:2])[2]
    raise RuntimeError('No compatible prepared MyCodex CUA bundle; prepare it or pass --runtime explicitly')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--codex', required=True, help='Absolute selected codex.exe path; no implicit runtime replacement')
    parser.add_argument('--runtime', help='Optional installed CUA root containing manifest.json and bin')
    parser.add_argument('--execute', action='store_true', help='Explicitly show and operate only the new fixture window')
    args = parser.parse_args()
    if os.name != 'nt':
        parser.error('This probe is Windows-only')
    if not args.execute:
        print('Validation only. Add --execute to create and operate the isolated fixture. No model calls are made.')
    cli = Path(args.codex)
    if not cli.is_absolute() or not cli.is_file() or cli.suffix.lower() != '.exe':
        parser.error('--codex must identify an existing absolute codex.exe')
    version = subprocess.check_output([str(cli), '--version'], text=True).strip().removeprefix('codex-cli ')
    runtime = runtime_root(args.runtime)
    print('Selected Codex: ' + version + '; CUA: ' + runtime.name)
    if not args.execute:
        print('Official native signatures and required paths verified. Protocol compatibility still requires --execute.')
        return

    from computer_use_probe_windows import Job, DesktopLease, restrict_pipe
    run = ROOT / 'artifacts/computer-use-probe' / uuid.uuid4().hex
    run.mkdir(parents=True)
    home, helper = run / 'mcp-home', run / 'helper-home'
    home.mkdir(); helper.mkdir()
    fixture = run / ('MyCodexCuaFixture' + uuid.uuid4().hex[:8] + '.exe')
    signal = run / 'fixture.ready'
    csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    subprocess.run([str(csc), '/nologo', '/target:winexe', '/platform:x64', '/out:' + str(fixture),
                    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
                    str(ROOT / 'tools/diagnostics/ComputerUseFixture.cs')], check=True, capture_output=True)
    # Only read hashes of real configs. No user config values or credentials are loaded.
    config_paths = {Path.home() / '.codex/config.toml'}
    if os.environ.get('CODEX_HOME'):
        config_paths.add(Path(os.environ['CODEX_HOME']) / 'config.toml')
    original = {path: digest(path) for path in config_paths}
    sid, session = subprocess.check_output(['powershell', '-NoProfile', '-Command',
        '[System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value; [System.Diagnostics.Process]::GetCurrentProcess().SessionId'],
        text=True).strip().splitlines()
    job, lease = Job(), DesktopLease(sid.strip(), session.strip())
    children = []
    results = {}
    host_rpc = None
    slot = None
    base = os.environ.copy()
    base.update(CODEX_HOME=str(home), CODEX_CLI_PATH=str(cli), MYCODEX_CUA_RUNTIME=str(runtime))

    def start(command, env, visible=False):
        child = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
            text=True, encoding='utf-8', env=env, cwd=run,
            creationflags=0 if visible else subprocess.CREATE_NO_WINDOW)
        children.append(child)
        try:
            job.add(child)
        except Exception:
            child.kill()
            child.wait(timeout=5)
            raise
        return child

    def host_event(rpc, value):
        # The probe has no parent policy to prepare, so it confirms preparation before the desktop lease.
        if value.get('event') == 'prepare':
            rpc.send({'id': 'prepare-' + value['id'], 'command': 'prepareResult',
                      'requestId': value['id'], 'prepared': True})
        elif value.get('event') == 'acquire':
            granted = lease.acquire(value['id'])
            rpc.send({'id': 'lease-' + value['id'], 'command': 'acquireResult',
                      'requestId': value['id'], 'granted': granted})
        elif value.get('event') == 'released':
            lease.release(value['leaseId'])
        elif value.get('event') == 'fatal':
            raise RuntimeError('PROBE_HOST_FATAL')

    try:
        window = start([str(fixture), str(signal)], base, visible=True)
        deadline = time.monotonic() + 10
        while not signal.exists():
            if window.poll() is not None or time.monotonic() > deadline:
                raise RuntimeError('Fixture failed to open')
            time.sleep(.02)
        window_id = int(signal.read_text())
        host = start([str(runtime / 'bin/node.exe'), str(ROOT / 'src/MyCodex.Infrastructure/ComputerUse/Host/main.mjs')], base)
        def host_closed():
            if lease.owner is not None:
                job.close()
                lease.release(lease.owner)
        host_rpc = Rpc(host, host_event, host_closed)
        assert host_rpc.ready.get(timeout=10).get('protocolVersion') == 2

        for mode in ('decline', 'once', 'session'):
            slot = host_rpc.request('allocate', {'helperHome': str(helper)}, control=True)
            restrict_pipe(slot['pipePath'], sid.strip())
            host_rpc.request('publish', {'slot': slot['slot']}, control=True)
            thread, turn = str(uuid.uuid4()), str(uuid.uuid4())
            host_rpc.request('bind', {'slot': slot['slot'], 'thread': thread}, control=True)
            host_rpc.request('begin', {'slot': slot['slot'], 'turn': turn}, control=True)
            env = base.copy()
            env.update(SKY_CUA_NATIVE_PIPE='1', SKY_CUA_NATIVE_PIPE_DIRECTORY=slot['pipePath'],
                NODE_REPL_NODE_PATH=str(runtime / 'bin/node.exe'), NODE_REPL_NODE_MODULE_DIRS=str(runtime / 'bin/node_modules'),
                NODE_REPL_TRUSTED_CODE_PATHS=str(runtime / 'bin/node_modules'),
                NODE_REPL_TRUSTED_SERVICES=json.dumps({'sky': '@oai/sky/service'}),
                NODE_REPL_UNTRUSTED_ENV_ALLOWLIST='CODEX_WINDOWS_REGISTERED_CORE')
            approvals = []

            def approval(rpc, message):
                params = message.get('params', {})
                meta = params.get('_meta', params.get('meta', {}))
                app = str(meta.get('tool_params', {}).get('app', '')).replace('\\', '/').lower().removeprefix('process:')
                exact = (message.get('method') == 'elicitation/create' and meta.get('connector_id') == 'computer-use'
                    and app in (fixture.name.lower(), str(fixture).replace('\\', '/').lower()))
                accepted = exact and mode != 'decline'
                if exact:
                    approvals.append(mode)
                answer = {'action': 'accept' if accepted else 'decline', 'content': {}}
                if accepted and mode == 'session':
                    answer['_meta'] = {'persist': 'session'}
                rpc.send({'jsonrpc': '2.0', 'id': message['id'], 'result': answer})

            mcp = start([str(runtime / 'bin/node_repl.exe')], env)
            rpc = Rpc(mcp, approval)
            rpc.request('initialize', {'protocolVersion': '2024-11-05', 'capabilities': {'elicitation': {}},
                'clientInfo': {'name': 'mycodex-owned-cua-probe', 'version': '1'}})
            rpc.send({'jsonrpc': '2.0', 'method': 'notifications/initialized', 'params': {}})

            def call(code):
                return rpc.request('tools/call', {'name': 'js', 'arguments': {'code': code,
                    'title': 'Owned Computer Use fixture only', 'timeout_ms': 30000},
                    '_meta': {'x-codex-turn-metadata': {'session_id': thread, 'turn_id': turn}}})

            # Never enumerate or select the user's applications. The target comes from
            # our own fixture process, and every action includes this exact window object.
            select = "globalThis.sky=(await import('@oai/sky')).sky;globalThis.target=await sky.get_window(" + json.dumps({
                'id': window_id, 'app': fixture.name}) + ");"
            # Official sky.get_window_state emits each screenshot through nodeRepl.emitImage.
            capture = "globalThis.state=await sky.get_window_state({window:target,include_text:true,include_screenshot:true});if(!state.accessibility?.tree?.includes('Fixture input'))throw Error('FIXTURE_UIA_MISSING');nodeRepl.write('FIXTURE_UIA_OK');"
            selected = call(select)
            captured = selected if selected.get('isError') else call(capture)
            if mode == 'decline':
                assert captured.get('isError') and approvals, 'Real fixture approval denial was not observed'
                results['denial'] = True
            else:
                assert not captured.get('isError') and approvals, 'Real approval/capture failed'
                assert any(item.get('type') == 'image' for item in captured.get('content', [])), 'No fixture screenshot'
                results[mode + '_screenshot_uia'] = True
                if mode == 'once':
                    count = len(approvals)
                    assert not call(capture).get('isError') and len(approvals) > count, 'Once grant unexpectedly persisted'
                    results['once_does_not_persist'] = True
                if mode == 'session':
                    count = len(approvals)
                    assert not call(capture).get('isError') and len(approvals) == count, 'Session grant was not reused'
                    original_pipe, old_turn = slot['pipePath'], turn
                    host_rpc.request('shutdown', control=True)
                    host.wait(timeout=10)
                    host = start([str(runtime / 'bin/node.exe'), str(ROOT / 'src/MyCodex.Infrastructure/ComputerUse/Host/main.mjs')], base)
                    host_rpc = Rpc(host, host_event, host_closed)
                    assert host_rpc.ready.get(timeout=10).get('protocolVersion') == 2
                    slot = host_rpc.request('allocate', {'helperHome': str(helper), 'pipePath': original_pipe}, control=True)
                    restrict_pipe(slot['pipePath'], sid.strip())
                    host_rpc.request('publish', {'slot': slot['slot']}, control=True)
                    host_rpc.request('bind', {'slot': slot['slot'], 'thread': thread}, control=True)
                    new_turn = str(uuid.uuid4())
                    host_rpc.request('begin', {'slot': slot['slot'], 'turn': new_turn}, control=True)
                    assert turn == old_turn and call(capture).get('isError'), 'Old turn survived host replacement'
                    turn = new_turn
                    assert not call(capture).get('isError'), 'Existing node_repl did not reconnect to restored pipe'
                    assert len(approvals) == count, 'Host recovery lost official session grant'
                    results['same_mcp_host_recovery_and_old_turn_rejection'] = True
                    code = ("var line=state.accessibility.tree.split('\\n').find(x=>x.includes('Fixture input')&&/settable/.test(x));"
                        "var index=Number(line?.match(/^\\s*(\\d+)/)?.[1]);if(!Number.isInteger(index))throw Error('FIXTURE_EDIT_MISSING');"
                        "await sky.click({window:target,element_index:index});await sky.type_text({window:target,text:'官方组件中文输入验证'});"
                        "await sky.press_key({window:target,key:'Tab'});await sky.press_key({window:target,key:'Enter'});")
                    assert not call(code).get('isError'), 'Fixture input failed'
                    result_path = Path(str(signal) + '.result')
                    deadline = time.monotonic() + 3
                    while not result_path.exists() and time.monotonic() < deadline:
                        time.sleep(.02)
                    assert '官方组件中文输入验证' in result_path.read_text(encoding='utf-8-sig')
                    results['session_reuse_and_chinese_keyboard_input'] = True
            host_rpc.request('remove', {'slot': slot['slot']}, control=True)
            slot = None
            mcp.stdin.close()
            mcp.wait(timeout=10)
        host_rpc.request('shutdown', control=True)
        host.wait(timeout=10)
        Path(str(signal) + '.stop').write_text('stop', encoding='utf-8')
        window.wait(timeout=5)
        results['graceful_owned_process_cleanup'] = job.active() == 0
        assert results['graceful_owned_process_cleanup'], 'Owned descendants survived graceful shutdown'
    finally:
        # Kill only this probe's Job, never process names or user desktop applications.
        job.close()
        for child in children:
            try:
                child.wait(timeout=5)
            except subprocess.TimeoutExpired:
                raise RuntimeError('Owned process remained after Job cleanup')
        if host_rpc is not None:
            host_rpc.reader.join(timeout=5)
            assert not host_rpc.reader.is_alive(), 'Host cleanup reader did not finish'
        lease.close()
        results['global_config_unchanged'] = all(digest(path) == before for path, before in original.items())
        (run / 'result.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
        assert results['global_config_unchanged'], 'A real global config changed during the probe'
    print(json.dumps(results, ensure_ascii=False))
    print('Results: ' + str(run / 'result.json'))


if __name__ == '__main__':
    main()
