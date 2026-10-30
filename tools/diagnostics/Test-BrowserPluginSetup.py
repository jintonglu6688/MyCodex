"""Fresh Codex home: install owned browser plugin and load real browser MCP metadata; no page/model actions."""
import argparse
import hashlib
import http.server
import importlib.util
import json
import os
import queue
from pathlib import Path
import subprocess
import threading
import time
import uuid

from computer_use_probe_windows import Job

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('owned_plugin_probe', Path(__file__).with_name('Probe-ComputerUse.py'))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--codex', required=True)
parser.add_argument('--marketplace', default=str(ROOT / 'src/MyCodex.Infrastructure/BrowserUse/PluginAssets'))
args = parser.parse_args()
cli = Path(args.codex).resolve(strict=True)
market = Path(args.marketplace).resolve(strict=True)
assert subprocess.check_output([str(cli), '--version'], text=True).strip().startswith('codex-cli ')
run = ROOT / 'artifacts/browser-plugin-setup' / uuid.uuid4().hex
home = run / 'home'
home.mkdir(parents=True)
captured = queue.Queue()
class MetadataFixture(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        captured.put(json.loads(self.rfile.read(int(self.headers['Content-Length']))))
        self.send_response(400)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(b'{"error":{"message":"Owned metadata fixture completed","type":"invalid_request_error"}}')
    def log_message(self, *args):
        pass
listener = http.server.HTTPServer(('127.0.0.1', 0), MetadataFixture)
threading.Thread(target=listener.serve_forever, daemon=True).start()
(home / 'config.toml').write_text('''model = "owned-model-unused"
model_provider = "owned_fixture"
[model_providers.owned_fixture]
name = "Owned offline fixture"
base_url = "http://127.0.0.1:9/v1"
wire_api = "responses"
requires_openai_auth = false
[features]
plugins = true
'''.replace('http://127.0.0.1:9/v1', 'http://127.0.0.1:' + str(listener.server_address[1]) + '/v1'), encoding='utf-8')
real_config = Path(os.environ.get('CODEX_HOME', Path.home() / '.codex')) / 'config.toml'
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest() if p.is_file() else None
before = digest(real_config)
environment = {k: v for k, v in os.environ.items() if k not in ['OPENAI_API_KEY', 'AZURE_OPENAI_API_KEY', 'CODEX_API_KEY', 'CHATGPT_AUTH_TOKEN']}
environment.update(CODEX_HOME=str(home), OTEL_SDK_DISABLED='true')
job = Job()
child = None
result = {}
try:
    child = subprocess.Popen([str(cli), 'app-server', '--listen', 'stdio://'], cwd=run, env=environment,
                             stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
    job.add(child)
    threading.Thread(target=lambda: list(child.stderr), daemon=True).start()
    rpc = probe.Rpc(child)
    rpc.request('initialize', {'clientInfo': {'name': 'owned-browser-setup-probe', 'version': '1'}, 'capabilities': {'experimentalApi': True}})
    rpc.send({'method': 'initialized', 'params': {}})
    initial = rpc.request('config/read', {'includeLayers': False})['config']
    assert 'cua_repl' not in initial.get('mcp_servers', {})
    result['fresh_home_has_no_browser_tool'] = True
    added = rpc.request('marketplace/add', {'source': str(market)})
    assert added['marketplaceName'] == 'mycodex-browser-use'
    rpc.request('plugin/install', {'marketplacePath': str(market / '.agents/plugins/marketplace.json'), 'pluginName': 'browser-use'})
    plugin_id = 'browser-use@mycodex-browser-use'
    def enable(value):
        rpc.request('config/batchWrite', {'edits': [{'keyPath': 'plugins."' + plugin_id + '".enabled', 'mergeStrategy': 'replace', 'value': value}], 'reloadUserConfig': True})
    enable(False)
    assert rpc.request('config/read', {'includeLayers': False})['config']['plugins'][plugin_id]['enabled'] is False
    enable(True)
    configured = rpc.request('config/read', {'includeLayers': False})['config']
    assert configured['plugins'][plugin_id]['enabled'] is True
    result['owned_plugin_enabled_through_official_api'] = True
    installed = rpc.request('plugin/installed', {})
    (run / 'installed.json').write_text(json.dumps(installed, indent=2), encoding='utf-8')
    runtime = ROOT / 'runtime/browser/b1'
    modules = runtime / 'bin/node_modules'
    server = {'command': str(runtime / 'bin/node.exe'), 'args': [str(modules / '@oai/cua-repl/bin/cua-repl.mjs')], 'enabled': True,
              'env': {'CODEX_CLI_PATH': str(cli), 'NODE_REPL_NODE_PATH': str(runtime / 'bin/node.exe'),
                      'NODE_REPL_NODE_MODULE_DIRS': str(modules), 'CODEX_HOME': str(home),
                      'NODE_REPL_TRUSTED_CODE_PATHS': str(home) + ';' + str(modules),
                      'NODE_REPL_TRUSTED_SERVICES': json.dumps({'browser': str(modules / '@oai/browser-desktop/scripts/browser-service.mjs')}),
                      'CUA_REPL_NODE_REPL_PATH': str(runtime / 'bin/node_repl.exe'), 'CUA_REPL_ENABLED_SURFACES': 'browser',
                      'BROWSER_USE_AVAILABLE_BACKENDS': 'chrome', 'BROWSER_USE_TINYSKY_ENABLED': '0',
                      'BROWSER_USE_ACCESS_EXECUTOR_ID': 'owned-metadata-fixture', 'BROWSER_USE_SECURITY_MODE': ''}}
    routing_marker = 'MyCodex owned native-tool routing protocol fixture'
    office_marker = 'MyCodex owned Office-mode protocol fixture'
    started = rpc.request('thread/start', {'cwd': str(run), 'ephemeral': True,
        'config': {'developer_instructions': routing_marker, 'mcp_servers': {'cua_repl': server}}})
    assert started['thread']['id']
    result['fresh_browser_enabled_thread_started'] = True
    rpc.request('turn/start', {'threadId': started['thread']['id'],
                             'collaborationMode': {'mode': 'default', 'settings': {'model': 'owned-model-unused',
                                 'reasoning_effort': None, 'developer_instructions': office_marker}},
                             'input': [{'type': 'text', 'text': 'Return an owned offline fixture marker only.'}]})
    request = captured.get(timeout=25)
    developer_messages = json.dumps([item for item in request.get('input', []) if item.get('role') == 'developer'])
    assert routing_marker in developer_messages and office_marker in developer_messages
    result['thread_routing_and_office_instructions_preserved'] = True
    names = []
    for item in request.get('tools', []):
        names.append(item.get('name', ''))
        if item.get('type') == 'namespace':
            names.extend(item['name'] + '__' + nested['name'] for nested in item.get('tools', []))
    (run / 'tool-shapes.json').write_text(json.dumps([
        {'type': item.get('type'), 'name': item.get('name'), 'keys': list(item),
         'nestedNames': [value.get('name') for value in item.get('tools', [])]} for item in request.get('tools', [])], indent=2), encoding='utf-8')
    print('Tool metadata evidence: ' + str(run / 'tool-shapes.json'))
    assert 'mcp__cua_repl__js' in names, 'Browser MCP tool missing from actual conversation tool metadata'
    result['real_cua_repl_js_in_conversation_metadata'] = True
    statuses = rpc.request('mcpServerStatus/list', {})
    (run / 'mcp-status.json').write_text(json.dumps(statuses, indent=2), encoding='utf-8')
    result['real_global_config_unchanged'] = digest(real_config) == before
    assert result['real_global_config_unchanged']
    result['no_external_model_or_page_actions'] = True
    (run / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))
    print('Owned evidence: ' + str(run))
finally:
    if child:
        try:
            child.stdin.close()
            child.wait(timeout=3)
        except (OSError, subprocess.TimeoutExpired):
            pass
    job.close()
    listener.shutdown()
    listener.server_close()
