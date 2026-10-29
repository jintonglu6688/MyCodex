# Browser Use runtime

MyCodex packages the reviewed external-browser runtime independently of its existing native Computer Use runtime. The browser access host belongs to MyCodex; the browser service and its website, history, upload and download permission checks remain the original runtime files.

| Component | Reviewed version |
|---|---|
| Runtime archive | `0.0.35/20261022235854-0e24579b4572`, Windows x64 |
| Node | `24.22.1-cua.1` |
| `@oai/browser-desktop` | `0.1.1` |
| `@oai/cua-repl` | `0.1.2` |

`tools/runtime/browser-use-bundle.json` pins these versions and the SHA256 of the complete file inventory. The preparation script verifies the original Node, Node REPL and Windows helper Authenticode signatures when preparing the inventory. At application startup, `BrowserUseRuntimeBundle` verifies the pinned inventory, file sizes, every file hash, required entry points, package metadata and the absence of extra files or filesystem links.

The payload uses the compact directory `runtime/browser/b1/`, with `bin/` directly below it. The bundle ID identifies a reviewed layout; component versions are in the pin and manifest. Keeping a long version directory plus an extra `cua_node/` layer caused functional JavaScript and LevelDB source paths to exceed Windows MAX_PATH in the default test build output. This layout retains all 2392 original files without pruning dependencies or changing Windows settings. For `E:\MyCodex`, the longest default project-reference output path is 255 characters for Debug and 257 for Release; longer checkout or installation paths still require separate verification.

Prepare or verify the pinned repository payload:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/runtime/prepare-browser-use-runtime.ps1
```

When absent, preparation looks for a unique installed official runtime matching the pin. `-SourceRuntime <path>` supplies a reviewed source explicitly. Missing inventories are recreated only if their computed hash matches the checked-in pin. `-UpdatePin` is for an intentional reviewed component update; it must not be used just to silence an integrity error.

Installer verification is read-only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/runtime/prepare-browser-use-runtime.ps1 -VerifyRoot <package-root>/runtime/browser/b1
```

The generated payload is ignored by Git. A successful runtime verification does not establish that a browser extension is connected or that actual browser actions and permission prompts have passed acceptance testing.

## MyCodex integration

The original failure came from loading the official app-managed `cua_repl` server in MyCodex without a browser authorization host. MyCodex now supplies its own per-thread executor identity, protected named pipe and temporary Ed25519 signing identity. The binding follows the existing connection/thread/turn lifecycle; stale, cancelled, foreign and replayed authorization requests are rejected. Host callbacks re-read effective configuration and managed requirements before approving the feature gate.

The `cua_repl` server uses the reviewed browser-only runtime and external-extension backend. It does not inherit the official app's desktop pipe. Native `node_repl` keeps its separate desktop binding. Explicit external-browser plugin/feature disables and managed denials are preserved. Disabling native desktop control does not itself disable an explicitly enabled external browser. Components are still shipped together in the application package.

Origin, history, upload, download and full-CDP permission checks stay in the original browser service. Browser tool approvals follow the same per-thread policy as other MCP tools: Full Access (including the legacy auto-approve mode) responds with Allow once and records the automatic decision without opening a dialog or saving a site grant. Other permission modes retain the existing accessible MCP confirmation dialog. Requests that explicitly require user input, strict review, URL authentication or additional form fields remain on the existing manual path. A persistent origin decision is labelled “Always allow this site”. The signed feature gate does not grant access to all websites.

Enable the external-browser plugin in MyCodex's existing plugin management UI and install/enable its browser extension. Start this build and reconnect its runtime before resuming a previously loaded conversation; loaded threads do not accept new server configuration retroactively. If the authorization host itself restarts, reconnect to obtain a new binding and signing identity. No global configuration or official-client pipe is modified by this integration.

The official in-app browser (IAB) is not exposed by this external host. Browser internal pages, including the Edge favorites interface, cannot be claimed through the extension; inspect that interface through the supported native accessibility channel. This is distinct from reading bookmarks data through an extension capability.

## Verification

### Browser extension setup

**Options > Browser Control** owns browser configuration for all conversations on the current local Windows target (Codex 0.164 or newer). The master switch is saved with **OK**; **Cancel** does not install plugins or modify bridge registrations. Saving an enabled switch automatically verifies the packaged native host and plugin, installs/enables the local browser plugin if necessary and prepares missing/invalid bridges for installed Edge and Chrome browsers. A valid existing bridge is reused. No browser registry configuration is written for an absent browser. The owning target's setting does not enable WSL or remote targets.

While the page is visible it automatically detects the two browsers and refreshes every five seconds, with no overlapping checks. Its two fixed action buttons change between **Install extension**, **Manage extension** and **Check / install extension** for indeterminate states, keeping keyboard order and control identity stable. Standard `Default` / `Profile N` profiles are checked separately using Secure Preferences before ordinary Preferences. An authoritative secure extension table records removals as well as installed entries; stale ordinary registrations and leftover files do not override a removal. Inaccessible, malformed or incomplete settings remain indeterminate, with a store action available for recovery. Custom user-data locations require a separate check. Read-only detection never installs a component or opens a browser.

An explicitly enabled setting also resumes preparation at application startup, so exiting while configuration is deferred does not lose the request. Startup repair follows the same idle transition lease and reuses complete components. Other modal dialogs defer the transition as well.

The independent Windows detector follows the installed official Chrome plugin's `check-extension-installed.js`, `check-native-host-manifest.js` and `extension-ids.json` metadata (26.1023.51421); running this detector does not require those scripts or the official desktop client. It displays only known extension states, profile directory names and bridge registration status; it does not log preference contents. Bridge registration checks the manifest, both expected extension origins and existence of its referenced executable. Registered does not mean the host was executed or its live connection verified.

**Install extension** opens that browser's official store page; **Manage extension** opens its extension management page. Both require an explicit click or keyboard activation. Users complete installation and permission confirmation in their browser; automatic detection updates the page afterward. Browser extension installation requires the store connection. The MyCodex plugin and native host are shipped locally and require no additional component download during setup.

Automatic setup installs and enables the self-contained `browser-use@mycodex-browser-use` plugin through the existing App Server marketplace/plugin/config APIs. Its three packaged files are compared with embedded resources before installation; unexpected files, including an injected MCP launch definition, are rejected. An enabled copy at the current installation path is reused. The plugin provides browser-selection guidance and routes Stop, Interrupt and SubagentStop cleanup to the runtime's original `cua_repl/turn_ended` hook. Existing per-thread preparation recognizes its enabled identity and supplies the scoped `cua_repl` server and authorization binding. No private pipe or identity is written into global configuration. Configuration changes automatically reconnect the owning runtime using the existing transition lease; active chats, commands, compactions, automations and auxiliary operations defer application until idle. Existing conversations are retained and cold-resume on subsequent use. Saving unchanged, complete configuration does not reconnect. Global MCP status does not necessarily include thread-scoped tools; configured state does not prove a live page connection.

Disabling saves a target-local host gate, suppresses `cua_repl` in subsequent thread configurations and revokes existing browser authorization pipes. The upstream service caches authorization within an active turn: `turn_ended` alone did not invalidate that cached access in the synthetic regression. MyCodex therefore calls the original thread-scoped `cua_repl/js_reset` after revocation. The real upstream fixture confirms reset succeeds and repeated calls from that same turn, as well as the next turn, no longer reach the browser backend. Native desktop REPL and ordinary conversation turns are retained. Failed stop confirmation is reported explicitly; reconnection waits for idle. Disabling retains extension installations, valid bridge files and plugin registrations for later reuse. Legacy settings without a saved master switch retain their existing plugin-based behavior until the user saves an explicit selection.

A fresh-home Codex 0.164 integration check installs this plugin, toggles its enabled setting through the official config API and starts a browser-configured thread. A loopback-only provider fixture captures the real conversation request metadata and asserts the `mcp__cua_repl` namespace contains `js`. It performs no external model or page actions and checks that the real global config was unchanged. The same check passed using `Release/browser-use` and the shipped CLI. This closes a setup gap where installing the extension and native bridge did not itself opt the fresh conversation into browser tools.

Automatic bridge preparation deploys a verified copy of the original standalone native host under `%LOCALAPPDATA%\MyCodex\browser-host\h1\<generation>`, writes its Native Messaging manifest and registers it under HKCU for each installed browser requiring repair. Edge uses `Software\Microsoft\Edge\NativeMessagingHosts\com.openai.codexextension`; Chrome uses its corresponding Google/Chrome key. Each individual operation writes only its selected browser's registration and never HKLM. Each update records the old default value, its registry type and new manifest in `browser-host\backups`; failed publication restores the old value if this operation's registration is still current. A concurrent foreign update is preserved. Existing processes and their old binary files remain untouched. A browser already running when its bridge is configured may need to be fully exited and reopened once; MyCodex does not terminate it automatically.

The separate `runtime/browser-host/h1/extension-host.exe` is pinned by `tools/runtime/browser-extension-host.json` (879408 bytes, SHA256 `D1AC4EF76E3C1604D69708725D27E77EB845073A31C9F45E25AD84DF114B0ACC`). Preparation verifies OpenAI's Authenticode signature and the exact content hash; the application verifies the embedded pin before deployment. `prepare-browser-extension-host.ps1` prepares it from the concrete reviewed Chrome plugin version or `-SourceExecutable`, and `-VerifyRoot` verifies a packaged copy. No official app or plugin cache is required on the user's machine.

The automation pipe is standalone. Controlled native-host tests for both official extension IDs complete a real bidirectional `getInfo` roundtrip without Node, Codex CLI, sibling `extension-host-config.json`, a V2 runtime manifest or an official desktop process. Each pipe's server PID is checked before sending fixture data. The installed real extension background also establishes this native transport without calling `codexRuntime/ensure`. The V2 manifest belongs to the separate browser sidebar App Server workflow; this installer does not configure that workflow.

Focused setup/service/installer/form/plugin/resource/mnemonic/source checks: 239 passed; one filesystem-link fixture was skipped because link creation was unavailable. Release build: zero warnings/errors. Embedded host pin, host signature, packaged browser inventory, plugin resource verification and the standalone native-host roundtrip passed. The earlier read-only detector matched the official script on this machine: Edge enabled with a registered bridge; Chrome had no standard profiles. Real fresh-VM browser registration/connection and screen-reader acceptance still require manual verification. Installer tests use a fake registry; no developer-machine browser registrations were changed. The reported `node_repl` desktop `CUA_EXECUTION_FAILED` lacks a detailed VM diagnostic and is not claimed fixed by this browser activation change; its subsequent `CUA_TURN_NOT_ACTIVE` must not be hidden by bypassing lifecycle checks.

```powershell
node tools/diagnostics/Test-BrowserAccessHost.mjs
node tools/diagnostics/Test-BrowserUseControl.mjs
node tools/diagnostics/Test-BrowserRuntime.mjs
node tools/diagnostics/Test-ComputerUseHost.mjs
python tools/diagnostics/Test-BrowserThreadBinding.py --codex runtime/codex/0.164.0/x86_64-pc-windows-msvc/vendor/x86_64-pc-windows-msvc/bin/codex.exe
dotnet test tests/MyCodex.Application.Tests/MyCodex.Application.Tests.csproj -c Debug -p:Platform=x64 --no-restore --filter "FullyQualifiedName~BrowserUse|FullyQualifiedName~ComputerUse|FullyQualifiedName~McpToolApproval|FullyQualifiedName~ProtocolCapabilities"
```

The browser runtime smoke check executes the actual pinned upstream MCP runtime with a temporary home and synthetic Edge pipe. It proves that the original missing-host error is reproduced, an owned signed host is accepted and an ended binding is rejected. It never reads a personal browser/profile or makes a model request. Actual browser connection and screen-reader acceptance remain separate manual checks.

### 2026-10-29 validation record

| Check | Result |
|---|---|
| Final focused .NET browser/native/approval/protocol/resource/source checks | 246 passed, one filesystem-link fixture skipped because link creation was unavailable |
| Explicit real .NET/Node host-reader regression | Original inline callback failed; asynchronous callback passed |
| Browser authorization host and lifecycle bridge | 8 and 10 real named-pipe/Ed25519 tests passed |
| Actual upstream browser MCP runtime | 4 checks passed: missing host, valid owned host, explicit Edge selection, ended binding |
| Original native host | 210 checks passed |
| Actual isolated Codex 0.164 thread startup | Plugin-defined MCP verified; three thread overrides, conflicting global server and unrelated server preservation passed; real global config unchanged |
| Release build and copied runtime inventory | Zero warnings/errors; packaged `runtime/browser/b1` verified |

The broad suite before the last hardening edits recorded 5066 passes and two failures. `SharedRuntimeMutationTests.PublishNewVersion_IsCompleteAndDoesNotReplaceRunningOldVersion` encountered a directory access error and passed in isolation. `UserCacheMaintenanceTests.Program_RunsUserCacheMaintenanceBeforeConfiguringDiagnostics` still fails its existing source-position assertion; its test and the inspected Program source are unchanged by this work. The broad suite is therefore not recorded as fully green. Relevant checks were rerun after hardening; live Edge connection, tab enumeration and the Google page DOM were subsequently verified from the test build. Automatic approval acceptance requires the updated approval build.

### 2026-10-29 Full Access approval update

Removed the browser-specific exclusion from the shared MCP tool approval path. The browser regression failed in both Full Access modes before the change and passes afterward. Related MCP, Full Access, App Server message and code-health checks passed (252 tests). Release output `artifacts/browser-use-approval-build` built with zero warnings/errors. It is separate from the running initial test build; restart into the new build and reconnect the runtime for live automatic-approval acceptance.

### 2026-10-29 Browser Control options and installer

The final focused browser, desktop lifecycle, approval, protocol, options, localization and source-guard run passed 534 tests; one filesystem-link fixture was skipped. Before the final startup/revocation hardening, the full suite passed 5125 tests with one skipped fixture and the existing `UserCacheMaintenanceTests.Program_RunsUserCacheMaintenanceBeforeConfiguringDiagnostics` source-position failure. Its test and `Program.cs` are unchanged by this task. The final focused run covers the subsequent changes; the full suite is not claimed green.

The original runtime smoke passed seven checks including active-turn revocation through `js_reset`. Authorization/control pipe suites passed 8 and 10 tests; the original native-host fixture passed for both extension IDs. A fresh Codex home using the actual packaged CLI and local marketplace installed/enabled the plugin and included `cua_repl/js` in the real conversation metadata, without external model/page actions or changes to the developer's global config. The final isolated Release build had zero warnings/errors. Packaging uses the pinned released authentication backend, Codex 0.164.0, native/browser runtimes, native host, local plugin, Relay and Git. Real clean-machine installation, websites, browser restart behavior and screen-reader acceptance remain manual checks.

### Clean-machine setup and extension removal regressions

The first clean-machine report exposed a missing boundary in the initial tests: browser setup acquired a runtime transition lease, but the real facade rejected its first `plugin/installed` request with `AuthenticationCenterException(runtime_busy)`. CLI-only fixtures and mocked request invokers had bypassed that gate. A regression through the actual facade, gate and transport reproduced the failure before the fix. Preparation now enters a scope bound to the owning facade and verified marketplace. Only that marketplace's observation/registration, local browser plugin installation and its single enable edit may use the transition owner's lease. Commands, other callers/clients, foreign marketplaces, provider changes and mixed edits stay blocked. Direct async preparation and the real WinForms wait dialog are covered. An explicit fresh-home integration test also runs the actual C# preparation service and pinned CLI using an isolated installation and fake registry; repeated preparation is idempotent.

Two uninstall fixtures reproduced the stale action: an empty secure settings table combined with leftover extension files yielded Unknown, and a stale ordinary registration yielded Enabled. Both now yield NotInstalled and the UI returns to the official store action. Indeterminate states retain their uncertainty label while offering a check/install store action. Setup failure diagnostics now include the prepare/reconnect stage and a bounded runtime-busy code, without raw configuration or exception messages.

Correction validation: 438 focused tests passed with one link fixture skipped; the explicit fresh-home C# preparation integration passed. The full suite passed 5132 tests with one skipped fixture and the same pre-existing `UserCacheMaintenanceTests.Program_RunsUserCacheMaintenanceBeforeConfiguringDiagnostics` failure. Release built with zero warnings/errors and the full Windows installer was rebuilt and its authentication receipt/hash verified. The user subsequently reported that testing passed on the target device.
