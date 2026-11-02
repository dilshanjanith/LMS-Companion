# AI LMS Companion

A read-only LMS companion that normalizes course information and makes it available to an Angular dashboard and authenticated AI assistants. The repository is being built in phases; the current implementation includes the bootstrap, normalized query contracts, authenticated connection management, Canvas read adapter, MCP tools, and an Angular OIDC sign-in/dashboard.

**Current product path:** [institutional OAuth onboarding](docs/production-onboarding.md). Stanford SEED remains disabled pending its developer key. Development hosts use the live Canvas adapter; no data is returned from snapshots as a fallback. The local/snapshot guides below describe earlier development utilities, not the multi-user product.

For the single-student course-learning test, the development portal can accept **your own** Canvas token, validate it, and store it encrypted. Canvas returned HTTP 401 for the previously tried Stanford token, so live access is still unverified. The MCP host now exposes module and learning-material tools for grounded tutoring. Use the [evaluation rubric](docs/learning-evaluation.md) to test answers, quizzes, cases and feedback once live content is accessible.

## Prerequisites

- .NET 10 SDK
- Node.js 24 and npm
- Docker Compose

## Start locally

From the repository root:

```powershell
docker compose up -d postgres
dotnet restore .\LmsCompanion.slnx
dotnet run --project .\backend\src\LmsCompanion.Api
```

The API listens on the URL printed by `dotnet run`. Its `/health` endpoint is anonymous. Protected routes require a configured OIDC issuer and the read:lms scope; the checked-in development settings reference the pilot Auth0 tenant, whose client grants and membership still need verification. Set `Authentication__Authority`, `Authentication__Audience`, and (if needed) `Authentication__TenantClaim` through user-secrets or environment variables. Canvas OAuth additionally requires a provisioned tenant membership and server-side institution configuration. The local Compose database password is for development only; replace it with a user secret or environment variable before using any shared environment.

Apply the schema explicitly after PostgreSQL is available:

```powershell
dotnet tool restore
dotnet dotnet-ef database update --project .\backend\src\LmsCompanion.Infrastructure --startup-project .\backend\src\LmsCompanion.Api
```

See [docs/api-contracts.md](docs/api-contracts.md) for routes and [docs/security.md](docs/security.md) for OIDC, Canvas, and production key-ring configuration.

In a second terminal:

```powershell
cd .\frontend\lms-companion-web
npm ci
npm.cmd start --script-shell=powershell.exe
```

Run the MCP host separately when needed:

```powershell
dotnet run --project .\backend\src\LmsCompanion.Mcp
```

The MCP server uses authenticated Streamable HTTP at `http://localhost:5124/mcp` when launched with the configured development profile. It requires the same OIDC bearer token, tenant membership, and LMS connection authorization as the API. See [docs/api-contracts.md](docs/api-contracts.md) for tool schemas and supported calls.

For a single-user Claude Desktop pilot using your own Canvas access token, follow [the local Canvas MCP guide](docs/claude-desktop-local.md). That path runs a separate stdio MCP host and does not depend on the Angular dashboard or Auth0 setup.

For Claude with **live** portal-connected Stanford data, use the [LMS Companion Claude plugin](docs/lms-companion-plugin.md) and [live MCP tunnel guide](docs/claude-live-mcp-tunnel.md) (`LmsCompanion.Mcp` + Auth0 + personal token in the portal).

For the free, client-neutral Stanford SEED **snapshot** demo, follow [the snapshot MCP and Cloudflare tunnel guide](docs/claude-web-cloudflare.md). That path uses `LmsCompanion.Mcp.Local` and imported JSON, not the authenticated product host.

## Build and test

```powershell
dotnet build .\LmsCompanion.slnx --configuration Release
dotnet test .\LmsCompanion.slnx --configuration Release
cd .\frontend\lms-companion-web
npm ci
node .\node_modules\@angular\cli\bin\ng.js build
node .\node_modules\@angular\cli\bin\ng.js test --no-watch --no-progress
```

The explicit PowerShell script shell keeps npm commands working when the Windows repository path contains shell metacharacters such as `&`.

## Architecture

See [docs/architecture.md](docs/architecture.md), [docs/security.md](docs/security.md), and [docs/api-contracts.md](docs/api-contracts.md).

## Current phase

Phase 0 established project boundaries, PostgreSQL/EF Core wiring, health endpoints, CI, and the Angular shell. Phase 1 adds normalized LMS contracts, owner/tenant-scoped query services, a mock adapter, and tests. Phase 2 adds OIDC bearer validation, tenant membership resolution, PostgreSQL persistence, encrypted credentials, Canvas authorization, refresh and revocation, disconnect endpoints, and audits. Phase 3 adds the typed read-only Canvas API adapter, pagination, safe error mapping, bounded retries, and authenticated course, assignment, schedule, and progress routes. Phase 4 adds authenticated MCP tools and OAuth resource metadata. Phase 5 adds the OIDC sign-in and responsive Canvas dashboard. Phase 6 pilot readiness and operational validation remain next.



