# GitCode Update Repository Setup

GitCode is used as the default public update hub for:

- MyCodex app installer updates.
- Legacy bundled-runtime manifests for older clients (packages use verified npm URLs).
- OfficeCLI mirror packages for users who cannot reliably download from GitHub.

Do not push the MyCodex source repository to GitCode. The GitCode repository is
only a distribution repository for manifests and release packages.

The client reads the stable manifest from a fixed GitCode Release attachment:

```text
https://api.gitcode.com/api/v5/repos/gcw_SpGZ48lW/mycodex-updates/releases/updates-stable/attach_files/stable.json/download
```

The `updates-stable` Release is the stable manifest pointer. GitCode Release
attachments are not reliably overwritten by uploading a file with the same name.
When replacing `stable.json`, delete the remote `updates-stable` tag/release and
create it again with the new attachment.

Package URLs in the manifest may point to GitCode Release assets or to an
external CDN/object storage URL when GitCode storage or traffic limits become a
problem.

## Repository Layout

Use the public GitCode repository dedicated to update distribution:

```text
https://gitcode.com/gcw_SpGZ48lW/mycodex-updates
```

Expected public repository contents:

```text
mycodex-updates/
  README.md
  updates/
    stable.json
    beta.json
```

Release assets should use versioned tags:

```text
app-v3.1.0
officecli-v1.2.3
updates-stable
officecli-stable
```

Previously published `runtime-standard-*` releases remain valid for old links; new
runtime publication uses direct verified npm URLs.

## Manifest Shape

Recommended `updates/stable.json`:

```json
{
  "schemaVersion": "1",
  "channel": "stable",
  "generatedAt": "2026-04-26T00:00:00Z",
  "packageBaseUrl": "https://api.gitcode.com/api/v5/repos/gcw_SpGZ48lW/mycodex-updates/releases/app-v3.1.0/attach_files/",
  "app": {
    "version": "3.1.0",
    "releaseDate": "2026-04-26",
    "releaseNotes": {
      "zh-CN": "Chinese release notes",
      "en-US": "English release notes"
    },
    "asset": {
      "url": "MyCodex_v3.1.0_Setup_20260426.exe/download",
      "fileName": "MyCodex_v3.1.0_Setup_20260426.exe",
      "sha256": "<sha256>",
      "size": 12345678
    }
  },
  "runtimes": [
    {
      "source": "Bundled",
      "version": "0.125.0",
      "targetTriple": "x86_64-pc-windows-msvc",
      "packageFormat": "npm-tgz",
      "asset": {
        "url": "codex-0.125.0-win32-x64.tgz/download",
        "fileName": "codex-0.125.0-win32-x64.tgz",
        "sha256": "<sha256>",
        "size": 12345678
      }
    }
  ]
}
```

`asset.url` may be either:

- Relative to `packageBaseUrl`, for GitCode Release assets. GitCode attachment
  downloads use `<fileName>/download`.
- Absolute, for direct CDN/object storage downloads.

## First-Time Setup

1. Sign in to GitCode.
2. Create a Personal Access Token from personal settings.
3. Put the token in the current PowerShell process:

```powershell
$env:GITCODE_TOKEN = "paste-token-here"
```

4. Create the public update repository:

```powershell
.\tools\gitcode\Initialize-GitCodeUpdateRepo.ps1 `
  -RepoName "MyCodex Updates" `
  -RepoPath "mycodex-updates" `
  -Description "Public update distribution repository for MyCodex"
```

5. Publish only distribution files to GitCode:

```powershell
.\tools\gitcode\Publish-GitCodeDistributionRepo.ps1 `
  -RepoUrl "https://gitcode.com/gcw_SpGZ48lW/mycodex-updates.git"
```

Do not run `git push gitcode main` from the MyCodex source repository.

## One-Command GitCode Release

The desktop installer must be built on Windows because it uses .NET Framework
and Inno Setup. Use the package-only GitCode release script:

```powershell
.\tools\gitcode\Publish-GitCodeRelease.ps1 `
  -ReleaseNotesZhCn "中文升级日志" `
  -ReleaseNotesEnUs "English release notes"
```

The script:

- Refuses to run if the source repository has a `gitcode` remote.
- Fetches and packages the official latest bundled Codex runtime by default.
- Builds the installer with that bundled runtime.
- Copies it to a unique release file name.
- Refreshes the legacy runtime manifest with verified npm download URLs before
  generating the app manifest; runtime archives are not uploaded to GitCode.
- Writes `updates/stable.json` as old-client-compatible ASCII JSON.
- Publishes only `README.md` and `updates/` to the GitCode distribution repo.
- Deletes and recreates `app-v<version>` and `updates-stable` tags/releases.
- Uploads the installer and manifest as GitCode Release attachments.
- Verifies the public manifest and installer size.

Use `-BundledCodexVersion <version>` only when you intentionally want to pin the
installer and standard runtime manifest to a specific Codex runtime version.
Use `-SkipStandardRuntimePublish` only when `updates/stable.json` already contains
correct verified runtime URLs for older clients. New clients discover stable
runtime versions independently of this app manifest.

Use `-SkipInstallerBuild` only when the versioned installer already exists and
you intentionally want to republish it.

Use `-PrepareOnly` with a non-stable channel when validating the local build and
manifest generation without touching GitCode:

```powershell
.\tools\gitcode\Publish-GitCodeRelease.ps1 `
  -Channel dryrun `
  -SkipInstallerBuild `
  -PrepareOnly `
  -ReleaseNotesZhCn "中文升级日志" `
  -ReleaseNotesEnUs "English release notes"
```

## Runtime Package Distribution

The bundled Windows runtime follows `@openai/codex@latest` directly. Version and
platform metadata come from the official npm registry, with a bounded npmmirror
metadata fallback when the official registry is unreachable. Only stable versions
are selected. Packages download from npmmirror first and fall back to the same
official archive if the mirror is unavailable or fails integrity validation.
The checked package version and upstream integrity are retained through the update
confirmation and download, so a later release cannot change an in-flight install.
Automatic updates do not downgrade installed versions. App updates still use the
GitCode manifest, and System, Custom, WSL and SSH runtimes remain separately managed.

Older clients still read `runtimes` in `updates/stable.json`. The daily GitCode
workflow `.gitcode/workflows/sync-standard-runtime.yml` refreshes that compatibility
manifest, verifies the Windows x64 npm archive, records a verified direct npm URL
and SHA-256/size, and republishes only the small `updates-stable` attachment.
It does not create a `runtime-standard-*` release or upload runtime tarballs.
This daily job does not control when new clients discover runtime updates.

The distribution repository still needs `GITCODE_TOKEN` permission to update its
manifest and Release pointer. No additional GitCode runtime upload is needed.
The existing command prepares local bundled files and refreshes the old-client
manifest:

```powershell
.\tools\gitcode\Publish-GitCodeRuntime.ps1
```

It resolves latest once, uses verified mirror-first downloads, retains the archive
under `artifacts/runtime/Bundled/<version>`, and updates only the legacy runtime
entries and stable pointer. App entries are preserved. `-Version <version>` still
pins an explicit local preparation/legacy publication; it does not pin new clients
to that version. `-TargetTriple x86_64-pc-windows-msvc,aarch64-pc-windows-msvc`
prepares both architectures; Windows x64 is the default. Preparation failure
preserves previous versions; publication failure leaves the prepared archive
available for retry. Repository cleanup is limited to validated `runtime/codex`
paths and never changes user installations or build output.

`tools/runtime/prepare-bundled-codex-runtime.ps1` is also used by builds and
packaging. Those callers prepare files without publishing or pruning versions.
The script uses the same mirror-first/official fallback and upstream integrity
rules; it does not change global npm configuration.

Offline verification:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/tools/PublishCodexRuntime.Tests.ps1
python tests/tools/test_runtime_registry_workflow.py
```

## OfficeCLI Mirror Distribution

OfficeCLI is mirrored to GitCode separately from the MyCodex app manifest. The
client first reads:

```text
https://api.gitcode.com/api/v5/repos/gcw_SpGZ48lW/mycodex-updates/releases/officecli-stable/attach_files/officecli.json/download
```

The `officecli-stable` Release contains only `officecli.json`. The versioned
`officecli-v<version>` Release contains:

```text
officecli-win-x64.exe
officecli-win-arm64.exe
SKILL.md
```

Publish or refresh the OfficeCLI mirror with:

```powershell
.\tools\gitcode\Publish-GitCodeOfficeCli.ps1
```

The script downloads the latest official OfficeCLI GitHub release, verifies the
Windows executables against `SHA256SUMS`, downloads the matching `SKILL.md`,
uploads the assets to GitCode, generates `officecli.json`, and republishes the
`officecli-stable` pointer. This does not modify `updates/stable.json`, so
OfficeCLI can be refreshed independently of a MyCodex release.

## GitCode Pipeline Boundary

GitCode's default hosted runners are Linux/EulerOS runners. They are suitable
for manifest validation and future scheduled Codex runtime synchronization, but
not for building the Windows WinForms installer unless a Windows runner is
available.
