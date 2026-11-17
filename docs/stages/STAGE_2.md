# Stage 2: News and Media

## Status and Sources

Product decisions are agreed. Stage 1 is complete and owner-accepted. Public-page prototypes are owner-reviewed, and the author/editor prototype follows delegated refinement. Production Media, configurable template notifications, and author drafts/taxonomy are implemented and locally verified. Review and first publication are implemented and pushed in 0708b94. Issue #12 implements published changes, withdrawal/republication, and basic public article SSR with current/historical URL visibility. Issue #13 delivers public SSR discovery, search, related/featured news and the production homepage; final verification and owner acceptance remain #14. Engineering defaults complete the implementation specification; final stage acceptance remains with the owner.

[VALUE.md](../VALUE.md) defines product behavior, [CORE.md](../CORE.md) defines architecture, and [STAGE_1.md](STAGE_1.md) records the delivered account and Email contracts. This specification distinguishes owner-approved product behavior from engineering defaults selected under delegated technical decisions. Changes to business behavior still require owner agreement.

## Goal and Scope

Deliver the complete editorial workflow, safe published revisions, image uploads, and public SSR news pages with basic search. Authors and editors must be able to complete the workflow through Angular and the API.

Included:

- Article drafts, submission, review assignment/takeover, return, final rejection, approval, and publication.
- Author-proposed published revisions, direct editor corrections, withdrawal, republication, and retained history.
- Categories and tags, including archival.
- Media ownership, validation, private drafts, publication access, safe cleanup, and moderated shared image templates.
- Public feed, article pages, categories/tags, search, filters, pagination, related articles, and featured news.
- Relevant author/editor workspaces, review notifications, channel preferences, authorization, diagnostics, and deployment checks.

Excluded: citizen suggestions, classifieds, comments, Telegram/weather/RSS, scheduled publication, advanced search, advanced revision comparison, and non-image uploads. Homepage composition and article SEO follow the contracts below; general portal-settings management is outside this stage. Future-module content is not implemented here.

## Tools and Module Ownership

Retain the stack and security baseline in CORE.md. Add one News project and one Media project, each owning its PostgreSQL schema, internal EF Core DbContext, and migrations. Extract contracts only where dependency direction requires them.

| Owner | Responsibility |
| --- | --- |
| News | Articles, published and working revisions, review decisions, assignments, taxonomy, publication/withdrawal, and editorial history. |
| Media | Image metadata, ownership, validation/processing, object keys, usage references, shared-template submission/moderation/history, and storage/cleanup operations. |
| Identity | Existing accounts, current effective roles, confirmed-Email enforcement, and sessions. |
| Notifications | Workflow delivery, preferences, attempts, retries, and account notification presentation. Business decisions remain in the owning News or Media module. |

Use S3-compatible object storage for images and PostgreSQL Full-Text Search for basic news search. Select the editor through a small prototype before building the article form. Select a maintained S3-compatible local service and processing library during the Media infrastructure task, checking compatibility, licensing, and security before pinning versions. This is an engineering task. Do not initially cache public article/feed/image responses; database visibility checks preserve immediate withdrawal and blocking. Redis remains selected infrastructure for later justified caching.

## Implementation Order

| Step | Result required before proceeding |
| --- | --- |
| 1. Specify workflows | Resolve permissions, state transitions, field/media limits, public behavior, notifications, and acceptance examples; create aligned GitHub Issues/Project tasks. |
| 2. Prototype the editor | Demonstrate required blocks, image references, document round-trip, and server validation/safe rendering; agree the editor and stored document format. |
| 3. Deliver Media | Explicit migrations, S3-compatible local infrastructure, processed private uploads, moderated shared-template library and staff review UI, ownership/usage contracts, and verified access/cleanup rules. |
| 4. Deliver drafting and taxonomy | Author workspace, category/tag management, structured content, validation, and version-conflict handling. |
| 5. Deliver review and first publication | Queue, assignment/takeover, return/resubmit, final rejection, approval, publication, history, and relevant notifications. |
| 6. Deliver published changes | Independent working/published revisions, direct corrections, stale-proposal handling, withdrawal/republication, and immediate public visibility checks. |
| 7. Deliver public discovery | SSR feed/article pages, search/filter/pagination, URLs/metadata, related/featured news, and agreed homepage composition. |
| 8. Verify and review | Integration/browser/security/concurrency/outage checks, migrations, database/media backup and restore, documentation, and owner acceptance. |

Each workflow receives its complete API/UI and verification before work moves to the next. Application dependencies may require adjusting implementation order without changing approved scope.

## Approved Workflows and Permissions

All staff operations require confirmed Email and current API-enforced permissions. Ownership and content state apply in addition to roles. Angular guards do not replace authorization. Administrators automatically have all Editor permissions without a separately assigned Editor role; this does not bypass private-draft ownership. Assigned roles and effective permissions remain distinct.

| Operation | Approved rule |
| --- | --- |
| Read published news | Public, without login. Drafts and private staff data never enter public SSR, search, or shared caches. |
| Create and edit articles | Authors prepare their own articles with server-side autosave and an explicit Save button. Incomplete drafts are allowed. Before submission, only the author can view a draft; editors and administrators cannot view another author's private draft. |
| Delete an unpublished article | The author may delete their own article at any stage before first publication, including during review, after return, or after final rejection. An ever-published article cannot be deleted through this operation. Remove the article from the author workspace and editorial queues but retain all version text and action/review history for editorial access. For submitted articles, the assigned editor and administrators can read retained versions/history after deletion. Never-submitted drafts remain closed to editors and administrators, including after deletion. |
| Access after Author removal | Close the author workspace and its API operations, including own unpublished articles/history, unless another current role grants the relevant permission. Ownership alone preserves no author access; public reading remains available. |
| Manage categories/tags | Editors and administrators create, rename, and archive; authors select existing entries. Category names and tag names are unique separately, ignoring case and outer whitespace, including archived entries. Archived entries cannot receive new associations; existing associations remain valid through submission and publication of new versions. |
| Submit for review | The author submits publication-ready content. Author edits are blocked during review; the author may withdraw the submission for corrections. Subsequent resubmission goes to the previous reviewer if effective editorial permissions remain, otherwise to the shared queue. After a reviewer returns content for corrections, current unsent edits are author-only until resubmission; the previous submitted version/history retain their agreed audience. |
| Review | Submitted articles enter a shared queue. An editor takes an article and becomes its sole assigned reviewer. The assigned editor may release it to the shared queue; another editor may explicitly take over. History records releases and transfers. Loss of all effective editorial permissions automatically returns assigned reviews to the shared queue with history; retaining Administrator preserves editorial permissions and assignments. |
| Return or finally reject | Return requires feedback; resubmission requires a change description and goes to the previous reviewer if effective editorial permissions remain, otherwise to the shared queue. Final rejection requires a reason and prevents resubmitting that article. |
| Approve | Editors approve. An author/editor may approve their own article. Editors do not rewrite another author's article before first publication. |
| Publish | The owning author or any editor may publish an approved version. Approval and publication are separate audited actions. For approved but unpublished content, disable autosave. An explicit Save validates submission fields, invalidates approval, and immediately sends the changed version to the previous eligible reviewer or shared queue, without a separate submission action. |
| Propose published changes | The author submits a replacement through review; the current published revision stays public until replacement publication. |
| Correct published text | Editors may directly change title, summary, body, images, sources, category, and tags without separate approval; original author credit remains. Close any author working version as superseded, whether draft, in review, or approved but unpublished. Retain its text and reason and notify the author. A version in review is rejected; no superseded version can be published. A new proposal starts from current published content. |
| Withdraw and republish | Editors may immediately withdraw seriously incorrect content with a reason. Only the owning author prepares corrections while withdrawn; editors review them. Editor/Administrator alone does not grant the author correction operation. Existing working content becomes an author-editable draft with review/approval reset. Submitted corrections go to the previous eligible reviewer or shared queue. Corrections require approval before republication. |
| Read history | After first submission, the author, assigned editor, and administrators can access history under current effective permissions. History is not public and an Editor role alone grants no access to every article history. The assigned editor retains access after review completion while holding effective editorial permissions. Takeover or release to the queue revokes the previous editor's access; administrators retain access. Deleted-archive access follows the deletion rule above. |
| Manage featured news | Editors and administrators select up to five featured articles for the large card. Sort by first publication, newest first, and immediately exclude withdrawn articles. Show one featured slide at a time, with arrows only when multiple selections exist. |

## Content and State Contracts

Submission requires title, summary, structured body, and one category. Author identity comes from the account. Cover, gallery, and tags are optional; sources are required when using others' material. Incomplete drafts may omit required submission fields; enforce their presence when submitting for review. Structural, security, and maximum limits still apply to supplied draft content.

| Content | Approved limit |
| --- | --- |
| Title | 1-200 characters at submission. |
| Summary | 1-500 characters at submission. |
| Body | 1-100,000 plain-text characters, excluding markup, at submission. |
| Tags | At most ten per article version. |
| Image formats | JPEG, PNG, and static WebP; reject SVG and animation. |
| Source image size | At most 10 MiB (10,485,760 bytes) and 40,000,000 pixels per file. |
| Referenced images | At most 20 distinct Media images per version across cover, gallery, and body. Repeated use of one image counts once. |

Sources are structured entries with a name and URL. A separate author-supplied flag records use of others' materials; when true, submission requires at least one source. Editors check attribution accuracy. Category selection uses one flat list without subcategories and exactly one category at submission.

Every image placement in cover, gallery, or body requires nonempty alternative text at submission; captions are optional. Store this text with the article placement, not as one mandatory shared description for the Media file, so reuse can describe different contexts. Incomplete drafts may omit the text until submission.

Autosave and the Save button persist drafts on the server using the expected record version. A conflicting save returns HTTP 409 and preserves the committed version, including when multiple tabs edit the same draft. Approved unpublished versions use only explicit Save: validate submission fields and atomically save, invalidate approval, and return to review. Invalid input does not commit that transition. Draft autosave runs after five seconds without input and serializes saves from one form. On 409, stop autosave and retain unsaved edits in the open form; offer copying those edits before loading the current server version. Do not silently merge or overwrite. A failed network save keeps the form dirty and shows retry feedback; do not report unsaved content as saved.

The editor supports paragraphs, subheadings, emphasis, lists, links, quotations, and captioned images. Arbitrary HTML and complex page layouts are excluded. Images reference Media IDs. Validate structure, URLs, and referenced-media access on the server and sanitize rendered content.

The approved main path is `Draft -> In Review -> Approved -> Published`, with return/resubmission, final rejection, author withdrawal, published corrections, and article withdrawal described above. Use independent publication and working-version state as specified below. Do not conflate final rejection of an article with rejection of a superseded revision that permits a new proposal.

Allow at most one unfinished working version per article, separate from the current published revision. Keep public revision identity separate from working content. Public reads return only the currently published revision. History records actors and timestamps; readers see publication and latest published-update times. UTC storage and configured-city display follow CORE.md.

A published title change updates the public URL when that change becomes public. Preserve previous URL mappings and redirect to the current URL; pending author changes cannot change public addresses. All aliases follow the same current-visibility checks as the current article URL. Use `/news/{articleId}/{slug}`; the stable opaque article ID disambiguates equal titles. Generate a normalized, URL-encoded title slug server-side. Keep previous real slugs as aliases; arbitrary unknown slugs are not aliases. Public aliases redirect directly to the current canonical URL without chains.

## Public News and Discovery

The Stage 2 homepage presents the editorial carousel, five latest news items, then two category blocks of five stories each, newest first. The large left card contains one to five editor-selected stories, displayed one at a time with arrows only for multiple stories. Immediately exclude withdrawn articles. Three compact cards on the right start the latest-news block; the next two stories continue below as wide rows. Category blocks use wide rows. Repeating stories across blocks is allowed. Every block ends with a link to the detailed news list; category links preselect their category. Header search opens that list. The sticky category bar starts with Latest and scrolls to the matching homepage block instead of opening the detailed list; its categories match displayed blocks. Homepage blocks have no inline filters or pagination. Transport and Culture are prototype examples. Editors and administrators choose the two production homepage categories and their order. Future-module blocks arrive with their owning workflows.

Without a query, order news by first publication time descending; corrections do not promote older articles. Search title, summary, and body using Russian word-form support and relevance ordering. Search and all discovery queries use only current published revisions. Use article ID as the final tie-breaker; order equal search ranks by first publication time descending, then ID.

Related news contains at most five other currently published articles: first those sharing tags, then additional articles in the same category; order each group newest first. Exclude the current article and duplicates. Withdrawal applies to these results immediately.

Filters include one category, one tag, and a first-publication date range, combinable with search. All filters are available in the detailed news list; links from category blocks preselect the category. News lists use 20 articles per numbered page. Interpret inclusive date filters in the configured city time zone, converting the start and the exclusive start of the following day to UTC. Pagination defaults to 20 and caps API page size at 100; public UI uses 20.

Current and historical URLs of a withdrawn article show "Материал снят с публикации" without article content or the withdrawal reason; evaluate withdrawn state before redirecting an alias. Never-published articles remain unavailable to anonymous readers. Return a non-cacheable 404 with the special withdrawn-page shell for withdrawn articles and a generic 404 for unknown/never-published articles. Do not include title, summary, author, withdrawal reason, or article-specific preview metadata in the withdrawn shell. Published aliases use a redirect whose response requires revalidation; evaluate current visibility on every request.

Public author credit uses the account's current display name in all articles, rather than a first-publication name snapshot. Preserve the original account ID when editorial changes are made. Name changes affect display only, not the article's publication/update times.

Articles have optional SEO title and description, falling back to the article title and summary respectively; use the cover for link-preview imagery when present. Store these fields with the revision and apply ordinary author/reviewer permissions and publication boundaries. Direct editorial corrections may include SEO fields. Unpublished SEO values must not leak through public SSR or metadata. SEO limits and canonical contracts are specified below. Public author data includes display name only, without Email or private account fields. A changed display name must be reflected on subsequent reads, without relying on an old cookie or revision snapshot.

## Image Processing and Shared Templates

Correct orientation and strip EXIF and other metadata; generate smaller page-delivery variants. Keep only the processed image and its variants, without retaining the original upload. Re-encode a full-resolution processed master and page variants with maximum long edges of 480, 1280, and 2560 pixels, without upscaling. Preserve transparency. Use metadata-free static WebP outputs, with encoding quality 85 for lossy outputs; validate the decoder/encoder and representative images in the Media task. Temporary input exists only during processing and is deleted on success or failure.

Authors may select their own uploads and approved shared templates. Any author may propose an image as a template. Review by a confirmed Moderator or Administrator is required before other authors can select it. Administrator template-review access does not grant all other Moderator permissions. Rejection requires a reason; the author may correct and resubmit. Existing published images are not automatically reusable templates. Moderation access to a proposed image does not expose unrelated private articles or uploads.

Approved template files are immutable; proposing a different image creates a separate submission requiring review. Moderators and administrators may archive templates: disallow new selections, retain existing article/version references. The author may request removal through a button in the template card; it enters a moderator/administrator queue without directly archiving the template. Staff must archive on this request; refusal is not an available outcome. Resolve the request with actor/time and notify the author. Duplicate pending requests return the existing request; concurrent resolution has one winner. Moderators and administrators may block an unacceptable template with a required reason, closing public delivery and replacing the image with a placeholder in articles. Archival and blocking have distinct effects; neither deletes referenced files. Moderators and administrators may undo an erroneous block with a reason and history record. Images reappear in currently published articles subject to the usual visibility checks. Unblocking does not itself change archival state. Moderators and administrators may restore an archived template with its unchanged approved file, recording the action in history; restoration does not remove a separate block.

The assigned reviewer may access images attached to their submitted article version under the review permissions. A review assignment does not grant general access to another author's private media library.

At article withdrawal, deny new anonymous access to an image unless another currently published article still uses it. Historical/draft references keep the file retained without making it public. Approved-template library/preview access is available to authors; approval alone does not permit anonymous access. A template block overrides publication references: deny public image delivery and render a placeholder even in currently published articles. Retain proposal revisions, moderation actions, and removal-request resolutions without automatic history deletion, with actor, time, state/version, and reason where required. Moderator/Administrator users can inspect this history; submitters can inspect their own proposal decisions while retaining author access. Do not expose moderation history to other authors selecting an approved template.

## API, Transactions, and Module Contracts

The following route design is an engineering contract for implementation, not a claim that these endpoints already exist. Routes are relative to `/api`; abbreviated action paths in one row share the fully written resource prefix. Retain generated OpenAPI/TypeScript clients, Problem Details, cookie/CSRF protection, and explicit DTOs. Every mutation requires CSRF and an expected version when updating an existing resource.

| Route group | Contract |
| --- | --- |
| GET `/news/homepage` | Bounded current-publication featured/latest blocks and the two ordered editorial category blocks; no automatic category selection before configuration. |
| GET/PUT `/editorial/homepage` | Effective-editor versioned category selection; writes require two distinct active categories, CSRF and atomic audit. |
| GET `/news/articles` | Current-publication summaries with query/category/tag/date filters, pagination, deterministic ordering, and optional featured filter. |
| GET `/news/articles/{id}` | Current published content, canonical URL, and optional `slug` resolution, or a sanitized withdrawn result; never return private working fields. Only actual previously published slugs resolve as aliases. The SSR route redirects known aliases after checking visibility. |
| GET `/news/articles/{id}/related` | At most five results under the approved tag/category rules. |
| GET `/news/categories`, `/news/tags` | Public taxonomy used in current public content; do not expose private-article counts. |
| GET/POST `/editorial/articles` | Authorized personal/work lists or create an author-owned draft. Lists never expose another author's unsent work. |
| GET/PATCH/DELETE `/editorial/articles/{id}` | Authorized private detail, versioned author save, or retained deletion before first publication. Save handles Approved -> InReview atomically. |
| POST `/editorial/articles/{id}/working-version` | Create from the current published revision; reject a competing unfinished version. The initial `/propose` action remains a compatibility alias. |
| GET `/editorial/publications`, `/editorial/articles/{id}/publication` | Effective-editor paginated publication summaries and the current published snapshot for direct correction. These routes never return unsent author content or grant private history access. |
| POST `/editorial/articles/{id}/submit`, `/recall` | Submission with required-field/change-description validation, or author recall; preserve reviewer routing. |
| GET `/editorial/review-queue` | Eligible submitted summaries; full history remains subject to assignment permissions. |
| POST `/editorial/articles/{id}/claim`, `/release`, `/takeover` | Atomic, versioned reviewer assignment transitions with history. |
| POST `/editorial/articles/{id}/approve`, `/return`, `/reject` | Assigned-review decisions; return/rejection require feedback/reason. A different editor must claim/take over before deciding. |
| POST `/editorial/articles/{id}/publish`, `/withdraw` | Approved publication by owner/effective editor, or reasoned withdrawal by effective editor. |
| POST `/editorial/articles/{id}/corrections` | Effective-editor update of current published revision, atomically superseding working content; not available on withdrawn content. |
| GET `/editorial/articles/{id}/history` | Permission-filtered retained snapshots/actions, paginated; does not expose author-only unsent content to reviewers. |
| PUT `/editorial/articles/{id}/featured` | Versioned effective-editor featured flag; atomically enforce at most five selected articles. |
| POST/PATCH `/editorial/categories`, `/editorial/tags` | Effective-editor/admin taxonomy create/update/archive under unique-name and existing-association rules; update routes append resource ID. |
| POST/GET `/media/images` | Upload/status or authorized own library, with processed-file validation and pagination. |
| GET `/media/images/{id}/{variant}` | Stream only after public-reference/block or protected preview authorization; no direct object-store bypass. |
| GET/POST `/media/templates` | Authorized selectable templates or an author's proposed shared image. |
| POST `/media/templates/{id}/submit`, `/approve`, `/reject` | Author submission/resubmission or Moderator/Administrator decision; rejection requires a reason. |
| POST `/media/templates/{id}/archive`, `/restore`, `/block`, `/unblock` | Moderator/Administrator versioned actions with retained audit; block/unblock require reason. |
| POST `/media/templates/{id}/removal-requests` | Submitter's idempotent request for mandatory archival. |
| GET `/media/template-review-queue`, `/media/template-removal-requests` | Moderator/Administrator queues with bounded pages. |
| POST `/media/template-removal-requests/{id}/resolve` | Moderator/Administrator archives and resolves atomically, recording actor and notifying submitter. |
| GET `/media/templates/{id}/history` | Moderator/Administrator history or submitter's own decisions; never a shared-library history feed. |
| GET `/account/notifications`; POST `/account/notifications/{id}/read`; DELETE `/account/notifications/{id}` | Own paginated notification list and own read/delete actions. |
| GET/PUT `/account/notification-preferences` | Own independent per-type/channel settings; authentication service messages are excluded. |

Authoring DTOs include article ID/version, working/public revision IDs and states, content/taxonomy/media placement/source/SEO fields, and permission-appropriate actions. Review DTOs expose only submitted snapshots. Notification DTOs omit protected text when the recipient cannot currently access it. Published DTOs expose public fields only. Current author display names come from an explicit Identity operation. Public update times derive from publication/correction history for the current public snapshot; draft saves and role/name changes do not change them. Actual published revision titles form the retained alias ledger, so no separate alias migration is required. Processing/status DTOs contain no credentials, object-store URLs, raw source file, or unredacted error payloads.

Return 201 for new resources, 200 for reads/updated DTOs, 202 for durable accepted processing/notification work, and 204 for actions without a response body. Errors follow Stage 1: 400 validation/CSRF, 401 no current session, 403 known operation without permission, 404 inaccessible private detail/unknown public content, 409 stale version/state/assignment/uniqueness, 413 oversized request, 415 unsupported upload type, and 429 with Retry-After for limits. Do not let differing private-detail errors enumerate hidden articles.

- News uses explicit Media operations to validate and retain references; it never reads or writes Media tables directly.
- Draft media remains private. Public image access depends on current publication references and absence of a template block, including after withdrawal; historical/draft references alone do not grant anonymous access. Approved-template previews require author access; Moderator/Administrator users can inspect proposals under their template-review permission. Approval never substitutes for a current-publication reference on anonymous image reads.
- Validate actual image format, byte size, dimensions, and animation against the approved limits above; extensions and declared MIME alone are insufficient. Generate object keys on the server. Processing follows the rules above; initial throttling defaults are specified below.
- News changes, history, and notification Outbox work commit atomically. Provider/broker outages do not undo committed decisions.
- An upload that has never been attached to an article or template proposal is eligible for automatic cleanup seven days after creation. Draft, retained-version, and history references exclude images from cleanup. Coordinate reference attachment and cleanup so a competing reference cannot lose its file.
- PostgreSQL and S3 do not share a database transaction. Represent images as Processing/Ready/Failed/Deleting and permit article/template attachment only to Ready images. Use durable idempotent processing/cleanup work and server-generated object keys. A partial upload never becomes selectable. Reconcile stale processing and partially written output objects; mark deletion under the same reference lock used by attachment before deleting objects. Keep referenced masters/variants and retry failed object deletion without removing references.
- Version checks and coordinated authorization/state rechecks cover competing review actions, publication, editor correction versus author revision, withdrawal, role revocation, article deletion, and media deletion. Coordinate role changes and assignment release through explicit Identity/News operations in one PostgreSQL transaction with shared connection/transaction. Reuse the existing administration serialization order before article/review locks; recheck the actor after waiting. A release records the role-changing actor and affected reviewer. No stale assignment remains actionable for a revoked reviewer. Deletion versus first publication must not permit deleting an article that wins publication.
- Public lists, direct URLs, search, SSR, media responses, and caches must enforce current visibility after withdrawal. Start with private S3 buckets, API-streamed image access, and no shared public-response cache. Do not issue anonymous object URLs or long-lived signed links bypassing current visibility/block checks. Return no-store on gated image/article responses and shared-cache exclusions for staff pages; in-flight/already-downloaded bytes cannot be recalled. Verify subsequent-request denial and concurrent visibility changes before considering caching.

## Notifications

Review outcomes and feedback are available in the account and by Email. Preferences provide separate switches per notification type/channel; disabling delivery must not hide decisions or feedback in article records. Confirmation and recovery remain mandatory messages.

| Recipient | Approved events |
| --- | --- |
| Article author | Return with feedback, final rejection, approval, publication, direct editor correction, article withdrawal, and closure of a superseded working version. |
| Template submitter | Template approval, rejection, archival, blocking, and unblocking. |
| Affected editors | Resubmission to their assigned review, author withdrawal/deletion, and review transfer. No broadcasts to all editors for new shared-queue articles. |
| Authors of affected articles | Template blocking/unblocking where the image is used, including draft articles. |

Every listed type defaults to enabled independently for account and Email delivery. Users may mark account notifications as read and delete them from their own list; underlying decisions, version text, and history follow their owning retention rules and are not deleted with notifications.

Account notification records expire 30 days after creation regardless of read state. This is separate from Email delivery-attempt retention and owning-module history. A notification or link grants no extra content permissions; a transfer/revocation notice must not expose article content that the recipient can no longer read.

Workflow Email expires seven days after the originating event; check expiry before SMTP and administrative replay. Reuse Stage 1 transient retry intervals of 1, 5, 15, 30, and 60 minutes after each preceding failed attempt; permanent failure stops automatic attempts. An administrator may replay failed work only within the same seven-day window. Replay does not move the event timestamp or expiry. Expired deliveries are skipped without deleting the business decision/history. Resolved delivery retention and retained replay audit follow Stage 1 independently of account-notification cleanup.

## Engineering Defaults

These parameters are implementation choices delegated by the owner; they do not extend the agreed feature scope. Adjust after measurement or compatibility checks, recording the reason and preserving product behavior.

| Area | Initial contract |
| --- | --- |
| Text counting | NFC-normalized Unicode scalar values for text fields; trim outer whitespace for names/titles and presence checks. Preserve body formatting. Check decoded URLs separately. Password rules remain unchanged. |
| Supporting fields | Category/tag/template/source name up to 100 characters; SEO title up to 200, SEO description up to 500; alt text up to 500; caption up to 1000; feedback/reasons/change summary up to 2000; up to 20 sources, URL up to 2048 characters. Required reasons and names must contain non-whitespace text. |
| Links | External links and source URLs use absolute HTTP/HTTPS only. Block active-content schemes; do not fetch arbitrary submitted URLs server-side. Render links safely with escaped text. |
| Payload bounds | Structured body up to 2 MiB UTF-8 JSON and 2000 blocks in addition to the approved plain-text bound; image requests bounded around the 10 MiB file plus form overhead. Validate before persistence. |
| Upload throttling | Initially 20 upload attempts per ten minutes per account and 60 per ten minutes per trusted client IP; rejected attempts count. Acquire one process-local upload slot before multipart model binding, rejecting competing work with 429/Retry-After; bound request buffering to memory. Stage 1 auth limits remain unchanged. |
| Write throttling | Initially 120 draft-save attempts per minute per account and 30 workflow/moderation mutations per minute per account; shared persistent enforcement and Retry-After. |
| Search bounds | Query up to 200 characters; bounded page size and stable ordering; database indexes on public revision/search, first publication, taxonomy, and reference joins. |
| Jobs | Worker jobs for notification expiry, never-attached upload cleanup, and object reconciliation; persistent Quartz schedules with no concurrent execution and idempotent restart recovery. |
| Baseline data | 10,000 public articles, 100,000 article-image references, 100 categories, and 1000 tags. Record environment, query plans, latency, and memory during load checks; measurements guide optimization rather than adding speculative caching. |

### State and Concurrency Mapping

Keep article publication state (`NeverPublished`, `Published`, `Withdrawn`) separate from working-version state (`Draft`, `InReview`, `Approved`, `FinalRejected`, `Superseded`). Published snapshots are retained independently. Return/author recall maps to Draft with a history event and previous-reviewer routing; explicit review release keeps InReview and removes assignment. Approval maps to Approved; a valid explicit approved-version save atomically maps back to InReview. Publication atomically replaces the public snapshot and completes the working version. Withdrawal preserves the public snapshot privately and maps any existing working content to Draft. Direct editorial correction creates a public revision and closes existing working content as Superseded. Final rejection enforces the agreed no-resubmission restriction; supersession is a distinct technical closure that permits a new proposal from current published content.

Represent template approval (`Pending`, `Rejected`, `Approved`) separately from archival and blocking flags, so restoration/unblocking cannot accidentally undo the other restriction. Store rejected proposal revisions and require resubmission for a new decision. Correcting a rejected proposal references a newly processed image rather than overwriting an existing Ready file. Serialize simultaneous decisions/removal resolution with versions and resource locks; retained published/approved files are immutable.

Save draft working data without snapshotting every keystroke into editorial history. Freeze and retain snapshots at submission and significant decisions/public changes; retain the full content on article deletion and supersession. Preserve all already-retained versions. Never expose current unsent draft edits through review history, media references, notification bodies, or shared queries.

### Prototype and Delivery Checks

The first implementation task prototypes the editor and homepage using the agreed rules. Choose editor/library/storage versions after checking current official documentation, licenses, structured-document round-trip, Angular compatibility, image access, and SSR safety. The editor prototype must cover all required blocks, image captions/alt text, links, validation, and unsafe pasted content. The homepage prototype uses the main featured block and five-item latest/category blocks and a separate detailed list with paging, responsive layout, a one-to-five-story carousel inside the large card, and three general latest-news cards alongside. The owner reviews concrete prototypes; do not restart a technical questionnaire for library/encoding/CSS choices.

Demonstrate backup and restore of PostgreSQL, protected configuration/keys, and processed image objects into an isolated environment. Verify published and private image access, retained history, pending delivery, migration repeatability, and application restart. No production/public deployment is implied.

## Remaining Decisions and Tracking

The category/search bar also includes an All News button that opens the complete detailed list with search, filters, and pagination, without a preselected category or query. Latest and category buttons retain homepage block scrolling. Hide the category/search bar on the detailed list; retain the site header and the list's own search/filter controls.

The owner approved mandatory archival on a template author's removal request and a seven-day workflow Email delivery/replay window. On 2026-11-16 the owner confirmed that editors choose the two production homepage categories and their order; Transport and Culture remain prototype examples. Administrator has the same effective editorial permission. Implementation may discover a real conflict or new business choice; discuss that specific issue rather than inventing behavior.

The following workflow issues are tracked in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3). Issues #7-#13 are implemented; final Stage 2 owner review remains outstanding. Issue #14 is In Progress: technical verification is complete and owner acceptance remains open. The current branch is `prototype/stage-2-news-review`; published revisions and discovery are captured in 4ec91bb and 28c050d.

| Issue | Workflow | Dependencies |
| --- | --- | --- |
| [#7](https://github.com/Pikatoise/city-portal/issues/7) | Editor and homepage prototypes | Stage 1 complete. |
| [#8](https://github.com/Pikatoise/city-portal/issues/8) | Processed private images and storage | #7. |
| [#9](https://github.com/Pikatoise/city-portal/issues/9) | Shared-template moderation and notifications | #8. |
| [#10](https://github.com/Pikatoise/city-portal/issues/10) | Author drafts, taxonomy, validation | #8, #9. |
| [#11](https://github.com/Pikatoise/city-portal/issues/11) | Review, first publication, notifications | #10. |
| [#12](https://github.com/Pikatoise/city-portal/issues/12) | Published revisions and withdrawal | #11. |
| [#13](https://github.com/Pikatoise/city-portal/issues/13) | SSR discovery and featured homepage | #7, #12. |
| [#14](https://github.com/Pikatoise/city-portal/issues/14) | Acceptance, backup/restore, owner review | #8-#13. |

Stage 1 acceptance remains closed. Stage 2 completion review remains outstanding.

Issue #14 evidence and the owner review script are in
[STAGE_2_ACCEPTANCE.md](../STAGE_2_ACCEPTANCE.md). Local engineering measurement
targets are API p95 <= 500 ms and SSR p95 <= 1,000 ms at eight readers on the
specified baseline, not a production SLA. The measured migration adds a partial
current-public-revision index and favors inline search-vector storage. Apply it
with writers stopped: it recomputes existing generated vectors without changing
article content or versions.

## Acceptance Criteria

- An author can draft, submit, correct a returned article, obtain approval, and publish through the UI and API. Final rejection prevents resubmission; assignments and takeover preserve history.
- Autosave and manual Save persist incomplete drafts without requiring submission fields. Stale saves cannot overwrite committed changes. Submission rejects missing required fields. Competing claims on one queued article produce only one assigned reviewer.
- Authors can delete their own never-published articles before or after submission, including during review and after rejection; this operation rejects ever-published articles. Removing Author closes author UI/API access, including own unpublished materials and history, unless a current role grants the relevant permission. Releasing a review removes its assignment, restores shared-queue availability, and records the actor in history.
- New unsent changes after a return are author-only; submitted snapshots/history retain their agreed audience. Resubmission after author withdrawal returns to the previous eligible reviewer or falls back to the shared queue. Saving approved unpublished content explicitly validates fields and atomically saves, invalidates approval, and returns the changed version to the previous eligible reviewer or queue. Autosave is disabled for this operation; invalid input cannot change approval or review state.
- API authorization rejects unconfirmed or unauthorized actors, stale versions, and invalid state transitions. Revoked permissions cannot bypass the agreed transaction checks.
- Administrators can perform Editor operations without an assigned Editor role. Editors and administrators cannot read another author's draft before submission through the UI, API, media, or history paths.
- At most one unfinished working version exists per article under concurrent creation. Losing all effective editorial permissions releases assigned reviews to the shared queue with history; retaining Administrator prevents release based solely on Editor removal.
- Proposed revisions never replace public content before publication. Direct corrections preserve credit/history, close an existing draft/reviewed/approved author version as superseded with retained text and reason, and notify the author. Superseded versions cannot be published.
- Deleting a never-published article removes it from author lists and review queues while retaining all versions and history for the assigned editor and administrators if previously submitted. Never-submitted deleted drafts remain closed to staff. History access after review completion persists for the assigned editor, but takeover/release removes the previous editor's access; current permissions still apply.
- Withdrawal immediately removes public article access from lists, direct URLs, search, SSR, and relevant media/cache paths. Current and historical article URLs show the agreed withdrawn-material page without content or reason. At withdrawal, existing working content becomes an author-editable draft with review/approval reset. Only the owning author prepares corrections; resubmission goes to the previous eligible reviewer or queue, and republication requires review and approval.
- Upload validation and ownership prevent unsafe files and unauthorized private-media access. Failed uploads and cleanup cannot destroy retained references.
- Processed images have corrected orientation and no EXIF/other source metadata; original uploads are not retained. Authors can select only their own uploads or approved shared templates. Moderator and Administrator can review proposals; rejection requires a reason and permits author correction/resubmission. Approval gives authors library/preview access without granting anonymous access. Unapproved template submissions are unavailable to other authors; reviewer/template-moderator access cannot expose unrelated private content. Withdrawal denies public image reads unless another current publication uses that image.
- Approved-template files cannot be replaced in place. Archival rejects new selections without breaking existing references. Template blocking requires a reason, defeats all public-delivery paths including stored image URLs, and makes articles render placeholders despite publication references. Reasoned, audited unblocking restores image rendering where current publication permits it.
- Template removal requests enter a moderator/administrator queue from the card. Audited restoration from archive preserves the approved file and does not lift an independent block. Cleanup after seven days deletes only never-attached uploads; concurrent attachment and retained draft/version/history references protect their files.
- Category/tag uniqueness includes archived entries and ignores case/outer whitespace within each separate type. Existing archived associations survive new-version submission/publication, while new associations to archived entries are rejected.
- Enforce the approved title/summary/body/tag boundaries and per-file image byte/pixel limits. Reject SVG, animation, unsupported or falsely labelled image content, and a 21st distinct image reference; repeated references to one image do not consume additional slots.
- Marking use of others' material requires at least one structured source on submission. Each cover/gallery/body image placement requires alternative text; captions remain optional. Categories remain flat with one category per submitted article.
- Public news renders through SSR with approved URLs/metadata, search/filter/pagination, featured/related content, and no private data. Public title changes update the URL, and previous URLs redirect to the current URL only while current visibility permits public access.
- Author credit follows the current account display name without changing authorship. Optional revision-owned SEO title/description fall back to article title/summary; public metadata never exposes unpublished overrides. Homepage featured news is the main block followed by the general latest-news feed; the large featured card contains at most five selected stories and shows arrows only for multiple stories.
- Homepage contains featured/latest news, categories, and search. Featured selection is limited to five articles, uses first-publication order, and immediately excludes withdrawn articles. One large card displays a single selected story at a time, with arrows only for multiple stories. Three compact cards and two wide rows form the five-item latest block. Two five-item category blocks follow. Stories may repeat across blocks; each block links to the detailed list, preselecting its category where applicable. Category, single-tag, and first-publication date filters combine with search; numbered news pages contain 20 articles. Default feed order follows first publication, not correction time. Search covers published title/summary/body with Russian word-form support and relevance ordering. Related results contain at most five distinct other public articles, prioritizing shared tags then category, newest first within each group.
- Decisions and notification work survive rollback/outage/duplicate scenarios; preferences do not remove feedback from content records.
- The approved author/template event types have independently configurable account/Email channels, both enabled by default. Read/delete actions affect only the user's notification list and preserve owning-module content/history.
- Only affected editors receive review-event notifications. Template block/unblock reaches authors of affected draft and published articles without exposing private content to other users. Account notifications are removed 30 days after creation, read or unread; owning-module history remains intact.
- Explicit/repeat migrations, generated contracts, formatting/build checks, real infrastructure tests, and desktop/mobile browser scenarios pass. Database/media backup and restore are demonstrated.
- The owner reviews the workflows, transaction/data flow, and a failure scenario before stage closure; limitations and actual verification evidence are recorded here.

## Implementation Status

### Progress Checkpoint: 2026-11-16

| Deliverable | Recorded status |
| --- | --- |
| Product specification and shared UI style | Recorded; homepage/article visuals owner-accepted. |
| Author/editor workspace | Production author/reviewer workspaces use real drafting, Media, review, first publication, and retained history. |
| Media uploads, processing, private storage, and recovery | Implemented, reviewed, and verified locally; issue #8 remains In Progress pending final stage review. |
| Shared-template moderation and library | API/UI, decisions/history, independent flags, and mandatory archival implemented locally. |
| Template workflow notifications | Implemented: independent account/Email preferences, atomic Outbox handoff, current-reference author fanout, expiry/replay, read/delete UI, and durable deduplication. Issue #9 stays In Progress for News integration/final review. |
| News drafts/taxonomy and validation | Implemented and verified API/UI in #10; see [NEWS.md](../NEWS.md). |
| Review and first publication | Implemented, verified, and pushed in 0708b94 under #11; final stage review remains pending. |
| Published revisions and withdrawal | API/UI and basic article SSR implemented in #12; final evidence is in NEWS.md. |
| Public discovery, search, related/featured news, and homepage | Implemented and locally verified in #13; final owner acceptance remains #14. |
| Final stage acceptance and capacity verification | Outstanding; #14 remains Todo. |

Review/first-publication verification: Release backend regression 134 passed, 0 failed, 0 skipped; frontend regression 70 passed. News checks cover drafts, private submitted snapshots, resubmission, assignment/role/version races, atomic notification rollback, review Media audiences, approval/explicit Save/publication, and publication versus deletion. Real HTTPS desktop/mobile workflows passed through first publication. Notification/storage evidence remains in MEDIA.md; the current requirement-to-test matrix and exact verification boundaries are in NEWS.md. Earlier independent reviews covered drafting; review/first-publication received an inline code and test-quality review. These are distinct runs, not additive totals.

Next: cross-workflow verification, capacity evidence, backup/restore and final owner review in #14. Issue #12 preserves public content while proposals await publication, retains superseded text/reasons, and closes article/Media access on withdrawal. Article SSR, alias visibility and public discovery are implemented; the final integration/recovery review remains outstanding. The current contract and delivered review boundary are in NEWS.md. Drafting/CSS checkpoint b38ed07 and review/first-publication commit 0708b94 are pushed. Final owner review remains deferred until the complete stage result. Stage 2 is open.

### Implementation Notes

On 2026-11-14 the owner delegated continuation of editor design and subsequent agreed stage work, reserving review for the final stage result. Do not require another block-by-block design interview. This does not close Stage 2 or authorize new business behavior. The fixture author/editor workspace includes review queues, decisions and history in memory. Production author drafting now exists separately at `/account/articles`, using the same five-section design, real persistence and Media. Review, publication, published changes, withdrawal/republication, article SSR and public discovery are implemented; final cross-workflow verification remains next. Design and implementation evidence are recorded in UI_STYLE.md and NEWS.md.

The owner requested a unified UI style across the application and prototypes. Shared tokens, consistent account/staff/editor controls, and verification are recorded in [UI_STYLE.md](../UI_STYLE.md). The public layout remains the accepted baseline; final editor workflow acceptance and the remaining editorial/public News workflows remain outstanding.

- Specification: product interview complete; engineering defaults and implementation contracts recorded.
- GitHub tracking: #7-#11 remain in progress for integration/final review; #12 and #13 are complete; #14 remains Todo in Project 3. Media, template notifications, drafting/taxonomy, review/publication, and published-change workflows are implemented. Public discovery is delivered in #13; final stage acceptance remains outstanding.
- Prototypes: isolated browser-only Angular configuration and Tiptap 3.34.0 candidate with a versioned JSON/media-reference document. Run/review instructions, saved progress, verification, and limitations are in [PROTOTYPE.md](../../frontend/src/app/news-prototype/PROTOTYPE.md). Only the selected header A remains; alternative headers and the variant switcher were removed at the owner's request. The current five-section author/editor workspace follows the delegated continuation described above. The owner accepted the news presentation for now: one-to-five-story editorial carousel in the large card, three latest general stories alongside, remaining news below, flat white 40%-opacity arrow background. The owner superseded the inline-filter proposal with five-item homepage blocks and a separate detailed list. The owner accepted the homepage visual prototype on 2026-11-14. A separate article-reader prototype now opens from news cards, with metadata, cover/body, sources/tags, and related fixtures. The owner accepted the article-page visual prototype after refinement of typography, media sizing, breadcrumbs, spacing, and the combined author/date footer. Continue agreed implementation; owner review is reserved for the final stage result. Editors and administrators choose the two production homepage categories and their order (owner decision, 2026-11-16). Homepage and article-page visual review are complete; editor/schema final acceptance remains pending.
- Verification: early prototype checks remain in PROTOTYPE.md. Current Media and production drafting/server/browser evidence is recorded in MEDIA.md and NEWS.md. Review/publication/public SSR acceptance and final stage acceptance remain pending.
- Production Media: owned migrations, private processed uploads, S3 storage, Worker reconciliation/cleanup, template moderation/history/removal requests, browser libraries, and configurable notifications are implemented. Drafting now uses the reference transaction contract. Review/publication references and News notifications are implemented in #11; withdrawal and basic public article integration are implemented in #12; discovery remains #13.
- Owner acceptance: not requested; the stage remains open.

### Historical Push Checkpoint: 2026-11-16 (#11)

The owner requested recording progress and pushing the current branch. Review and first publication are delivered in 0708b94, with the verification evidence above. Issues #7-#11 remain In Progress pending integration and final owner review; #12-#14 remain Todo. No Stage 2 acceptance or public news delivery is claimed. The next implementation task is #12.


### Historical Issue #12 Start: 2026-11-16

The initial server checkpoint adds versioned own-author proposals, replacement review/publication preserving first-publication time, and reasoned editorial withdrawal. Withdrawal resets the working content to a private author draft and disables publication Media references atomically. Action responses and retained history do not expose unsent author content to the withdrawing editor. Details and remaining scope are in NEWS.md. Direct corrections/supersession, visibility-checked URLs, UI integration, full failure/race verification and final acceptance remain open; issue #12 is not complete.

Initial #12 verification: 14 News integration tests passed in Release, zero failed/skipped; solution formatting and whitespace checks passed. No frontend/live browser or complete issue acceptance is claimed. Project 3 status is In Progress.


### Issue #12 Delivery: 2026-11-16

Published proposals, direct editorial corrections/supersession, retained history/reasons, withdrawal/republication, and basic public article SSR/current and historical URL visibility are implemented and verified locally. Required author/editor UI actions use real API operations and generated contracts. No schema migration was necessary: actual Published snapshots retain aliases and publication/correction history supplies public update times.

Final verification: Release solution build passed with zero warnings/errors; backend 151 passed, zero failed/skipped, with TRX result validation. Targeted News 28 passed in a separate run. Final frontend 79 passed in 13 files; production Angular/SSR build, full Prettier, generated-client consistency, solution formatting, and explicit/repeat module migration checks passed. Exact requirement-to-test evidence and boundaries are in [NEWS.md](../NEWS.md).

Live isolated HTTPS desktop/mobile workflows covered author proposal isolation, direct correction and retained supersession, withdrawal, reviewed republication, canonical links, and configured-city time. Current public SSR returned no-store 200; two real aliases redirected directly with 307; unknown slugs returned 404. After withdrawal, current and both historical URLs returned no-store 404 shells without article content, credit, private reason, or article-specific metadata. Desktop 1440px/mobile 390px had no horizontal overflow. Live acceptance used an Author/Administrator account; separate-role isolation and concurrency were verified by backend tests.

Independent Spec and Standards/test-quality review found no blocking findings. Live SSR found and resolved a server-DOM `append` incompatibility; final regression also verifies cleanup after partial DOM failure. Docker Desktop was resumed through a data-preserving official restart after a paused-daemon infrastructure failure; the successful full rerun is the recorded completion evidence.

Issue #12 is complete. This does not close Stage 2 or record owner acceptance. Continue with #13 public discovery, then #14 cross-workflow acceptance/backup/restore and owner review. No push or production deployment is implied by this local delivery.


### Issue #13 Delivery: 2026-11-16

Public SSR discovery and the production homepage are implemented. The detailed `/news` list combines Russian word-form title/summary/body search with category, single-tag and inclusive city-local publication-date filters, deterministic ordering and numbered 20-item pages. Public taxonomy and related news use only current published revisions. The homepage renders up to five editorial selections, the three-plus-two latest split, and two ordered category blocks; staff configure categories and featured articles through `/staff/publications` using versioned generated-client operations. News owns the curation audit and generated search/filter migration.

Final backend regression passed 163 tests, zero failed/skipped, with parsed TRX validation. Final frontend regression passed 90 tests in 14 files. Release build, solution formatting, production Angular/SSR build, full Prettier and generated-client/runtime OpenAPI consistency passed. Explicit migration on the existing local acceptance database backfilled discovery values; repeat migration reported no pending work. Search, tag, category and publication-order indexes were verified in PostgreSQL. The requirement-to-test matrix and exact boundaries are in NEWS.md.

Real isolated HTTPS checks covered editor category order/featured UI, homepage/list/article SSR with no-store, Russian inflection search, category filtering, related cards, carousel, 1440px/390px layouts and no horizontal overflow. Final alias/withdrawal regression is recorded in NEWS.md. No shared public cache or direct object-storage bypass was added. Current author credit and revision-owned SEO/URL rules remain enforced.

Spec and Standards reviews found and resolved interruptible-scroll cleanup, reused-slide image failure state, missing featured credit and one-line metadata. Backend regression also corrected the explicit migration-count expectation and serialized test classes sharing the production process-wide upload slot; production throttling was preserved. The owner authorized GPT to finish implementation directly after DeepSeek hit its free quota; the old Orca assignment was confirmed stopped before GPT edits.

Issue #13 is delivered locally. Stage 2 stays open. Continue with #14 for cross-workflow acceptance, representative capacity measurements, fresh backup/restore, failure/recovery evidence and final owner review. No Stage 3 specification, new push or production deployment is implied.

### Issue #14 Technical Verification: 2026-11-17

Owner follow-up: local stand startup must include browser-based pgAdmin and
print credentials for separate RegularUser, Editor, Author, Moderator and
Administrator demo accounts on every launch. The local helper and isolated
review resources are documented in DEVELOPMENT.md. This is development/review
tooling and does not change production registration or permissions.

Follow-up verification: full API/Worker/frontend container build and local
Compose startup passed with pgAdmin healthy. First setup, four real registration/
Email-confirmation workflows and API role assignment prepared five separate
confirmed accounts. A repeated `--no-build` launch retained the same credential
file hash and printed all credentials again. Independent HTTPS login/profile
checks verified each account's exact roles. Browser verification logged into
pgAdmin, connected the preloaded PostgreSQL server and opened the stand database
schemas. API/Worker readiness and base/stand/public Compose configuration passed;
public configuration excludes pgAdmin by default. No additional .NET test run
was needed for this infrastructure/helper-only change.

Release build passed with zero warnings/errors. The complete backend suite
passed 165 tests, zero failed/skipped, with parsed TRX outcomes. Frontend passed
90 tests; production SSR build, formatting, generated-client and runtime OpenAPI
consistency passed. Real infrastructure regressions cover authorization, races,
rollback, broker/SMTP/storage outages, duplicate delivery and restarts.

The new combined regression restores an actual PostgreSQL dump, processed
objects into an isolated bucket and Data Protection keys into a new directory.
It verifies public/private News and Media, retained history, encrypted SMTP
configuration, pending image processing and pending Email from an independent
worker host. Paginated object copying is exercised. The migration regression
verifies preserved existing text/search/version, physical policy and repeatability.

The 10,000-article/100,000-reference/100-category/1,000-tag baseline justified the
DiscoveryReadPerformance migration. Final warm eight-reader measurements passed
local p95 targets with zero HTTP errors: API 19.66-81.94 ms, SSR 302.44-875.91 ms.
Individual SSR requests still reached 2.96 seconds; a fresh post-migration tag
run exceeded its p95 target before the repeated warm run passed. Environment,
plans, memory and limitations are in STAGE_2_ACCEPTANCE.md.

Fresh isolated HTTPS browser checks with separate Author-only and Administrator
accounts covered return/private correction/resubmission/publication, proposal
isolation, direct correction/retained supersession, template block placeholders,
withdrawn current/old URLs and reviewed republication. The reviewer had no
assigned Editor role. Real Worker processing and SMTP delivery were checked;
1440px/390px layouts had no overflow and recorded no browser page errors.

Owner review of workflows, transaction/data flow and a failure scenario is still
required. Issue #14 and Stage 2 remain open. No Stage 3 specification, push or
production deployment is included.

The owner-requested workspace cleanup removed completed diagnostic artifacts,
copied runtimes, build outputs and disposable infrastructure fixtures. The old
acceptance proxies and alternate portal port were retired; the local review
stand uses only https://localhost:8443 and cityportal_stand. Active credentials,
normal installation data, the required local SDK and frontend dependencies are
preserved. Verification results remain in STAGE_2_ACCEPTANCE.md; future temporary
files and task-owned fixtures must be removed when their task finishes.
