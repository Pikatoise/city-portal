# News UI Prototype

Throwaway code for issue [#7](https://github.com/Pikatoise/city-portal/issues/7). Question: which homepage and article-writing layout should become the production interface? The owner selected B's news layout, then requested a full replacement of the area before news. Three new header studies are ready; final design approval remains pending. Production routes, authentication, and APIs are unchanged.

Header review: keep the current navigation and logo font/color/orange dot. Render the configured site name using the existing public SiteMetadata service (`GET /api/system/info`), not a hardcoded product name. The prototype uses the existing development API proxy. If the API is unavailable, show a generic site-name placeholder. No account or content mutations are made.

The old introductory slogan and masthead are removed. The new headers independently switch via `top=A/B/C`; all three retain B's original news sizes, including the 420px lead cover. The prototype banner remains removed. Live weather, advertising, and future-module workflows are not implemented.

Research references: [E1's own redesign guide](https://new.e1.ru/) describes direct category access, a lead-story showcase, and a separate chronological feed. [vtomske.ru](https://vtomske.ru/) places the date and category navigation before news. These informed compact navigation and direct access to stories; the prototypes are original compositions, not copies. The Village page could not be fetched and was not used as a visual reference.

Search forms and category buttons in each new header use the existing fixture filters and scroll to latest-news results. Date follows the browser locale/time zone in this prototype; production must use the configured city time zone.

The owner corrected the selection to header A for further refinement: move city/date into the header next to the site name, stack date below city, then place a vertical separator and vertically centered temperature to their right. Temperature is an explicitly labelled demo fixture (+3 Celsius), not a weather-provider integration; live weather remains deferred. Reduce the search/topic panel's outer and internal spacing. News sizes stay unchanged.

Header A refinement: inherit the page background for both header and search; use only a bottom border under search and a magnifying-glass submit icon. Remove the button divider and rule below the category/search row. Keep keyboard focus indication. News section headings use 10px top padding and 10px bottom margin.

Featured-news review: preserve the large left card and three compact cards on its right. Only the large card is editor-selected: it switches among one to five selected stories, with arrows only for multiple stories and no automatic advance. Right-hand cards are the three latest general-news stories; the remaining general stories appear as wide rows below without repeating those three. For prototype boundary checks, append `&featured=1` through `&featured=5` to the URL; this parameter changes fixture count only and adds no curation controls to the page.

## Saved Review Progress

The owner accepted the current header A and news-block presentation for now. Arrow controls use a flat white background at 40% opacity with dark icons/counter; no gradient. Preserve these choices when continuing. This is visual prototype approval, not Stage 2 acceptance or approval of the editor/document schema.

Next review: general-feed filters below the cards. The proposed removal of duplicated search/category controls and compact placement of tag/date filters has **not** been approved; resume from that proposal without changing it in advance. Then continue the remaining homepage and article-editor review. Prototype branch: `prototype/stage-2-news-review`; keep the throwaway variants out of main until the chosen interface is implemented properly.

## Run and Compare

From `frontend`, run `npm run prototype` (`npm.cmd run prototype` in PowerShell if script execution is restricted). Open:

- Homepage: <http://127.0.0.1:4300/?screen=home&variant=B&top=A> (`top=B` or `top=C` for the other headers).
- Article editor: <http://127.0.0.1:4300/?screen=editor&variant=A>.

Use the floating bottom arrows or Left/Right keys outside form fields. On the homepage they switch `top`; in the editor they switch `variant`. Both parameters survive reloads; document and form data do not. The homepage always uses B's news layout. All stories are fictional fixtures. CSS illustrations represent media placeholders, not uploaded files.

| Variant | New homepage header (`top`)                                                          | Article editor (`variant`)                               |
| ------- | ------------------------------------------------------------------------------------ | -------------------------------------------------------- |
| A       | Compact portal: name/navigation row and category/search row.                         | Writing canvas with a settings sidebar; preview below.   |
| B       | City newspaper: centered nameplate, city/date, quiet subtitle, category/search rule. | Text, settings, and preview are separate steps.          |
| C       | City desk: name/navigation followed by a shaded city/search panel.                   | Writing and reader preview side by side; settings below. |

There are five editor-selected fixtures and 46 general-news fixtures. Three general stories appear alongside the featured card; the other 43 use numbered pages of 20 below. Filters combine category, one tag, date range, and simple substring search. PostgreSQL ranking and Russian word forms are not simulated. Story buttons show a demo notice, not a full article route.

## Editor Candidate and Document

Candidate: Tiptap core, ProseMirror, and StarterKit **3.34.0**, pinned as development dependencies. The installed package metadata declares MIT licensing. The framework-independent Editor is mounted after Angular view creation and destroyed with the component. A separate browser-only build configuration loads it; the production application excludes this source directory. A successful production SSR build verifies compilation isolation, not actual public article SSR behavior.

References checked during preparation:

- [Official vanilla integration](https://tiptap.dev/docs/editor/getting-started/install/vanilla-javascript).
- [JSON storage and rendering](https://tiptap.dev/docs/guides/output-json-html).
- [Link configuration](https://tiptap.dev/docs/editor/extensions/marks/link).
- [Image extension scope](https://tiptap.dev/docs/editor/extensions/nodes/image): image display does not provide upload/storage.
- [Server versus browser rendering utilities](https://tiptap.dev/docs/editor/api/utilities/html).

Prototype envelope: `{ schemaVersion: 1, body: <ProseMirror JSON> }`. Allowed body nodes are doc, paragraph, heading (levels 2/3), text, bulletList, orderedList, listItem, blockquote, hardBreak, and the custom mediaFigure. Marks are bold, italic, and http(s) link. `mediaFigure` stores `mediaId`, contextual `alt`, and optional `caption`, not object-storage URLs. Category, tags, title, summary, SEO overrides, and sources remain separate revision fields. This is a candidate contract, not an approved production schema.

The prototype uses an independent allowlist renderer with escaped text/attributes for reader preview. Angular sanitizes the resulting HTML as well. Rich clipboard content is deliberately flattened to plain text; pasted/drop images are rejected. This conservative paste behavior needs owner review alongside layout. No arbitrary HTML is stored or rendered. A production .NET validator/renderer must enforce the complete schema, nesting/size/attribute limits, safe URLs, image authorization, and media states independently of this client.

## Review Scenarios

1. Edit text, heading, emphasis, numbered/bulleted lists, quote, and http(s) links. Insert the demo media reference with alternative text and caption; compare preview.
2. In the expandable prototype panel, take a JSON copy, change the text, and restore the copy. Inspect the JSON envelope.
3. Clear a required field. Save the incomplete draft, then attempt submission; only submission rejects it. Draft autosave waits five seconds and saves in memory.
4. Choose Approved in the prototype scenario selector. Autosave stops; explicit save validates fields and moves the version to InReview. Choose InReview to inspect locked editing. These controls simulate states, not role authorization or actual review routing.
5. Compare A/B/C on both surfaces. In B, use all three editor steps. On the homepage, use `featured=1` to verify absent arrows, then `featured=2` through `featured=5` to verify slide navigation. Verify that the three compact latest cards stay unchanged when slides change and are not repeated in the continuation. Combine search and filters, including an empty result.
6. Review at desktop and narrow mobile widths before choosing a layout.

## Evidence and Remaining Work

On 2026-11-13, prototype compilation, production SSR compilation, and formatting checks passed. An ephemeral Node/JSDOM check using the real Editor verified JSON round-trip including media ID/alt/caption, escaped script-like text, and javascript/data URL rejection. The dev server returned HTTP 200. `npm install` reported zero known vulnerabilities at that time.

Native Brave access subsequently became available. On 2026-11-14 local time, all three new headers were inspected at a 1040px desktop window width, their switcher changed the URL, and a header search for a fixture title fragment reduced the latest feed from 46 to eight results and scrolled to it. Mobile visual checks for the replacement headers remain pending. There is no server validator, real upload, durable save, review API, role enforcement, conflict handling, or completed article SSR in this prototype. Cover/gallery/source-list controls are incomplete; the image block and single-source fields demonstrate the candidate integration only. Issue #7 remains in progress pending these checks and owner layout/editor review.

After review, record the selected layout/editor/schema and limitations in Stage 2 and issue #7. Preserve the full prototype on a throwaway branch with an issue pointer; do not merge all variants into main. Production forms must be implemented under normal validation and testing requirements.
