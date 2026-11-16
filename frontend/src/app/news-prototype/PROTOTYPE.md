# News UI Prototype

Throwaway code for issue [#7](https://github.com/Pikatoise/city-portal/issues/7). Question: which homepage and article-writing layout should become the production interface? The owner selected B's news layout, then requested a full replacement of the area before news. Only selected header A remains; the other headers and variant switcher were removed at the owner's request. Production routes, authentication, and APIs are unchanged.

Header review: keep the current navigation and logo font/color/orange dot. Render the configured site name using the existing public SiteMetadata service (`GET /api/system/info`), not a hardcoded product name. The prototype uses the existing development API proxy. If the API is unavailable, show a generic site-name placeholder. No account or content mutations are made.

The old introductory slogan, prototype banner, and alternative headers are removed. The approved news composition is retained. Live weather, advertising, and future-module workflows are not implemented.

Research references: [E1's own redesign guide](https://new.e1.ru/) describes direct category access, a lead-story showcase, and a separate chronological feed. [vtomske.ru](https://vtomske.ru/) places the date and category navigation before news. These informed compact navigation and direct access to stories; the prototypes are original compositions, not copies. The Village page could not be fetched and was not used as a visual reference.

Search forms and category buttons in each new header open the detailed list for search; category buttons scroll to their matching homepage blocks. Date follows the browser locale/time zone in this prototype; production must use the configured city time zone.

The owner corrected the selection to header A for further refinement: move city/date into the header next to the site name, stack date below city, then place a vertical separator and vertically centered temperature to their right. Temperature is an explicitly labelled demo fixture (+3 Celsius), not a weather-provider integration; live weather remains deferred. Reduce the search/topic panel's outer and internal spacing. News sizes stay unchanged.

Header A refinement: inherit the page background for both header and search; use only a bottom border under search and a magnifying-glass submit icon. Remove the button divider and rule below the category/search row. Keep keyboard focus indication. News section headings use 10px top padding and 10px bottom margin.

Featured-news review: preserve the large left card and three compact cards on its right. Only the large card is editor-selected: it switches among one to five selected stories, with arrows only for multiple stories and no automatic advance. Right-hand cards are the three latest general-news stories; the next two general stories appear below as wide rows, completing the five-item latest block. Five-item Transport and Culture blocks follow. Stories may repeat across blocks. Each block links to the detailed news list, preselecting its category where applicable. For prototype boundary checks, append `&featured=1` through `&featured=5` to the URL; this parameter changes fixture count only and adds no curation controls to the page.

## Saved Review Progress

A UI consistency pass now shares the application token/control foundation; see [UI_STYLE.md](../../../../docs/UI_STYLE.md) for the selected style, findings, scope, and verification. Editorial panes and reader text use the same system font, with Georgia retained only for the lead-story headline. The owner-reviewed public layout, gestures, and spacing remain intact.

Accepted article refinements: natural headline wrapping; 7vw additional content insets including headline/breadcrumbs and related news, excluding site header/footer; centered media with captions aligned to the figure; compact author/date footer after sources; 24px gap before related news. The owner accepted this page after these refinements.

Article images preserve their intrinsic aspect ratio and are not upscaled to fill the page: image elements use auto dimensions bounded by content width and 70svh, centered without cropping. CSS placeholder covers/inline illustrations demonstrate narrower centered media (1200px/960px maximum), while text remains full-width. Actual image uploads are still outside this prototype.

Article images use shallower proportions after the body-width refinement: cover 3:1, inline illustration 5:2, both 2:1 up to 800px. Related-story thumbnails retain 16:9.

The article body follows the cover's full content width. Its former 72ch centered-column cap was removed at the owner's request to avoid excessive side gaps on Full HD displays. Sources, dates, author, and inline media follow the same body edges.

Page navigation uses a 180ms opacity-only entrance on the main content, leaving the header stationary. It runs on screen/article changes and list navigation, not carousel movement or homepage category scrolling. Interrupted entrance animations are cancelled before restarting. The treatment is included in the accepted public-page prototype.

Article review refinements: author credit and publication/update dates share one footer row after sources, without bottom padding. The tag moves from the bottom into the breadcrumb row: Home / Category / Tag. Category/tag controls retain their filtered-list actions; the breadcrumb wraps on narrow screens.

The owner accepted the homepage and article-page visual prototypes. This does not close Stage 2 or approve the editor/document schema. The owner delegated editor refinement and stage continuation; final stage acceptance remains outstanding. Review the current author/editor workspace at `?screen=editor`. The accepted article page remains available from news cards or at `?screen=article&id=1`.

The reader candidate shows the selected fixture title/summary, author, publication/update metadata, cover, readable body with headings/quote/inline illustration, source link, tag, and up to five related fixtures (shared tags first, then same category, excluding the current ID). Category/tag links open the detailed list with that filter. Related cards open their story. Bodies, dates, and sources are fictional demonstration content; there are no real article APIs, SSR, media uploads, or SEO/withdrawal behavior here. The homepage design is preserved. Review page structure first, then refine each block with the owner.

Image buttons have transparent normal/hover backgrounds. Compact image areas fill their allocated cells without white bands; actual image elements use `object-fit: cover` for proportional cropping rather than letterboxing. Prototype artwork remains CSS placeholders.

On desktop widths above 800px, the editorial carousel and three compact stories share a height bounded by the available viewport after the measured header, category bar, and section headings (up to 620px). ResizeObserver and window resize recalculate that space. Three equal grid rows, flexible images, and container-height typography/spacing keep the showcase inside that area. Shorter windows hide secondary summaries/bylines before primary headlines. Mobile keeps the vertical arrangement and natural heights. Prototype compilation passes; live multi-resolution review remains pending.

The featured carousel has a stationary outer card, clipped viewport, and internal three-slide track. Mouse dragging/touch swiping moves only the stories; adjacent stories enter the viewport, with a 380ms slide/snap animation and gentle deceleration. Arrows stay fixed and move the same track. Left advances and right goes back. A drag threshold prevents accidental slide changes, clicks after dragging are suppressed, and vertical touch scrolling remains available. Prototype compilation passes; live gesture review remains pending.

The detailed list has 32px of space above its search/filter controls, uses the heading Fresh News, and omits the result-count label. Homepage block labels stay unchanged.

The category/search bar also includes an All News button that opens the complete detailed list with search, filters, and pagination, without a preselected category or query. Latest and category buttons retain homepage block scrolling. Hide the category/search bar on the detailed list; retain the site header and the list's own search/filter controls.

The category/search row sticks to the viewport top while scrolling; the upper brand/navigation header scrolls away. The row is outside the header box and the component host uses `display: contents` so the sticky boundary follows the page. Its opaque page-colored background prevents news showing through.

Public prototype pages use fluid width without the former 1440px outer and 1280px feed caps. Header/category rows retain 6vw desktop side padding; news uses 8vw. Mobile retains 5vw. Editorial form/reader text limits remain scoped to their existing content. This prevents additional centered-container margins on wider displays.

The owner requested slightly wider news content and shorter cards: desktop side padding is 8vw and the editorial cover minimum height is 380px. Images take about 44% of wide rows and compact cards, with responsive proportions and reduced row padding. Wide images use 16:9; compact images use 3:2. Mobile side padding remains 5vw.

The news content area, including the editorial carousel and category blocks, uses wider side padding: 8vw on desktop and 5vw at widths up to 800px. Header and article-editor spacing retain their existing values.

Header A's category/search row spans the same width as the upper brand/navigation row (6vw side padding, 5vw up to 700px). News content retains its narrower width.

The news main container has no top padding: category/search row padding provides the gap before the first section rule, avoiding an extra 8px desktop/12px mobile below the row.

Wide news rows use the owner's Minimal Editorial News List direction: date/time above the headline, no separate date column, two-line headline/summary, and a prominent right-side decorative thumbnail (about 44% of the row on desktop, 42% on tablet, 40% on mobile). Category labels are omitted inside category blocks and category-filtered lists. Desktop rows use 16px vertical padding and 24px image gap; tablet rows use 16px and mobile rows use 14px padding and hide the summary. Existing fonts/palette are retained. The headline button has a stretched click target covering the row, visible keyboard focus, subtle hover treatment, and reduced-motion support. Global navigation, editorial/compact cards, story counts, filters, and pagination are unchanged. Prototype compilation passes; owner visual review remains pending.

The owner accepted the current header A and news-block presentation for now. Arrow controls use a flat white background at 40% opacity with dark icons/counter; no gradient. Preserve these choices when continuing. This is visual prototype approval, not Stage 2 acceptance or approval of the editor/document schema.

The owner superseded the inline-filter proposal with five-item homepage blocks and a separate detailed list containing search, category, tag, date range, and numbered pages. Header search opens that list. The sticky category bar starts with Latest and scrolls to matching homepage blocks, accounting for the pinned row height. Homepage blocks have no inline filters or pagination. Transport and Culture are examples; production category selection/order remain open. Continue agreed stage implementation; owner review is reserved for the final result. Prototype branch: `prototype/stage-2-news-review`; keep throwaway variants out of main.

## Run and Review

From `frontend`, run `npm.cmd run prototype` in PowerShell. Open:

- Homepage: <http://127.0.0.1:4300/?screen=home>.
- Detailed list: <http://127.0.0.1:4300/?screen=list>.
- Article reader: <http://127.0.0.1:4300/?screen=article&id=1>.
- Article editor: <http://127.0.0.1:4300/?screen=editor>.

Header A is the only homepage design. The article editor now contains a unified author/editor workspace with five document sections and a contextual workflow panel. There is no variant switcher or Left/Right keyboard handler. Legacy `top` and `variant` query parameters are ignored. Data lives in memory until reload. All stories are fictional fixtures; CSS illustrations represent media placeholders.

There are five editor-selected fixtures and 46 general-news fixtures. Three general stories appear alongside the featured card and two below. Transport and Culture each show five stories. The detailed list includes all 46 fixtures with numbered pages of 20. Filters combine category, one tag, date range, and simple substring search. PostgreSQL ranking and Russian word forms are not simulated. Story buttons open the reader prototype using the fixture ID in the URL.

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

## Current Workspace and Review Scenarios

`editorial-workspace.ts/html/css` owns the authoring prototype independently of public news. This replaces the former three-step form and root Tiptap instance. Design rationale and primary research references are in [UI_STYLE.md](../../../../docs/UI_STYLE.md#authoring-and-editorial-workspace). The owner delegated continuation instead of intermediate block-by-block approval; final Stage 2 acceptance remains required.

1. Write in Text with paragraphs, H2/H3, emphasis, lists, quotes, undo/redo, and selected-text links. Inline images expose contextual alternative-text/caption controls when selected.
2. Use Images for optional cover/gallery, and choose own fixture images or an approved shared template. Alternative text is required at submission; incomplete drafts may omit it. There is no upload control pretending to persist files.
3. Use Parameters for category, tags, repeatable named URL sources, the attribution flag, and optional SEO fields. Free tag entry is a fixture shortcut; production authors select existing taxonomy.
4. Preview at desktop or phone width. Nothing is published by switching views. History opens retained submitted text and metadata, not advanced diffs or arbitrary restoration.
5. Open My Articles for draft, review, approved, published, and returned fixtures. Save incomplete content; submit validates required fields. Approved edits use explicit save and immediately re-enter review. Returned submissions require a change note.
6. Expand Demonstration Modes at the bottom and select Editor. Open the queue, claim a review, return with feedback or approve, then publish separately. Transfer, release, final rejection, takeover, withdrawal, and explicit published corrections have dedicated confirmations. Author and editor personas are fixtures, not API authorization.
7. Switch back to Author to correct returned content. Private unsent changes stay outside the editor's submitted snapshot, including list headlines. A published correction buffer is separate from the author's one working version; direct corrections retain superseded text in history. This does not yet implement durable revisions.
8. Use homepage `featured=1` through `featured=5` to check carousel boundaries; verify three compact plus two wide latest items, five per category, combined detailed-list filters, and pagination. Public visual decisions remain unchanged.

## Verification and Remaining Work

On 2026-11-14 both the production SSR and prototype builds passed. Browser checks covered author submission locking, editor claim, return with feedback, mandatory resubmission notes, approval, separate publication, published correction, withdrawal/read-only behavior, and isolation of an unsent returned headline from editor views. At 390px all five sections and the image dialog had no horizontal page overflow; desktop review used 1440px. Native dialogs were inspected in the real browser. The only observed browser resource failure was the unavailable existing branding API; it uses the established placeholder.

Earlier 2026-11-13 checks verified prototype JSON/media round-trip and safe rendering. The isolated prototype still uses fixtures for review/publication and public pages. Production Media/template notifications now exist separately, and `/account/articles` implements durable author drafts, real Media, server validation, role enforcement, five-second autosave and HTTP 409 recovery. See [MEDIA.md](../../../../docs/MEDIA.md) and [NEWS.md](../../../../docs/NEWS.md). Production review/first publication is implemented at /staff/reviews with real submitted snapshots, assignments, decisions, history, and notifications. Published corrections and public delivery remain next. Issue #7 remains in progress and no final stage review has happened. Drafting/CSS checkpoint b38ed07 is pushed; review/first-publication implementation is local on prototype/stage-2-news-review.
