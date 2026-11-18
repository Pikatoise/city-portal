# News Drafting, Review, and Published Changes

Current Stage 2 technical acceptance, combined recovery, capacity measurements
and completed owner review are recorded in [Stage 2 acceptance](stages/STAGE_2.md#final-owner-acceptance-2026-11-17).
`DiscoveryReadPerformance` adds the measured public-revision lookup index and
search-vector storage policy. Apply the explicit upgrade with writers stopped
as described in DEVELOPMENT.md. Owner acceptance was recorded on 2026-11-17.

## Delivered Boundary

Issue #10 implements the production author workspace at `/account/articles` and editorial category/tag management at `/staff/taxonomy`. Authors create, reopen, save, preview, and delete incomplete drafts; choose own Ready uploads or approved shared templates; and edit structured body, sources, contextual image text, taxonomy, and SEO fields. The five sections follow the accepted Text/Images/Parameters/Preview/History layout. Review actions now use production API operations rather than simulated state.

Issue #11 adds production submission, assignment/decisions, first publication, history, and News notifications. Issue #12 adds published proposals, direct corrections/supersession, withdrawal/republication, and basic public article SSR with current/historical URL visibility. Issue #13 delivers production SSR discovery, Russian search, related/featured content, ordered homepage categories and curation UI. Stage 2 was owner-accepted on 2026-11-17.

[Stage 3](stages/STAGE_3.md) uses the same editorial workflow for articles
created by claiming resident suggestions. News retains the idea contributor
and records the origin in article history. The public article credits the
contributor by display name. On first publication, News queues the resident
notice in the same transaction as publication.

## Persistence and Permissions

`CityPortal.News` owns the `news` schema, `NewsDbContext`, and explicit migrations. API/Worker readiness requires the News migration; normal startup never migrates. Upgrade existing installations using the explicit migration command in DEVELOPMENT.md.

An article owns author identity, optimistic version, publication state, working/public/submitted revision pointers, current reviewer, first-publication time, timestamps, and retained deletion. Its working revision owns canonical content, safe preview HTML, and plain text. Draft saves mutate one working revision without snapshotting each keystroke. Significant records retain actor/time. Deletion preserves saved text and references while removing the draft from author reads. Never-submitted deleted drafts have no staff-access operation; submitted deletion retains assigned-reviewer/administrator access under current roles.

Author operations require confirmed Email, a current Author role, and ownership. Administrator/Editor alone do not bypass draft privacy. Taxonomy writes require Editor or Administrator. Identity's authorized transaction scope serializes role changes before News resource locks and rechecks session/roles after waiting. Stale writes return HTTP 409. Media reference validation joins the same connection/transaction, so failure cannot partially save article fields.

Taxonomy names are trimmed/NFC-normalized, limited to 100 Unicode scalar values, and unique by kind using invariant case normalization, including archived names. Existing archived associations survive saves; new archived associations are rejected. Category/tag namespaces are separate. This slice adds create, rename, and archive, without taxonomy deletion/restoration.

Taxonomy changes retain actor, time, action, name, archival state, and version in the owning module. Audit records commit with the change; rejected/stale requests create no audit entry. The additional audit migration was verified on 2026-11-16.

## Version 1 Document Contract

Revision content has `schemaVersion: 1` and a ProseMirror JSON `body`; title/summary, category/tag IDs, cover/gallery placements, attribution flag/sources, and SEO overrides are separate revision fields. Images use Media IDs and contextual alternative text/captions, without object-store URLs. The browser-only editor uses pinned Tiptap 3.34.0 packages.

Supported nodes: doc, paragraph, heading (H2/H3), text, bulletList, orderedList, listItem, blockquote, hardBreak, and mediaFigure. Marks: bold, italic, and absolute HTTP/HTTPS link. Ordered-list start is retained within 1-1,000,000. Canonical link storage discards known Tiptap presentation metadata; server HTML escapes attributes and supplies `noopener noreferrer`. Pasted rich content becomes text; dropped/pasted image bytes are not stored as article HTML.

Text and hardBreak nodes both accept the supported marks, matching editor
formatting applied across line breaks. Formatted hard breaks round-trip through
validation, canonical storage and rendering; unsafe links and unsupported marks
remain rejected.

The independent server allowlist enforces structure, properties, attributes, safe URLs, normalization, and field bounds. It renders from structured content rather than trusting HTML. Limits are 2 MiB body JSON, 2,000 blocks, 20,000 nodes, 24 node levels, and a 4 MiB complete save request. Body text is limited to 100,000 normalized scalars; repeated placements count once toward 20 distinct images. The depth bound keeps canonical serialization within runtime limits.

Incomplete drafts may omit submission fields/alternative text, while supplied values retain maximum/security checks. `ArticleDocuments.Validate(..., requireComplete: true)` implements submission presence rules, including sources and every image's alternative text. Submission and explicit approved Save both enforce these rules before changing state.

## Save and Failure Behavior

Autosave runs after five idle seconds, with one active request. Edits arriving during a save stay dirty and subsequently use the returned version. Manual Save shares that boundary. Network/validation failures preserve dirty fields and do not report success. HTTP 409 stops autosave, preserves a complete local JSON copy, and offers copying before server reload. Edits arriving during reload are also preserved rather than overwritten.

HTTP 400 feedback names the affected field and, where useful, its workspace tab,
then explains the correction in Russian, including character limits and URL rules.
Submission presence errors identify title, summary, article text, category, sources
and alternative text separately. Feedback does not claim an earlier successful
draft save failed. Unknown diagnostic strings are not shown as user instructions.

The image picker uses real paginated own/template lists, upload, and processing state. Gated preview responses show unavailable images as placeholders. Draft references remain private; deletion makes them historical-only while preserving storage retention. Author/staff routes are browser-rendered with no-store responses.

## Verification: 2026-11-15–16

Release backend: 127 passed, 0 failed, 0 skipped; TRX counters were checked. Frontend: 63 passed. Production Angular/SSR build, solution formatting, generated-client consistency, explicit/repeat migrations, and independent standards/spec reviews passed. Reviews found and resolved ordered-list preview numbering and an in-flight conflict-reload race.

After the restart, the complete backend suite passed again with taxonomy audit enabled. Its migration applied to the isolated acceptance database and a second invocation applied nothing. The original frontend build had a one-byte author-workspace CSS budget warning; scoped shared upload styles resolved it in pushed commit b38ed07. Container builds and original drafting browser checks preceded the final taxonomy audit addition; that addition was verified by the backend suite and explicit migrations.

Real isolated HTTPS checks covered five-second autosave, durable reopening, category/tag creation/selection, sources/SEO, a real Ready-image cover and contextual text, preview rendering, desktop 1440px/mobile 390px without horizontal overflow, two-tab 409 with retained local copy/server reload, and deletion followed by 404. A source-encoding error found in browser checks was corrected and covered by a rendered Russian-label regression. Deliberate 409/404 responses are expected. This does not claim final owner acceptance or delivered editorial/public workflows.

| Requirement | Evidence |
| --- | --- |
| Incomplete persistence, one revision, competing writes, rejected input | `Drafts_IncompleteSaveReopenAndCompetingVersions_PreserveOneCommittedRevisionAsync` |
| Privacy, staff isolation, current role | `Drafts_PrivateOwnersAndRoleRemoval_DenyOtherAuthorsAndStaffAsync` |
| Taxonomy permissions, uniqueness/archive, versions, existing associations | `Taxonomy_UniqueArchivedNamesAndExistingAssociations_EnforcePermissionsAndVersionsAsync` |
| Own/shared/processing Media, atomic rollback, retained deletion | `Drafts_MediaSelectionAndRetainedDeletion_CommitReferencesAtomicallyWithoutPublicAccessAsync` |
| Draft versus submission fields | `Documents_IncompleteDraftAndCompleteSubmission_ApplyDifferentPresenceRules` |
| Unicode and block boundaries | `Documents_NfcScalarAndBlockBoundaries_RejectOnlyBeyondTheAgreedMaximum` |
| Malformed/unsafe JSON, depth/size | `Documents_InvalidShapesAndLinks_AreValidationErrors` |
| Safe rendering, distinct image placements | `Documents_SafeRenderingAndMediaPlacements_EscapeTextAndCountDistinctReferences` |
| Russian UI and actual editor mounting | `renders the Russian workspace and real editor labels` |
| Idle timing, serialized saves, concurrent edits | `waits five idle seconds, serializes saves and preserves edits made during the first request` |
| Conflict/copy and failed retry | `stops autosave on conflict and exposes a complete local copy without replacing the form`; `retains failed network saves for an explicit retry and never reports them as saved` |
| Reload race and role gating | `keeps edits typed while a conflict reload is in flight`; `does not request private article data without a current confirmed Author role` |
| Preview consistency | `preserves bounded ordered-list starts and escapes text and image captions` |

## Review and First Publication: Issue #11

The existing author route provides submission, author recall, required resubmission notes, separate publication, and paginated retained history. `/staff/reviews` provides the shared queue, personal assignments/history, claim/release/takeover, feedback and final rejection, approval, publication, and retained snapshots. Queue summaries include approved versions so any effective editor may publish. Full submitted detail/history still requires current ownership, assignment, or Administrator permission. The additional read route is `GET /api/editorial/reviews/{id}`; the shared queue and versioned action/history routes follow STAGE_2.md. Every mutation is CSRF protected and rate limited.

Submission creates a retained content snapshot instead of promoting a mutable draft in place. Return/recall creates a new author-only working draft, while the reviewer continues to read the preceding submitted snapshot. New submission requires a change description after any previous submission and reuses the previous eligible reviewer; otherwise it enters the shared queue. Submitted text cannot be edited. Final rejection prevents further editing/submission. Assigned reviewers alone decide; release/takeover updates all retained Media audiences and history access. Administrator retains effective editorial permissions independently of an Editor assignment.

Approval is distinct from publication. Approved content has no autosave; explicit Save validates a complete replacement snapshot and immediately returns it to review. Publication records actor/time, first-publication timestamp, and the public revision pointer atomically with Media public references and notifications. Public news delivery follows the Stage 2 discovery contract. Ever-published articles cannot use unpublished deletion. Publication versus deletion has one winner.

Identity invokes `IAccountRoleChangeObserver` within its existing role-change transaction and serialization lock. News clears affected reviewer assignments, updates their versions/history, and removes retained Media access before the role change commits. Keeping Administrator preserves effective editorial assignments. Notification persistence failure rolls back both role changes and assignment release. Generic delivery bodies contain no title, text, or reason; feedback remains in permission-gated News history. Existing notification preferences, transactional Outbox, retry/expiry, and receipt deduplication apply unchanged.

## Drafting Requirement Evidence

| Requirement | Evidence |
| --- | --- |
| Submission validation, retained snapshots, unsent privacy, feedback and resubmission routing/notifications | `Review_ReturnAndResubmit_PreserveSnapshotAndPrivateUnsentChangesAsync` |
| Self-approval, full validation on approved Save, separate publication by another editor, stale writes, no published deletion | `Review_ApprovedSaveInvalidatesApprovalAndPublicationIsSeparateAsync` |
| Competing claims, takeover/release access, atomic role-loss release, Administrator permission preservation | `Review_AssignmentRacesTakeoverReleaseAndRoleLoss_EnforceCurrentAccessAsync` |
| Role removal versus approval rechecks authorization after serialization and leaves no actionable assignment | `Review_RoleRemovalVersusApproval_SerializesAuthorizationAndAssignmentAsync` |
| Recall, final rejection, retained deletion and assigned-editor history | `Review_RecallFinalRejectionAndRetainedDeletion_EnforceTransitionsAsync` |
| Current reviewer Media access, former reviewer denial, anonymous publication access, publication/deletion race | `Review_MediaAudienceChangesAndPublicationDeletionRace_AreAtomicAsync` |
| Failed notification writes roll back approval and role/assignment changes | `Review_NotificationFailure_RollsBackDecisionAndRoleReleaseAsync` |
| Approved autosave disabled and explicit Save re-enters review | `disables approved autosave and explicitly saves a new reviewed version` |
| Failed save prevents submission; current review is read-only and versioned recall restores editing | `does not submit when the required draft save fails`; `keeps review content read-only and recalls it through a versioned action` |
| A validation error can be retried after correcting only the required change description | `retries resubmission after correcting the change description without editing saved content` |
| Rendered snapshot and required feedback; conflict preserves feedback; release clears private detail; role gating | `renders the submitted snapshot and requires feedback for return and rejection`; `preserves feedback on a competing decision and clears private detail after release`; `does not fetch the review queue for an author without editorial permissions` |

## Issue #12: Published Changes and Withdrawal

The API and UI implement the contracts below. Verification and owner acceptance are recorded in STAGE_2.md.

### Revision and Correction Contracts

The author workspace remains `/account/articles`. Effective editors use `/staff/publications` for published materials and `/staff/articles/{id}` for explicit direct corrections. Public readers use the address contract in STAGE_2.md. The correction workspace reuses the structured editor, requires a reason, disables autosave, and preserves local edits after a stale-version conflict. These routes use the generated API client and the existing confirmed-role/CSRF boundaries.

`POST /api/editorial/articles/{id}/working-version` creates an own-author draft from the current public revision with the expected article version. The initial `/propose` route remains compatible. The current published revision stays unchanged through private saves, submission, review, and approval; only publication replaces it. Replacement publication preserves first-publication time.

Effective editors use `/api/editorial/publications` and `/api/editorial/articles/{id}/publication` to read only current published content. `POST /api/editorial/articles/{id}/corrections` accepts expected version, complete content, and a required reason. It validates taxonomy and Media, creates a new retained publication, preserves original authorship, supersedes unfinished Draft/InReview/Approved proposals with retained text and reason, and queues the corrected/superseded notices in one authorized transaction. Closed snapshots cannot be published. FinalRejected is terminal and is not rewritten into Superseded by a direct correction.

The owning author retains private superseded text in history. Staff history access still follows assignment/current roles and excludes unsent content. Correction does not grant the correcting editor access to private proposals. A subsequent permitted proposal starts from the current publication. Withdrawal preserves existing working text in a new private author draft, resets review/approval, disables public Media grants atomically, and requires fresh review/approval before republication. Editor/Administrator alone cannot prepare withdrawn corrections.

### Public URL and Visibility Contracts

The current address, withdrawal and migration contracts are in
[STAGE_2.md](stages/STAGE_2.md#content-and-state-contracts). Earlier ID/slug routes
and 307 aliases were superseded; old addresses now return 404. Public responses
remain non-cacheable and never expose private working content or withdrawal reasons.

### Issue #12 Requirement Evidence

Alias-specific rows record historical checks; the current contract requires 404 for old addresses.

| Requirement | Evidence |
| --- | --- |
| Proposals remain separate through review; only replacement publication changes public content and preserves first-publication time | `Published_ProposalKeepsPublicRevisionAndReplacementKeepsFirstPublicationAsync` |
| Draft/InReview/Approved supersession retains text/reason, preserves authorship/history, notifies the author, and prevents stale publication | `Published_DirectCorrectionSupersedesWorkingTextAndPreservesCreditAsync` |
| Complete validation, expected versions, CSRF, current roles, and denial of direct rewriting while withdrawn | `Published_CorrectionValidationPermissionsAndWithdrawnState_DoNotMutateAsync` |
| Public current revision, real aliases, unknown/draft slug denial, current display name, stable public time, private SEO isolation, and sanitized withdrawal | `Published_PublicReadsAliasesAndWithdrawal_ExposeOnlyCurrentPublicContentAsync` |
| Correction versus publication/withdrawal has one version winner and consistent public/Media visibility | `Published_CorrectionVersusPublicationOrWithdrawal_HasOneVersionWinnerAsync` |
| Revoked editorial permission cannot bypass serialized correction authorization | `Published_CorrectionVersusEditorialRoleRevocation_RechecksCurrentPermissionAsync` |
| Notification persistence failure rolls back public text, history, revision pointers, URLs, and Media grants | `Published_NotificationFailureRollsBackPublicTextHistoryAndMediaAsync` |
| Withdrawal preserves another current publication's image grant; template blocking overrides publication access | `Published_SharedImageReferencesAndTemplateBlocks_EnforceCurrentVisibilityAsync` |
| Ownership without a current Author role does not reveal a private proposal on withdrawal | `Published_FormerAuthorWithEditorRoleCannotReadPrivateProposalOnWithdrawalAsync` |
| Editor Media selection uses the actor's uploads and preserves original article ownership | `Published_EditorMediaSelectionUsesActorUploadsAndRetainsOriginalOwnershipAsync` |
| Direct correction does not reopen a finally rejected author proposal | `Published_EditorialCorrectionAfterFinalRejectionPreservesTerminalAuthorStateAsync` |
| Withdrawal resets approval, closes anonymous image access, and requires author correction/review before republication | `Published_WithdrawalResetsReviewClosesPublicMediaAndRequiresApprovalAsync` |
| Author proposals remain editable without granting deletion of withdrawn material | `creates a separate published proposal and allows draft autosave without deleting withdrawn material` |
| Direct correction requires an explicit reasoned save and avoids autosave/author-only operations | `requires an explicit reasoned correction save without autosave or author-only API requests` |
| Staff withdrawal validates its reason and retains input on a stale version | `requires a reason and retains withdrawal input on a stale version` |
| Public metadata is removed on withdrawal; real aliases redirect without content; unknown content and blocked images render safely | `renders only the publication and clears its metadata when it becomes withdrawn`; `returns a revalidated SSR alias redirect without rendering article content`; `distinguishes unknown material from withdrawal and displays blocked body-image placeholders` |
| SSR city time uses the configured absolute API origin | `loads the configured city time zone from the absolute server API root` |

Independent final read-only Spec and Standards/test-quality review found no blocking findings. Earlier review findings were corrected: deletion is hidden for ever-published articles, correction-save wording is accurate, failed body images display placeholders, withdrawal removes old metadata, and CityClock uses the configured server API root. The reviewer did not execute tests or browser acceptance; those results are recorded separately.

## Issue 13: Public Discovery and Editorial Curation

The production homepage at `/` and detailed list at `/news` use real current-publication data. Homepage blocks contain up to five featured selections, five latest stories (three compact cards and two wide rows), and two editor-selected ordered category blocks of up to five stories. Stories may repeat across blocks. Before initial category configuration, no category blocks are chosen automatically. Empty categories remain visible as selected blocks with a link to their list.

Editors and administrators configure category order and toggle featured articles from `/staff/publications`. Category choices must be distinct active categories. The singleton category selection has its own expected version; featured writes require the article version. Identity's existing authorization transaction serializes role changes and curation before the article lock, so concurrent selections cannot exceed five. Decisions append News-owned curation audit records. Withdrawal clears featured selection in the same transaction and removes public article/media access under the existing contract. Invalid/stale/unauthorized writes do not create audit records.

The public API adds `GET /news/articles`, `/news/articles/{id}/related`, `/news/categories`, `/news/tags`, and `/news/homepage`. Editorial routes are `GET/PUT /editorial/homepage` and `PUT /editorial/articles/{id}/featured`. Writes require current confirmed editorial permissions and CSRF. Public taxonomy includes only entries used in current published content, with no private counts.

Search uses generated PostgreSQL revision columns and a weighted Russian `tsvector`: title A, summary B, body C. GIN indexes cover search and tags; category and partial publication-order indexes support filtering and paging. These generated values backfill existing rows and stay synchronized on draft saves, publication and editorial corrections. Public queries always join `PublishedRevisionId` and current article visibility; private/old indexed revisions cannot enter results. The implementation follows [Npgsql full-text mapping](https://www.npgsql.org/efcore/mapping/full-text-search.html) and [JSON translations](https://www.npgsql.org/efcore/mapping/translations.html).

Query parameters are `query`, `categoryId`, `tagId`, `from`, `to`, `page`, `pageSize`, and `featured`. Query text is limited to 200 Unicode scalars. The default page size is 20, with an API maximum of 100. Category/tag/date/search filters combine. Inclusive date bounds use the installed city time zone and an exclusive next-local-day UTC end, including daylight-saving transitions. No-query order is first publication descending then article ID ascending; search prepends descending rank. Corrections never promote an older article. Related results prioritize shared tags then category, newest first within each group, excluding self/duplicates and capped at five.

Summaries contain public card fields and current author display names from one bounded Identity batch operation, with no Email, working revision fields, history, full body, object keys or unpublished SEO. Public generated-client requests omit credentials and disable browser/transfer caching. API and SSR responses use no-store, including validation failures. No shared public cache or direct object-storage URL is introduced.

The list owns search/filter controls and preserves them in navigation links. The homepage category bar scrolls to blocks and its All News link opens the unfiltered list. Article category/tag links open filtered lists. Article SSR uses the current canonical address, withdrawal and revision-owned SEO rules, adds current related cards, and follows the existing fluid typography/media style. Related-data failure does not hide the main article. Real image failures render placeholders, including reused carousel slide elements; author/date metadata stays on one line. Category scrolling remains interruptible at 650ms, carousel settling at 380ms and content entrance at 180ms.

### Requirement-to-Test Evidence

| Requirement | Evidence |
| --- | --- |
| Russian title/summary/body inflection, combined search/taxonomy, private/old revision exclusion, immediate correction/withdrawal, current author credit | `Discovery_RussianSearchFiltersAndReplacement_UseOnlyCurrentPublicationAsync` |
| Query/page/date/ID validation and no-store failures | `Discovery_InvalidQueriesReturnValidationAsync`; oversized query assertion in `Discovery_PagesDatesAndTiesAreBoundedAndDeterministicAsync` |
| Non-UTC and DST local-day boundaries, deterministic ties, 20/100-item page bounds and no duplicate/omitted IDs | `Discovery_PagesDatesAndTiesAreBoundedAndDeterministicAsync` |
| Related tag-before-category order, five-result limit, exclusion/deduplication, withdrawal and public-only taxonomy | `Discovery_RelatedAndTaxonomyUsePublicTagThenCategoryOrderAsync` |
| Ordered category persistence, empty blocks, expected versions, distinct active choices, permissions, CSRF and audit | `Curation_CategoriesPersistOrderVersionPermissionsAndAuditAsync` |
| Concurrent featured capacity, stale/forbidden writes, latest/featured bounds and immediate withdrawal | `Curation_ConcurrentFeaturedLimitAndWithdrawalPreserveVisibilityAsync` |
| Explicit/repeat owned-schema migration and retained data | `ModuleMigrations_OnEmptyDatabase_CreateOwnedSchemasAndPreserveDataOnRepeatAsync`; real existing acceptance database migrated and reapplied successfully |
| URL filter contract, numbered links, empty/errors, stale responses, five-item latest split and category order | `uses all URL filters, twenty-item pages and numbered links`; `clears results on invalid query and offers a retry`; `shows the empty result without a pagination bar`; `ignores a slow older response after a newer query`; `renders five latest items and keeps category block order` |
| Scroll interruption and event/frame cleanup; carousel single/multiple controls; failed-image reuse and author credit | `interrupts category scrolling on wheel, touch and keys and cancels it on destruction`; `hides arrows for a single article and exposes one active slide`; `keeps image failures scoped to their media ID when slide elements are reused` |
| Generated public-request credential/cache isolation | `omits credentials and disables transfer/browser caching for generated public requests` |
| Editorial category conflict input retention and versioned feature toggles | `retains category order input when a stale homepage save fails`; `sends the article version when adding and removing editorial selections` |
