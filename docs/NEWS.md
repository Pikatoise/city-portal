# News Drafting, Review, and First Publication

## Delivered Boundary

Issue #10 implements the production author workspace at `/account/articles` and editorial category/tag management at `/staff/taxonomy`. Authors create, reopen, save, preview, and delete incomplete drafts; choose own Ready uploads or approved shared templates; and edit structured body, sources, contextual image text, taxonomy, and SEO fields. The five sections follow the accepted Text/Images/Parameters/Preview/History layout. Review actions now use production API operations rather than simulated state.

Issue #11 adds production submission, assignment/decisions, first publication, history, and News notifications. Published corrections, withdrawal/republication, public article delivery, and SSR/search remain #12-#13. Final owner acceptance remains at the complete Stage 2 result.

## Persistence and Permissions

`CityPortal.News` owns the `news` schema, `NewsDbContext`, and explicit migrations. API/Worker readiness requires the News migration; normal startup never migrates. Upgrade existing installations using the explicit migration command in DEVELOPMENT.md.

An article owns author identity, optimistic version, publication state, working/public/submitted revision pointers, current reviewer, first-publication time, timestamps, and retained deletion. Its working revision owns canonical content, safe preview HTML, and plain text. Draft saves mutate one working revision without snapshotting each keystroke. Significant records retain actor/time. Deletion preserves saved text and references while removing the draft from author reads. Never-submitted deleted drafts have no staff-access operation; submitted deletion retains assigned-reviewer/administrator access under current roles.

Author operations require confirmed Email, a current Author role, and ownership. Administrator/Editor alone do not bypass draft privacy. Taxonomy writes require Editor or Administrator. Identity's authorized transaction scope serializes role changes before News resource locks and rechecks session/roles after waiting. Stale writes return HTTP 409. Media reference validation joins the same connection/transaction, so failure cannot partially save article fields.

Taxonomy names are trimmed/NFC-normalized, limited to 100 Unicode scalar values, and unique by kind using invariant case normalization, including archived names. Existing archived associations survive saves; new archived associations are rejected. Category/tag namespaces are separate. This slice adds create, rename, and archive, without taxonomy deletion/restoration.

Taxonomy changes retain actor, time, action, name, archival state, and version in the owning module. Audit records commit with the change; rejected/stale requests create no audit entry. The additional audit migration was verified on 2026-11-16.

## Version 1 Document Contract

Revision content has `schemaVersion: 1` and a ProseMirror JSON `body`; title/summary, category/tag IDs, cover/gallery placements, attribution flag/sources, and SEO overrides are separate revision fields. Images use Media IDs and contextual alternative text/captions, without object-store URLs. The browser-only editor uses pinned Tiptap 3.34.0 packages.

Supported nodes: doc, paragraph, heading (H2/H3), text, bulletList, orderedList, listItem, blockquote, hardBreak, and mediaFigure. Marks: bold, italic, and absolute HTTP/HTTPS link. Ordered-list start is retained within 1-1,000,000. Canonical link storage discards known Tiptap presentation metadata; server HTML escapes attributes and supplies `noopener noreferrer`. Pasted rich content becomes text; dropped/pasted image bytes are not stored as article HTML.

The independent server allowlist enforces structure, properties, attributes, safe URLs, normalization, and field bounds. It renders from structured content rather than trusting HTML. Limits are 2 MiB body JSON, 2,000 blocks, 20,000 nodes, 24 node levels, and a 4 MiB complete save request. Body text is limited to 100,000 normalized scalars; repeated placements count once toward 20 distinct images. The depth bound keeps canonical serialization within runtime limits.

Incomplete drafts may omit submission fields/alternative text, while supplied values retain maximum/security checks. `ArticleDocuments.Validate(..., requireComplete: true)` implements submission presence rules, including sources and every image's alternative text. Submission and explicit approved Save both enforce these rules before changing state.

## Save and Failure Behavior

Autosave runs after five idle seconds, with one active request. Edits arriving during a save stay dirty and subsequently use the returned version. Manual Save shares that boundary. Network/validation failures preserve dirty fields and do not report success. HTTP 409 stops autosave, preserves a complete local JSON copy, and offers copying before server reload. Edits arriving during reload are also preserved rather than overwritten.

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

Approval is distinct from publication. Approved content has no autosave; explicit Save validates a complete replacement snapshot and immediately returns it to review. Publication records actor/time, first-publication timestamp, and the public revision pointer atomically with Media public references and notifications. This slice provides the publication transition, not a public news page. Ever-published articles cannot use unpublished deletion. Publication versus deletion has one winner.

Identity invokes `IAccountRoleChangeObserver` within its existing role-change transaction and serialization lock. News clears affected reviewer assignments, updates their versions/history, and removes retained Media access before the role change commits. Keeping Administrator preserves effective editorial assignments. Notification persistence failure rolls back both role changes and assignment release. Generic delivery bodies contain no title, text, or reason; feedback remains in permission-gated News history. Existing notification preferences, transactional Outbox, retry/expiry, and receipt deduplication apply unchanged.

### Review Verification: 2026-11-16

Release backend regression: 134 passed, zero failed/skipped. Frontend regression: 70 passed. The complete backend run includes the new review queue and all seven review integration scenarios below. Real HTTPS browser checks exercised submission, claim, return, private unsent correction, required resubmission notes, approval, explicit approved Save returning to review, a second approval, and first publication; a final API read confirmed Published. Reviewed 390px/1440px layouts had no horizontal overflow. A navigation before Save completion triggered the intended unsaved-changes warning; the run resumed after the dialog. Initial local SSR API-origin setup was corrected before acceptance. Existing favicon 404 responses were unrelated to the workflow. Live browser acceptance used an Author/Administrator account; separate-account isolation and role races were proved by integration tests.

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

The UI mode change uses Tiptap's non-emitting `setEditable` form: changing permissions must not create another edit or trigger autosave. The pre-existing serialized autosave/retry regressions detected and verify this interaction. Final UI regression is 70 passed after adding change-description retry coverage. Final Release rebuild had zero warnings/errors; after the last audit clarification, all 11 News integration tests passed again. Final formatting, generated-client consistency, production Angular build, and explicit repeat migration passed. Public discovery and published-change acceptance remain outstanding; Stage 2 is open.

The isolated live Worker delivered article.returned, review.resubmitted, article.approved, and article.published account notices and Email. Each observed account-notice ID matched a Sent delivery in the administration API; SMTP delivery was verified against Mailpit. These checks reuse the existing notification foundation and do not claim another SMTP outage/replay run for this slice.

## Delivery Checkpoint: 2026-11-16

Review and first publication are captured in pushed commit 0708b94 on prototype/stage-2-news-review, following the pushed drafting/CSS checkpoint b38ed07. The owner requested this progress checkpoint and push. Current completion evidence is recorded above; #11 remains In Progress for integration/final review. Published changes and withdrawal (#12), public delivery (#13), and final Stage 2 acceptance (#14) remain outstanding.


## Issue #12: Initial Server Slice (2026-11-16)

Implementation is in progress; this checkpoint delivers the initial server slice. The versioned review-action route now accepts `propose` (owning Author only) and `withdraw` (Editor/Administrator, required reason). Proposals clone the current publication into a separate private draft. Review and approved explicit Save can operate on replacements; replacement publication preserves the first-publication timestamp. Article-row locking and expected versions serialize these transitions.

Withdrawal retains the current published pointer for history, switches visibility to Withdrawn, and clones existing working content into an author-only draft with review/approval reset. Public Media references are disabled in the same transaction; other current publications may still grant access. The withdrawal event queues the existing generic author notification atomically. A non-owner editor receives the preceding published snapshot in the action response, never the private proposal. Withdrawal history references the published snapshot so the reason remains visible without disclosing unsent content.

This is an initial API slice, not completed issue #12. Direct editorial corrections/supersession, URL aliases and sanitized withdrawn delivery, production UI integration, generated-contract verification, and the full concurrency/rollback/browser matrix remain outstanding. Public SSR/discovery remains #13. No owner acceptance is claimed.


Initial-slice verification: all 14 News integration tests passed in Release (TRX: 14 passed, zero failed/skipped); solution formatting and `git diff --check` passed. This is scoped verification, not a full backend/frontend regression or live browser acceptance.

| Requirement | Evidence |
| --- | --- |
| Separate proposals, single active proposal, permissions, stale writes, replacement publication and stable first-publication time | `Published_ProposalKeepsPublicRevisionAndReplacementKeepsFirstPublicationAsync` |
| Reason/role enforcement, withdrawal Media denial, reset approval, author-only correction and reviewed republication | `Published_WithdrawalResetsReviewClosesPublicMediaAndRequiresApprovalAsync` |
| Another editor cannot read unsent proposal text through withdrawal response/history; owner retains it | `Published_WithdrawalByAnotherEditorDoesNotExposePrivateProposalAsync` |
