# LMS Companion Claude plugin

Mintlify-style packaging for the authenticated LMS Companion MCP host.

- [`.mcp.json`](.mcp.json) — Canvas Streamable HTTP connector (update the URL after running the live tunnel script or deploying production).
- [`skills/lms-companion/SKILL.md`](skills/lms-companion/SKILL.md) — Study workflow and tool usage for Claude.

Install in Claude Code:

```text
/plugin marketplace add <path-to-this-repo>/claude-plugin/lms-companion
/plugin install lms-companion@lms-companion-marketplace
```

For Claude web, add the same `/mcp` URL under **Customize → Connectors** with **Sign in now**.

See [docs/lms-companion-plugin.md](../../docs/lms-companion-plugin.md) and [docs/claude-live-mcp-tunnel.md](../../docs/claude-live-mcp-tunnel.md).
