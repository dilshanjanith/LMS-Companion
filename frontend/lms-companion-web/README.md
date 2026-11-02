# Campus Companion

An Angular dashboard for securely browsing Canvas courses, assignments, schedule events, and course progress through the LMS Companion API.

## Local setup

From this directory:

```powershell
npm ci
npm.cmd start --script-shell=powershell.exe
```

Open `http://localhost:4200`.

## Runtime configuration

The SPA loads `public/app-config.json` at runtime. Set:

- `apiBaseUrl` to the LMS Companion API origin.
- `oidc.authority` to the exact HTTPS issuer that serves OIDC discovery.
- `oidc.clientId` to a public single-page application client ID.
- `oidc.scope` to `openid profile` plus the API scope expected by the issuer.
- `oidc.audience` to the exact API identifier configured by the OIDC provider.
- `canvasInstitutionId` to the institution provisioned on the API.

Register `http://localhost:4200/auth/callback` as the OIDC redirect URI and `http://localhost:4200/` as the post-logout redirect URI. Enable Authorization Code with PKCE and allow the SPA origin for OIDC discovery and token requests. Do not configure a client secret in the browser. Configure the API's exact CORS origin allowlist and its fixed Canvas return URL separately. Production deployments must use HTTPS and their real frontend origin.

The checked-in OIDC authority is intentionally invalid and the client ID is empty, so sign-in stays disabled until a real issuer and public client are provisioned. Canvas developer credentials remain server-side.

## Build and tests

The repository path contains a PowerShell metacharacter (`&`), so invoke the Angular CLI through Node directly:

```powershell
node .\node_modules\@angular\cli\bin\ng.js build
node .\node_modules\@angular\cli\bin\ng.js test --no-watch --no-progress
```
