# Native Computer Use runtime

MyCodex packages the original Windows Computer Use plugin, Node REPL, Sky library and native helper from the official ChatGPT Windows package. Its native host and authorization checks remain separate from [browser automation](browser-use-runtime.md).

## Reviewed update: 2026-10-29

| Component | Previous | Current |
|---|---|---|
| ChatGPT Windows x64 package | `26.1002.7124.0` | `26.1023.6621.0` |
| Computer Use plugin | `26.1002.52244` | `26.1023.51421` |
| Runtime archive | `0.0.29/20261003001300-807782c586fc` | `0.0.35/20261022235854-0e24579b4572` |
| Node | `24.21.0-cua.1` | `24.22.1-cua.1` |
| `@oai/sky` | `0.7.6` | `0.7.13` |

The [official stable release feed](https://persistent.oaistatic.com/codex-app-prod/windows-release.json) identified this package on 2026-10-29, with release date `2026-10-28T19:56:31Z`. The [official Windows deployment guide](https://learn.chatgpt.com/docs/enterprise/windows-deployment) documents direct MSIX downloads; the version-specific package used here is [ChatGPT x64 26.1023.6621.0](https://persistent.oaistatic.com/codex-app-prod/releases/26.1023.6621.0/ChatGPT-x64.msix).

The [Computer Use setup guide](https://learn.chatgpt.com/docs/computer-use) describes installation inside ChatGPT. This review found no documented standalone distribution of the native plugin and its complete runtime. Anonymous public npm requests for `@oai/sky` and `@oai/cua-repl` returned HTTP 404, and the plugin still declares a proprietary license and a repository under `openai/openai` that is not publicly accessible. This is a bounded availability check, not proof that no other channel exists. The public Responses API computer-use SDK is a separate integration and does not replace this native package.

## Acquisition and integrity

The downloaded MSIX is 979132234 bytes, with SHA256:

```text
FDA8B07433D2E8872B8833BB665552EB8DE6475BA9CCA54FB8AF28436629D133
```

Its Authenticode status was `Valid`. The package identity is `OpenAI.Codex`, architecture `x64`, version `26.1023.6621.0`, publisher `CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B`.

Only these original package trees are needed:

- `app/resources/cua_node/`
- `app/resources/plugins/openai-bundled/plugins/computer-use/`

MSIX archive member names use URI escaping, including `%40oai` for `@oai`. Decode names before materializing files, then reject absolute paths, traversal, links and duplicate destination paths. This update verified each selected file's size and every 64 KiB block hash against `AppxBlockMap.xml`, in addition to verifying the package signature. All 2398 selected original files were retained without editing their contents. The preparation script adds only MyCodex's local marketplace catalog and bundle inventory.

An installed official client of the reviewed version can also supply `app/resources` without extraction. After reviewing the source versions and editing the version fields in `tools/runtime/computer-use-bundle.json`, prepare the new bundle with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/runtime/prepare-computer-use-runtime.ps1 -SourceResources <reviewed-app-resources> -UpdatePin
powershell -NoProfile -ExecutionPolicy Bypass -File tools/runtime/prepare-computer-use-runtime.ps1
```

Preparation verifies the OpenAI signatures of `node.exe`, `node_repl.exe` and `codex-computer-use.exe`. The pin records the SHA256 of the complete inventory; verification rejects changed, missing, extra or linked files. Keep the previous runtime cache available to existing users of that version. Installer preparation removes obsolete bundle directories only from its package output.

## Verification

For this update:

- Bundle preparation and complete inventory verification passed; 2399 files are inventoried, including the generated marketplace catalog.
- `node tools/diagnostics/Test-ComputerUseHost.mjs`: 210 checks passed with a synthetic executor.
- `node tools/diagnostics/Test-BrowserRuntime.mjs --check-tool-routing`: the actual new Node REPL/Sky reached only the synthetic native executor, rejected calls after turn end, and all seven browser regression checks passed.
- The original new Windows helper accepted `end_turn` through the production `WindowsHelperTransport` and closed successfully, using a temporary helper home and the bundled Codex CLI `0.164.0`.
- Focused .NET tests for Computer Use, Browser Use, Office instructions, App Server messages and code health: 386 passed, one filesystem-link fixture skipped.
- Release build: zero warnings and errors.

These automated checks made no real desktop observations, screenshots, input actions or model requests. The user subsequently reported that the updated build passed testing; clean-device coverage was not specified.
