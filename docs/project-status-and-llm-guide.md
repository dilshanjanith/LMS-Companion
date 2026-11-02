# LMS Companion: status, blockers, and live learning guide

Updated: 30 October 2026 (Asia/Colombo).

## Goal and current result

LMS Companion lets a learner connect a university LMS account and ask an AI assistant about current courses and learning material through read-only MCP tools. Canvas is the first LMS adapter; Stanford SEED is the pilot institution. The long-term flow is: add LMS Companion in an AI client, sign in to LMS Companion, select an institution, authorize Canvas, and ask questions grounded in the learner's permitted course content.

**Verified now:** a newly generated Stanford SEED personal access token returned **HTTP 200** from `GET https://seedonline.stanford.edu/api/v1/courses?per_page=1` in Postman. The same token connected through the local portal at `http://localhost:4200/dashboard`. The dashboard reported **1 account connected**, **1 course in progress**, and displayed the live course **STP South Asia '26**. This proves the local API-to-Canvas path for that learner. It does **not** yet prove that Claude can authenticate to the remote MCP host or retrieve module text.

The test token expires **1 November 2026 at 12:00 AM**, as shown in Canvas. Replace it through the portal when it expires. Do not put tokens in chat, URLs, source files, screenshots, or this document.

## What has been implemented

| Area | Implemented state |
| --- | --- |
| Architecture | .NET 10 Clean Architecture solution with Domain, Application, Infrastructure, API, and MCP hosts; Angular portal; PostgreSQL/EF Core persistence. |
| Identity | Auth0 OIDC sign-in with Authorization Code + PKCE for Angular; API and MCP bearer validation, `read:lms` scope, trusted tenant claim, and provisioned membership checks. |
| Connections | Tenant-scoped institution discovery, owner-scoped connection management, Canvas OAuth start/callback, encrypted token storage, refresh/disconnect, and audit records. Stanford's institutional OAuth route is disabled until its developer key is approved. |
| Development token path | In Development only, the signed-in learner can enter their own Canvas personal access token in the portal. The API validates it against Canvas and stores it encrypted for that learner. |
| Live reads | Canvas adapter for courses, assignments, calendar, progress where Canvas supplies it, modules, module items, and accessible Page/Assignment text. It uses bounded pagination, response limits, safe errors, and owner checks. |
| MCP | Authenticated Streamable HTTP `/mcp` host with `get_connection_status`, `get_my_courses`, `get_upcoming_assignments`, `get_class_schedule`, `get_course_progress`, `get_course_modules`, `get_module_items`, and `get_learning_material`. These are retrieval tools; the LLM produces explanations, quizzes, cases, and feedback. |
| UI and tests | Angular dashboard and connection controls. Automated .NET and Angular tests cover important application flows with synthetic identity/Canvas responses; they are not a substitute for live Claude validation. |

The old `LmsCompanion.Mcp.Local` and Cloudflare Quick Tunnel guide serve imported **snapshots**. They are a separate, legacy demo and do not read the live connection established in the portal.

## Problems encountered and resolution

| Problem | Cause or evidence | Resolution / present state |
| --- | --- | --- |
| Auth0 sign-in returned `provider_invalid_request` | The web client was not authorized for the `https://lms-companion-api` resource server. | Authorized the client and requested the API's `read:lms` scope; sign-in then worked. |
| Sign-in returned `provider_access_denied` / no institution | Auth0 Action required the trusted institution/tenant assignment. | Corrected the assignment and provisioned the local user with an active tenant membership. Auth0 identity and Canvas identity may be different accounts; linking is based on the signed-in app user, not email equality. |
| `membership_not_found` and dashboard access errors | The JWT subject did not match a provisioned `AppUsers.ExternalSubject` record. | Provisioned the exact validated `sub` and active membership in PostgreSQL; avoid using an email address as the subject. |
| `dotnet ef` could not retrieve project metadata | Command was run from `backend` but included an extra `backend` path segment. | Used `--project .\src\LmsCompanion.Infrastructure --startup-project .\src\LmsCompanion.Api` from `backend`; migration applied. |
| Earlier Canvas tokens returned 401 `Invalid access token` | Canvas itself rejected them in Postman. This was unrelated to Auth0 or the portal. | Generated a fresh token on the signed-in Stanford SEED Canvas Settings page, set a short expiry, tested it in Postman (200), then connected it in the portal. |
| Claude custom connector asked for an API-key header that its UI did not offer | The earlier snapshot demo expected a shared key; the Claude form exposed no request-header field. | That snapshot workaround is not the target product. The live MCP host uses per-user OIDC/OAuth. Claude-to-Auth0 OAuth registration and deployment remain to be verified. |
| Stanford OAuth developer key unavailable | A student personal token is not an institutional OAuth application registration. | Use the gated personal-token path for this learner's local development test. Keep the production Stanford OAuth institution disabled while approval is pending. |

## Local startup and connection check

From the repository root `D:\Azend\canvas R&D\CD`:

```powershell
docker compose up -d postgres
dotnet tool restore
dotnet dotnet-ef database update --project .\backend\src\LmsCompanion.Infrastructure --startup-project .\backend\src\LmsCompanion.Api
dotnet run --project .\backend\src\LmsCompanion.Api
```

In separate terminals, run:

```powershell
dotnet run --project .\backend\src\LmsCompanion.Mcp
```

```powershell
cd .\frontend\lms-companion-web
npm.cmd start --script-shell=powershell.exe
```

Check the API at `http://localhost:5150/health`, MCP host at `http://localhost:5124/health`, and portal at `http://localhost:4200/dashboard` (use the URLs printed by each host if ports differ). Sign in with the provisioned Auth0 account. For the **Development-only** personal-token path, create a token while signed in to `https://seedonline.stanford.edu/profile/settings` under **Approved Integrations → New Access Token**. Test `GET https://seedonline.stanford.edu/api/v1/courses?per_page=1` in Postman using **Authorization → Bearer Token**; continue only after a 200 response. Enter the token into **Test with your own Canvas access token → Connect my token** in the portal. Confirm that a live course appears under **My courses**. A Canvas password or Auth0 client secret does not belong in this field.

## Connect the live account to Claude or another LLM client

The portal connection alone does not make Claude able to reach the MCP host. The current authenticated MCP endpoint is `http://localhost:5124/mcp`, which is only local. Claude web requires a stable public **HTTPS** `/mcp` URL and a completed per-user OAuth flow. Do not use the old snapshot tunnel URL for this live-data test.

1. Deploy the **`LmsCompanion.Mcp` host**, API, PostgreSQL, and protected Data Protection key ring behind stable HTTPS. Give the MCP host the same database, Auth0 issuer/audience configuration, tenant membership data, and encrypted-key access as the API. Configure `Mcp__PublicResourceUri=https://<mcp-host>/mcp` and `Frontend__ConnectionManagementUri=https://<portal-host>/dashboard`.
2. In Auth0, register/authorize the Claude OAuth client for the MCP resource and `read:lms` permission. The resource indicator, Auth0 API audience, and `Authentication__Audience` must match. Register the exact callback URL shown by Claude; do not guess it or use a wildcard. Configure the trusted tenant claim for this client. See [production-onboarding.md](production-onboarding.md) for the full deployment details.
3. In Claude web, use **Customize → Connectors → Add custom connector**, enter the stable HTTPS `/mcp` URL, choose **Sign in now** (or **Sign in when needed**), and complete Auth0 sign-in with the same provisioned LMS Companion account used in the portal. Select **Streamable HTTP**. Do not choose **No sign-in** for this authenticated endpoint.
4. Enable the connector in a Claude chat. Ask: **“Use LMS Companion `get_connection_status` and `get_my_courses` to show my live Stanford SEED course.”** The connected account should appear. If Claude says no connection exists, verify that its Auth0 `sub` and tenant match the portal account and that both hosts use the same database and encryption keys.
5. Ask Claude to call `get_course_modules`, `get_module_items`, and `get_learning_material` for an accessible module. Compare one retrieved source title and link with Canvas before evaluating generated answers. A module may contain unsupported or locked item types; only accessible Page text and Assignment descriptions are currently retrievable as learning material.

Any LLM client that supports authenticated MCP over Streamable HTTP can use the same `/mcp` endpoint and tool contract. The client's OAuth setup differs, but Canvas integration and learner isolation stay server-side. A local client may be configured differently, provided it can supply a valid Auth0 bearer token for the MCP audience; this has not been verified with the current Stanford account.

## Learning tasks to try in Claude

Start each task by asking Claude to retrieve the relevant module and learning material through LMS Companion. Tell it to cite source links and distinguish course facts from invented practice examples. Paste **your own answer** only when asking for feedback. These prompts are examples, not new server tools.

| Task | Prompt after selecting a module |
| --- | --- |
| Answer questions | “Use LMS Companion to read the accessible material in module **[name]**. Answer **[specific question]** using only those sources, with links. Say if the material does not answer it.” |
| Explain in different ways | “Explain **[concept]** first in plain language, then for an experienced executive. Keep both explanations faithful to the retrieved module text.” |
| Guide through topics | “Create a 20-minute study path for this module. Order the topics, give one check question per step, and link each step to its source.” |
| Create mock quizzes | “Write five practice questions from this module: two recall, two application, one scenario. Do not reveal the answers yet.” |
| Produce answers and explanations | “Give the answer key for that quiz. Explain each answer and link the supporting source.” |
| Build case studies | “Create a clearly fictional business case that applies the module's concepts. Separate invented company details from course-grounded principles.” |
| Create scenario exercises | “Give me a decision scenario with three possible actions and trade-offs. Let me choose before you show your analysis.” |
| Review learner answers | “Here is my answer: **[your answer]**. Compare its claims with the retrieved module material; identify correct points and unsupported claims.” |
| Give constructive feedback | “Give specific, respectful feedback on my answer: what worked, what to change, and one stronger revised example grounded in the source.” |
| Suggest improvement areas | “Based on my answer and the module sources, name three topics to revisit, why each matters, and which source item to reread.” |

For repeatable evaluation, use [learning-evaluation.md](learning-evaluation.md): test two modules and one question the material cannot answer. A plausible answer without retrieved support is a failure.

## Remaining tasks and priorities

1. **Verify live module-content retrieval.** The course list is verified, but modules, Page text, Assignment descriptions, and the ten learning tasks have not yet been tested against this Stanford account. Check what the learner can access and record unsupported/locked items without claiming complete coverage.
2. **Make Claude's authenticated remote MCP connection work.** Deploy stable HTTPS hosts, finish Auth0 resource/client/callback configuration, and verify `get_connection_status`, `get_my_courses`, and `get_learning_material` end to end. Confirm tenant isolation and source citations in the AI response.
3. **Obtain Stanford's Canvas OAuth developer key and processing approval.** Configure exact approved read scopes and callback, then replace the personal-token development path with participant authorization. The student's token proves a limited local path but is not the intended multi-user onboarding.
4. **Pilot hardening.** Add invitation/provisioning UX, stable deployment and backups, distributed token-refresh coordination, rate limits, monitoring, retention/deletion controls, and a security review. Test refresh, revoke/reconnect, disconnect, and cross-user denial.
5. **Broader LMS support.** Implement another `ILmsProvider` adapter behind the normalized application contract after Canvas pilot validation. Keep one LMS Companion MCP surface so each AI client sees consistent tools regardless of the institution's LMS.

The current result is a successful **local live Canvas connection**, not a completed Claude learning evaluation or production deployment.
