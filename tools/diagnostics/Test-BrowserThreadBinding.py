"""Owned Codex 0.164 thread/MCP configuration probe; no turns, models, browser, or UI."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import time
import uuid

from computer_use_probe_windows import Job

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('owned_cua_probe', Path(__file__).with_name('Probe-ComputerUse.py'))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)

FAKE_MCP = r'''import json,os,sys
from pathlib import Path
Path(sys.argv[1]).write_text(json.dumps({'sentinel':os.environ.get('MYCODEX_BINDING_SENTINEL')}),encoding='utf-8')
for line in sys.stdin:
 try:
  message=json.loads(line)
  if 'id' not in message: continue
  method=message.get('method')
  if method=='initialize':
   result={'protocolVersion':message.get('params',{}).get('protocolVersion','2024-11-05'),'capabilities':{'tools':{}},'serverInfo':{'name':'owned-fixture','version':'1'}}
  elif method=='tools/list': result={'tools':[]}
  elif method=='resources/list': result={'resources':[]}
  elif method=='prompts/list': result={'prompts':[]}
  else: result={}
  print(json.dumps({'jsonrpc':'2.0','id':message['id'],'result':result}),flush=True)
 except (ValueError,KeyError): pass
'''


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--codex', required=True, help='Explicit existing Codex 0.164 executable.')
    args = parser.parse_args()
    cli = Path(args.codex).resolve()
    assert cli.is_file() and cli.name.lower() == 'codex.exe'
    version = subprocess.check_output([str(cli), '--version'], text=True).strip()
    assert version == 'codex-cli 0.164.0', 'This probe is pinned to the selected MyCodex runtime contract.'
    run = ROOT / 'artifacts/browser-thread-binding' / uuid.uuid4().hex
    home = run / 'home'
    home.mkdir(parents=True)
    server = run / 'fixture-mcp.py'
    server.write_text(FAKE_MCP, encoding='utf-8')
    market = run / 'marketplace'
    plugin = market / 'plugins/browser-probe'
    write_json(market / '.agents/plugins/marketplace.json', {
        'name': 'owned-browser-probe', 'plugins': [{'name': 'browser-probe',
            'source': {'source': 'local', 'path': './plugins/browser-probe'},
            'policy': {'installation': 'AVAILABLE', 'authentication': 'ON_INSTALL'}, 'category': 'Productivity'}]})
    write_json(plugin / '.codex-plugin/plugin.json', {'name': 'browser-probe', 'version': '1.0.0'})

    def mcp(name):
        return {'command': sys.executable, 'args': [str(server), str(run / (name + '.json'))],
                'env': {'MYCODEX_BINDING_SENTINEL': name}, 'enabled': True}

    write_json(plugin / '.mcp.json', {'mcpServers': {'cua_repl': mcp('plugin-cua')}})
    config = home / 'config.toml'
    config.write_text('\n'.join([
        'model = "owned-model-unused"', 'model_provider = "owned_probe"',
        '[model_providers.owned_probe]', 'name = "Owned offline fixture"',
        'base_url = "http://127.0.0.1:9/v1"', 'wire_api = "responses"', 'requires_openai_auth = false',
        '[mcp_servers.unrelated]', 'command = ' + json.dumps(sys.executable),
        'args = ' + json.dumps(mcp('unrelated')['args']),
        'env = { MYCODEX_BINDING_SENTINEL = "unrelated" }', 'enabled = true',
        '[features]', 'plugins = true', ''
    ]), encoding='utf-8')
    real_config = Path(os.environ.get('CODEX_HOME', Path.home() / '.codex')) / 'config.toml'
    original = digest(real_config)
    env = dict(os.environ)
    for name in ['OPENAI_API_KEY', 'AZURE_OPENAI_API_KEY', 'CODEX_API_KEY', 'CHATGPT_AUTH_TOKEN']:
        env.pop(name, None)
    env.update(CODEX_HOME=str(home), OTEL_SDK_DISABLED='true')
    job = Job()
    child = None
    result = {'runtime': version, 'no_turn_or_model_request': True}
    try:
        child = subprocess.Popen([str(cli), 'app-server', '--listen', 'stdio://'], cwd=run, env=env,
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
        job.add(child)
        def drain_stderr():
            for _ in child.stderr:
                pass
        threading.Thread(target=drain_stderr, daemon=True).start()
        rpc = probe.Rpc(child)
        rpc.request('initialize', {'clientInfo': {'name': 'owned-browser-binding-probe', 'version': '1'},
            'capabilities': {'experimentalApi': True}})
        rpc.send({'method': 'initialized', 'params': {}})
        rpc.request('marketplace/add', {'source': str(market)})
        rpc.request('plugin/install', {'marketplacePath': str(market / '.agents/plugins/marketplace.json'), 'pluginName': 'browser-probe'})

        def wait_marker(label):
            marker = run / (label + '.json')
            deadline = time.monotonic() + 10
            while not marker.is_file() and time.monotonic() < deadline:
                time.sleep(.02)
            return marker.is_file() and json.loads(marker.read_text())['sentinel'] == label

        rpc.request('thread/start', {'cwd': str(run), 'ephemeral': True})
        result['plugin_defined_server_initially_started'] = wait_marker('plugin-cua')
        for label in ['thread-one', 'thread-two', 'thread-three']:
            if label == 'thread-three':
                rpc.request('config/batchWrite', {'edits': [{'keyPath': 'mcp_servers.cua_repl',
                    'value': mcp('global-cua'), 'mergeStrategy': 'replace'}]})
            started = rpc.request('thread/start', {'cwd': str(run), 'ephemeral': True,
                'config': {'mcp_servers': {'cua_repl': mcp(label)}}})
            assert started.get('thread', {}).get('id'), 'Owned thread was not created'
            result[label + '_override_applied'] = wait_marker(label)
        result['unrelated_server_preserved'] = (run / 'unrelated.json').is_file()
        result['real_global_config_unchanged'] = digest(real_config) == original
        write_json(run / 'result.json', result)
        assert all(value is True for key, value in result.items() if key != 'runtime'), 'Thread/MCP contract probe failed: ' + json.dumps(result)
    finally:
        if child:
            try:
                child.stdin.close()
                child.wait(timeout=3)
            except (OSError, subprocess.TimeoutExpired):
                pass
        job.close()
    print(json.dumps(result))
    print('Owned evidence: ' + str(run / 'result.json'))


if __name__ == '__main__':
    main()
