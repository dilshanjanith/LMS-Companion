# Security baseline

- API and MCP hosts require OIDC bearer authentication by default; `/health` is explicitly anonymous.
- Default and fallback authorization policies require the exact `read:lms` scope. API and MCP clients must request it; an authenticated token without this scope is insufficient.
- Institution discovery exposes only tenant-scoped public summaries. `Enabled=false` institutions cannot start Canvas OAuth and need no placeholder credentials. See production-onboarding.md for the pending Stanford setup.
- The MCP host publishes Protected Resource Metadata that points clients to the configured OIDC authority. Its audience, issuer, signature, and token lifetime checks use the same bearer configuration as the API.
- MCP callers cannot choose a user, tenant, Canvas host, or credential. Tool handlers resolve claims to an active tenant membership and call the same owner- and tenant-scoped application queries as the API.
- MCP tools are read-only, bound date ranges to 90 days, cap list responses at 100 records, and map provider failures to safe tool errors. Course titles, assignment text, calendar titles, and progress explanations remain untrusted LMS data.
- API requests resolve the OIDC `sub` and configured tenant claim to a provisioned, active membership. Connection queries and mutations are scoped to both user and tenant.
- Canvas institutions, HTTPS base URLs, callback URI, client credentials, and explicit `url:GET|...` scopes are server configured. A caller cannot supply a Canvas host or broader scope.
- OAuth state uses a cryptographically random value, only its SHA-256 hash is stored, and it is consumed once. State is bound to user, tenant, institution, base URL, redirect URI, and a ten-minute expiry.
- Canvas access and refresh tokens are encrypted with ASP.NET Core Data Protection before persistence. The opaque credential reference is owner-scoped. Tokens are never included in API DTOs or audit data. Expiring tokens are refreshed server-side; invalid refresh grants mark the connection `ReconnectRequired`.
- Disconnect attempts Canvas token revocation, then removes local access even if Canvas is unavailable. Provider HTTP redirects are disabled and outbound calls have a bounded timeout.
- Canvas API calls use bearer headers, bounded retries, same-origin pagination validation, and page/item limits. No write methods are implemented in the adapter.
- Audit records contain event names and opaque connection/institution identifiers only; they have no token or course-content fields.
- Database schema changes are explicit EF migrations; startup does not mutate the schema.

## Required deployment configuration

The repository does not choose the pilot OIDC provider, production tenant provisioning path, or managed Data Protection key service. The Angular SPA uses a public OIDC client with Authorization Code + PKCE and keeps the OIDC access token in session storage; Canvas credentials remain server-side. API CORS uses the exact `Cors:AllowedOrigins` allowlist and denies other origins by default. Before deployment:

1. Set `Authentication__Authority` to the exact HTTPS OIDC issuer, `Authentication__Audience` to the API audience, and `Authentication__TenantClaim` to the trusted tenant claim emitted by that issuer. Never accept a tenant ID from request data.
2. Set `Canvas__Institutions__0__Id`, `TenantId`, `BaseUrl`, `ClientId`, `ClientSecret`, `RedirectUri`, and `Scopes__0...` from the Canvas developer key and deployment. Store `ClientSecret` in a secret manager or environment secret. Use only the exact callback URL registered in Canvas and least-privilege GET scopes for courses, course assignments, calendar events, and course progress.
   The adapter needs these GET scopes (use `Scopes__0` through `Scopes__3`): `url:GET|/api/v1/courses`, `url:GET|/api/v1/courses/:course_id/assignments`, `url:GET|/api/v1/calendar_events`, and `url:GET|/api/v1/courses/:id`.
3. Configure a durable, shared, access-controlled Data Protection key ring for every API instance using `DataProtection__KeyDirectory`, `DataProtection__CertificatePath`, and `DataProtection__CertificatePassword`. The certificate must include its private key. Key persistence and certificate encryption are enabled together by Infrastructure. Development uses default local key persistence; that default is not suitable for production or multiple replicas. Back up and rotate keys under an operational procedure.
4. Set `ConnectionStrings__LmsCompanion` from a secret manager, require TLS to PostgreSQL, and run the reviewed EF migration as a deployment step.
5. Set `Cors:AllowedOrigins` to the exact production Angular origin(s), set `Frontend:CanvasCallbackUri` to the fixed HTTPS dashboard destination, deploy behind HTTPS, configure rate limits and callback protections, and keep secrets and authorization headers out of logs. Provision the SPA client with the exact `/auth/callback` redirect URI and API audience/scope.

Tenant provisioning and rate limiting remain deployment responsibilities. The Angular OIDC client, API CORS policy, and fixed frontend return from Canvas OAuth are implemented. Live sign-in and Canvas connection require a provisioned pilot issuer/client, tenant membership, and Canvas institution configuration. Canvas authorization uses the documented confidential-client authorization-code exchange and does not assume PKCE parameters that Canvas does not document.

All LMS operations remain read-only. LMS-provided text is untrusted and must be treated as data, not instructions, when later passed to an AI model.

