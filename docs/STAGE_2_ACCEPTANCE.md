# Stage 2 Acceptance Record

## Status

Issue #14 is in progress on `prototype/stage-2-news-review`, from baseline
`28c050d`. Technical verification completed on 2026-11-17.
Owner review and Stage 2 acceptance remain outstanding. No Stage 3 specification,
public deployment or remote CI result is implied.

## Acceptance evidence map

The rows follow the acceptance criteria in `stages/STAGE_2.md`. Named backend
tests are in `tests/CityPortal.IntegrationTests/`; the complete run must pass
before this map is treated as current execution evidence. Existing detailed
frontend mappings remain in `NEWS.md` and `MEDIA.md`.

| Criteria | Concrete evidence |
| --- | --- |
| Draft, submit, return, resubmit, approve, publish; rejection and history | `Review_ReturnAndResubmit_PreserveSnapshotAndPrivateUnsentChangesAsync`; `Review_RecallFinalRejectionAndRetainedDeletion_EnforceTransitionsAsync`; fresh split-role browser workflow. |
| Durable incomplete saves, conflicts and single review claim | `Drafts_IncompleteSaveReopenAndCompetingVersions_PreserveOneCommittedRevisionAsync`; `Review_AssignmentRacesTakeoverReleaseAndRoleLoss_EnforceCurrentAccessAsync`. |
| Never-published deletion, Author removal and review release | `Drafts_PrivateOwnersAndRoleRemoval_DenyOtherAuthorsAndStaffAsync`; `Review_RecallFinalRejectionAndRetainedDeletion_EnforceTransitionsAsync`; assignment-race test above. |
| Private unsent changes, recall routing, approved Save and disabled autosave | `Review_ReturnAndResubmit_PreserveSnapshotAndPrivateUnsentChangesAsync`; `Review_ApprovedSaveInvalidatesApprovalAndPublicationIsSeparateAsync`; author-workspace frontend regressions in `NEWS.md`. |
| Current permissions, versions and state transitions | `Review_RoleRemovalVersusApproval_SerializesAuthorizationAndAssignmentAsync`; `Published_CorrectionVersusEditorialRoleRevocation_RechecksCurrentPermissionAsync`; `Published_CorrectionValidationPermissionsAndWithdrawnState_DoNotMutateAsync`. |
| Effective Administrator editorial permissions and draft privacy | `Drafts_PrivateOwnersAndRoleRemoval_DenyOtherAuthorsAndStaffAsync`; `Published_DirectCorrectionSupersedesWorkingTextAndPreservesCreditAsync`; split-role browser reviewer has Administrator without Editor. |
| Single working version and transactional reviewer-role loss | `Review_AssignmentRacesTakeoverReleaseAndRoleLoss_EnforceCurrentAccessAsync`; `Review_RoleRemovalVersusApproval_SerializesAuthorizationAndAssignmentAsync`; published-revision concurrency scenarios. |
| Public revision isolation, direct corrections, retained supersession and notices | `Published_ProposalKeepsPublicRevisionAndReplacementKeepsFirstPublicationAsync`; `Published_DirectCorrectionSupersedesWorkingTextAndPreservesCreditAsync` for Draft/InReview/Approved. |
| Retained deletion and current history audience after transfer/release | `Review_RecallFinalRejectionAndRetainedDeletion_EnforceTransitionsAsync`; `Review_AssignmentRacesTakeoverReleaseAndRoleLoss_EnforceCurrentAccessAsync`. |
| Withdrawal across discovery, URLs and media; author-only correction/republication | `Published_WithdrawalResetsReviewClosesPublicMediaAndRequiresApprovalAsync`; `Published_PublicReadsAliasesAndWithdrawal_ExposeOnlyCurrentPublicContentAsync`; `Published_FormerAuthorWithEditorRoleCannotReadPrivateProposalOnWithdrawalAsync`; live alias/withdrawal checks. |
| Upload validation, ownership and cleanup safety | `Upload_PrivateFilesAndRevokedAuthor_EnforcesAccessAsync`; `Cleanup_ConcurrentAttachment_WaitsAndPreservesTheWinningReferenceAsync`; `Processing_StorageOutage_PreservesPayloadAndRetriesAfterRestartAsync`. |
| Orientation, stripped metadata, allowed reuse and submitted/public media audiences | `Process_ExifOrientation_RotatesAndStripsSourceMetadataAsync`; `Process_ValidFormats_EmitsBoundedWebpWithoutMetadataAsync`; `References_PublicationWithdrawalAndBlock_EnforceCurrentVisibilityAsync`; `Review_MediaAudienceChangesAndPublicationDeletionRace_AreAtomicAsync`. |
| Immutable approved templates, archive/block independence, unblock and placeholders | `Templates_ReviewArchiveBlockAndRemoval_PreserveIndependentStatesAsync`; `Published_SharedImageReferencesAndTemplateBlocks_EnforceCurrentVisibilityAsync`; public-article failed-image frontend regressions. |
| Removal-request archive, restoration, seven-day cleanup and attachment race | `Templates_ReviewArchiveBlockAndRemoval_PreserveIndependentStatesAsync`; `Cleanup_RetainedReferencesAndUnusedUploads_KeepOnlyProtectedObjectsAsync`; `Cleanup_ConcurrentAttachment_WaitsAndPreservesTheWinningReferenceAsync`. |
| Taxonomy uniqueness and inherited archived associations | `Taxonomy_UniqueArchivedNamesAndExistingAssociations_EnforcePermissionsAndVersionsAsync`. |
| Text/tag/image boundaries, format/MIME/animation and distinct-image counting | `NewsDocumentTests`, `MediaBoundaryTests`, `MediaProcessorTests`; exact case mapping in `NEWS.md` and `MEDIA.md`. |
| Source attribution, contextual alternative text and one flat category | `NewsDocumentTests`; production drafting submission and preview regressions. |
| SSR, metadata, aliases and publication-safe search/filtering | `Published_PublicReadsAliasesAndWithdrawal_ExposeOnlyCurrentPublicContentAsync`; `NewsDiscoveryTests`; fresh HTTP/HTTPS and browser verification. |
| Current author credit, revision-owned SEO and featured limits/order | `Discovery_RussianSearchFiltersAndReplacement_UseOnlyCurrentPublicationAsync`; `Published_PublicReadsAliasesAndWithdrawal_ExposeOnlyCurrentPublicContentAsync`; `Curation_ConcurrentFeaturedLimitAndWithdrawalPreserveVisibilityAsync`. |
| Homepage blocks, filters, deterministic paging, Russian ranking and related results | `NewsDiscoveryTests`, `NewsCurationTests`; discovery frontend regressions; capacity fixture and browser layouts. |
| Atomic decisions/notifications, outages, restart and duplicate delivery | `Review_NotificationFailure_RollsBackDecisionAndRoleReleaseAsync`; `Published_NotificationFailureRollsBackPublicTextHistoryAndMediaAsync`; `TemplateNotifications_QueueFailure_RollsBackDecisionAndHistoryAsync`; `CommittedOutbox_WhenBrokerAndWorkerRecover_DeliversOnceAndSurvivesRestartAsync`. |
| Independent channel switches and user-owned notification read/delete | `Notifications_OwnershipCsrfAndPreferences_KeepChannelsIndependentAsync`; `WorkflowMail_DisabledAfterQueue_SkipsSmtpAndPreservesAccountNoticeAsync`. |
| Affected-editor/current-author fanout, privacy and 30-day notification expiry | `TemplateNotifications_DecisionsAndAffectedCurrentAuthors_QueueOnceWithoutPrivateContentAsync`; `Notifications_RollbackDuplicateAndRetention_PreserveDurabilityBoundariesAsync`; `WorkflowMail_SevenDayExpiryAndThirtyDayRetention_RejectReplayAndRemoveReadAndUnreadNoticesAsync`. |
| Migrations, generated contracts, builds/formatting, real infrastructure and backup/restore | `ModuleMigrations_OnEmptyDatabase_CreateOwnedSchemasAndPreserveDataOnRepeatAsync`; `DiscoveryMigration_ExistingPublishedSearchData_PreservesContentAndUsesMainStorageAsync`; `Recovery_DatabaseObjectsAndKeys_PreserveNewsPrivacyHistoryAndPendingDeliveryAsync`; final command record below. |
| Owner reviews workflows, data flow and a failure scenario | Pending owner review; review script below. |

## Combined recovery

`Recovery_DatabaseObjectsAndKeys_PreserveNewsPrivacyHistoryAndPendingDeliveryAsync`
uses the production operations to publish an article, retain an independent
private proposal and private image, and leave an image in Processing and a
publication notification pending. It persists encrypted SMTP configuration,
exports PostgreSQL with `pg_dump --format custom`, restores it with `pg_restore`
into a new database, copies objects into a separate private bucket and copies
the Data Protection key files into a separate directory. The source API stops.

The restored application repeats explicit module/messaging migrations. It proves
public content and all four public processed variants survive byte-for-byte,
private proposal/media/history remain restricted, retained publication history
is readable by the author, pending image processing completes and an independent
worker host decrypts and sends the pending publication Email through real SMTP.
The persisted SMTP fixture password also round-trips through restored keys.
Two-object listing pages force the object-copy loop to traverse multiple pages.

PostgreSQL and S3 resources are isolated databases/buckets on the local test
services. This is logical recovery verification, not a second-machine disaster
recovery or a production RPO/RTO measurement. SMTP acknowledgment followed by a
database failure can still produce a duplicate external Email.

## Measured query changes

The initial capacity check exposed slow broad search and taxonomy reads. The
`DiscoveryReadPerformance` News migration adds a partial index on the current
public `PublishedRevisionId` and selects `MAIN` storage for the generated
search vector. Recomputing existing generated values applies the physical
storage choice to existing revisions without changing text or article versions.
The upgrade regression verifies preserved content/search, the physical policy,
the index and repeat application.

An isolated comparison measured the representative tag-page query at 62.428 ms
before the index and 4.078 ms after it. A representative broad ranked search
measured 471.552 ms before the storage change and 20.571 ms after it. These are
single-query diagnostics, separate from HTTP percentiles.

`SET STORAGE` controls future row storage and does not itself rewrite existing
values; the migration therefore recomputes generated vectors. See the
[PostgreSQL 18 ALTER TABLE reference](https://www.postgresql.org/docs/18/sql-altertable.html).
Apply this migration with application writers stopped: it updates all existing
revisions and takes normal PostgreSQL DDL/write locks. Allow time and disk space
for that work and take the consistent backup first. A migration failure rolls
back; a later explicit run may retry it. No public cache was introduced.

## Final command and capacity results

The Release solution build passed with zero warnings/errors. The complete run
`dotnet test CityPortal.slnx -c Release --no-build --logger "trx;LogFileName=issue14-full.trx" --results-directory .local/issue14/results`
completed with **Passed: 165, Failed: 0, Skipped: 0**. TRX counters and all 165
individual outcomes were parsed and matched. This includes real PostgreSQL,
RabbitMQ, RustFS and SMTP, the new combined recovery and upgrade regressions,
broker/SMTP/storage outage recovery, duplicates, rollback and permission races.
Focused recovery and upgrade runs each passed one test separately; those are
included in the complete count, not additional tests.

Production Angular/SSR build, all 90 frontend tests in 14 files, frontend
Prettier, generated-client consistency, solution formatting verification and
runtime OpenAPI comparison passed. Explicit migrations on the capacity fixture
and repeat application passed. HTTPS system metadata returned 200 with the
exported Caddy CA and hostname verified by Windows curl; browser certificate
exceptions are confined to the isolated automation contexts.

The final capacity run reused the same isolated, migrated 10,000-article fixture
after warm-up. Each route received 24 warm-up requests at eight readers, then
80 measured requests at eight readers. All 800 measured requests and 240 warm-up
requests completed without HTTP errors. Public API and SSR responses carried
`Cache-Control: no-store`.

| Route | Warm p50 ms | Warm p95 ms | Maximum measured ms |
| --- | ---: | ---: | ---: |
| Feed | 29.05 | 53.94 | 56.96 |
| Page 500 | 59.82 | 71.35 | 81.69 |
| Broad Russian search | 61.20 | 78.49 | 86.68 |
| Combined category/tag | 17.18 | 19.66 | 29.59 |
| Homepage API | 75.94 | 81.94 | 99.62 |
| Public tags, first 100 | 26.79 | 36.50 | 46.74 |
| Related articles | 28.73 | 35.01 | 47.07 |
| Homepage SSR | 256.37 | 302.44 | 2305.11 |
| List SSR | 637.09 | 875.91 | 2957.98 |
| Article SSR | 571.43 | 743.10 | 785.71 |

The local targets of API p95 <= 500 ms and SSR p95 <= 1,000 ms passed in this
final run. Latency tails remain: warm-up reached 2,791 ms and measured SSR
requests reached 2,958 ms. An earlier fresh post-migration run also exceeded
the tag p95 target (1,195 ms); its repeat on the same fixture measured 36.50 ms.
These are recorded limitations, not a production latency guarantee or proof of
steady performance over a long run. The pre-change broad-search p95 was 1,906 ms
and list/article SSR p95 was 4,624/4,241 ms.

Environment: Windows 11 build 28000, 12 logical CPUs; Docker Desktop 29.9.3 with
12 CPUs and 7,974,043,648 bytes of VM memory; PostgreSQL 18.6 Alpine x86-64;
.NET SDK 10.0.403; Node 26.8.1. Host API/SSR processes used Release/production
builds and loopback HTTP for measurements. HTTPS and browser behavior were
verified separately. Informational SQL logging remained enabled.

After measurement, API/SSR working sets were approximately 231/281 MiB,
compared with 178/92 MiB before measurement. These are process snapshots,
not peak or leak measurements. Representative final EXPLAIN ANALYZE times
were feed 0.390 ms, search 18.395 ms, image visibility 0.031 ms and tag page
3.221 ms. The helper saves plans with buffers and full environment/latency/memory
JSON in ignored `.local/issue14/capacity_<uuid>/` artifacts.

The fresh HTTPS browser run passed with separate Author-only and
Administrator/Moderator accounts; the reviewer had no assigned Editor role.
UI actions covered five-second autosave, submission, claim, return, private
unsent correction, resubmission, approval, first publication, a private published
proposal, direct correction/supersession, withdrawal and reviewed republication.
API/history reads independently checked each committed result.

A real Worker processed the uploaded image and delivered both publication
notices through SMTP. Template approval/block/unblock checks proved anonymous
image denial and the browser placeholder. Real old URLs redirected directly
with 307. After withdrawal, old/current SSR URLs returned no-store 404 without
content, private reason or article metadata; feed/homepage/related responses
excluded the article and anonymous image reads closed. Republication retained
the first-publication timestamp.

Homepage/list/article layouts at 1440px and 390px had no horizontal overflow;
desktop homepage and mobile article screenshots were inspected. The completed
run recorded zero browser page errors. Earlier automation attempts used an
overlength fixture password and wrong review URL; those local helper errors
were corrected. HTTPS proxy trust was explicitly configured for the loopback
proxy; authentication and CSRF enforcement were preserved. The final run is
the execution evidence. Fixtures, secrets, screenshots and logs remain ignored.

Capacity reproduction after building Release API and production Angular SSR:

```powershell
python scripts/check-news-capacity.py
```

The helper requires the local Compose infrastructure and ignored `.env`. Each
fresh run creates a unique isolated database and broker vhost. It leaves those
fixtures for inspection and stops its API/SSR processes on exit. `--serve`
keeps them available for local review; interrupt it to stop. `--reuse` accepts
only a previously created `.local/issue14/capacity_<uuid>` directory.

The baseline contains 10,000 public articles with roughly 3,800 body characters,
100,000 image-reference rows, 100 categories and 1,000 tags. Reference rows
exercise metadata/query volume; processed-object transfer throughput is outside
this measurement and is covered functionally by recovery/live Media checks.
No maximum-length-content or multi-hour soak claim is made.

## Owner review script

1. Follow an author draft through return, private correction, resubmission,
   approval and publication. Inspect the separate current public revision.
2. Open a proposed published change and verify public text stays unchanged.
   Inspect direct editorial correction, retained supersession and notifications.
3. Withdraw an article and visit both its current and historical URLs. Verify
   its images are no longer public unless another publication still uses them.
   Review corrected republication and retained history.
4. Inspect template moderation, archive/block independence and notification
   preferences. Distinguish a content decision from Email delivery status.
5. Explain the transaction path: Identity rechecks current permissions, News
   changes its revision/history, Media changes reference audiences and
   Notifications writes account notices and Email Outbox work in the same
   PostgreSQL transaction. Worker/broker/SMTP recovery happens after commit.
6. Review the failure example: a broker/SMTP outage preserves the decision and
   pending delivery; a failure inside the database transaction rolls back the
   decision, history and visibility changes together. Inspect recovery evidence.

Record the owner's outcome and remaining limitations in `stages/STAGE_2.md`
before closing #14 or the stage.
