# Local Canvas MCP for Claude Desktop

This is a single-user pilot path for reading your own Canvas data in Claude Desktop. It runs an MCP server on your Windows computer using stdio. It does not need the Angular dashboard, Auth0, PostgreSQL, or a Canvas administrator developer key. The existing HTTP MCP server and Canvas OAuth flow remain the multi-user path.

Canvas permits manually generated access tokens for testing your own application. Canvas requires OAuth for applications used by multiple users. Never paste a Canvas token into chat or into Claude's MCP JSON configuration. See [Canvas OAuth documentation](https://canvas.instructure.com/doc/api/file.oauth.html).

## 1. Publish the local server

In PowerShell, from the repository root:

```powershell
dotnet publish .\backend\src\LmsCompanion.Mcp.Local\LmsCompanion.Mcp.Local.csproj -c Release -p:UseAppHost=false
```

The published DLL is at `backend\src\LmsCompanion.Mcp.Local\bin\Release\net10.0\publish\LmsCompanion.Mcp.Local.dll`.

## 2. Create a Canvas token for your own account

In the Canvas site containing your courses, open **Account → Settings → Approved Integrations → New Access Token**. Give it a purpose such as `LMS Companion local test` and a short expiration if your Canvas site allows one. Copy it once and keep it private. The button may be disabled by your institution.

## 3. Save the Canvas connection locally

From the repository root, run:

```powershell
dotnet .\backend\src\LmsCompanion.Mcp.Local\bin\Release\net10.0\publish\LmsCompanion.Mcp.Local.dll setup
```

Enter the root HTTPS URL of the Canvas site you actually use, such as `https://school.instructure.com/`. Paste the token at the hidden prompt and press Enter. Setup calls `GET /api/v1/courses?per_page=1` to verify that the URL and token work before saving anything.

The token is saved using ASP.NET Core Data Protection in `%USERPROFILE%\.lms-companion`. Its key ring is protected with Windows DPAPI. This path is shared by ordinary terminals and the Windows Store edition of Claude Desktop. The protected token is bound to the Canvas URL, so changing the saved URL will not redirect the token to another site. Setup can be rerun to replace an expired token. Do not copy the protected configuration file to another machine or Windows account; its key is local to the user profile.

## 4. Connect Claude Desktop

Install and open Claude Desktop once. Open **Settings → Developer → Edit Config**. Add this server under `mcpServers` in `%APPDATA%\Claude\claude_desktop_config.json`, preserving any servers already there:

```json
{
  "mcpServers": {
    "lms-companion-canvas": {
      "command": "C:\\Program Files\\dotnet\\dotnet.exe",
      "args": [
        "D:\\Azend\\canvas R&D\\CD\\backend\\src\\LmsCompanion.Mcp.Local\\bin\\Release\\net10.0\\publish\\LmsCompanion.Mcp.Local.dll",
        "serve"
      ]
    }
  }
}
```

Fully quit and reopen Claude Desktop. In a new chat, ask: **“Use LMS Companion to list my Canvas courses.”** Claude should offer the `get_my_courses` tool. Other tools are `get_upcoming_assignments`, `get_class_schedule`, and `get_course_progress`.

The Canvas token stays in the local MCP process; Claude receives the course or assignment data returned by the tool. The local server makes read-only Canvas API calls. When you no longer need the token, revoke it in Canvas under Approved Integrations.

Claude Desktop's local MCP configuration is distinct from its remote custom connectors. A local stdio server is launched on your computer and does not need a public HTTPS URL. See [Claude's local MCP guidance](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp) and the [MCP server quickstart](https://modelcontextprotocol.io/docs/develop/build-server).

## Browser-export snapshot when a Canvas access token is unavailable

The snapshot mode lets Claude read data you export from your own signed-in Canvas browser session. It does not require a Canvas access token, Auth0, the backend API, or PostgreSQL. The data is current only when you refresh the export.

1. While signed in to Canvas, open `https://seedonline.stanford.edu/api/v1/courses?per_page=100` and save the JSON response as `canvas-courses.json` in Downloads. If the browser Save command produces HTML, copy the entire JSON array into Notepad and save it as UTF-8 JSON instead.
2. Import the file from the repository root:

   ```powershell
   dotnet .\backend\src\LmsCompanion.Mcp.Local\bin\Release\net10.0\publish-snapshot\LmsCompanion.Mcp.Local.dll import-courses https://seedonline.stanford.edu/ C:\Users\ADMIN\Downloads\canvas-courses.json
   ```

3. In Claude Desktop **Settings → Developer → Edit Config**, point the `lms-companion-canvas` MCP server at `D:\Azend\canvas R&D\CD\backend\src\LmsCompanion.Mcp.Local\bin\Release\net10.0\publish-snapshot\LmsCompanion.Mcp.Local.dll` and set its second argument to `serve-snapshot`. Keep `C:\Program Files\dotnet\dotnet.exe` as the command. Fully quit and restart Claude, then ask it to use `get_my_courses`.

For upcoming assignments and events, open `https://seedonline.stanford.edu/api/v1/users/self/upcoming_events?per_page=100`, save the JSON as `canvas-upcoming.json`, and run the same command with `import-upcoming` and that filename. Claude can then use `get_upcoming_canvas_events`. These endpoints can be paginated; each export imports only the response page you saved. Snapshot mode does not provide module progress.
