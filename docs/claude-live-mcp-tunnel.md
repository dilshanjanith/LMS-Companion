# Live MCP tunnel for Claude (Stanford personal-token pilot)

This path exposes **`LmsCompanion.Mcp`** over HTTPS for Claude web or Claude Code. It uses the same PostgreSQL database, encrypted credentials, and Auth0 membership as the Angular portal. It does **not** use `LmsCompanion.Mcp.Local` or the snapshot Cloudflare script in [claude-web-cloudflare.md](claude-web-cloudflare.md).

## Prerequisites

- PostgreSQL, API, Angular portal, and MCP host configured like [project-status-and-llm-guide.md](project-status-and-llm-guide.md)
- A provisioned Auth0 user with `read:lms` and the pilot tenant claim
- Stanford SEED connected in the portal via **Connect my token** (development personal-token path)
- `cloudflared` installed at `C:\Program Files (x86)\cloudflared\cloudflared.exe`

## 1. Start the stack

```powershell
docker compose up -d postgres
dotnet run --project .\backend\src\LmsCompanion.Api
dotnet run --project .\backend\src\LmsCompanion.Mcp
cd .\frontend\lms-companion-web
npm.cmd start --script-shell=powershell.exe
```

Confirm `http://localhost:5150/health`, `http://localhost:5124/health`, and the dashboard show your live course.

## 2. Open the public HTTPS `/mcp` URL

From the repository root:

```powershell
.\backend\tools\start-live-mcp-claude.ps1
```

The script tunnels `http://127.0.0.1:5124` through Cloudflare Quick Tunnel, copies `https://<host>.trycloudflare.com/mcp` to the clipboard, and updates [claude-plugin/lms-companion/.mcp.json](../claude-plugin/lms-companion/.mcp.json) for local plugin installs.

The tunnel hostname changes when the tunnel restarts. Update Auth0 and restart MCP when it changes.

## 3. Auth0 (Claude OAuth)

Claude requests tokens for the MCP **resource** URL, not the API audience used by the Angular SPA.

1. In Auth0, create an **API** whose identifier is exactly the tunnel URL, for example `https://abc.trycloudflare.com/mcp`.
2. Add permission `read:lms` (same semantics as the SPA API).
3. Authorize the Claude OAuth client for this API and register Claude’s exact callback URL (from Claude’s connector setup).
4. Issue the trusted tenant claim for that client (same pilot tenant as the portal user).
5. Restart the MCP host with matching configuration:

```powershell
$env:Mcp__PublicResourceUri = 'https://abc.trycloudflare.com/mcp'
$env:Authentication__Audience = 'https://abc.trycloudflare.com/mcp'
dotnet run --project .\backend\src\LmsCompanion.Mcp
```

Use the same `DataProtection` key ring as the API so MCP can decrypt the portal-stored Canvas token.

See [production-onboarding.md](production-onboarding.md) for production hostnames and [lms-companion-plugin.md](lms-companion-plugin.md) for plugin installation.

## 4. Connect in Claude

**Claude web:** Customize → Connectors → Add custom connector → URL `https://<host>/mcp` → **Sign in now** → Streamable HTTP.

**Claude Code:** install the plugin from the marketplace file (see [lms-companion-plugin.md](lms-companion-plugin.md)).

Ask: *Use LMS Companion `get_connection_status` and `get_my_courses`.*

## Operational notes

- Personal Canvas tokens expire; paste a new token in the portal when reads fail with reconnection errors.
- Stanford institutional OAuth remains disabled until a developer key is approved.
- Do not share tunnel URLs or paste Canvas tokens into chat.
