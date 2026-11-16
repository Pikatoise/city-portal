# News Drafting and Taxonomy

## Delivered Boundary

Issue #10 implements the production author workspace at `/account/articles` and editorial category/tag management at `/staff/taxonomy`. Authors create, reopen, save, preview, and delete incomplete drafts; choose own Ready uploads or approved shared templates; and edit structured body, sources, contextual image text, taxonomy, and SEO fields. The five sections follow the accepted Text/Images/Parameters/Preview/History layout. The editor contains no simulated production review/publication actions.

Submission, assignment/decisions, public revisions, withdrawal/republication, public SSR/search, and News notification emission remain #11-#13. Final owner acceptance remains at the complete Stage 2 result.

## Persistence and Permissions

`CityPortal.News` owns the `news` schema, `NewsDbContext`, and explicit migrations. API/Worker readiness requires the News migration; normal startup never migrates. Upgrade existing installations using the explicit migration command in DEVELOPMENT.md.

An article owns author identity, optimistic version, publication state, working/public revision pointers, timestamps, and retained deletion. Its working revision owns canonical content, safe preview HTML, and plain text. Draft saves mutate one working revision without snapshotting each keystroke. Significant creation/deletion records retain actor/time. Deletion preserves saved text and references while removing the draft from author reads. Never-submitted deleted drafts have no staff-access operation; submitted snapshot audiences belong to #11.

Author operations require confirmed Email, a current Author role, and ownership. Administrator/Editor alone do not bypass draft privacy. Taxonomy writes require Editor or Administrator. Identity's authorized transaction scope serializes role changes before News resource locks and rechecks session/roles after waiting. Stale writes return HTTP 409. Media reference validation joins the same connection/transaction, so failure cannot partially save article fields.

Taxonomy names are trimmed/NFC-normalized, limited to 100 Unicode scalar values, and unique by kind using invariant case normalization, including archived names. Existing archived associations survive saves; new archived associations are rejected. Category/tag namespaces are separate. This slice adds create, rename, and archive, without taxonomy deletion/restoration.

Taxonomy changes retain actor, time, action, name, archival state, and version in the owning module. Audit records commit with the change; rejected/stale requests create no audit entry. The additional audit migration was verified on 2026-11-16.

## Version 1 Document Contract

Revision content has `schemaVersion: 1` and a ProseMirror JSON `body`; title/summary, category/tag IDs, cover/gallery placements, attribution flag/sources, and SEO overrides are separate revision fields. Images use Media IDs and contextual alternative text/captions, without object-store URLs. The browser-only editor uses pinned Tiptap 3.34.0 packages.

Supported nodes: doc, paragraph, heading (H2/H3), text, bulletList, orderedList, listItem, blockquote, hardBreak, and mediaFigure. Marks: bold, italic, and absolute HTTP/HTTPS link. Ordered-list start is retained within 1-1,000,000. Canonical link storage discards known Tiptap presentation metadata; server HTML escapes attributes and supplies `noopener noreferrer`. Pasted rich content becomes text; dropped/pasted image bytes are not stored as article HTML.

The independent server allowlist enforces structure, properties, attributes, safe URLs, normalization, and field bounds. It renders from structured content rather than trusting HTML. Limits are 2 MiB body JSON, 2,000 blocks, 20,000 nodes, 24 node levels, and a 4 MiB complete save request. Body text is limited to 100,000 normalized scalars; repeated placements count once toward 20 distinct images. The depth bound keeps canonical serialization within runtime limits.

Incomplete drafts may omit submission fields/alternative text, while supplied values retain maximum/security checks. `ArticleDocuments.Validate(..., requireComplete: true)` implements submission presence rules, including sources and every image's alternative text; the actual submission transition remains #11.

## Save and Failure Behavior

Autosave runs after five idle seconds, with one active request. Edits arriving during a save stay dirty and subsequently use the returned version. Manual Save shares that boundary. Network/validation failures preserve dirty fields and do not report success. HTTP 409 stops autosave, preserves a complete local JSON copy, and offers copying before server reload. Edits arriving during reload are also preserved rather than overwritten.

The image picker uses real paginated own/template lists, upload, and processing state. Gated preview responses show unavailable images as placeholders. Draft references remain private; deletion makes them historical-only while preserving storage retention. Author/staff routes are browser-rendered with no-store responses.

## Verification: 2026-11-15–16

Release backend: 127 passed, 0 failed, 0 skipped; TRX counters were checked. Frontend: 63 passed. Production Angular/SSR build, solution formatting, generated-client consistency, explicit/repeat migrations, and independent standards/spec reviews passed. Reviews found and resolved ordered-list preview numbering and an in-flight conflict-reload race.

After the restart, the complete backend suite passed again with taxonomy audit enabled. Its migration applied to the isolated acceptance database and a second invocation applied nothing. The frontend build succeeded with an author-workspace CSS budget warning of one byte above 4 kB. Container builds and browser checks preceded the final taxonomy audit addition; the final audit code was verified by the backend suite and explicit migrations.

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
