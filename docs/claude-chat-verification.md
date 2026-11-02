# Claude chat verification (Stanford live MCP)

Use this after [claude-live-mcp-tunnel.md](claude-live-mcp-tunnel.md) and [lms-companion-plugin.md](lms-companion-plugin.md).

## Automated pre-checks

```powershell
dotnet test .\LmsCompanion.slnx --configuration Release
```

Confirm `Personal_token_is_validated_then_stored_only_for_the_signed_in_user` and `Connect_read_from_mcp_deny_other_user_and_disconnect` pass in `CanvasConnectionJourneyTests`.

## Manual Claude checks

1. Portal shows the live Stanford course (for example **STP South Asia '26**).
2. Claude connector uses the tunneled or production `https://<host>/mcp` URL with **Sign in now**.
3. Prompt: *Use LMS Companion `get_connection_status` and `get_my_courses`.*
   - **Pass:** connected account and the same course name as the portal.
   - **Fail:** wrong Auth0 user, MCP audience mismatch, or MCP host cannot decrypt portal credentials (Data Protection keys).
4. Prompt: *For that course, call `get_course_modules`, `get_module_items`, and `get_learning_material` for one Page item. Cite the source URL.*
   - **Pass:** title/link match Canvas for an item you can open in the browser.
   - **Fail:** token expired (reconnect in portal), locked item type, or module not published to students.

Record results using [learning-evaluation.md](learning-evaluation.md).
