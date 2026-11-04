# Canvas Learning Path retrieval

Some Canvas courses organize learning through linked Pages rather than native Modules. In the supplied course 322 exports, the modules response is `[]`, the page-list response reports that the page has been disabled, and the saved Organizational Strategy HTML contains a readable page body with links and embeds. These findings do not establish whether the project's connected Canvas token can read individual pages; that still requires a live check.

Canvas's page-list API checks whether the Pages tab is enabled; its individual-page API separately checks permission to read the page. A disabled listing therefore does not prove an individual page is inaccessible. See the [official controller](https://github.com/instructure/canvas-lms/blob/master/app/controllers/wiki_pages_api_controller.rb), methods `index` and `show`.

## Using Claude with the live MCP host

1. Build and restart `LmsCompanion.Mcp`. Use the authenticated live host, not `LmsCompanion.Mcp.Local` (the older snapshot demo).
2. Refresh the Claude connector's tools or reconnect so `get_course_page` is discovered. Update/reload the local Claude plugin if installed to pick up its revised study instructions.
3. Call `get_connection_status` and `get_my_courses`. Confirm the correct connection includes course `322`; use that connection ID for subsequent calls if multiple connections exist.
4. Read each supplied source through `get_course_page`:

   ```json
   { "courseId": "322", "pageSlug": "part-1-%7C-learn" }
   ```

   ```json
   { "courseId": "322", "pageSlug": "part-1-apply" }
   ```

   ```json
   { "courseId": "322", "pageSlug": "organizational-strategy" }
   ```

   The `%7C` URLs were reported unavailable in the first live check. The adapter now retries an alternate interpretation of a percent-escaped slug only when Canvas returns Not Found. Verify the result against the visible Canvas page; an authorization error is never retried.

5. Follow relevant `Resources` entries with a non-null `PageSlug` by calling the same tool. Track visited slugs; navigation includes back/next links and may loop. Do not infer learning sequence from the order of external resource links.
6. Ground answers in returned `Content` and cite `Url`. `Resources` inventories linked sources. When an answer depends on a link, call `get_linked_resource` with the exact resource `Url` plus the source `courseId` and `pageSlug`. If `NextOffset` is present, repeat with `startOffset=NextOffset` to read later text. The resource tool can extract text from accessible PDFs, Google Docs, Slides exports, public Google Forms, and captions from public YouTube videos. If the tool reports `accessRequired`, the linked file needs a separate Google account connection. If it reports `transcriptRequired`, captions were missing or inaccessible and a transcript from another source is needed. Forms responses, images, and embedded tools are not included. Never infer those contents from the link title.

For automatic YouTube captions, install Python 3 and run `python -m pip install -r backend/tools/requirements-transcripts.txt` on the machine running the live MCP host, then restart it. The server runs a bounded helper for a single video ID taken from a linked Canvas page. It does not send the Canvas token, browser cookies, or credentials to YouTube. The helper returns caption text only, not audio. English manual captions are preferred, then English automatic captions. The tested Section 3 podcast returned 6,723 characters of caption text. YouTube can change its public caption behavior; when retrieval fails, the tool returns `transcriptRequired` instead of guessed content. Playlist links need an individual video URL.

The Business Model Canvas Section 1 deck uses a published Google Slides `/presentation/d/e/.../pubembed` URL. An unauthenticated browser was redirected to Google sign-in. The reader now recognizes this URL and returns `accessRequired` for that redirect rather than `unsupported`. A published embed ID is different from the original presentation ID and cannot be passed to the normal Google Slides export URL. To retrieve actual slide text through the existing reader, the Canvas page must link the original `/presentation/d/{id}/...` URL with download access. If that link requires Seed Google sign-in, the service also needs a separate authorized Google connection; Canvas credentials do not grant Google access. A public published viewer without text export returns `unavailable` rather than viewer controls as slide content.

An alternative for Claude is its Google Drive connector, connected once to the learner's Seed Google account. Claude can search Drive for the South Asia regional deck, check title and course context, and read the matching Google Slides file separately from the Canvas page. This does not require changing the Canvas page or extracting browser cookies. It works only if the connected Google account can access the source deck and the organization's administrator allows the connector. A published embed URL alone is insufficient to identify the original Drive file, so Claude must verify any search result rather than matching a title blindly. The connector extracts text; slide images are not covered.

## Interpreting failures

- Empty modules: continue using a known Learning Path page URL. Do not invent module IDs.
- Disabled page listing: use a known individual-page slug. Do not attempt to enumerate hidden pages.
- Authorization error: browser login and the server's Canvas token are separate. Reconnect through the portal and verify the token belongs to an account with access to the course.
- Missing/locked/unpublished page: report the limitation; the tool does not fall back to saved HTML or bypass page permissions.
- Tool not found: verify the running MCP host/version and refresh Claude's connector. The snapshot host has no direct-page tool.

The supplied `.json` file containing Organizational Strategy is actually a saved HTML page. It was inspected only for diagnosis. Course content, browser session metadata, and original exports are not checked into the repository or used as a live fallback.

## Verification

Synthetic tests cover direct retrieval with no modules, link/embed extraction, encoded slugs, lock and error handling, output limits, MCP discovery and invocation, foreign-course/foreign-owner denial, and reads after disconnect. A successful synthetic test is not a successful live Stanford fetch. Complete the three calls above using the authorized portal connection to verify live access.
