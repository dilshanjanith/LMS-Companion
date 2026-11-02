# LMS Companion Claude plugin

The plugin packages a **hosted Canvas MCP connector** and a **study skill**, following the same layout as [Mintlify’s Claude plugin](https://github.com/mintlify/mintlify-claude-plugin): minimal `.claude-plugin/plugin.json`, root [`.mcp.json`](../claude-plugin/lms-companion/.mcp.json), [`.claude-plugin/marketplace.json`](../claude-plugin/lms-companion/.claude-plugin/marketplace.json), and `skills/lms-companion/SKILL.md`.

Installing the plugin does **not** connect Stanford. Each learner signs in to LMS Companion (Auth0) in Claude, then connects Canvas in the portal (personal access token for the Stanford SEED pilot until OAuth is approved).

## Package layout

| File | Role |
| --- | --- |
| [claude-plugin/lms-companion/.claude-plugin/plugin.json](../claude-plugin/lms-companion/.claude-plugin/plugin.json) | Plugin metadata |
| [claude-plugin/lms-companion/.mcp.json](../claude-plugin/lms-companion/.mcp.json) | Fixed HTTPS `/mcp` URL for the Canvas connector |
| [claude-plugin/lms-companion/.claude-plugin/marketplace.json](../claude-plugin/lms-companion/.claude-plugin/marketplace.json) | Local or Git marketplace entry |
| [claude-plugin/lms-companion/skills/lms-companion/SKILL.md](../claude-plugin/lms-companion/skills/lms-companion/SKILL.md) | When and how to call MCP tools |

Future LMS providers (Moodle, Blackboard) add additional entries under `mcpServers` in the same plugin, each with its own URL and auth flow, sharing one .NET backend.

## Public MCP URL

The connector URL must be stable **HTTPS** Streamable HTTP `/mcp` on **`LmsCompanion.Mcp`**, not `LmsCompanion.Mcp.Local`.

**Local Stanford pilot:** run [backend/tools/start-live-mcp-claude.ps1](../backend/tools/start-live-mcp-claude.ps1) after the API, portal, and MCP host are up. It updates `.mcp.json` with the Cloudflare Quick Tunnel URL. Follow [claude-live-mcp-tunnel.md](claude-live-mcp-tunnel.md) for Auth0 audience registration and MCP restart.

**Production:** set `.mcp.json` to `https://<mcp-host>/mcp` and deploy using [appsettings.Production.example.json](../backend/src/LmsCompanion.Mcp/appsettings.Production.example.json).

## Auth0 for Claude

Claude OAuth tokens must validate against the **MCP resource identifier** (the public `/mcp` URL), not only the SPA API audience (`https://lms-companion-api`).

1. Create or update an Auth0 API whose identifier equals `https://<mcp-host>/mcp`.
2. Define permission `read:lms`.
3. Authorize Claude’s OAuth client; register Claude’s exact callback URL.
4. Set MCP `Authentication:Audience` and `Mcp:PublicResourceUri` to the same `/mcp` URL.
5. Provision the same tenant membership and Data Protection key ring as the API.

Details: [production-onboarding.md](production-onboarding.md), [claude-live-mcp-tunnel.md](claude-live-mcp-tunnel.md).

## Install in Claude Code

From the repository root (adjust the path to your clone):

```text
/plugin marketplace add D:\Azend\copied from CD\CD\claude-plugin\lms-companion
/plugin install lms-companion@lms-companion-marketplace
```

Reload plugins if needed. Enable **LMS Companion Canvas** and complete Auth0 sign-in when prompted.

## Install in Claude web

1. **Customize → Connectors → Add custom connector**
2. URL: the same value as in `.mcp.json` (for example `https://<host>/mcp`)
3. **Sign in now** (or sign in when needed), **Streamable HTTP**
4. Enable the connector in chat

## Connect Stanford SEED

1. Open the portal (`http://localhost:4200/dashboard` locally, or your deployed dashboard).
2. Sign in with the **same** LMS Companion account used in Claude.
3. Select Stanford SEED → **Connect my token** → paste a personal access token from Canvas **Approved Integrations**.
4. Confirm the live course appears on the dashboard.
5. In Claude, ask: *Use LMS Companion `get_connection_status` and `get_my_courses`.*

Then use modules and learning material:

- `get_course_modules`, `get_module_items`, `get_learning_material`

For tutoring behavior (quizzes, cases, feedback), follow [learning-evaluation.md](learning-evaluation.md). Canvas tokens belong only in the portal, never in chat or the plugin files.

## Verification checklist

- [ ] MCP `/health` returns 200 behind HTTPS
- [ ] Anonymous `/mcp` returns 401 with OAuth metadata
- [ ] Claude sign-in completes with `read:lms`
- [ ] `get_my_courses` returns the same course as the portal
- [ ] One module item matches Canvas after `get_learning_material`

Automated integration tests cover personal-token storage and MCP reads with synthetic Canvas data; they do not replace Claude browser verification.
