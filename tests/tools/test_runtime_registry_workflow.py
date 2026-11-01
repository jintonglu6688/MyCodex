"""Run the actual legacy-manifest workflow against fake registries and GitCode."""
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
import textwrap
import unittest
import urllib.error
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / '.gitcode/workflows/sync-standard-runtime.yml'
CODE = textwrap.dedent(WORKFLOW.read_text(encoding='utf-8').split("python3 - <<'PY'\n", 1)[1].rsplit('\n          PY', 1)[0])
ARCHIVE = b'verified runtime archive'
VERSION = '0.165.0'
FILE = 'codex-0.165.0-win32-x64.tgz'
TOKEN = 'fixture-secret-token'
CURRENT_ASSET = 'https://registry.npmjs.org/@openai/codex/-/' + FILE


class RuntimeWorkflowTests(unittest.TestCase):
    def run_workflow(self, mode='ok', existing='0.164.0', other_architecture=False, published='missing',
                     asset_url=None, upload_failures=0, check_error=None):
        requests = []
        uploads = []
        releases = []
        self.git_calls = []
        self.git_envs = []
        failures = {'upload': upload_failures}
        digest = 'sha512-' + base64.b64encode(hashlib.sha512(ARCHIVE).digest()).decode()

        def http(request, timeout=None):
            url = request.full_url if hasattr(request, 'full_url') else request
            method = request.get_method() if hasattr(request, 'get_method') else 'GET'
            requests.append((method, url))
            if mode == 'offline_metadata' and url.startswith('https://registry.npmjs.org/'):
                raise urllib.error.URLError('official metadata unavailable')
            if 'attach_files/stable.json/download' in url:
                if check_error is not None:
                    raise urllib.error.HTTPError(url, check_error, 'check failed', None, None)
                if uploads:
                    return io.BytesIO(json.dumps(uploads[-1]).encode())
                if published == 'missing':
                    raise urllib.error.HTTPError(url, 404, 'missing', None, None)
                if published == 'corrupt':
                    return io.BytesIO(b'<html>')
                return io.BytesIO(manifest_path.read_bytes())
            if url.endswith('/latest'):
                data = {'version': VERSION}
            elif url.endswith('/0.165.0-win32-x64'):
                data = {'dist': {'tarball': 'https://registry.npmjs.org/@openai/codex/-/' + FILE,
                                 'integrity': digest, 'shasum': hashlib.sha1(ARCHIVE).hexdigest()}}
            elif url.endswith('.tgz'):
                if mode == 'missing_mirror' and 'npmmirror.com' in url:
                    raise urllib.error.URLError('mirror not synchronized')
                corrupt = mode == 'both_corrupt' or (mode == 'corrupt_mirror' and 'npmmirror.com' in url)
                return io.BytesIO(b'corrupt' if corrupt else ARCHIVE)
            elif 'upload_url?' in url:
                self.assertIn('file_name=stable.json', url)
                data = {'url': 'https://upload.fixture/stable.json', 'headers': {}}
            elif method == 'POST' and url.endswith('/releases'):
                from urllib.parse import parse_qs
                releases.append(parse_qs(request.data.decode())['tag_name'][0])
                data = {}
            elif method == 'PUT' and url == 'https://upload.fixture/stable.json':
                if failures['upload'] > 0:
                    failures['upload'] -= 1
                    raise urllib.error.URLError('upload interrupted')
                uploads.append(json.loads(request.data))
                return io.BytesIO(b'')
            else:
                raise AssertionError('Unexpected HTTP request: ' + url)
            return io.BytesIO(json.dumps(data).encode())

        class Result:
            returncode = 1
            stdout = 'fixture-tag'

        def git(arguments, **kwargs):
            self.git_calls.append(arguments)
            self.git_envs.append(kwargs.get('env'))
            return Result()

        (ROOT / 'temp').mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='runtime-workflow-', dir=ROOT / 'temp') as directory:
            root = Path(directory)
            (root / 'updates').mkdir()
            manifest = {'app': {'version': '8.0.5'}, 'runtimes': [{
                'source': 'Bundled', 'targetTriple': 'x86_64-pc-windows-msvc', 'version': existing,
                'asset': {'url': asset_url or 'https://api.gitcode.com/old-package/download'}}]}
            if other_architecture:
                manifest['runtimes'].append({'source': 'Bundled', 'targetTriple': 'aarch64-pc-windows-msvc',
                                             'version': '0.166.0', 'asset': {'url': 'https://fixture/keep'}})
            manifest_path = root / 'updates/stable.json'
            manifest_path.write_text(json.dumps(manifest), encoding='utf-8')
            previous = Path.cwd()
            try:
                os.chdir(root)
                with patch.dict(os.environ, {'GITCODE_TOKEN': TOKEN, 'GITCODE_OWNER': 'owner', 'GITCODE_REPO': 'updates'}), \
                     patch('urllib.request.urlopen', http), \
                     patch('subprocess.run', git), \
                     patch('time.sleep'):
                    if mode == 'both_corrupt':
                        with self.assertRaisesRegex(ValueError, 'integrity'):
                            exec(compile(CODE, str(WORKFLOW), 'exec'), {})
                        self.assertEqual(json.loads(manifest_path.read_text()), manifest)
                    elif upload_failures >= 3 or check_error is not None:
                        with self.assertRaises(OSError):
                            exec(compile(CODE, str(WORKFLOW), 'exec'), {})
                    else:
                        try:
                            exec(compile(CODE, str(WORKFLOW), 'exec'), {})
                        except SystemExit as result:
                            self.assertEqual(result.code, 0)
            finally:
                os.chdir(previous)
        return requests, uploads, releases

    def test_only_small_legacy_manifest_is_uploaded(self):
        _, uploads, releases = self.run_workflow()
        self.assertEqual(releases, ['updates-stable'])
        self.assertEqual(len(uploads), 1)
        self.assertEqual(uploads[0]['app']['version'], '8.0.5')
        package = uploads[0]['runtimes'][0]
        self.assertEqual(package['version'], VERSION)
        self.assertTrue(package['asset']['url'].startswith('https://registry.npmmirror.com/'))
        self.assertEqual(package['asset']['sha256'], hashlib.sha256(ARCHIVE).hexdigest())

    def test_missing_or_corrupt_mirror_uses_identical_official_archive(self):
        for mode in ('missing_mirror', 'corrupt_mirror'):
            with self.subTest(mode=mode):
                requests, uploads, _ = self.run_workflow(mode)
                self.assertEqual(len([url for _, url in requests if url.endswith('.tgz')]), 2)
                package = uploads[0]['runtimes'][0]['asset']
                self.assertTrue(package['url'].startswith('https://registry.npmjs.org/'))
                self.assertEqual(package['sha256'], hashlib.sha256(ARCHIVE).hexdigest())

    def test_offline_official_metadata_falls_back(self):
        _, uploads, _ = self.run_workflow('offline_metadata')
        self.assertEqual(uploads[0]['runtimes'][0]['version'], VERSION)

    def test_two_corrupt_copies_do_not_publish(self):
        _, uploads, releases = self.run_workflow('both_corrupt')
        self.assertEqual(uploads, [])
        self.assertEqual(releases, [])

    def test_unmanaged_architectures_keep_their_existing_version(self):
        _, uploads, _ = self.run_workflow(other_architecture=True)
        self.assertEqual(uploads[0]['runtimes'][1]['version'], '0.166.0')
        self.assertEqual(uploads[0]['runtimes'][1]['asset']['url'], 'https://fixture/keep')

    def test_lagging_metadata_does_not_downgrade_the_published_manifest(self):
        _, uploads, releases = self.run_workflow(existing='0.166.0', published='current')
        self.assertEqual(uploads, [])
        self.assertEqual(releases, [])

    def test_git_commands_never_carry_the_token(self):
        self.run_workflow()
        pushes = [call for call in self.git_calls if call[:2] == ['git', 'push']]
        self.assertTrue(pushes)
        self.assertFalse([call for call in self.git_calls if any(TOKEN in part for part in call)])
        self.assertFalse([env for call, env in zip(self.git_calls, self.git_envs)
                          if call[:2] == ['git', 'push'] and 'GIT_ASKPASS' not in env])

    def test_current_manifest_with_matching_attachment_publishes_nothing(self):
        _, uploads, releases = self.run_workflow(existing=VERSION, asset_url=CURRENT_ASSET, published='current')
        self.assertEqual(uploads, [])
        self.assertEqual(releases, [])
        self.assertFalse([call for call in self.git_calls if call[:2] == ['git', 'push']])

    def test_retry_repairs_a_missing_or_corrupt_attachment(self):
        for published in ('missing', 'corrupt'):
            with self.subTest(published=published):
                requests, uploads, releases = self.run_workflow(existing=VERSION, asset_url=CURRENT_ASSET, published=published)
                self.assertEqual(releases, ['updates-stable'])
                self.assertEqual(uploads[0]['runtimes'][0]['version'], VERSION)
                self.assertFalse([url for _, url in requests if url.endswith('.tgz')])

    def test_interrupted_upload_is_retried_in_the_same_run(self):
        _, uploads, releases = self.run_workflow(upload_failures=1)
        self.assertEqual(releases, ['updates-stable', 'updates-stable'])
        self.assertEqual(len(uploads), 1)

    def test_upload_that_keeps_failing_fails_the_run(self):
        _, uploads, releases = self.run_workflow(upload_failures=3)
        self.assertEqual(uploads, [])
        self.assertEqual(len(releases), 3)

    def test_refused_attachment_check_never_deletes_the_release(self):
        _, uploads, releases = self.run_workflow(existing=VERSION, asset_url=CURRENT_ASSET, check_error=403)
        self.assertEqual(uploads, [])
        self.assertEqual(releases, [])
        self.assertFalse([call for call in self.git_calls if call[:2] == ['git', 'push']])


if __name__ == '__main__':
    unittest.main()
