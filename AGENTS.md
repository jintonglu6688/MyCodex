# AGENTS.md

MyCodex is a Windows desktop client for Codex App Server, built with .NET Framework 4.8 WinForms. Many of its users rely on screen readers.

These rules cover this repository only. Sibling repositories (such as `..\MyCodex-auth-core`) stay out of scope unless the user places cross-repository work in scope. This file is the single source of repository rules for every coding agent; `CLAUDE.md` only imports it. Durable rules go here; feature progress and task state go in recovery notes.

## Working here

- Carry implementation tasks through relevant verification and a review of your own diff, then report what changed, what ran, and what is left. Fix failures your change causes; name pre-existing or environment failures as such.
- Decisions live in code comments and commit messages; this repository keeps no ADR directory, glossary, or context document, and ordinary development needs no specifications, issues, sub-issues, or new labels.
- `docs\` holds operational documentation. When a document and the current source or configuration disagree, the source wins.
- Issue work uses `gh` against `jintonglu6688/MyCodex`. Verify labels and relationships before changing them, keep the parent when working on a child, and follow the `.github` templates for issues and PRs the user asks for.

## Working Sessions And Git Safety

- Commit, amend, push, force-push, merge, rebase, or switch branches only when the user asks for that Git operation in the current task. Stage only your own changes; unrelated changes in the worktree belong to the user and stay as they are.
- For long or multi-step work, keep a UTF-8 recovery note at the path the `SessionStart` hook gives under `.codex/session-state/` (Git-ignored; without a hook, name it after the session ID). Put the objective and next action first, then confirmed decisions, finished work, touched files, verification, user-owned changes, and risks. Secrets, private data, and transcripts stay out of it. Delete your note when the task is done; leave other sessions' notes.
- After a resume or compaction, reconcile the note with the latest request, `git status`, `git diff`, and the files, reading the whole note if the hook preview is truncated. The latest request and the actual repository state win over the note.

## Hooks

- `.codex/hooks/*.ps1` serve Codex (registered in `.codex/hooks.json`) and Claude Code (registered in `.claude/settings.json`: `SessionStart`, plus `PreToolUse` for `Bash` and `PowerShell`). Change both registrations together and run `powershell -NoProfile -ExecutionPolicy Bypass -File .codex\hooks\test_hooks.ps1`.
- `PreToolUse` is a best-effort text check of common shell commands, not proof that a command is safe. When it blocks something the task needs, report the conflict and keep the hook. When hooks are unavailable, follow this file by hand and leave global agent settings as they are.

## Build and test

Windows with Git, Windows PowerShell, and a .NET SDK/MSBuild that builds net48 WinForms. Check what is installed before installing anything. A fresh checkout has no build output, restore assets, `runtime\codex` payload, or recovery notes; that is expected.

Restore once; the test project references every application project:

```bat
dotnet restore tests\MyCodex.Application.Tests\MyCodex.Application.Tests.csproj -p:Platform=x64
```

Then, from the repository root:

```bat
dotnet build src\MyCodex.WinForms\MyCodex.WinForms.csproj -c Debug -p:Platform=x64 -p:MyCodexChannel=Dev --no-restore -v:minimal
dotnet build src\MyCodex.WinForms\MyCodex.WinForms.csproj -c Release -p:Platform=x64 --no-restore -v:minimal
dotnet test tests\MyCodex.Application.Tests\MyCodex.Application.Tests.csproj -c Debug -p:Platform=x64 --no-restore -v:minimal
```

- Narrow a test run with `--filter "FullyQualifiedName~FormResourceUsageTests"`. Run the checks that match the risk and broaden only for new changes or failures; routine local builds and tests need no approval.
- Rerun restore only after package or project reference changes or when restore assets are missing.
- A running MyCodex locks `debug\` or `Release\`; build into `-p:OutputPath=<absolute path under artifacts\>/` instead of stopping it.
- Direct builds do not prepare the authentication backend or the bundled Codex runtime, so a green build is not a runnable installation.
- Packaging and release: before running `build.bat`, `build-dev.bat`, `package.bat`, or anything in `tools\installer`, `tools\update`, or `tools\gitcode`, before changing authentication backend, Relay, or runtime pins, and before adding packaged assets, read `docs/packaging-and-release.md`.

## Generated areas

- Everything Git ignores is generated: `debug\`, `Release\`, `artifacts\`, `temp\`, `bin`, `obj`, test results, and `runtime\*` except `runtime\putty`. Change the preparation scripts rather than the payloads (`runtime\codex` above all), and judge source behavior from `src`, `tests`, `tools`, `docs`, and checked-in assets, never from build output.
- `runtime\putty`, `tts`, `sound`, `help`, `updates`, `.github`, `.gitcode`, and `tools` are source-controlled inputs.

## Where code goes

| Project | Holds |
|---|---|
| `MyCodex.Shared` | constants, path and command-line helpers, `JsonValueReader`, `TextValueHelper`, shared localization |
| `MyCodex.Domain` | serializable entities and enums only |
| `MyCodex.Application` | DTOs, interfaces, capabilities, internal tool contracts, planners, and pure services: the default home for logic that can be tested without WinForms or processes |
| `MyCodex.Infrastructure` | App Server transport and mappers, process launch, WSL and remote bridges, persistence, updates, marketplace, Git and GitHub execution, screen readers and TTS |
| `MyCodex.WinForms` | `Program`, forms, controls, theming, localization, resources, composition |

- Application and the projects below it reference no WinForms types. A planner that consumes WinForms types moves down only after that dependency is replaced by an Application type.
- The application version lives in `Version.props`.
- Forms sit in feature folders under `Forms\` (`Main`, `Git`, `Sessions`, `Settings`, and so on) and keep the `MyCodex.WinForms.Forms` namespace; only cross-feature helpers sit at the `Forms` root. SDK-style projects pick up new `.cs` files without `.csproj` edits.
- `MainForm` coordinates dependencies and top-level UI flow. New feature logic goes into a focused class that MainForm holds (planner, presenter, state object); partial files carry only wiring.
- Planners and builders return data or commands and leave controls and processes to their callers.
- App Server wire translation lives in `Infrastructure\AppServer` mappers, one protocol family per mapper; reuse the mapper that already owns a payload family. Process and execution-target launch stays in the Infrastructure launch services, persistence normalization in stores, Git command building and parsing in Application services (`GitCommandLineBuilder`, `GitRepositoryService`, `GitDiffService`), GitHub CLI behind `Infrastructure\GitHosting`, marketplace catalogs and package handling behind `Infrastructure\Markets`. Forms reach App Server registration and plugin operations through Application interfaces and services, not the transport.
- Read JSON payload values with `JsonValueReader` (`AppServerJsonValueReader` inside App Server code). A private reader is justified only by different semantics, such as invariant-culture conversion.
- Dynamic and internal tools register and run through `Application\InternalTools` contracts and the App Server dynamic tool mappers.
- Remove interface members that have no production caller, together with their implementations, helpers, and tests.

## Size and the ratchet

- `SourceGuards\CodeHealthRatchetTests` caps MainForm size and fields, long methods, large files, and copies of known duplicate helpers. When your change lowers a metric, lower its ceiling to the new value; raise one only for justified, scoped growth and say why.
- Split by responsibility, never to satisfy a count: moving code into another partial or a static helper of the same feature lowers a number without lowering coupling. Prefer files under about 1,200 lines and methods under about 80; before growing a file past about 800 lines, look for a real extraction.
- Extracted behavior takes its tests along, preferably as focused service or planner tests; update source guards when ownership moves, keeping their behavioral and safety assertions.

## Refactor when touched

When a task changes a feature (a narrow bug fix is exempt), do the matching cleanup first as a separate behavior-preserving commit, then the change:

- MainForm feature code: extract it into a focused class and lower the ratchet ceilings it improves.
- `Forms\Git` forms: move layout, list, progress, or credential code repeated across sibling forms into a shared control next to `UiControlFactory`.
- `SourceBehavior` tests that read the code you change: rewrite them as behavior tests.

Every change, bug fixes included, lands on both sides of the WSL and Remote Unix SSH pairs: `WslFileSystemBridge`/`RemoteMacFileSystemBridge`, `WslGitCommandRunner`/`RemoteMacGitCommandRunner`, `WslGitEnvironmentService`/`RemoteMacGitEnvironmentService`. When a pair's shared logic is substantial, move it into a `Unix*` core over `UnixCommandInvoker` (as `UnixGitSshKeyService`, `UnixGitCloneService`, and `UnixRuntimeFileSystemOperations` do), keeping the public classes as thin wrappers with the same scripts, operation names, and timeouts.

## App Server and execution targets

- `AppServerClient`, the facade for `IAppServerClient`, sits on the send, reconnect, reader, timeout, pending-request, event-delivery, dynamic-tool, and target-setup paths; treat changes there as high risk. Keep `IAppServerClient` signatures and event shape, the JSON-RPC wire format, request IDs, and timeout, cancellation, and disconnect behavior unless the task changes that contract.
- Send App Server only collaboration mode strings that its schema or `collaborationMode/list` confirms; MyCodex-side names such as `office`, `daily`, or `coding` stay local.
- WSL paths go through the WSL bridge, path mapper, command executor, and command-line helpers. Remote Mac SSH and Remote Unix SSH share the remote Unix bridges; route remote filesystem, Git, skill, plugin, and MCP work through them with remote paths.

## Multiple targets

- Each target owns its own process, main window, and `AppServerClient`. Within one app channel, Windows is one target; bundled, system, and custom runtimes are properties of that target.
- Resolve target identity and paths before loading configuration or composing services, through `TargetCatalogStore` and `TargetInstanceCoordinator`. Identify instances by stable target ID and app channel, never by display name.
- Options, conversation state, logs, temporary files, and automation are target-scoped; the catalog, installation resources, and phone connection service settings are shared. Renaming a target, changing its runtime, or migrating data keeps its target ID, conversation history, and phone pairing; shared phone settings never make two targets' identities or pairings interchangeable.
- Auxiliary windows, hotkeys, and tray actions belong to their owning target. Closing or removing one target leaves the others running, and removal deletes only validated MyCodex configuration paths, keeping projects, repositories, Office files, and remote directories.
- Shared authentication, Relay, and runtime resources go through the existing ownership and lease coordination. Exit-all and updates finish save/cancel checks in every affected instance before stopping processes or replacing shared files; a refusal stops the operation (no forced termination), and runtime versions still in use stay installed.
- Automatic reading of the selected conversation follows each target's own setting in the foreground, background, and tray, however many targets run. New targets inherit the local Windows target's TTS channel name, and releasing one target leaves a shared TTS channel running.
- Notifications are the configured sounds only. Requests that open a dialog (approvals, tool and MCP input, pending questions) go through `InteractionRequestPromptPlanner`: the sound in the foreground and background, followed by speech only while the target is in the background; in the foreground the screen reader reads the dialog.

## Office Mode

- Office Mode is a MyCodex host-level mode for document work (`AppInteractionMode.Office`, workspaces marked `WorkspaceKind.Office`), not an App Server protocol mode. It maps onto App Server collaboration modes with developer instructions from `OfficeDeveloperInstructionComposer`.
- Coding and Office are coexisting interaction contexts: two tabs in one main window sharing its execution target. Switching tabs keeps work in the other mode running. Import and sync keep Office sessions and coding sessions apart.
- Office settings and retained Office state are scoped by execution target: the local Windows target, each WSL distribution, and each Remote Unix SSH profile. Options edits only the active target's Office root.
- The Office root defaults to the Documents `MyCodex-office` folder on Windows and `$HOME/MyCodex-office` on WSL and Remote Unix; an unresolved Unix home is reported as an error, never replaced by a Windows path. Target-specific directory work goes through `IOfficeRuntimeFileSystem` and the WSL and remote bridges.
- Office capabilities and document tooling live in `Application\Capabilities`, `Application\InternalTools`, the App Server dynamic tool mappers, and WinForms mode routing, outside general App Server transport code.

## Code style

- Target `net48` and its APIs; `LangVersion` is `latest`, with `Nullable` and `ImplicitUsings` disabled. Match the surrounding file: explicit `using` directives, block-scoped namespaces, Allman braces, `sealed` concrete classes.
- New dependencies need a clear task reason. JSON persistence and protocol parsing use `DataContractJsonSerializer` and `JavaScriptSerializer`.
- Non-linguistic comparisons use `StringComparison.Ordinal` or `OrdinalIgnoreCase`. Paths use `Path.Combine`, `Path.GetFullPath`, and `WorkspacePathUtilities`; text trimming uses `TextValueHelper.TrimToNull` and `TextValueHelper.FirstNonEmptyTrimmed`.
- Line endings follow `.gitattributes`: CRLF for C#, project, props, targets, solution, and batch files; LF for Markdown, JSON, and YAML. Scripted edits keep each file's existing encoding and line endings, including `.resx` and project files.

## WinForms, localization, and accessibility

Every UI change keeps keyboard and screen reader behavior working.

- UI strings use `UiLocalization.Text("Resource.Key", "Fallback")`; non-UI strings use `SharedLocalization.Text(...)`. Keys stay string literals. Each key goes into both the English and the `zh-CN` resource file (`Strings*.resx` in WinForms, `InternalStrings*.resx` in Shared) with the XML structure preserved. Resource tests fail on missing and unused keys.
- Mnemonic markers stay unique within a form, especially in `Strings.zh-CN.resx`; adding buttons, labels, or menu items with accelerators means updating the mnemonic tests.
- Build controls with `UiControlFactory` (`CreateActionButton`, `CreateDialogButton`, `CreateMutedLabel`, `CreateFillTextBox`, `CreateDropDownList`, `CreateDialogButtonBar`), the `UiTheme.Apply*` helpers with `UiGlyph` icons, `FormInteractionConventions.ConfigureRefreshButton` and `ApplyMnemonic`, and the existing themed controls (`ThemedTextBox`, `ChatWorkbenchControl`, `ActivityVirtualBrowseControl`). A per-form `Create*` helper is for layouts the factory does not cover.
- Focusable controls get a concise `AccessibleName` (no long visible descriptions or control-type words) and a predictable `TabIndex`. Display-only labels, status areas, decorative controls, and refresh controls get `TabStop = false` unless they need interaction.
- Status changes appear in visible status text that native controls expose. Forms stay resizable with a sensible `MinimumSize`.
- The authentication center and sign-in UI stay silent: screen readers read their controls on demand, and errors also open `AuthenticationCenterUiText.ShowError`. Their only spoken message is a short confirmation after a provider is enabled and MyCodex reconnects.
- Tests reach some private fields by name; rename such fields together with their tests.

## Tests

- NUnit 3 with `Assert.That`; `[Apartment(ApartmentState.STA)]` for WinForms. Tests stay deterministic and offline unless the user scopes manual or integration work: use fakes for network, external processes, GitHub, WSL, SSH, and Codex; put files under `TestContext.CurrentContext.WorkDirectory` in unique subdirectories; reach non-public members and controls through `TestSupport\FormTestDriver`.
- Add or update tests for behavior, contracts, safety boundaries, and regressions; reversible low-impact edits are covered by the existing checks. Test the mapper, planner, resolver, store, command builder, or protocol adapter before reaching for broad integration tests.
- Prefer behavior tests to tests that read source text. Source guards locate files through `SourceGuards\RepositorySource`, carry a `SourceGuardCategory`, and list retired names in `RetiredIdentifierGuardTests`.
- UI changes cover accessible names, tab order, layout and minimum size, mnemonics, and resource usage. Features that cross hosts cover Windows, WSL, Remote Mac SSH, and Remote Unix SSH paths.

## Security

- Logs and diagnostics carry no API keys, OAuth tokens, passwords, private key material, or secret-bearing command lines; diagnostics name only non-sensitive environment variables.
- Remote SSH passwords stay protected, and password and sensitive command temp files are deleted through the existing cleanup helpers. Git and GitHub output that may hold secrets passes through the existing sanitizers.
- Path mapping (Windows Sandbox mapping included), command-line escaping, and the allowed-root checks in deletion and extraction helpers are guardrails. Keep or strengthen them in scripts, archive extraction, plugin installation, build preparation, Windows Sandbox setup, update package handling, and remote file operations.
