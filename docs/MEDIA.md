# Media Implementation and Operations

## Ownership and Tools

Media owns processed images, private S3 objects, revision references, shared-template proposals, moderation history, and removal requests. Product rules remain in [Stage 2](stages/STAGE_2.md). Browser-rendered libraries are at `/account/media` and `/staff/media`.

The pinned implementation uses SkiaSharp/Linux native assets 4.156.0 ([MIT license](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md)), AWSSDK.S3 4.1.0, and RustFS 1.0.0 ([Apache 2.0](https://docs.rustfs.com/en/installation)). The local RustFS image is pinned to digest `sha256:8cc9801755448b71a786705ce76692c77e14936cccd87cf2fc31842e58f4d1ff`; compare the [official Compose example](https://github.com/rustfs/rustfs/blob/main/docker-compose.yml). Other S3-compatible deployments need their own compatibility checks.

## Processing and Recovery

1. Authenticate and recheck confirmed Author access. Enforce CSRF and persistent attempt limits before processing. Rejected attempts count. A process-local upload slot is acquired before model binding; competing work receives 429 with Retry-After. Multipart buffering stays in memory within an 11 MiB request bound.
2. Validate actual JPEG/PNG/static WebP, matching MIME, byte/pixel limits, and animation including animated single-frame containers. Correct orientation, preserve transparency, and re-encode without source metadata. Do not persist the original upload.
3. Atomically create a Processing image and four processed WebP payloads in PostgreSQL: master, 480, 1280, and 2560 maximum long edges without upscaling, quality 85. These processed payloads make object-store retry durable without a database/S3 distributed transaction.
4. The Worker writes deterministic server-generated keys, then atomically marks Ready and clears database payloads. Failed writes retain Processing/payloads and back off from 20 seconds to 15 minutes. Partial objects never become selectable. Missing payloads mark Failed.
5. Persistent Quartz reconciliation runs every ten seconds without concurrent execution. It joins the existing Worker scheduler registration; the legacy name `CityPortal.Email` is preserved for compatibility. Registering that named scheduler twice is prohibited.
6. Cleanup locks never-attached images at least seven days old using the same lock as reference attachment. Recheck retention, commit Deleting, delete objects, then remove metadata. Storage failures retain Deleting for retry. Any previous article/template attachment excludes the image from this cleanup, including retained history.

Streams and native buffers are disposed on success, failure, or cancellation. Decode concurrency is one per process; final-stage capacity checks still need native-memory measurements at the 40 MP ceiling.

## Authorization and Integration

Buckets/objects remain private; no public policies or presigned links are issued. Keep an externally provisioned bucket private. Every image response checks current visibility/block state, uses no-store/nosniff, and disables range/conditional delivery bypasses. Previously downloaded bytes cannot be revoked.

Template approval grants author preview/library access, not anonymous access. Public delivery requires a current published reference and no template block for the image. Staff cannot inspect unrelated private uploads; assigned editors see submitted-reference images only. Administrator does not bypass unsent-draft privacy. Template moderators can inspect proposal history and its retained images. Other authors receive neither moderation history nor private decision reasons in library DTOs.

Approval, archival, and blocking are independent. Approved files are immutable. Rejection/resubmission can reference a different owned Ready file. Archival prevents new selection while retaining existing references. Block/unblock require reasons. Author removal requests are idempotent while open and resolve only by archival. Decisions retain actor, timestamp, image, name, state/version, and reason. Removal resolution checks the request version.

Writes use Identity's authorized transaction scope: administration-role lock, account locks, session/role recheck after waiting, then Media resource locks. News calls `MediaReferences.ReplaceAsync` on its own authorized connection/transaction, preserving historical revision references. The operation checks the 20-image limit, Ready/selectable status, reviewer audience, and public visibility. For a new revision inheriting an archived template, News supplies the validated preceding revision ID from the same article. There is no HTTP endpoint for manufacturing publication references.

## Configuration and Startup

Set separate random `MEDIA_ACCESS_KEY` and `MEDIA_SECRET_KEY` in ignored `.env`. Compose supplies API/Worker `Media:Endpoint`, `AccessKey`, `SecretKey`, and `Bucket`; RustFS uses persistent `media-data` and a loopback-only port 9000. HTTPS is required unless trusted local HTTP is explicitly allowed. Do not reuse development credentials in a deployment.

```powershell
docker compose up -d --wait postgres rabbitmq mailpit media-store
docker compose build api worker frontend
docker compose run --rm --no-deps api --migrate
docker compose up -d api worker frontend proxy
```

Migration initializes configured storage and applies owned migrations explicitly. Normal startup never migrates. Configured Media adds database/schema and S3 readiness checks. A transitional Stage 1 installation without Media configuration retains account functionality; Media reports unconfigured storage.

## Backup and Restore

Stop API writes and the Worker for a consistent backup. Take a custom-format PostgreSQL dump and export all processed objects with authenticated S3 operations. Protect configuration, credentials, and Data Protection keys separately. Do not treat a live RustFS filesystem copy as a portable object export.

Restore into an isolated database/bucket, configure matching keys/settings, apply explicit migrations, then start the Worker. Processing payloads reconcile partial writes and Deleting rows resume cleanup. Verify public/private reads, retained decisions, and pending work before using a restored deployment. Full-stage notification/key-recovery acceptance remains separate.

`Restore_MetadataAndProcessedObjects_PreservesPrivateReadsAndPendingWorkAsync` performs actual `pg_dump`/`pg_restore` and S3 copy into isolated resources, checking byte-identical public output, private denial/authenticated access, and continuation of pending processing.

## Verification and Limits

On 2026-11-15 local time, the full backend regression run passed 102 tests and the frontend run passed 51 tests. The focused Media run passed 23 tests and native Linux processing passed 14; final targeted template/privacy and upload guard checks passed 1 and 2 cases respectively. Production Angular build, generated-client consistency, explicit/repeat migrations, and API/Worker/frontend container builds passed. A real HTTPS browser run used an isolated database, S3 bucket, broker virtual host, and test account: upload, Worker completion, proposal, approval, shared library, removal request, archival, and history worked. Desktop 1440px and mobile 390px had no page overflow. This is not final owner acceptance of Stage 2.

| Requirement | Evidence |
| --- | --- |
| Formats, variants, metadata, transparency | `Process_ValidFormats_EmitsBoundedWebpWithoutMetadataAsync`, `Process_TransparentPng_PreservesAlphaAsync` |
| Orientation, animation, size limits | `Process_ExifOrientation_RotatesAndStripsSourceMetadataAsync`, `Process_RealAnimatedPngAndWebp_RejectsAnimationAsync`, `Process_OverFortyMegapixels_RejectsBeforePixelAllocationAsync`, `Process_ExactlyTenMebibytes_AcceptsValidJpegWithTrailingPaddingAsync`, `Process_MismatchedMimeAndOversizedInput_RejectsBeforeStorageAsync` |
| Privacy, role removal, direct object denial | `Upload_PrivateFilesAndRevokedAuthor_EnforcesAccessAsync` |
| CSRF and rejected-attempt throttling | `Upload_RejectedAttemptsAndMissingCsrf_EnforcePersistentLimitsAsync` |
| Template decisions, independent flags, removal and history | `Templates_ReviewArchiveBlockAndRemoval_PreserveIndependentStatesAsync`, `Moderation_StaleVersionAndLostRole_DoNotCommitDecisionsAsync` |
| Publication, withdrawal, blocking, reviewer access | `References_PublicationWithdrawalAndBlock_EnforceCurrentVisibilityAsync` |
| Durable retry and retention races | `Processing_StorageOutage_PreservesPayloadAndRetriesAfterRestartAsync`, `Cleanup_RetainedReferencesAndUnusedUploads_KeepOnlyProtectedObjectsAsync`, `Cleanup_ConcurrentAttachment_WaitsAndPreservesTheWinningReferenceAsync` |
| Database/object recovery | `Restore_MetadataAndProcessedObjects_PreservesPrivateReadsAndPendingWorkAsync` |
| Browser contracts | `sends the selected file with a fresh CSRF token as multipart data`, `sends the removal request version and never converts resolution into rejection`, `does not request private libraries for an unconfirmed author` |

Production drafts now call the Media transaction contract and use real images in the author editor; see [NEWS.md](NEWS.md). Publication/withdrawal/reviewer audiences and public blocked-image placeholders still require integration in #11-#13. The review/publication prototype remains isolated. Template notifications are implemented below; final stage acceptance remains outstanding.

## Workflow Notifications: 2026-11-15

Approval, rejection, archival (including mandatory removal resolution), block, and unblock queue notifications to the template submitter. Block/unblock also queue the corresponding image event once per distinct author with current image references, including private drafts. Historical-only references retain files and their audience without producing affected-author fanout. News must set `isCurrent: false` when a revision becomes retained history and maintain both current working and public references in its transaction. This is separate from public image visibility.

Media decision/history writes and Notifications receipts, account notices, Email delivery records, and Wolverine Outbox work share one PostgreSQL transaction. A notification write failure rolls back the decision. The dispatch marker permits recovery of events from the earlier Media slice; durable receipts prevent recreation after retries, user deletion, and retention cleanup. Older handoffs retain their original timestamp and do not extend delivery eligibility. Backfill uses references available at dispatch; new decisions capture their audience in the decision transaction.

Notifications owns independent account/Email switches for every approved Stage 2 event type, enabled by default. News event types are available for preferences; emitting News events remains part of the corresponding News workflows. Confirmation and recovery messages bypass these switches. Generic account/Email copy contains no article title/body, template name, or private decision reason; access to details remains governed by the owning API.

The account page `/account/notifications` provides bounded pagination, read/delete actions, and delivery settings. Every write requires CSRF. Reads and writes use the current account/session and cannot operate on another account's notices. Thirty-day expiry applies to reads immediately, whether read or unread; the existing daily persistent retention job removes expired rows. Deduplication receipts and owning Media history survive. Deleting a notice does not delete its independently queued Email.

Workflow Email uses the existing encrypted payload, SMTP configuration, Outbox/Inbox, attempt numbering, retry intervals, and replay audit. Its deadline is seven days from the originating event. Delivery/replay recheck both deadline and the current Email switch; replay never changes expiry. The retention job also marks expired unsent/failed workflow deliveries Skipped and clears protected payloads. Broker/provider outages do not undo committed decisions.

Latest verification: Release backend 112 passed, 0 failed, 0 skipped; frontend 56 passed. Production Angular build, solution formatting, generated client consistency, explicit/repeat migrations, and separate standards/spec reviews passed. Real HTTPS browser checks on isolated database/bucket/broker resources completed upload -> Worker Ready -> template submission/approval -> account notice and SMTP Sent, then read/delete and independently persisted channel switches. Desktop 1440px and mobile 390px had no horizontal overflow. The browser's only resource error was the existing missing favicon. This is local workflow verification, not final owner acceptance.

| Requirement | Evidence |
| --- | --- |
| Exact template events and private decision retention | `Templates_ReviewArchiveBlockAndRemoval_PreserveIndependentStatesAsync` |
| Distinct current-author fanout, historical exclusion, duplicate/backfill handling | `TemplateNotifications_DecisionsAndAffectedCurrentAuthors_QueueOnceWithoutPrivateContentAsync` |
| Decision/history rollback on notification-write failure | `TemplateNotifications_QueueFailure_RollsBackDecisionAndHistoryAsync` |
| Unicode name/reason bounds | `Templates_UnicodeLimits_NormalizeNamesAndCountScalarsAsync` |
| Independent preferences, ownership, CSRF and deletion deduplication | `Notifications_OwnershipCsrfAndPreferences_KeepChannelsIndependentAsync` |
| Queue rollback and exact thirty-day read boundary | `Notifications_RollbackDuplicateAndRetention_PreserveDurabilityBoundariesAsync` |
| Stable pagination and expired handoff deadlines | `Notifications_PaginationAndExpiredHandoff_UseEventTimeAndStableOrderingAsync` |
| SMTP outage, retry and replay with generic content | `WorkflowMail_OutageRetryAndReplay_DeliversGenericNoticeWithoutAnAuthenticationLinkAsync` |
| Seven-day replay/SMTP expiry and read/unread cleanup | `WorkflowMail_SevenDayExpiryAndThirtyDayRetention_RejectReplayAndRemoveReadAndUnreadNoticesAsync` |
| Email disabled after enqueue | `WorkflowMail_DisabledAfterQueue_SkipsSmtpAndPreservesAccountNoticeAsync` |
