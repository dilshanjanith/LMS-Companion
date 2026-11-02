from pathlib import Path
from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml.ns import qn
from docx.oxml import OxmlElement
from docx.shared import Inches, Pt, RGBColor


def set_run_font(run, name="Calibri", size=11, bold=False, color=None):
    run.font.name = name
    run.font.size = Pt(size)
    run.bold = bold
    run._element.rPr.rFonts.set(qn("w:eastAsia"), name)
    if color:
        run.font.color.rgb = color


def add_heading(doc, text, level):
    p = doc.add_heading(text, level=level)
    for run in p.runs:
        run.font.color.rgb = RGBColor(0x1F, 0x3A, 0x5F)
    return p


def add_para(doc, text, size=11, bold=False, space_after=8):
    p = doc.add_paragraph()
    run = p.add_run(text)
    set_run_font(run, size=size, bold=bold)
    p.paragraph_format.space_after = Pt(space_after)
    p.paragraph_format.space_before = Pt(0)
    return p


def add_code(doc, text):
    p = doc.add_paragraph()
    run = p.add_run(text)
    set_run_font(run, name="Consolas", size=9)
    p.paragraph_format.space_after = Pt(10)
    p.paragraph_format.left_indent = Inches(0.15)
    shading = OxmlElement("w:shd")
    shading.set(qn("w:fill"), "F4F6F8")
    shading.set(qn("w:val"), "clear")
    p.paragraph_format.element.get_or_add_pPr().append(shading)
    return p


def shade_header_row(row):
    for cell in row.cells:
        tc = cell._tePr if hasattr(cell, "_tePr") else cell._tc
        tcPr = tc.get_or_add_tcPr()
        shd = OxmlElement("w:shd")
        shd.set(qn("w:fill"), "1F3A5F")
        shd.set(qn("w:val"), "clear")
        tcPr.append(shd)
        for p in cell.paragraphs:
            for run in p.runs:
                run.font.color.rgb = RGBColor(255, 255, 255)
                run.bold = True
                run.font.size = Pt(10)
                run.font.name = "Calibri"


def add_table(doc, headers, rows):
    table = doc.add_table(rows=1 + len(rows), cols=len(headers))
    table.style = "Table Grid"
    table.autofit = True
    for i, h in enumerate(headers):
        cell = table.rows[0].cells[i]
        cell.text = h
        for p in cell.paragraphs:
            for run in p.runs:
                set_run_font(run, size=10, bold=True)
    shade_header_row(table.rows[0])
    for r_i, row in enumerate(rows):
        for c_i, value in enumerate(row):
            cell = table.rows[r_i + 1].cells[c_i]
            cell.text = value
            for p in cell.paragraphs:
                for run in p.runs:
                    set_run_font(run, size=10)
    doc.add_paragraph()
    return table


def bullet(doc, text):
    p = doc.add_paragraph(text, style="List Bullet")
    for run in p.runs:
        set_run_font(run, size=11)
    return p


def numbered(doc, text):
    p = doc.add_paragraph(text, style="List Number")
    for run in p.runs:
        set_run_font(run, size=11)
    return p


def main():
    doc = Document()
    section = doc.sections[0]
    section.top_margin = Inches(0.85)
    section.bottom_margin = Inches(0.85)
    section.left_margin = Inches(1.0)
    section.right_margin = Inches(1.0)

    title = doc.add_paragraph()
    title.alignment = WD_ALIGN_PARAGRAPH.LEFT
    r = title.add_run("LMS Companion")
    set_run_font(r, size=26, bold=True, color=RGBColor(0x1F, 0x3A, 0x5F))
    title.paragraph_format.space_after = Pt(4)

    sub = doc.add_paragraph()
    r = sub.add_run("Project overview, architecture, and how to run")
    set_run_font(r, size=13, color=RGBColor(0x4A, 0x55, 0x68))
    sub.paragraph_format.space_after = Pt(4)

    meta = doc.add_paragraph()
    r = meta.add_run("Updated 2 November 2026  |  Stanford SEED Canvas pilot")
    set_run_font(r, size=10, color=RGBColor(0x71, 0x80, 0x96))
    meta.paragraph_format.space_after = Pt(16)

    add_heading(doc, "1. What this project is", 1)
    add_para(
        doc,
        "LMS Companion is a read-only bridge between a university learning management system (LMS) "
        "and an AI assistant. Canvas is the first LMS. Stanford SEED is the pilot institution.",
    )
    add_para(
        doc,
        "A learner signs in to the LMS Companion app, connects their university Canvas account, then "
        "asks Claude about their own courses. The model only sees material that learner is allowed to read. "
        "The product does not invent new LMS write APIs. Retrieval is server-side; explanations, quizzes, "
        "cases, and feedback are produced by the LLM from retrieved text.",
    )
    add_para(doc, "Current verified result", bold=True, size=12)
    bullet(doc, "Portal at http://localhost:4200 can connect Stanford SEED with a personal Canvas access token.")
    bullet(doc, "Live course data has been retrieved (for example STP South Asia '26).")
    bullet(doc, "Claude can call authenticated MCP tools against the same stored connection.")
    bullet(doc, "Stanford institutional OAuth (developer key) is still pending. Moodle and Blackboard are not implemented yet.")

    add_heading(doc, "2. How it works", 1)
    add_para(
        doc,
        "Installing a Claude plugin does not grant Stanford access. There are two separate logins.",
    )
    add_table(
        doc,
        ["Login", "Who", "Purpose"],
        [
            ["Auth0 (LMS Companion)", "App user", "Identifies who is calling the API and MCP host"],
            ["Canvas personal token (portal)", "Stanford SEED account", "Lets the server read that learner's courses"],
        ],
    )
    add_para(doc, "End-to-end flow", bold=True, size=12)
    numbered(doc, "Learner signs in to the Angular portal with Auth0 (Google for the pilot).")
    numbered(doc, "Portal calls the API. The API checks tenant membership, then stores the Canvas token encrypted.")
    numbered(doc, "Dashboard reads courses, assignments, and progress through the API.")
    numbered(doc, "Claude connects to the public HTTPS /mcp endpoint and signs in with the same Auth0 user.")
    numbered(doc, "MCP uses the same PostgreSQL database and the same stored Canvas token. It never sends the token to Claude or the browser.")
    add_para(
        doc,
        "The LLM does not have a quiz or grading tool. MCP only retrieves data. Claude writes explanations, quizzes, and feedback from that text.",
    )

    add_heading(doc, "3. MCP tools", 1)
    add_table(
        doc,
        ["Tool", "What it returns"],
        [
            ["get_connection_status", "Linked accounts, institutions, portal management URL"],
            ["get_my_courses", "Active courses for the signed-in learner"],
            ["get_upcoming_assignments", "Assignments in a date range (max 90 days)"],
            ["get_class_schedule", "Calendar events in a date range"],
            ["get_course_progress", "Progress for one owned course, if Canvas supplies it"],
            ["get_course_modules", "Module list for one course"],
            ["get_module_items", "Items in a module"],
            ["get_learning_material", "Page text or Assignment description for an accessible item"],
        ],
    )

    add_heading(doc, "4. Folder structure", 1)
    add_code(
        doc,
        "CD/\n"
        "  backend/\n"
        "    src/\n"
        "      LmsCompanion.Domain            entities and business rules\n"
        "      LmsCompanion.Application       use cases and ILmsProvider\n"
        "      LmsCompanion.Infrastructure    PostgreSQL, Canvas adapter, encryption\n"
        "      LmsCompanion.Api               HTTP API for the Angular portal\n"
        "      LmsCompanion.Mcp               authenticated MCP host for Claude\n"
        "      LmsCompanion.Mcp.Local         legacy snapshot demo (not the live path)\n"
        "    tests/                           unit and integration tests\n"
        "    tools/                           live MCP HTTPS tunnel script\n"
        "  frontend/\n"
        "    lms-companion-web                Angular portal\n"
        "  claude-plugin/\n"
        "    lms-companion                    plugin manifest, MCP URL, study skill\n"
        "  docs/                              architecture and onboarding guides\n"
        "  docker-compose.yml                 local PostgreSQL\n"
        "  LmsCompanion.slnx                  .NET solution",
    )
    add_para(
        doc,
        "Do not use LmsCompanion.Mcp.Local for live Stanford data. That host reads imported JSON snapshots, not the portal connection.",
    )

    add_heading(doc, "5. Backend architecture", 1)
    add_para(
        doc,
        "The backend is .NET 10 Clean Architecture. Business rules live once. The API and MCP hosts both call the same application services.",
    )
    add_code(
        doc,
        "Domain  <-  Application  <-  Infrastructure\n"
        "                                ^           ^\n"
        "                                |           |\n"
        "                           API host     MCP host",
    )
    add_table(
        doc,
        ["Project", "Role"],
        [
            ["Domain", "Connections, memberships, status. No HTTP."],
            ["Application", "Queries, connection management, ILmsProvider contract."],
            ["Infrastructure", "EF Core, Canvas REST, Data Protection vault, Auth0 JWT validation."],
            ["Api", "REST for Angular. Never returns Canvas secrets."],
            ["Mcp", "Streamable HTTP at /mcp. Same queries as the API."],
        ],
    )
    add_para(doc, "Identity", bold=True, size=12)
    bullet(doc, "Auth0 OIDC. Portal (SPA) uses Authorization Code + PKCE. Audience: https://lms-companion-api.")
    bullet(doc, "Claude uses OAuth against the public MCP resource URL (https://<host>/mcp).")
    bullet(doc, "Tokens need read:lms, a trusted tenant claim, and a matching AppUsers + TenantMemberships row.")
    bullet(doc, "Pilot tenant id: 30bc4d78-e13c-4583-8f07-9a7f06a301f2.")
    add_para(doc, "Canvas", bold=True, size=12)
    bullet(doc, "Stanford OAuth institution stays Enabled=false until a developer key is approved.")
    bullet(doc, "Development uses Connect my token: validate GET /api/v1/courses?per_page=1, then store encrypted.")
    bullet(doc, "Adapter maps Canvas JSON to provider-neutral DTOs: courses, assignments, schedule, modules, Page/Assignment text.")
    add_para(doc, "Data", bold=True, size=12)
    bullet(doc, "PostgreSQL database lms_companion.")
    bullet(doc, "Persists users, memberships, connections, encrypted credentials, OAuth state, audit events.")
    bullet(doc, "API and MCP must share the same database and Data Protection key ring.")

    add_heading(doc, "6. Frontend architecture", 1)
    add_para(
        doc,
        "The portal is an Angular SPA in frontend/lms-companion-web. It talks only to the API. It never receives LMS credentials after the personal-token form is submitted.",
    )
    add_table(
        doc,
        ["File / area", "Role"],
        [
            ["auth.service.ts", "Auth0 sign-in (Authorization Code + PKCE)"],
            ["auth.interceptor.ts", "Attaches the API bearer token"],
            ["auth.guard.ts", "Protects the dashboard route"],
            ["lms-api.service.ts", "Calls /api/v1 connections, courses, assignments, schedule, progress, personal token"],
            ["dashboard.page.ts", "Connect / disconnect and show live courses"],
            ["sign-in.page.ts", "Public sign-in"],
            ["auth-callback.page.ts", "Auth0 redirect"],
            ["public/app-config.json", "apiBaseUrl, Auth0 client, allowPersonalTokenTesting"],
        ],
    )
    add_para(doc, "Routes: / (sign-in), /auth/callback, /dashboard (guarded). CORS allows http://localhost:4200.")
    add_para(doc, "Main API routes the UI uses", bold=True, size=12)
    add_table(
        doc,
        ["Route", "Purpose"],
        [
            ["GET /health", "Anonymous health check"],
            ["GET /api/v1/connections", "This user's LMS connections"],
            ["GET /api/v1/connections/institutions", "Tenant-scoped institutions"],
            ["POST /api/v1/connections/canvas/personal-token", "Validate and store a personal Canvas token"],
            ["POST /api/v1/connections/canvas/start", "Start Canvas OAuth (disabled for Stanford until a key exists)"],
            ["DELETE /api/v1/connections/{id}", "Disconnect"],
            ["GET /api/v1/courses", "Active courses"],
            ["GET /api/v1/assignments/upcoming", "Assignments in a date range"],
            ["GET /api/v1/schedule", "Calendar events"],
            ["GET /api/v1/courses/{id}/progress", "Course progress"],
        ],
    )

    add_heading(doc, "7. How to run locally", 1)
    add_para(doc, "Prerequisites: .NET 10 SDK, Node.js 24 and npm, Docker Compose, a provisioned Auth0 user.")
    add_para(doc, "From the repository root:", bold=True)
    add_code(
        doc,
        "docker compose up -d postgres\n"
        "\n"
        "dotnet tool restore\n"
        "dotnet dotnet-ef database update --project .\\backend\\src\\LmsCompanion.Infrastructure --startup-project .\\backend\\src\\LmsCompanion.Api\n"
        "\n"
        "dotnet run --project .\\backend\\src\\LmsCompanion.Api\n"
        "dotnet run --project .\\backend\\src\\LmsCompanion.Mcp\n"
        "\n"
        "cd .\\frontend\\lms-companion-web\n"
        "npm.cmd start --script-shell=powershell.exe",
    )
    add_table(
        doc,
        ["Service", "URL"],
        [
            ["PostgreSQL", "localhost:5432  (database lms_companion)"],
            ["API", "http://localhost:5150   health: /health"],
            ["MCP", "http://localhost:5124/mcp   health: /health"],
            ["Portal", "http://localhost:4200"],
        ],
    )
    add_para(doc, "Connect Stanford SEED", bold=True, size=12)
    numbered(doc, "Sign in to https://seedonline.stanford.edu and open Settings.")
    numbered(doc, "Approved Integrations → New Access Token. Test GET /api/v1/courses?per_page=1 in Postman (expect HTTP 200).")
    numbered(doc, "In the portal dashboard, use Connect my token. Do not paste the token into Claude or chat.")
    numbered(doc, "Confirm the live course appears under My courses.")
    add_para(doc, "Connect Claude", bold=True, size=12)
    numbered(doc, "Expose LmsCompanion.Mcp on stable public HTTPS (tunnel or deploy). Path must be /mcp.")
    numbered(doc, "In Auth0, create an API whose Identifier equals that exact /mcp URL. Add permission read:lms.")
    numbered(doc, "Restart MCP with Authentication__Audience and Mcp__PublicResourceUri set to that URL.")
    numbered(doc, "In Claude: Customize → Connectors → Add custom connector. Authentication: Sign in now.")
    numbered(doc, "Sign in with the same Google account used in the portal.")
    add_para(
        doc,
        "Account-less Cloudflare Quick Tunnels (trycloudflare.com) often block Claude's servers with HTTP 403. A named tunnel, ngrok, or real hosting is required for a lasting public URL.",
    )

    add_heading(doc, "8. Claude plugin package", 1)
    add_para(
        doc,
        "The installable package follows the Mintlify Claude plugin layout. It points at the hosted MCP URL. It does not embed Canvas tokens.",
    )
    add_table(
        doc,
        ["File", "Role"],
        [
            ["claude-plugin/lms-companion/.claude-plugin/plugin.json", "Plugin name and metadata"],
            ["claude-plugin/lms-companion/.mcp.json", "Canvas Streamable HTTP connector URL"],
            ["claude-plugin/lms-companion/.claude-plugin/marketplace.json", "Local / Git marketplace entry"],
            ["claude-plugin/lms-companion/skills/lms-companion/SKILL.md", "When and how Claude should call the tools"],
        ],
    )
    add_para(
        doc,
        "Later LMS providers (Moodle, Blackboard) add extra mcpServers entries in the same plugin, sharing one .NET backend.",
    )

    add_heading(doc, "9. Done vs next", 1)
    add_table(
        doc,
        ["Done", "Next"],
        [
            ["Auth0 + portal + encrypted Canvas token", "Stanford Canvas OAuth developer key (multi-user)"],
            ["Live Canvas reads for the pilot learner", "Stable production HTTPS host"],
            ["Claude MCP retrieval of SEED content", "Moodle and Blackboard ILmsProvider adapters"],
            ["Mintlify-style plugin package", "Marketplace directory listing"],
        ],
    )
    add_para(
        doc,
        "Long-term product: one plugin, one backend, separate MCP entries per LMS (Canvas now; Moodle and Blackboard later). Each user still authorizes their own university account. Installing the plugin does not grant university access.",
    )

    add_heading(doc, "10. Related docs in the repo", 1)
    bullet(doc, "docs/architecture.md — layering and provider boundary")
    bullet(doc, "docs/api-contracts.md — REST and MCP contracts")
    bullet(doc, "docs/security.md — OIDC, encryption, key ring")
    bullet(doc, "docs/production-onboarding.md — institutional OAuth")
    bullet(doc, "docs/lms-companion-plugin.md — Claude plugin install")
    bullet(doc, "docs/claude-live-mcp-tunnel.md — public HTTPS MCP for Claude")
    bullet(doc, "docs/project-status-and-llm-guide.md — status and learning prompts")

    out = Path(__file__).with_name("LMS-Companion-Project-Overview.docx")
    doc.save(out)
    print(out)


if __name__ == "__main__":
    main()
