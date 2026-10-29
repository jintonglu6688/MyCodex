---
name: browser-use
description: Use for inspecting or interacting with websites in the user's Microsoft Edge or Google Chrome, including webpage accessibility checks. Use the cua_repl browser API supplied by MyCodex.
---

# Browser automation

For web pages in Edge or Chrome, use `mcp__cua_repl__js`. Read its current tool instructions and returned documentation before further actions. Follow its first-call initialization rule; when selecting an already-running Edge browser, the entry point is `await cua.getBrowser({ id: "edge" })`. Use `chrome` for Google Chrome.

Continue with the documented tab-listing and page-inspection APIs. For accessibility work, inspect the requested page's semantic structure and keyboard behavior through the browser API.

`node_repl` with `@oai/sky` controls Windows desktop interfaces. Keep website work on the browser API. Browser-internal pages and browser chrome require a supported native interface and may be inaccessible through the extension.

If `cua_repl` is absent, tell the user to open MyCodex's Options > Browser Control, enable browser control and select OK. MyCodex prepares the plugin and local bridge and applies the change when current work finishes. Extension installation is offered separately for Edge and Chrome on that page. If a browser or its extension cannot connect, report the actual browser-tool error. Preserve site approvals and all tool safety boundaries.
