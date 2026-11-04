# API contracts

All protected routes require a bearer access token issued by the configured OIDC authority. The API validates issuer, audience, signature, and lifetime. A tenant claim (default name `tenant_id`) must contain a provisioned tenant GUID, and the subject must have an active tenant membership. Unknown users and tenants are not provisioned automatically.

## Implemented routes

| Method and route | Access | Result |
| --- | --- | --- |
| `GET /health` | Anonymous | Health shell; does not expose LMS data |
| `GET /api/v1/connections` | Authenticated tenant member | Current user's connection metadata |
| `POST /api/v1/connections/canvas/start` | Authenticated tenant member | Starts authorization for a server-configured Canvas institution; body: `{ "institutionId": "..." }` |
| `GET /api/v1/connections/canvas/callback` | Anonymous, one-use state required | Completes Canvas OAuth and redirects to the configured frontend with a generic success/error marker (or returns the safe JSON result if no frontend callback is configured) |
| `DELETE /api/v1/connections/{connectionId}` | Authenticated owner | Revokes the Canvas token when possible and disconnects locally |
| `GET /api/v1/courses` | Authenticated tenant member | Active normalized courses; optional `connectionId` |
| `GET /api/v1/assignments/upcoming?from={utc}&to={utc}` | Authenticated tenant member | Normalized assignment events; date range capped at 90 days |
| `GET /api/v1/schedule?from={utc}&to={utc}` | Authenticated tenant member | Normalized course calendar events; date range capped at 90 days |
| `GET /api/v1/courses/{courseId}/progress` | Authenticated tenant member | Module-requirement progress or null metrics with an explanation |

OAuth callback failures are returned generically. Provider error details, access tokens, and refresh tokens are not returned to the browser. State is random, stored as a hash, bound to the initiating user, tenant, institution, host, and redirect URI, and expires after ten minutes.

The application token service refreshes Canvas credentials shortly before expiry for owner-scoped connections. If Canvas rejects the refresh token, the connection moves to `ReconnectRequired` and the caller receives a safe authorization error.

List routes accept an optional `connectionId`; when omitted, the user must have exactly one active connection. They return `{ "items": [...], "page": 1, "pageSize": 50, "totalItems": 0 }` by default and accept `page` plus `pageSize` (1–100). Errors use safe provider-neutral codes and never include raw Canvas response bodies. The Canvas adapter follows paginated `Link` headers only within the configured institution origin, uses bounded GET retries, includes course and personal calendar events, and applies explicit page, item, response-size, and 100-course limits for assignment and calendar queries.

`GET /api/v1/me` remains deferred. The Angular application uses a public OIDC client with Authorization Code + PKCE. API CORS accepts only configured exact origins (`Cors:AllowedOrigins`), with the development profile allowing `http://localhost:4200` and no credentialed cross-origin requests. The Canvas callback return destination is fixed by `Frontend:CanvasCallbackUri`; arbitrary redirect URLs are not accepted.

## MCP server

The MCP server exposes Streamable HTTP at `POST /mcp` using the official .NET MCP SDK. Requests require a bearer token validated by the configured OIDC issuer, with the configured audience, a valid `sub`, a tenant claim (default `tenant_id`), and an active provisioned tenant membership. The `/health` endpoint is anonymous and does not expose LMS data.

The server publishes OAuth Protected Resource Metadata at `/.well-known/oauth-protected-resource/mcp` and includes its location in the unauthenticated bearer challenge. The metadata points clients to the configured OIDC authority. Tool identity is resolved from validated claims; callers cannot supply a user or tenant ID. Optional date ranges default to the next 30 days and are capped at 90 days. List results are capped at 100 items per call. When more than one active LMS connection exists, the caller must supply `connectionId`. Course progress is restricted to a course visible through the selected connection.

Tool results use structured JSON output schemas. LMS-provided text is untrusted data and tool descriptions instruct AI clients not to follow embedded instructions. Provider exceptions and internal details are mapped to safe, provider-neutral tool errors. MCP auth and query handlers reuse the same bearer authentication, tenant membership resolver, and application query service as the API.

### Direct course pages

`get_course_page` accepts `courseId`, `pageSlug`, and optional `connectionId`. It uses the same tenant, owner, active-connection, and course-access checks as module tools, then requests `GET /api/v1/courses/{courseId}/pages/{pageSlug}`. It does not call the page index or require a module. Copy the final segment of a Canvas page URL as-is, preserving percent escapes; the adapter encodes this slug once for the API request.

The result contains `CourseId`, `PageSlug`, `Title`, `Content`, `Url`, `Resources`, `ContentTruncated`, `ResourcesTruncated`, `ContentScope`, and `RetrievedAt`. Text is limited to 20,000 characters and resources to 100 unique HTTPS links or media embeds, in page order. Each resource has `Title`, `Url`, `Type` (`CanvasPage`, `Link`, or `Embed`), and a nullable `PageSlug` for navigation within the same course. External sources are not fetched and do not receive Canvas credentials. Locked, unpublished, inaccessible, or malformed page responses remain errors, not empty successful results.

For page-based Learning Paths such as Stanford SEED's, see [the page retrieval guide](canvas-learning-paths.md). The snapshot MCP host does not expose this live tool.

`get_linked_resource` takes the same `courseId`, `pageSlug`, optional `connectionId`, and the exact `resourceUrl` returned by `get_course_page`. It re-reads the authorized page and requires the URL to appear in its visible resources before making any external request. The Canvas token is never sent to external hosts. Downloads are limited to supported Google hosts, HTTPS, four redirects, and 8 MB. Extracted text is limited to 30,000 characters. PDF text extraction does not include scanned images; Google Docs text export, Slides PDF export, and public Forms HTML are supported where the linked content is accessible. A status of `available`, `accessRequired`, `transcriptRequired`, `unsupported`, `tooLarge`, or `unavailable` tells the caller whether it can use `Content`. Private Google Drive files need a separate Google authorization path, and video spoken content needs a transcript source.
Pass `startOffset=0` for the first segment. If `NextOffset` is present, repeat the same request with `startOffset=NextOffset` until it is null. Each segment is at most 30,000 characters; the extraction ceiling is 1,000,000 characters, after which `Truncated` remains true with no `NextOffset`.
For an individual public YouTube video linked from the page, the tool uses a bundled Python helper and `yt-dlp` to request English captions. Install the pinned dependency from `backend/tools/requirements-transcripts.txt` on the MCP host. The helper validates the video ID and caption host, runs without Canvas credentials or cookies, and is bounded by a 35-second process timeout and text limits. If captions are absent, blocked, or the helper is unavailable, the tool returns `transcriptRequired` with no `Content`.

# Institution and MCP onboarding additions

`GET /api/v1/connections/institutions` requires an authenticated, provisioned tenant member and returns `{ id, name, provider, baseUrl, canConnect }[]` for that tenant. It never returns client IDs, secrets or OAuth scopes. `canConnect=false` means the administrator has not enabled the institution. OAuth start rejects such entries.

`get_connection_status` is an authenticated MCP tool with no user-supplied arguments. It returns `connections`, `institutions`, and `managementUrl` (null if no safe HTTPS portal URL is configured). Use it to select a connection or direct the participant to the account portal. All API/MCP protected endpoints require the exact `read:lms` scope in addition to identity and ownership checks.

