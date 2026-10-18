# CLAUDE.md

@AGENTS.md

## Claude Code

- `AGENTS.md`, imported above, is the single source of repository rules for every coding agent. Put durable rules there; keep this file to Claude Code specifics.
- `.claude/settings.json` registers the shared `.codex/hooks/*.ps1` scripts for Claude Code: `SessionStart`, and `PreToolUse` for the `Bash` and `PowerShell` tools. `.codex/hooks.json` registers the same scripts for Codex. Change both registrations together and run `.codex\hooks\test_hooks.ps1`.
- Personal permissions and local overrides belong in `.claude/settings.local.json`, which Git ignores.
