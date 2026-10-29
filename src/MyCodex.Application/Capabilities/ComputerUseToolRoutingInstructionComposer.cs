namespace MyCodex.Application.Capabilities
{
    public static class ComputerUseToolRoutingInstructionComposer
    {
        public static string Compose(string existingInstructions, bool nativeAvailable, bool browserAvailable)
        {
            if (!nativeAvailable && !browserAvailable) return existingInstructions;
            var instructions = "# MyCodex UI tool routing\n" +
                "Use the capabilities configured for this conversation. Generic upstream tool descriptions can describe additional providers that this host has not enabled.\n";
            if (nativeAvailable)
                instructions += "For Windows desktop interfaces, including Settings, system dialogs and browser-internal pages, use the node_repl MCP server (mcp__node_repl__js) with @oai/sky, following the installed computer-use skill. That server owns the authorized desktop connection.\n";
            else
                instructions += "Native desktop control is unavailable in this conversation. Do not claim to inspect Windows interfaces through a browser tool.\n";
            if (browserAvailable)
                instructions += "The cua_repl MCP server (mcp__cua_repl__js) is configured for external browser pages only. Its sky RPC service is intentionally absent. Use it for Edge/Chrome web pages; native window APIs and nodeRepl.rpc('sky', ...) belong to node_repl instead.\n";
            if (nativeAvailable && browserAvailable)
                instructions += "If cua_repl reports 'Trusted RPC service is not configured: sky', this is the wrong tool for native inspection, not evidence that the desktop connection is broken. Continue the pending read-only inspection through node_repl while preserving the user's requested target and observation limits. Do not repeat an input action or use shell-based UI automation as a fallback.\n";
            if (!string.IsNullOrEmpty(existingInstructions) && existingInstructions.Contains(instructions)) return existingInstructions;
            return string.IsNullOrEmpty(existingInstructions) ? instructions : existingInstructions + "\n\n" + instructions;
        }
    }
}
