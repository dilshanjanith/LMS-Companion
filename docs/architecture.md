# Architecture

## Dependency direction

```text
Domain <- Application <- Infrastructure
                         ^           ^
                         |           |
                    API host      MCP host
```

- **Domain** owns provider-neutral entities, value objects, and business rules.
- **Application** owns use cases, authorization-aware queries, and provider contracts.
- **Infrastructure** implements persistence, credential handling, and LMS adapters.
- **API** and **MCP** are independent ASP.NET Core hosts. They share application services and do not duplicate business rules.
- **Angular** talks only to the API and never receives LMS credentials.

The API and MCP hosts use OIDC bearer authentication with an authenticated-user fallback policy. The API maps the configured subject and tenant claims to a provisioned membership before connection operations. `/health` is explicitly anonymous.

## Provider boundary

Application defines the normalized read-only LMS contract, connection repository boundary, query service, tenant membership, OAuth flow, token refresh, and connection management. Infrastructure provides Canvas OAuth exchange/refresh/revocation, the typed Canvas REST read adapter, PostgreSQL repositories, and Data Protection credential storage. The provider maps active courses, calendar assignments, schedule events, and module requirement progress into provider-neutral DTOs. Provider calls receive a server-created request context only after user, tenant, and connection ownership checks. Date ranges are normalized to UTC and limited to 90 days, and progress lookups first verify that the course belongs to the selected connection.

## Persistence and configuration

Infrastructure owns `CompanionDbContext` and its versioned PostgreSQL migrations. Memberships, connections, credentials, OAuth state, and audit events are persisted. Provider credentials are encrypted before storage and addressed through owner-scoped opaque references. Connection strings and institution secrets are supplied by environment configuration or .NET user-secrets. Local development uses the PostgreSQL service in `docker-compose.yml`. Production migrations are explicit; the application does not create or migrate the schema automatically on startup. Production must supply a durable, protected Data Protection key ring.

## Free single-user MCP demo

`LmsCompanion.Mcp.Local` offers a separate snapshot path for the Stanford SEED pilot. Its Canvas import parser accepts browser-exported JSON and maps courses and upcoming events into read-only local snapshots. The same snapshot tools are available through stdio and a stateless Streamable HTTP endpoint that returns JSON. Claude is an optional MCP client; the transport and tool names are independent of the model provider. The remote demo binds to loopback and requires a random bearer key, while a temporary Cloudflare Quick Tunnel provides HTTPS reachability. This path does not use Auth0, tenant membership, the Angular dashboard, or live Canvas OAuth. It is limited to one user's imported data. Future university LMS adapters should implement the existing application `ILmsProvider` boundary and preserve the normalized tool contract; multi-user remote access additionally requires per-user authorization rather than the demo's shared key.
