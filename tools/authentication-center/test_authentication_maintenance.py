"""Offline Windows deployment regression; uses only a new directory under artifacts."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import tempfile
import threading
import uuid
import winreg


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--previous-package", type=Path, help="Verify an older package upgrades to --package")
    parser.add_argument("--application", required=True, type=Path)
    parser.add_argument("--installer", type=Path, help="An installer compiled with AuthenticationInstallTest, never a production installer")
    args = parser.parse_args()
    if args.previous_package and args.installer:
        parser.error("Run cross-version package and fresh installer checks separately")
    repository = Path(__file__).resolve().parents[2]
    root = Path(tempfile.mkdtemp(prefix="auth-maintenance-", dir=repository / "artifacts"))
    install = root / "install with spaces"
    package = install / "runtime" / "auth-core"
    package.mkdir(parents=True)
    for name in ("mycodex-auth-host.exe", "mycodex-auth-host.manifest.json", "LICENSE"):
        shutil.copy2((args.previous_package or args.package) / name, package / name)
    for source in args.application.parent.iterdir():
        if source.suffix == ".dll" or source.name in (args.application.name, args.application.name + ".config"):
            shutil.copy2(source, install / (source.name.replace(args.application.name, "MyCodex.exe") if source.name.startswith(args.application.name) else source.name))
    executable = package / "mycodex-auth-host.exe"
    expected_hash = hashlib.sha256(executable.read_bytes()).hexdigest()
    homes = [root / "home one", root / "home two"]
    stores = [root / "private one", root / "private two"]
    for directory in homes + stores:
        directory.mkdir()
    children = []
    flags = subprocess.CREATE_NO_WINDOW
    state = root / "maintenance.json"
    script = repository / "tools" / "installer" / "Update-AuthenticationHosts.ps1"

    def run_installer(binary, installing=True):
        arguments = [str(binary.resolve()), "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
            "/LOG=" + str(root / ("install.log" if installing else "uninstall.log"))]
        if installing:
            arguments += ["/DIR=" + str(install.resolve()), "/NOICONS", "/MERGETASKS=!desktopicon"]
        result = subprocess.run(arguments, timeout=180, creationflags=flags,
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        assert result.returncode == 0, "Isolated installer failed; inspect its test-directory log"

    if args.installer:
        # Remove only our freshly staged fixture to exercise a genuinely fresh install.
        assert install.resolve().is_relative_to((repository / "artifacts").resolve())
        shutil.rmtree(install)
        run_installer(args.installer)
        assert hashlib.sha256(executable.read_bytes()).hexdigest() == expected_hash
        print("Checked fresh isolated install", flush=True)

    def command(index, verb, binary=executable):
        return [str(binary), verb, "--data-dir", str(stores[index]), "--codex-home", str(homes[index])]

    def rpc(index, method, params=None, binary=executable):
        result = subprocess.run(command(index, "rpc", binary),
            input=json.dumps({"jsonrpc": "2.0", "id": 1, "method": method, "params": params or {}}),
            capture_output=True, encoding="utf-8", timeout=20, creationflags=flags)
        assert result.returncode == 0, "RPC bridge failed"
        reply = json.loads(result.stdout)
        assert "error" not in reply, "RPC returned a fixed error"
        return reply["result"]

    def start(index, binary=executable):
        process = subprocess.Popen(command(index, "serve", binary), stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, encoding="utf-8", creationflags=flags)
        children.append(process)
        ready = queue.Queue()
        threading.Thread(target=lambda: ready.put(process.stdout.readline()), daemon=True).start()
        assert json.loads(ready.get(timeout=20))["event"] == "ready"
        return process

    def maintenance(action, success=True, expected_error=None):
        # A resumed resident may inherit the shell's standard handles. Use a file,
        # not a captured pipe whose EOF would wait for the independent service.
        with tempfile.TemporaryFile() as errors:
            arguments = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script),
                "-Action", action, "-InstallRoot", str(install), "-StatePath", str(state)]
            if action == "Finalize":
                arguments += ["-ExpectedManifestPath", str(args.package / "mycodex-auth-host.manifest.json")]
            result = subprocess.run(arguments,
                stdout=subprocess.DEVNULL, stderr=errors, timeout=75, creationflags=flags)
            errors.seek(0)
            error_text = errors.read().decode(errors="replace")
            assert (result.returncode == 0) == success, "Unexpected maintenance outcome: " + action + "\n" + error_text
            if expected_error:
                assert expected_error in error_text, "Maintenance failed for the wrong reason: " + error_text
        print("Checked maintenance " + action, flush=True)

    def apply(index, provider):
        selection = {"providerId": provider["id"], "expectedVersion": provider["version"]}
        plan = rpc(index, "gui/provider/preflight", selection)
        selection.update(expectedFingerprint=plan["fingerprint"], operationId=uuid.uuid4().hex)
        assert rpc(index, "gui/provider/apply", selection)["status"] == "applied"

    registration = None
    try:
        first, second = start(0), start(1)
        # Reject a legacy installation process without sending it any old RPC.
        legacy = install / "runtime" / "auth-center" / executable.name
        legacy.parent.mkdir(parents=True)
        shutil.copy2(executable, legacy)
        legacy_home, legacy_store = root / "legacy home", root / "legacy store"
        legacy_home.mkdir()
        legacy_store.mkdir()
        homes.append(legacy_home)
        stores.append(legacy_store)
        legacy_process = start(2, legacy)
        try:
            maintenance("Prepare", success=False)
            assert legacy_process.poll() is None
            assert first.poll() is None and second.poll() is None
        finally:
            legacy_process.terminate()
            legacy_process.wait(timeout=20)
        assert legacy.parent.resolve().is_relative_to(root.resolve())
        shutil.rmtree(legacy.parent)
        routed = rpc(0, "gui/provider/save", {"id": "converted", "name": "Synthetic", "kind": "chat_completions",
            "presetId": "custom", "commonConfigEnabled": True, "baseUrl": "http://127.0.0.1:9/v1", "apiKey": "synthetic-upstream-key", "model": "model"})
        apply(0, routed)
        config = (homes[0] / "config.toml").read_bytes()
        assert rpc(0, "status")["route"]["running"] is True
        maintenance("Uninstall", success=False)
        assert first.poll() is None, "Uninstall must not stop an active route"
        maintenance("Prepare")
        first.wait(timeout=20)
        second.wait(timeout=20)
        maintenance("Prepare")  # A retry must retain the already stopped targets.
        assert len(json.loads(state.read_text(encoding="utf-8"))["targets"]) == 2
        executable.write_bytes(b"simulate interrupted replacement")
        maintenance("Resume")
        assert hashlib.sha256(executable.read_bytes()).hexdigest() == expected_hash
        assert rpc(0, "status")["route"]["running"] is True
        assert len(rpc(1, "gui/provider/list")["providers"]) == 0
        assert (homes[0] / "config.toml").read_bytes() == config
        maintenance("Resume")  # A matching resident replies with plain backend_already_running.
        assert rpc(0, "status")["route"]["accepting"] is True
        assert (homes[0] / "config.toml").read_bytes() == config
        if not args.previous_package:
            maintenance("Finalize")

        if args.previous_package:
            replacement = json.loads((args.package / "mycodex-auth-host.manifest.json").read_text(encoding="utf-8-sig"))
            previous_version = rpc(0, "status")["hostVersion"]
            assert previous_version != replacement["hostVersion"], "Cross-version check needs different versions"
            maintenance("Prepare")
            for name in ("mycodex-auth-host.exe", "mycodex-auth-host.manifest.json", "LICENSE"):
                shutil.copy2(args.package / name, package / name)
            maintenance("Finalize")
            status = rpc(0, "status")
            assert status["hostVersion"] == replacement["hostVersion"]
            assert status["sha256"] == replacement["sha256"]
            assert status["route"]["running"] and status["route"]["accepting"]
            assert any(item["id"] == "converted" for item in rpc(0, "gui/provider/list")["providers"])
            assert len(rpc(1, "gui/provider/list")["providers"]) == 0
            assert (homes[0] / "config.toml").read_bytes() == config
            print("Checked package upgrade", previous_version, "->", status["hostVersion"], flush=True)

        if args.installer:
            run_installer(args.installer)
            assert rpc(0, "status")["route"]["running"] is True
            assert (homes[0] / "config.toml").read_bytes() == config
            print("Checked actual installer update with active idle route", flush=True)

        # Simulate the Windows Run entry by invoking exactly its hidden app mode.
        rpc(0, "backend/shutdown", {"forUpdate": True})
        app = install / "MyCodex.exe"
        key = hashlib.sha256(str(app.resolve()).upper().encode("utf-8")).hexdigest().upper()
        home_key = hashlib.sha256(str(homes[0].resolve()).upper().encode("utf-8")).hexdigest().upper()
        registration = "Software\\MyCodex\\AuthenticationHosts\\" + key
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, registration) as reg:
            winreg.SetValueEx(reg, home_key, 0, winreg.REG_MULTI_SZ,
                [str(executable), str(homes[0]), str(stores[0])])
        result = subprocess.run([str(app), "--resume-authentication-hosts"], timeout=40, creationflags=flags)
        assert result.returncode == 0
        assert rpc(0, "status")["route"]["running"] is True
        assert (homes[0] / "config.toml").read_bytes() == config
        direct = rpc(0, "gui/provider/save", {"id": "direct", "name": "Synthetic direct", "kind": "responses",
            "presetId": "custom", "commonConfigEnabled": True, "baseUrl": "http://127.0.0.1:9/v1", "apiKey": "synthetic-key", "model": "model"})
        apply(0, direct)
        rpc(0, "backend/shutdown", {"forUpdate": False})
        other = root / "other installation" / executable.name
        other.parent.mkdir()
        shutil.copy2(executable, other)
        foreign = start(0, other)
        maintenance("Uninstall", success=False, expected_error="authentication_update_installation_mismatch")
        assert foreign.poll() is None, "Matching binary hashes must not permit stopping a different installation"
        rpc(0, "backend/shutdown", {"forUpdate": True}, other)
        foreign.wait(timeout=20)
        start(0)
        maintenance("Uninstall")
        registration = None  # Uninstall removes the isolated per-installation registration.
        assert (homes[0] / "config.toml").exists(), "Uninstall must retain user configuration"
        assert (stores[0] / "cc-switch.db").exists(), "Uninstall must retain provider data"
        if args.installer:
            run_installer(install / "unins000.exe", installing=False)
            assert not executable.exists()
            assert (stores[0] / "cc-switch.db").exists()
            assert (homes[0] / "config.toml").exists()
            print("Checked actual uninstall and retained user state", flush=True)
        print("PASS: route-aware uninstall, safe stop, retry journal, corrupt-package recovery, hidden sign-in resume, data retention")
    finally:
        if registration:
            try:
                winreg.DeleteKey(winreg.HKEY_CURRENT_USER, registration)
            except FileNotFoundError:
                pass
        for index in range(2):
            try:
                rpc(index, "backend/shutdown", {"forUpdate": True})
            except Exception:
                pass
        for process in children:
            if process.poll() is None:
                process.wait(timeout=20)


if __name__ == "__main__":
    main()
