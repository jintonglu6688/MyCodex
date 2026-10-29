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
