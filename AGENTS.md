# AGENTS.md

## Repository Overview

MyCodex is a Windows desktop client for Codex App Server, built with a .NET Framework 4.8 WinForms UI.

These instructions govern this repository only. Do not carry requirements, implementation details, build steps, or task state from sibling client or service repositories unless the user explicitly places cross-repository work in scope.

## Workflow And Completion

- Read the files and callers relevant to the task; expand the search when behavior crosses a boundary. A small edit does not require rereading the whole repository.
- Reuse existing helpers and patterns. Keep the change scoped and preserve user-owned work.
- For implementation tasks, continue through the requested behavior, relevant verification, and review of your own diff. Fix failures caused by the change; distinguish pre-existing or environment failures from regressions.
- Use the smallest meaningful checks for the risk. After they pass, broaden or repeat only for new changes, failures, or unresolved concerns. Routine local verification within the requested task needs no separate approval.
- Finish by stating what changed, what was verified, and any remaining limitation. Never claim a check passed when it was not run.
- Record a durable decision only when the task needs it; no ADR directory, glossary, or separate context document is required.

## Project Layout

Primary solution and versioning:

- `MyCodex.sln` contains only `Debug|x64` and `Release|x64` solution configurations.
- Main application outputs are repository-root `debug\` and `Release\`, configured by `Directory.Build.props`.
- The test project overrides common output behavior and writes to `tests\MyCodex.Application.Tests\bin\...`.
- Application version is centralized in `Version.props`.

Project layout:

- `src\MyCodex.Shared`: constants, path helpers, command-line escaping, message utilities, and shared/internal localization.
- `src\MyCodex.Domain`: serializable entities and enums only. Keep this free of UI, infrastructure, process, network, execution-target, and persistence implementation concerns.
- `src\MyCodex.Application`: DTOs, interfaces, capabilities, internal tool contracts, planners, and pure application services. Prefer this layer for behavior that can be tested without WinForms or real processes.
- `src\MyCodex.Infrastructure`: App Server integration, process launching, WSL/remote bridges, persistence, updates, Plugin Marketplace discovery, Git/GitHub integration, process execution, and screen reader/TTS implementations.
- `src\MyCodex.WinForms`: `Program`, forms, controls, workbenches, localization, resources, theming, skill UI, and desktop composition. `Forms` is split into feature subfolders (`Main`, `Git`, `Sessions`, `Settings`, and so on) that all keep the `MyCodex.WinForms.Forms` namespace; put new form files in the matching subfolder and keep only cross-feature helpers at the `Forms` root.
- `tests\MyCodex.Application.Tests`: NUnit 3 test project covering application, infrastructure, and WinForms behavior.
- `tools`: PowerShell and batch entry points for builds, installer/update packaging, GitCode distribution, diagnostics, testing, refactoring, and remote support.
- `docs`: checked-in operational documentation. Confirm a document exists before relying on it, and use current source and configuration to resolve stale guidance.
- `.github`: issue and pull request templates.
- `.gitcode`: GitCode distribution workflows for update manifest validation and other distribution automation.
- `.codex`: development hook scripts shared by Codex and Claude Code, the Codex hook registration, and ignored per-session recovery notes.
- `.claude` and `CLAUDE.md`: Claude Code hook registration for the shared scripts and an entry file that imports this one; personal `.claude/settings.local.json` is ignored.
- `help`, `sound`, `tts`, and `updates`: shipped assets and update/distribution metadata.

## Issue Work

When the user places GitHub Issue work in scope, use `gh` for `jintonglu6688/MyCodex`.

- Use existing issue context, labels, and relationships; verify them before applying changes. Preserve a parent issue when working on a child.
- Ordinary development requests do not require creating specifications, issues, sub-issues, or new workflow labels. Follow `.github` templates when preparing an issue or PR requested by the user.

## Generated And Vendored Areas

Ignore these during searches and do not edit them as source:

- `.git`, `.vs`, `.idea`, `.vscode`
- `bin`, `obj`
- repository-root `debug`, `Release`
- `artifacts`, `temp`
- `TestResults`, `TestResult*`, `coverage`, `publish`, `dist`
- `node_modules`, `packages`
- `runtime\codex`
- `runtime\auth-core`
- `tests\MyCodex.Application.Tests\artifacts`
- `.playwright-mcp`

`runtime\putty`, `tts`, `sound`, `help`, `updates`, `.github`, `.gitcode`, and `tools` are source-controlled inputs unless a task explicitly identifies a generated file inside them.

Do not edit generated payloads under `runtime\codex`. Update their source preparation logic instead.

Do not use `debug\`, `Release\`, or `artifacts\package` output as evidence for source behavior. Inspect `src`, `tests`, `docs`, `tools`, and checked-in assets.

## Build And Test Entry Points

### First checkout

- Develop and run WinForms tests on Windows with Git, Windows PowerShell, and a .NET SDK/MSBuild installation capable of building .NET Framework 4.8 WinForms projects (including the net48 targeting components). Check available tools before installing anything; a missing local prerequisite is not a source regression.
- A fresh checkout has no build outputs, NuGet restore assets, prepared `runtime\codex` payload, or `.codex/session-state` notes. These are generated locally; their absence is expected.
- Restore the test project once; it references all application projects. Restore may use the network. Building only the WinForms project does not restore the test project's packages.

```bat
dotnet restore tests\MyCodex.Application.Tests\MyCodex.Application.Tests.csproj -p:Platform=x64
```

### Ordinary verification

After restore, use direct `dotnet` commands from the repository root:

```bat
dotnet build src\MyCodex.WinForms\MyCodex.WinForms.csproj -c Debug -p:Platform=x64 -p:MyCodexChannel=Dev --no-restore -v:minimal
dotnet build src\MyCodex.WinForms\MyCodex.WinForms.csproj -c Release -p:Platform=x64 --no-restore -v:minimal
dotnet test tests\MyCodex.Application.Tests\MyCodex.Application.Tests.csproj -c Debug -p:Platform=x64 --no-restore -v:minimal
```

Focused test example:

```bat
dotnet test tests\MyCodex.Application.Tests\MyCodex.Application.Tests.csproj -c Debug -p:Platform=x64 --no-restore -v:minimal --filter "FullyQualifiedName~FormResourceUsageTests"
```

For hook changes (`.codex\hooks`, `.codex\hooks.json`, or `.claude\settings.json`), run `powershell -NoProfile -ExecutionPolicy Bypass -File .codex\hooks\test_hooks.ps1`.

Direct build/test commands do not prepare or download the authentication backend or bundled Codex runtime. The WinForms project copies an existing `runtime\auth-core` package when available. Running a chat still requires a working Codex execution target; a successful compile is not a complete runtime or installer setup.

### Full build, packaging, and release

Use these scripts only when their side effects are intended:

- `build-dev.bat` and `build.bat` invoke `tools\build\Build-MyCodex.ps1`. They prepare the pinned authentication backend, may download the bundled Codex runtime, build this app, clean obsolete output, and prepare the pinned Relay release. Relay preparation verifies and reuses `runtime\relay` cache or downloads and verifies the release; no Relay source checkout is required.
- Authentication preparation uses `tools\authentication-center\Prepare-AuthenticationHost.ps1`: reuse a verified `runtime\auth-core` package or obtain the pinned Windows package through `Prepare-AuthenticationHostRelease.ps1` from its download cache or GitHub Release. Both build profiles use the same released backend; no sibling checkout or Rust toolchain is required. Building the backend from source requires explicitly passing `-SourceRoot` to the preparation script.
- `package.bat` always builds Release first, then invokes `tools\installer\build-installer.ps1` with `-SkipBuild` to avoid a second build. The installer script requires Inno Setup 6 and a valid prepared `runtime\auth-core` package, prepares the bundled runtime, and prepares the same pinned Relay release from verified cache or download. Its `-SkipBuild` switch does not remove authentication/Relay/runtime prerequisites or skip the batch entry point's initial build. Relay pins are in `tools\runtime\relay-runtime.version` and `relay-runtime.sha256`; verify preparation with `powershell -NoProfile -ExecutionPolicy Bypass -File tests\tools\PrepareRelayRuntime.Tests.ps1`.
- `tools\update\Build-MyCodexRelease.ps1` builds installer and update manifest artifacts and inherits installer prerequisites unless using an existing installer.
- `tools\gitcode\Publish-GitCodeRelease.ps1`: distribution workflow for GitCode; do not run unless explicitly releasing.
- When authentication backend version bumps, cloud builds, releases, or GitCode mirroring are in scope, read `docs/mycodex-auth-release.md` in the backend repository (normally `..\MyCodex-auth-core`) first. It covers backend publication and the frontend version/hash updates; MyCodex's app-update publishing script is a separate workflow.

Do not create, change, or build a sibling repository merely to make ordinary verification pass. Report missing release prerequisites when packaging is in scope. Rerun restore after package/project reference changes or missing restore assets; otherwise use `--no-restore`.

If a running MyCodex locks build output, use `-p:OutputPath=<absolute-repository-artifacts-directory>/` for verification instead of stopping the user's app. This verifies the build, not installer packaging.

## Working Sessions And Git Safety

`AGENTS.md` contains durable repository rules only. Do not add feature-specific progress, temporary plans, or conversation history here.

For long or multi-step work:

- Use the per-session recovery path supplied by the `SessionStart` hook under `.codex/session-state/`. If no hook path is available, use the current session ID when provided, otherwise a unique filename in that directory. Create the directory only when needed; no note or template is required to start a fresh task.
- Write the recovery note as UTF-8. Keep it concise: current objective, confirmed user decisions, completed work, next steps, touched areas, verification, user-owned changes, and open risks. Put the objective and next action first.
- Update it after material decisions, scope changes, or milestones. Never store secrets, credentials, private data, or full transcripts in it, and never commit it.
- Remove this session's note when its task is complete; preserve notes belonging to other sessions.
- After resume or context compaction, reconcile the recovery note with the latest user request, `git status`, `git diff`, and the actual files before continuing. Read the full recovery file if the hook preview is truncated. The latest user instruction and repository state override stale notes.

Preserve user-owned work in a dirty worktree. Do not restore, overwrite, stage, or commit unrelated changes. Commit, amend, push, force-push, merge, rebase, or switch branches only when the user explicitly requests that Git operation in the current task.

### Development hooks

- `.codex/hooks/*.ps1` implement this repository's coding-agent development workflow on Windows. `.codex/hooks.json` registers them for Codex and `.claude/settings.json` registers the same scripts for Claude Code; keep both registrations in step. They are not MyCodex application settings or policies for all app users.
- `AGENTS.md` is the single source of repository rules for all coding agents. `CLAUDE.md` only imports it and notes Claude Code specifics; do not copy rules into it.
- Hook availability depends on the agent runtime and its trust/configuration state. Script tests verify behavior, not whether a new installation has enabled and trusted the hooks. If hooks are unavailable, follow this file and maintain recovery notes manually; do not block ordinary development or change global settings automatically.
- SessionStart provides a current Git snapshot and restores only this session's saved note on resume/compaction. A new checkout has no past session state to restore.
- PreToolUse checks common direct shell command forms sent through the `Bash` tool and, in Claude Code, the `PowerShell` tool. It is a best-effort guard, not a shell parser or proof that a command is safe. Preserve the Git and path-safety rules even when it returns no decision; report an explicit hook conflict rather than silently disabling the hook.

## Architecture Rules

Use the project ownership described in the repository layout above.

When adding behavior:

- Put pure selection, mapping, formatting, validation, filtering, or planning logic in `Application\Services`, `Application\Capabilities`, or a focused WinForms planner/helper so it can be unit tested.
- Keep `MainForm.cs` changes narrow. It should coordinate dependencies and top-level UI flow only; do not add feature logic, protocol mapping, persistence normalization, execution-target selection, or large event workflows there.
- Prefer existing partial files such as `MainForm.Sessions.cs`, `MainForm.Workspaces.cs`, `MainForm.AppServerEvents.cs`, `MainForm.SessionSettings.cs`, `MainForm.GitHosting.cs`, and focused planner classes. When a partial file starts becoming a mixed-purpose bucket, create a more specific partial/helper instead of continuing to append.
- Keep App Server wire/protocol translation inside focused mappers and transport helpers under `src\MyCodex.Infrastructure\AppServer`.
- Keep process and execution-target launch selection in the existing Infrastructure launch services and App Server helpers.
- Keep persistence normalization in store/service classes, not in forms.

## Structural Guardrails

Keep coordination classes split by responsibility. Source guard tests protect ownership boundaries. `SourceGuards\CodeHealthRatchetTests` caps MainForm size and fields, long methods, large files, and copies of known duplicate helpers; its ceilings only go down. When a change improves a metric, lower that ceiling to the new value. Raise one only for justified, scoped growth and explain why.

- Mapper classes should map one protocol/domain family. Do not add unrelated payload types to a mapper just because it is nearby.
- Builder/planner classes should return data or commands. They should not directly mutate WinForms controls or start external processes.
- Before substantial growth in a file near or above 800 lines, look for a focused extraction. Prefer files under roughly 1,200 lines and methods under roughly 80 lines; explain necessary meaningful growth rather than forcing incoherent splits.
- Keep feature state in focused state objects, panels, planners, or services. Name new files for their responsibility rather than using catch-all partials.
- Preserve public contracts unless the task changes them. Separate behavior-preserving extraction from behavior changes in the patch.
- Move tests with extracted behavior, preferring focused service/planner tests over WinForms reflection where possible. Update source guards when ownership moves; preserve behavioral and safety assertions.

### Refactor when touched

These cleanups are not standalone tasks. When a change already touches the area, do the matching cleanup as a separate behavior-preserving step (its own commit when committing), then make the requested change:

- MainForm: before changing a feature whose code still sits in `MainForm.cs` or a mixed-purpose partial, extract that feature into a focused partial, helper, or planner, and lower the `CodeHealthRatchetTests` ceilings it improves.
- Git/GitHub forms (`Forms\Git`): when changing one, move layout, list, progress, or credential code it repeats from sibling forms into a shared control or helper, alongside `UiControlFactory`.
- WSL/Remote Unix SSH pairs that still duplicate logic (`WslFileSystemBridge`/`RemoteMacFileSystemBridge`, `WslGitCommandRunner`/`RemoteMacGitCommandRunner`, `WslGitEnvironmentService`/`RemoteMacGitEnvironmentService`): apply every change to both sides. When the pair's shared logic is substantial, move it into a `Unix*` core over `UnixCommandInvoker`, following `UnixGitSshKeyService`, `UnixGitCloneService`, and `UnixRuntimeFileSystemOperations`. Keep the public `Wsl*`/`RemoteMac*` classes as thin wrappers that supply operation-name prefix, timeouts, and shell differences, and keep scripts, operation names, and timeouts unchanged.
- Pure planners still under WinForms because they consume WinForms types (such as `ChatHistoryItem` or `AppServerEventRoute`): move them to `Application\Services` only together with replacing that dependency by an Application type; do not move WinForms types down just to relocate a planner.
- Interfaces: remove members that have no production caller, together with their implementations, helpers, and tests. Keep a member only when a current, concrete feature needs it.
- `SourceBehavior` tests: rewrite as behavior tests when the change touches the code they read (see Test Conventions).

## App Server And Execution Target Cautions

`AppServerClient` is the facade for `IAppServerClient` and sits on the send path, reconnect path, stdout/stderr readers, timeout handling, pending request handling, App Server event delivery, dynamic tools, and execution-target setup flows. Treat changes there as high risk.

Important boundaries:

- Preserve `IAppServerClient` public method signatures and event shape unless the task explicitly changes the contract.
- Preserve App Server JSON-RPC wire format, request IDs, timeout behavior, cancellation behavior, and disconnect behavior.
- Read App Server payload values with `AppServerJsonValueReader` (or `AppServerJsonValue`) instead of adding private `GetString`/`GetDictionary`/`GetArray` copies; keep a local reader only when its semantics differ, such as invariant-culture conversion.
- Use existing mapper classes for account config, app summaries, MCP servers, hooks, dynamic tools, filesystem entries, fuzzy search, interaction requests, thread payloads, Windows Sandbox, and protocol capability parsing.
- Keep dynamic/internal tool registration and execution routed through `Application\InternalTools` contracts and App Server dynamic tool mappers.

Remote execution-target details:

- Remote Mac SSH and Remote Unix SSH share the remote Unix SSH-family bridges; do not duplicate platform handling without checking the existing family helpers.
- WSL path handling should use the existing WSL bridge, path mapper, command executor, and command-line helpers rather than manual string concatenation.
- Remote Unix filesystem, Git, skill, plugin, and MCP behavior should use the remote bridges and not assume local Windows paths.

## Multi-Target Ownership

- Each target owns a separate process, main window, and `AppServerClient`. Opening another target starts or activates its instance. Within one app channel, Windows is one target; bundled, system, and custom runtimes are properties of that target.
- Resolve target identity and paths before loading configuration or composing services. Reuse `TargetCatalogStore` and `TargetInstanceCoordinator`; identify instances by stable target ID and app channel, not display name. Options, conversation state, logs, temporary files, and automation are target-scoped; the catalog, installation resources, and phone connection service settings are shared.
- Preserve target IDs, conversation history, and phone pairing when renaming targets, changing runtimes, or migrating data. Shared phone service settings do not make target identities or pairings interchangeable.
- Associate auxiliary windows, hotkeys, and tray actions with their owning target. Closing or removing one target must leave other targets running. Removal may delete only validated MyCodex configuration paths; retain projects, repositories, Office files, and remote directories.
- Reuse existing ownership and lease coordination for shared authentication, Relay, and runtime resources. Exit-all and updates must complete save/cancel checks for every affected instance before stopping processes or replacing shared files. Do not bypass a save/cancel refusal with forced termination or delete runtime versions still in use.
- Automatic reading for the selected conversation follows each target's setting in the foreground, background, and tray, regardless of target count. New targets inherit the local Windows target's TTS channel name; do not generate names from target IDs. Releasing one target must not stop a shared TTS channel. Keep configured sounds and do not add desktop notifications. Requests that open a dialog (approvals, tool and MCP input, pending questions) go through `InteractionRequestPromptPlanner`: the configured sound in the foreground and background, plus speech after the sound only while the target is in the background; in the foreground the screen reader reads the dialog. Do not add other notification speech.

## Office Mode

Office Mode is a MyCodex host-level mode for document-centered work. It is not a separate Codex App Server protocol mode.

Office Mode rules:

- `AppInteractionMode.Office` separates Office sessions from coding sessions.
- `WorkspaceKind.Office` marks Office categories as Office workspaces.
- Coding and Office are coexisting interaction contexts in one main window and share that window's App Server execution target. Switching tabs must not stop work in the other mode.
- Office settings and retained Office state are scoped by execution target: the local Windows target, each WSL distribution, and each Remote Unix SSH profile are independent.
- The Office root defaults to the user's Documents `MyCodex-office` folder on Windows and `$HOME/MyCodex-office` on WSL or Remote Unix. Do not fall back to a Windows path when a Unix home cannot be resolved.
- Options edits only the active execution target's Office root. Target-specific directory work must use `IOfficeRuntimeFileSystem` and the existing WSL/remote bridges rather than local filesystem assumptions.
- Import and sync flows must keep Office sessions and coding/project sessions separate.
- MyCodex maps Office Mode onto App Server collaboration modes with developer instructions from `OfficeDeveloperInstructionComposer`.
- Do not send unrecognized App Server collaboration mode strings such as `office`, `daily`, or `coding` unless the App Server schema or `collaborationMode/list` confirms support.

Office capability and document tooling belong in `Application\Capabilities`, `Application\InternalTools`, App Server dynamic tool mapping, and WinForms mode routing. Keep document-generation implementation details out of general App Server transport code unless the protocol itself requires them.

## Release And Distribution Conventions

- `updates\stable.json` is the stable manifest example/distribution file and may lag `Version.props` between releases.
- GitCode is used only as a public distribution repository for manifests and packages.
- Do not add or push a `gitcode` remote from the MyCodex source repository.
- `.gitcode\workflows\update-verify.yml` validates update JSON shape.
- The Windows installer must be built on Windows; hosted GitCode Linux runners are suitable for manifest validation and other platform-neutral checks, not WinForms installer builds.

## Git, Plugins, And Marketplace

Git and GitHub behavior:

- Keep Git command construction and parsing in application services such as `GitCommandLineBuilder`, `GitRepositoryService`, `GitDiffService`, and related DTOs.
- Keep local execution in `Infrastructure\Git` and GitHub CLI/hosting behavior behind `Application` interfaces and `Infrastructure\GitHosting` implementations.
- Use remote-specific Git services for WSL and Remote SSH paths instead of reusing local filesystem assumptions.

Plugin and marketplace behavior:

- App Server plugin, skill, MCP, and app summaries are protocol data; map them in `Infrastructure\AppServer`.
- Keep marketplace catalog retrieval, package handling, and path validation behind `Application` marketplace interfaces and `Infrastructure\Markets` implementations.
- Route App Server registration and Plugin operations through the existing application interfaces, services, and protocol mappers rather than forms or direct transport calls.

## Code Style

Follow the existing C# style:

- Target `net48`; avoid APIs unavailable on .NET Framework 4.8. `LangVersion` is `latest`; `Nullable` and `ImplicitUsings` are disabled.
- Use explicit `using` directives, block-scoped namespaces, 4-space indentation, and Allman braces. Match surrounding `var` usage; prefer `sealed` for concrete classes without intended inheritance.
- SDK-style projects auto-include new `.cs` files; do not edit `.csproj` just to include source files.
- Avoid adding or replacing external dependencies unless the task clearly requires it; follow the existing project-level `PackageReference` conventions when a dependency is justified.
- For JSON persistence and protocol parsing, prefer the existing `DataContractJsonSerializer` and `JavaScriptSerializer` patterns over introducing a new JSON stack.
- Use `StringComparison.Ordinal` or `StringComparison.OrdinalIgnoreCase` for non-linguistic comparisons.
- Use `Path.Combine`, `Path.GetFullPath`, and existing path normalization helpers such as `WorkspacePathUtilities` instead of manual path concatenation.
- Use `TextValueHelper.TrimToNull` and `TextValueHelper.FirstNonEmptyTrimmed` from `MyCodex.Shared.Helpers` instead of adding private copies.
- Preserve existing `.resx`, project-file, and checked-in asset encoding; do not normalize unrelated encoding or line endings.

Line endings are controlled by `.gitattributes`:

- `.cs`, `.csproj`, `.props`, `.targets`, `.sln`, `.bat`: CRLF.
- `.md`, `.yml`, `.yaml`, `.json`: LF.

## WinForms, Localization, And Accessibility

All user-facing UI changes must preserve keyboard and screen reader behavior.

Use existing helpers and conventions:

- `UiLocalization.Text("Resource.Key", "Fallback")` for WinForms UI strings.
- `SharedLocalization.Text(...)` for shared/non-UI strings.
- `UiTheme.ApplyForm`, `ApplyButton`, `ApplyDialogButton`, `ApplyTextBox`, `ApplyListView`, `ApplyListBox`, and related theme helpers.
- `UiGlyph` values with `UiTheme.ApplyButton` or `UiTheme.ApplyDialogButton` where the form pattern uses icons.
- `FormInteractionConventions.ConfigureRefreshButton` for refresh buttons.
- `FormInteractionConventions.ApplyMnemonic` when assigning or adjusting mnemonic suffixes.
- `UiControlFactory` for standard dialog controls: `CreateActionButton`, `CreateDialogButton`, `CreateMutedLabel`, `CreateFillTextBox`, `CreateDropDownList`, and `CreateDialogButtonBar`. Do not add a per-form `CreateButton`/`CreateLabel`/`CreateTextBox`/`CreateComboBox` helper when a factory method matches.
- Existing themed controls such as `ThemedTextBox`, `ChatWorkbenchControl`, and `ActivityVirtualBrowseControl` where they match the workflow.

For controls:

- Set `AccessibleName` on focusable controls.
- Use predictable `TabIndex`.
- Set `TabStop = false` for display-only labels, status areas, decorative controls, and refresh controls unless interaction is required.
- Use concise accessible names; do not duplicate long visible descriptions or append control-type words unnecessarily.
- Put status changes in visible status text.
- Keep status changes visible and readable through native controls. Authentication center and sign-in UI must not call `IScreenReaderService.Speak(...)` for operation progress, results, or errors; let the user's screen reader read the controls on demand. The one exception is a short spoken confirmation after a provider is enabled and MyCodex reconnects. Errors there also open a standard error message box (`AuthenticationCenterUiText.ShowError`) so screen reader users notice them.
- Keep forms resizable and set sensible `MinimumSize` where the surrounding form pattern expects it.
- Avoid changing private field names that tests access through reflection unless tests are updated intentionally.

For resources:

- Add or update both `src\MyCodex.WinForms\Resources\Strings.resx` and `src\MyCodex.WinForms\Resources\Strings.zh-CN.resx`.
- Keep resource keys referenced as string literals in code; `FormResourceUsageTests` checks for unused keys.
- Keep mnemonic markers unique within a form, especially in `Strings.zh-CN.resx`.
- Update mnemonic tests if adding active buttons, labels, or menu items with accelerators.
- Preserve existing `.resx` XML structure.

## Test Conventions

Use NUnit 3.

Add or update tests for behavior, contracts, safety boundaries, or regressions. For reversible, low-impact edits, use relevant existing checks instead of tests that mirror the implementation.

Typical patterns:

- Use `[TestFixture]`, `[Test]` / `[TestCase]`, and `Assert.That(...)`; use `[Apartment(ApartmentState.STA)]` for WinForms tests.
- Use fakes/stubs instead of real network, external processes, GitHub calls, WSL, SSH, or actual Codex processes.
- Use `TestContext.CurrentContext.WorkDirectory` plus unique subdirectories for file-system tests.
- Access non-public members and find controls through `TestSupport\FormTestDriver` instead of local reflection helpers.
- Source guard tests locate and read repository files through `SourceGuards\RepositorySource`, which finds production files by name so folder moves do not break them. Do not hard-code source folder paths.
- Tag every source guard with a `SourceGuardCategory`. Add retired identifiers and file names to `RetiredIdentifierGuardTests` instead of writing a new `Does.Not.Contain` test. `SourceBehavior` tests stand in for behavior tests; when a change touches the code such a test reads, prefer rewriting it as a behavior test.

When changing UI behavior or an existing UI contract:

- Add or update WinForms tests for accessible names, tab behavior, layout sizing, mnemonic resources, and resource usage.
- For dynamic forms, verify resizable size and minimum-size behavior when the existing pattern expects it.
- Keep reflection-based tests in sync when intentionally changing private field names or form structure.

When changing App Server, execution-target, Git hosting, Plugin Marketplace, Office Mode, internal tools, or Windows Sandbox behavior:

- Add focused tests for mappers, planners, resolvers, stores, command builders, and protocol adapters before broad integration tests.
- Cover Windows, WSL, Remote Mac SSH, and Remote Unix SSH paths when a feature crosses host boundaries.
- Keep tests deterministic and offline unless explicitly scoped as manual/integration work.

## Security And Secrets

Never log API keys, OAuth tokens, passwords, private key material, or full secret-bearing command lines.

Use the existing protection and cleanup paths:

- Remote SSH passwords must remain protected; password temp files and sensitive command files must be deleted through existing cleanup helpers.
- Safe diagnostics should report environment variable names only when they are not sensitive.
- Git and GitHub output should pass through existing sanitizers when secrets may appear.

Do not weaken path mapping (including Windows Sandbox mapping), command-line escaping, or path-safety checks in scripts, archive extraction, plugin installation, build preparation, Windows Sandbox setup, update package handling, or remote file operations. Deletion and extraction helpers intentionally validate that targets stay under allowed roots.

## Packaging Assets

The WinForms project copies or embeds these assets:

- `src\MyCodex.WinForms\Assets\Donate\wechat-donate.png` as an embedded resource.
- `tts\byctrl`, `tts\ZDSRAPI`, and optional `tts\nvdaControllerClient`.
- `sound\Approval.wav`, `sound\End of session.wav`, `sound\MsgSent.wav`, and `sound\Subagent completed.wav`.
- `help\UserManual.en.html` and the Chinese user manual.

When adding new packaged assets, update `src\MyCodex.WinForms\MyCodex.WinForms.csproj` intentionally and keep `CopyToOutputDirectory` behavior consistent.

## Search Guidance

Prefer `rg` and exclude generated areas, for example:

```powershell
rg "Pattern" src tests docs tools .github .gitcode help sound tts updates -g '!**/bin/**' -g '!**/obj/**' -g '!**/artifacts/**' -g '!runtime/codex/**'
```
