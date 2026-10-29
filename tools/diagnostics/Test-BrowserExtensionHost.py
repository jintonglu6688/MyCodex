import argparse
import ctypes
import hashlib
import json
import msvcrt
import os
from pathlib import Path
import queue
import shutil
import struct
import subprocess
import tempfile
import threading
import time


def encode(value):
    data = json.dumps(value).encode()
    return struct.pack('<I', len(data)) + data


def receive(stream):
    def exact(count):
        data = b''
        while len(data) < count:
            chunk = stream.read(count - len(data))
            if not chunk:
                raise EOFError('Incomplete fixture frame')
            data += chunk
        return data
    length, = struct.unpack('<I', exact(4))
    assert 0 < length <= 65536, length
    return json.loads(exact(length))


def timed_receive(stream):
    result = queue.Queue()
    def worker():
        try:
            result.put(receive(stream))
        except Exception as exc:
            result.put(exc)
    threading.Thread(target=worker, daemon=True).start()
    value = result.get(timeout=3)
    if isinstance(value, Exception):
        raise value
    return value


def pipes():
    return {name for name in os.listdir('\\\\.\\pipe\\') if name.startswith('codex-browser-use')}


parser = argparse.ArgumentParser()
parser.add_argument('--host', required=True)
args = parser.parse_args()
source = Path(args.host).resolve(strict=True)
assert source.is_file() and os.name == 'nt'
pin = json.loads((Path(__file__).resolve().parents[1] / 'runtime' / 'browser-extension-host.json').read_text(encoding='utf-8'))
assert hashlib.sha256(source.read_bytes()).hexdigest() == pin['sha256'].lower(), 'Host does not match reviewed pin'
server_pid = ctypes.WinDLL('kernel32', use_last_error=True).GetNamedPipeServerProcessId
server_pid.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
server_pid.restype = ctypes.c_int
root = Path(tempfile.mkdtemp(prefix='mycodex-nativehost-standalone-'))
host = root / 'extension-host.exe'
shutil.copy2(source, host)
assert hashlib.sha256(host.read_bytes()).hexdigest() == pin['sha256'].lower(), 'Copied fixture host changed'
environment = {key: value for key, value in os.environ.items()
               if not key.startswith(('CODEX_', 'BROWSER_USE_'))}
for name in ['local', 'home', 'roaming', 'codex-home']:
    (root / name).mkdir()
environment.update(LOCALAPPDATA=str(root / 'local'), APPDATA=str(root / 'roaming'), HOME=str(root / 'home'),
                   USERPROFILE=str(root / 'home'), CODEX_HOME=str(root / 'codex-home'))
for extension, family in [('hehggadaopoacecdllhhajmbjkdcmajg', 'chrome'),
                          ('odlomjlbamekndcpllcnffbgeohgkmjh', 'edge')]:
    before = pipes()
    child = subprocess.Popen([str(host), 'chrome-extension://' + extension + '/'],
                             cwd=root, env=environment, stdin=subprocess.PIPE,
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             creationflags=subprocess.CREATE_NO_WINDOW)
    client = None
    try:
        endpoint = None
        for unused in range(80):
            created = pipes() - before
            if len(created) == 1:
                endpoint = next(iter(created))
                break
            time.sleep(.025)
        assert endpoint, 'Owned native-host endpoint was not published uniquely'
        client = open('\\\\.\\pipe\\' + endpoint, 'r+b', buffering=0)
        pid = ctypes.c_ulong()
        assert server_pid(msvcrt.get_osfhandle(client.fileno()), ctypes.byref(pid)), ctypes.get_last_error()
        assert pid.value == child.pid, 'Refusing to send data to a foreign native host'
        client.write(encode({'jsonrpc': '2.0', 'id': 'owned-get-info', 'method': 'getInfo', 'params': {}}))
        forwarded = timed_receive(child.stdout)
        assert forwarded['method'] == 'getInfo' and forwarded['id'] != 'owned-get-info'
        info = {'type': 'extension', 'family': family, 'name': 'Owned fixture',
                'metadata': {'extensionInstanceId': 'fixture'}}
        child.stdin.write(encode({'jsonrpc': '2.0', 'id': forwarded['id'], 'result': info}))
        child.stdin.flush()
        response = timed_receive(client)
        assert response == {'jsonrpc': '2.0', 'id': 'owned-get-info', 'result': info}, response
        print(json.dumps({'family': family, 'pass': True, 'protocol': 'configless-native-pipe-roundtrip'}))
    finally:
        if client is not None:
            client.close()
        child.stdin.close()
        try:
            child.wait(timeout=2)
        except subprocess.TimeoutExpired:
            child.terminate()
            child.wait(timeout=2)
        assert child.returncode == 0, child.returncode
        assert child.stderr.read() == b''
assert not (root / 'extension-host-config.json').exists()
assert not (root / 'local' / 'OpenAI' / 'Codex' / 'chrome-native-hosts-v2.json').exists()
assert root.resolve().parent == Path(tempfile.gettempdir()).resolve()
assert root.name.startswith('mycodex-nativehost-standalone-')
shutil.rmtree(root)
print(json.dumps({'hostSha256': pin['sha256'].lower(), 'checksPassed': 2,
                  'realBrowserLaunched': False, 'officialAppLaunched': False, 'fixtureRemoved': True}))
