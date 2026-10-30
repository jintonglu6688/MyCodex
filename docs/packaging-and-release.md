# Packaging and Release

Read this before running a full build, packaging, installer, update, or GitCode
script, before changing the authentication backend, Relay, or runtime pins, and
before adding packaged assets. Ordinary development uses the direct `dotnet`
commands in `AGENTS.md` instead.

## Full build and packaging scripts

These scripts have side effects. Run them only when that side effect is the task.

- `build-dev.bat` and `build.bat` invoke `tools\build\Build-MyCodex.ps1`. They
  prepare the pinned authentication backend, may download the bundled Codex
  runtime, build the app, clean obsolete output, and prepare the pinned Relay
  release. Relay preparation reuses a verified `runtime\relay` cache or downloads
  and verifies the release; no Relay source checkout is needed.
- Authentication preparation uses
  `tools\authentication-center\Prepare-AuthenticationHost.ps1`: it reuses a
  verified `runtime\auth-core` package or gets the pinned Windows package through
  `Prepare-AuthenticationHostRelease.ps1` from its download cache or the GitHub
  Release. Both build profiles use the same released backend; no sibling checkout
  or Rust toolchain is needed. Building the backend from source requires passing
  `-SourceRoot` explicitly.
- `package.bat` builds Release first, then runs `tools\installer\build-installer.ps1`
  with `-SkipBuild` to avoid a second build. The installer script needs Inno Setup 6
  and a valid prepared `runtime\auth-core` package, prepares the bundled runtime,
  and prepares the same pinned Relay release. `-SkipBuild` skips neither those
  prerequisites nor the batch file's initial build.
- Relay pins live in `tools\runtime\relay-runtime.version` and
  `relay-runtime.sha256`. Verify preparation with
  `powershell -NoProfile -ExecutionPolicy Bypass -File tests\tools\PrepareRelayRuntime.Tests.ps1`.
- `tools\update\Build-MyCodexRelease.ps1` builds the installer and update manifest
  artifacts and inherits the installer prerequisites unless it uses an existing
  installer.
- `tools\gitcode\Publish-GitCodeRelease.ps1` publishes to GitCode. Run it only for
  an explicit release; see `docs/gitcode-update-repo.md`.
- When packaging is in scope and a prerequisite is missing, report it. Build or
  change a sibling repository only when the user puts that work in scope.

## Authentication backend

For backend version bumps, cloud builds, releases, or GitCode mirroring, first read
`docs/mycodex-auth-release.md` in the backend repository (normally
`..\MyCodex-auth-core`). It covers backend publication and the frontend
version and hash updates. MyCodex's own app-update publishing is a separate
workflow.

## Distribution

- `updates\stable.json` is the app manifest distribution file and may lag
  `Version.props` between releases. New clients discover bundled runtime updates
  directly from stable npm metadata and prefer verified npmmirror downloads;
  the manifest's runtime entries remain for older clients. Runtime archives are
  no longer uploaded to GitCode.
- GitCode is only a public distribution repository for manifests and packages.
  The source repository never gets a `gitcode` remote.
- `.gitcode\workflows\update-verify.yml` validates the update JSON shape.
- Build the Windows installer on Windows. Hosted GitCode Linux runners suit
  manifest validation and other platform-neutral checks only.

## Packaged assets

The WinForms project copies or embeds shipped assets (the donate image, `tts`
screen reader libraries, `sound` files, and the `help` manuals). When adding one,
update `src\MyCodex.WinForms\MyCodex.WinForms.csproj` on purpose and match the
existing `CopyToOutputDirectory` behavior.
