# LMS Companion MCP over a free Cloudflare tunnel

This single-user demo serves browser-exported Canvas snapshots through an MCP Streamable HTTP endpoint. Claude's connector can use a private, unguessable URL when its setup form has no custom header field. Other MCP clients can use a custom `x-api-key` or bearer `Authorization` header. The endpoint returns JSON to each POST, so it works through a Cloudflare Quick Tunnel. The Canvas snapshot is read-only and does not update automatically.

## 1. Export your Canvas courses

While signed in to Stanford SEED Canvas, open `https://seedonline.stanford.edu/api/v1/courses?per_page=100` in the same browser. Save the JSON response as `canvas-courses.json` on your computer. Keep it private: course data may be sensitive.

For upcoming events, open `https://seedonline.stanford.edu/api/v1/users/self/upcoming_events?per_page=100` and save its JSON response separately.

## 2. Build and start the MCP tunnel

From the repository root in a normal PowerShell window:

```powershell
dotnet publish .\backend\src\LmsCompanion.Mcp.Local -c Release -p:UseAppHost=false -o .\backend\src\LmsCompanion.Mcp.Local\bin\Release\net10.0\publish-remote
.\backend\tools\start-claude-web-mcp.ps1 -CoursesJson "C:\path\to\canvas-courses.json"
```

The script requires `cloudflared` at `C:\Program Files (x86)\cloudflared\cloudflared.exe`. It imports the snapshot, generates separate random keys in `.local\remote-key.txt` and `.local\url-key.txt`, starts the local MCP server and tunnel, and copies the private Claude MCP URL to your clipboard. Both key files are ignored by Git. Treat the entire private URL as a password: do not share it, paste it into chat, or put it in screenshots.

To import upcoming events while the server is running:

```powershell
dotnet .\backend\src\LmsCompanion.Mcp.Local\bin\Release\net10.0\publish-remote\LmsCompanion.Mcp.Local.dll import-upcoming 'https://seedonline.stanford.edu/' 'C:\path\to\canvas-upcoming.json'
```

## 3. Add the connector in Claude web

In Claude, open **Customize (or Plugins) → Connectors → Add custom connector**. Enter a name such as `LMS Companion` and paste the **private MCP URL** copied by the script into the MCP server URL field. Choose **No sign in** and **Streamable HTTP**, then add the connector. No request-header field is needed. Enable the connector for a chat and ask: “Use LMS Companion to list my courses.” For upcoming events, ask it to use `get_upcoming_events` after importing that snapshot.

Claude Free allows one custom remote connector. The optional Claude plugin package is described in [the plugin guide](lms-companion-plugin.md); installing plugins may require a paid Claude plan. The MCP connector itself does not depend on Claude.

## Other MCP clients

Use the base `/mcp` URL and `x-api-key: <key>` header in MCP clients that support Streamable HTTP and custom headers. Clients can also send `Authorization: Bearer <key>`. Clients limited to stdio can run the same executable with `serve-snapshot` and read the same imported snapshot. Clients without MCP support require a separate bridge.

## Limits and shutdown

The tunnel host changes when the tunnel restarts. The connector works only while this computer, the MCP server, and cloudflared are running. Course data is a snapshot, not a live Canvas API connection. Anyone who obtains the private URL can access this snapshot until the server stops or the URL key rotates; URL paths may also be retained in client or proxy logs. To stop access, stop the `dotnet` and `cloudflared` process IDs printed by the script. Rotate the keys by stopping both processes, deleting `.local\remote-key.txt` and `.local\url-key.txt`, and rerunning the script. A multi-user deployment needs account-specific authorization, durable hosting, and a managed tunnel/domain.
