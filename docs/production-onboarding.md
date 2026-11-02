# LMS Companion: institutional OAuth implementation

Status: development implementation; live Stanford SEED authorization and Claude OAuth have not been validated. Student personal tokens and exported course snapshots are not the product onboarding path.

## Single-student course-learning test

The development API now supports an explicit `Canvas:AllowPersonalTokenTesting` option. It is enabled only in the checked-in **Development** API settings. The Angular portal exposes a password field for the signed-in learner's own Canvas token when its runtime config enables the form. It validates the token against `GET /api/v1/users/self/profile` on the configured institution origin before storing it encrypted. A 401/403 returns `canvas_token_rejected` and stores nothing. Do not enter a token in Claude chat, source control, or a URL. Use this only for your own account; the multi-user Canvas flow still requires OAuth.

The remote MCP host uses the same database, encryption key ring, tenant membership, and Canvas provider as the API. New tools list modules, list module items, and retrieve current text for accessible Page and Assignment items. Each request checks course ownership; the content request verifies that the item belongs to the selected module. Text is bounded and includes source metadata. See [learning-evaluation.md](learning-evaluation.md) for the ten learning tasks and pass criteria.

The prior Stanford token attempt returned HTTP 401. The new form cannot override Stanford authorization. A successful profile validation and a successful module-content request are required before any live learning claim.

## User journey

1. Add the authenticated LMS Companion `/mcp` endpoint in Claude.
2. Sign in through the configured application identity provider (Auth0). A valid `read:lms` scope and provisioned tenant membership are required.
3. Ask to connect a learning account. `get_connection_status` returns the participant's connections, available institutions, and a fixed HTTPS management URL.
4. Open that portal, sign in to the same application account if needed, select Stanford SEED, and choose Connect Canvas.
5. The API creates single-use state bound to that user, tenant, institution and callback. Canvas performs university sign-in/consent. The backend exchanges the code and encrypts the access/refresh tokens.
6. The portal reports completion and tells the participant to return to their AI chat. Subsequent tools use the live Canvas adapter under that participant's ownership.

This release uses a portal handoff after MCP sign-in. It does not automatically chain Canvas onboarding inside Auth0's login transaction. The portal never receives Canvas credentials. Account linking uses the authenticated application identity, not matching emails between Canvas and Auth0.

## Current implementation and pending work

Implemented: OIDC SPA sign-in; bearer issuer/audience/lifetime checks; `read:lms` authorization; active tenant membership checks; institution discovery; disabled/pending institutions; Canvas authorization-code callback and hashed single-use state; encrypted token storage; refresh and disconnect; live courses, assignments, calendar and available course progress; MCP connection discovery and seven learning tools. A gated personal-token path supports development with one's own account.

Not yet verified externally: Claude-to-Auth0 registration and consent; production HTTPS hosting; Stanford developer key/scopes and live reads. Automated HTTP/provider tests use synthetic data and do not establish institutional access.

Remaining product work: invitation/account provisioning UX (currently operator-provisioned), announcements and content retrieval, connection relinking/deduplication, distributed refresh concurrency, operational rate limits, retention/deletion policy, monitoring/backups and a deployment security review. Do not describe the current code as production-ready or deploy it to a multi-user pilot until these gates are reviewed.

## While Stanford approval is pending

Both development hosts now select the Canvas provider with no mock fallback. The Stanford institution is listed as `Enabled: false`; it can appear in the portal without fake credentials and cannot start OAuth. Tests retain isolated mock providers. The configured pilot tenant is `30bc4d78-e13c-4583-8f07-9a7f06a301f2`; change it to the actual deployment tenant when needed.

Run PostgreSQL and migrations using the README, then the API, Angular app, and `LmsCompanion.Mcp` host. Use the same issuer and database in API and MCP. Provision an AppUser with the exact validated OIDC `sub` and an active membership matching the trusted tenant claim. Never grant membership based solely on email or a tenant ID supplied by a browser/tool argument.

The earlier `LmsCompanion.Mcp.Local` snapshot host, secret-URL route, Cloudflare demo script and snapshot plugin configuration remain legacy development utilities. They are not the production host and must not be used for this journey.

## Canvas administrator request

Request a scoped **Canvas API OAuth developer key**, not an LTI-only registration. Confirm approval for sending participant-authorized content to their selected AI service. Provide the final HTTPS callback once hosting is selected:

`https://<api-host>/api/v1/connections/canvas/callback`

The currently implemented adapter requires these exact scopes:

- `url:GET|/api/v1/courses`
- `url:GET|/api/v1/courses/:course_id/assignments`
- `url:GET|/api/v1/calendar_events`
- `url:GET|/api/v1/courses/:id` (course details with available progress)

Announcements, pages/files and module-content retrieval are future work; agree their exact endpoint scopes before enabling those features. Availability of a course progress field must be verified per course, and missing progress remains null.

## Enable an approved institution

Configure the same institution entry in API and MCP using environment variables or a secrets manager:

```text
Canvas__Institutions__0__Id=stanford-seed
Canvas__Institutions__0__Name=Stanford SEED
Canvas__Institutions__0__TenantId=<provisioned-tenant-guid>
Canvas__Institutions__0__BaseUrl=https://seedonline.stanford.edu/
Canvas__Institutions__0__Enabled=true
Canvas__Institutions__0__ClientId=<Canvas-developer-key-id>
Canvas__Institutions__0__ClientSecret=<secret-manager-value>
Canvas__Institutions__0__RedirectUri=https://<api-host>/api/v1/connections/canvas/callback
Canvas__Institutions__0__Scopes__0=url:GET|/api/v1/courses
Canvas__Institutions__0__Scopes__1=url:GET|/api/v1/courses/:course_id/assignments
Canvas__Institutions__0__Scopes__2=url:GET|/api/v1/calendar_events
Canvas__Institutions__0__Scopes__3=url:GET|/api/v1/courses/:id
Lms__Provider=Canvas
Lms__AllowMockProvider=false
```

Do not paste the client secret into a chat, source file, plugin manifest or frontend configuration. Enabled entries fail configuration validation if credentials, callback or explicit GET scopes are missing. Disabled entries cannot be used for OAuth, token refresh, or reads with an existing unexpired token.

## Claude / Auth0 deployment configuration

Select stable public HTTPS addresses before registering callbacks. Configure:

- MCP `Mcp__PublicResourceUri=https://<mcp-host>/mcp` (required outside Development). This fixes the discovery resource identity behind proxies.
- MCP `Frontend__ConnectionManagementUri=https://<portal-host>/dashboard`. No token, query or fragment is allowed in the management URL.
- API `Frontend__CanvasCallbackUri=https://<portal-host>/dashboard` and exact `Cors__AllowedOrigins__0=https://<portal-host>`.
- Exact API/MCP `AllowedHosts`, shared PostgreSQL connection, and shared encrypted Data Protection key ring (see security.md).
- Register an Auth0 API whose identifier is the MCP public resource URI; configure the matching `Authentication__Audience` and SPA audience, with permission `read:lms`. This avoids mismatching MCP's resource indicator with the old development API audience.
- Configure Auth0's Resource Parameter Compatibility Profile so MCP `resource` requests resolve to the correct audience. Decide explicit client registration versus supported dynamic registration; authorize the Claude client for this API. Obtain the exact callback from Claude's setup rather than guessing or allowing wildcards.
- Keep the SPA public client separate from the Claude OAuth client and the Canvas confidential client. Issue the trusted tenant claim for both application clients, and provision the matching database membership.

Claude must use OAuth/sign-in, not No sign-in or a shared secret URL. The MCP host validates access tokens; it does not implement an authorization server or client-registration endpoint. Those responsibilities belong to Auth0. These account-side settings require validation with the actual Auth0 tenant and Claude account.

Use a stable HTTPS deployment that supports the SDK's Streamable HTTP transport. The previous temporary snapshot tunnel is not a deployment plan. Configure trusted proxy forwarding and TLS according to the chosen hosting provider; never trust arbitrary forwarded hosts.

## Acceptance checks before pilot

1. Anonymous MCP requests receive 401 with OAuth discovery; wrong issuer/audience/expired tokens are rejected; missing `read:lms` is denied.
2. Two users can see only their own connections and tenant-approved institutions. Cross-user guessed IDs and unprovisioned users are denied.
3. Pending Stanford cannot start OAuth; after configuration, its authorization page uses the exact approved callback/scopes.
4. State replay, expiry, revoked membership and provider denial fail without storing credentials.
5. Connect with the student account; compare live courses and assignments against Canvas. No snapshot fallback is allowed.
6. Test refresh, revoke/reconnect, disconnect, and deletion of local credentials. Subsequent reads must fail after disconnect.
7. Verify no credentials, authorization codes, state query strings, or course bodies appear in application/proxy logs.

## Verification on 2026-10-30

- .NET solution compiled; 45 integration tests and 18 unit tests passed.
- Connected backend journey tests exercise the real controllers, application services, EF connection/membership repositories, encrypted vault, Canvas HTTP adapter and MCP transport. They cover connection start/callback, encrypted storage, courses/assignments through MCP, cross-account denial, replay, disconnect, missing scope and invalid inputs.
- These tests substitute application identity and Canvas HTTP responses, use EF InMemory, and substitute the PostgreSQL state repository. They do not validate browser sign-in, real JWT discovery, PostgreSQL transaction semantics, or live Claude/Stanford access.
- Fixed an HTTP request-binding failure in the Canvas start endpoint: positional-record validation attributes must target the constructor parameter. Added no-store/no-referrer headers on Canvas callbacks.
- Angular: 8 tests passed; production build passed.
- Angular reports a missing optional `lmdb` compiler-cache module; output is unaffected.
- No Stanford token exchange, Claude browser OAuth, or public deployment was executed in this change.
- Stanford developer-key approval is still pending, as confirmed by the user. Keep the institution disabled until the approved credentials are configured.

References checked 2026-10-30:

- https://canvas.instructure.com/doc/api/file.oauth.html
- https://canvas.instructure.com/doc/api/file.developer_keys.html
- https://support.auth0.com/center/s/article/mcp-audience-error-with-auth0
- https://support.claude.com/en/articles/11176164-use-connectors-to-extend-claude-s-capabilities
