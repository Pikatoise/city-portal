# Stage 4: Classifieds and Moderation

## Status and sources

Complete and owner-accepted on 2026-11-25. The account's My
Advertisements page supports private drafts, their photo carousel, and
submission and recall. The category and district administration screen and
API are implemented. Staff can review, return, reject, and approve pending
submissions. Approved listings have public detail pages. Staff can block an
individual listing or an account. Published edits, comparison review,
renewal, withdrawal and relisting are implemented. The public catalog and
its homepage entry point are implemented. Visitors can report public articles
and listings. Staff review them in separate queues.
Owners and staff can read moderation history. Listing and account decisions
queue the agreed account and Email notices. See the [owner review](#owner-acceptance).
[VALUE.md](../VALUE.md#classified-advertisements) owns product rules and
[CORE.md](../CORE.md#classifieds-and-moderation) owns architecture and
transaction requirements.
Specification decisions were tracked in [issue #15](https://github.com/Pikatoise/city-portal/issues/15)
in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3).

## Goal and scope

Residents can manage advertisements in their accounts. Visitors can browse
and report public listings. Staff can review listings and reports, block
listings, and restrict accounts within their permissions.

Deliver configurable categories, listing images, the public
catalog and detail pages, a homepage link to the catalog, owner and staff
workspaces, decision history, and notifications. Listing contacts are phone
and/or Email.

## Workflows and permissions

| Actor | Agreed behavior |
| --- | --- |
| Visitor | Browse available listings, search and filter by category, price and district, and submit a report without logging in. |
| Owner | With confirmed Email, create and save drafts, submit, edit, withdraw, delete and renew own listings subject to their current state. See listing status and review feedback in the account. |
| Moderator | Review other users' submitted versions and reports, block their listings with a reason, and block or unblock accounts whose only role is Regular User. Corrections belong to the listing owner. |
| Administrator | Review other users' submitted versions, block their listings with a reason, configure the catalog, and block or unblock accounts, including staff. Self-blocking and blocking the last active administrator are unavailable. |

- First publication requires approval. After first publication, valid owner
  edits take effect immediately except during a direct listing block. Each
  edited listing enters the staff queue once, comparing the first published
  version with the latest one. Staff can complete the review or block it.
- Approval publishes immediately. Return for corrections and rejection require
  a reason. The owner can recall a pending submission to resume editing.
- Publication lasts seven days. Edits and review preserve the expiry date.
  The owner can renew active or expired content for seven days from renewal,
  including during review of a live edit. Withdrawal hides the listing. The
  owner can relist it for seven days, including after an account unblock.
  Expiry leaves the listing in the owner's account. Deletion retains
  moderation decisions and releases unreferenced photos.
- A direct listing block requires a reason of up to 500 characters. It hides
  the listing and prevents renewal and relisting.
  The owner corrects and resubmits it for approval.
- Account blocking requires a reason and persists until staff remove it. The
  owner retains account access and sees the reason in their profile. Blocking
  restricts existing content to reading and disables new content and staff
  actions. The block and hiding all the account's listings
  commit together. A blocked owner cannot publish or renew through an owner
  or staff action. Unblocking leaves listings hidden until the owner acts.
  Direct listing blocks still apply.
- Reports leave listings visible until a moderator acts. The queue shows the
  total count per subject. Moderators and administrators open a subject to
  review all reports and previous decisions. They can dismiss the reports
  they saw or hide the subject with a reason. New reports stay pending.

### Draft form and starting catalog

The owner can save an incomplete draft. Submission requires a title,
description, category, district, and at least one contact (phone or Email).
Owners select an installation district managed by administrators. The initial
electronics and belongings, real estate, and vehicle categories require a
price. Price is optional in the other initial categories. Zero means free.

Start with a flat list of electronics and belongings, real estate, vehicles,
services, jobs, animals and lost pets, and miscellaneous listings. Administrators
configure categories and attributes. The first version uses the common listing
fields. Archived categories stay associated with existing advertisements and
drafts. Only active categories can be newly assigned.

### Listing photos

A listing revision can contain zero to five photos. The owner attaches photos
separately from the description and provides alternative text for each photo.
Use the Media pipeline's JPEG, PNG and static WebP support with the existing
10 MiB (10,485,760 bytes) and 40,000,000 pixel limits per source file. On the
detail page, the carousel goes immediately after the title, followed by the
description. Omit the carousel when there are no photos.

### Private catalog and draft API

The first API increment stores catalog entries and draft fields in Classifieds.
Apply the Classifieds migration through the API's existing `--migrate` command.
Use GUIDs for catalog entries. Names are unique per catalog, ignoring case and
surrounding whitespace. List entries in name order, 20 per page. The category
catalog includes its price requirement. The district list starts empty and an
administrator adds installation districts. Preserve referenced catalog entries.

| Routes | Contract |
| --- | --- |
| GET `/api/account/classifieds/catalog/{kind}` | Confirmed accounts list active `categories` or `districts`. |
| GET/POST `/api/admin/classifieds/catalog/{kind}` | Administrators list entries including archived ones, or create an entry. |
| PUT `/api/admin/classifieds/catalog/{kind}/{id}` | Administrators update a named entry using its current version. Categories support archival. |
| GET/POST `/api/account/classifieds/drafts` | The confirmed owner lists their drafts, newest update first, or creates an incomplete draft. |
| GET/PUT/DELETE `/api/account/classifieds/drafts/{id}` | The owner reads, replaces, or deletes a draft. Writes check the current version. Foreign IDs return 404. |

The browser-rendered `/staff/classifieds/catalog` page opens from the
administrator's profile and account management screen. Administrators with
confirmed Email can add and rename categories and districts, set the category's
price requirement, and archive or restore categories. Version conflicts preserve
the form so the administrator can reload and check the current entry.

The initial draft fields are title, plain-text description, category ID,
district ID, optional whole-number price, phone and Email. Bound fields at 200 title
characters, 10,000 description characters, 50 phone characters and 254 Email
characters. Store prices as PostgreSQL `bigint` values from 0 to 999,999,999,999.
The API rejects fractional prices. Draft writes accept null fields. Selecting an
archived category returns 400 unless the draft already refers to it. Serialize
catalog changes against draft saves through the existing administration lock.
Create returns 201, update returns 200, and draft deletion returns 204.

### Draft photo API

Confirmed owners stage photos through multipart POST
`/api/account/classifieds/uploads`. The response includes the image ID and
processing status. Create and update accept an ordered `photos` array of up to
five distinct image IDs with alternative text of up to 300 characters. Omitting
it on update preserves photos. An empty array removes them. An upload can attach
to one draft and must belong to that draft's owner. Photo changes share the
draft version and transaction.

The draft detail and write responses include attached photos in selection order
with processing status. GET `/api/account/classifieds/drafts/{id}/images/{imageId}`
streams a ready photo to its owner. Draft deletion and detachment release the
private references and queue the unused objects for cleanup. The existing
Media job removes unused staged uploads after seven days. Review and public
delivery extend the contract when those workflows are implemented.

The browser-rendered `/account/classifieds` page opens from the profile.
It lists the owner's drafts and supports creation, editing, deletion, submission
and recall.
The draft form places the photo carousel immediately below the title field,
followed by the description. It shows one photo at a time and supports buttons
for moving between photos. Ready images load from the owner-only endpoint.

### Owner submission and recall

The confirmed owner sends the last saved draft for review with a versioned
POST to `/api/account/classifieds/drafts/{id}/submit`. Submission checks the
required fields, the category's current price rule, supplied contact formats
and readiness of attached photos. Existing archived category associations
remain valid. Incomplete submissions return HTTP 400 with field errors.

A successful submission records its UTC time and returns the draft with
state `InReview` and an incremented version. The owner can view the saved
content and photos. Editing or deleting the draft requires recalling it first
with a versioned POST to `/api/account/classifieds/drafts/{id}/recall`. Recall
returns a never-published listing to `Draft` or a directly blocked listing
to `Blocked`. It preserves fields and attachments and increments the version. A stale version or invalid transition returns
HTTP 409. The shared account authorization lock serializes these actions
with concurrent owner writes and excludes catalog changes during validation.
First publication requires the later staff decision and account-blocking workflow.

### Staff review and owner feedback

Confirmed moderators and administrators can review pending submissions from
other accounts. The browser-rendered `/staff/classifieds/review` opens from
their profile. The queue shows 20 items per page in ascending submission time
with listing ID as a tie-breaker. Live-edit reviews use their queue entry time.
The preview includes the saved title,
description, contacts, category, district, price, and attached photos.

| Routes under `/api/staff/classifieds/review` | Contract |
| --- | --- |
| GET the collection | List pending submissions and published edits from other accounts. |
| GET `/{id}` and `/{id}/images/{imageId}` | Read the current review preview or a ready attached photo. Photo reads require the preview version in the query. Return 404 outside an accessible review. |
| POST `/{id}/return` or `/{id}/reject` | Accept the preview version and a reason of 1 to 500 characters after trimming. Require CSRF. Return 204 on success and 409 for a stale version or closed review. |

Return sets the owner state to `Returned` and allows editing, deletion, and
resubmission. Rejection sets `Rejected` and allows owner reads and deletion.
The owner sees the decision and reason in My Advertisements. Resubmission
opens a new review without deleting the prior decision record. Classifieds
stores the decision, reason, actor, UTC time, and submitted version together
with the draft state change. Staff decisions use the exclusive administration
lock to serialize with owner writes and recheck the current version.

### First publication and public detail

Staff approval publishes the submitted version and starts its seven-day
window. The public detail page at `/classifieds/{id}` displays the current
public phone number when present. The seller's Email remains private. Unavailable
listings return the standard HTTP 404 page. Public detail and photo endpoints
use the same visibility rule and exclude private drafts and blocked corrections.

| Routes | Contract |
| --- | --- |
| POST `/api/staff/classifieds/review/{id}/approve` | Approve a first submission or a directly blocked correction, or mark a published edit reviewed. Check the version. Publication returns 403 for a blocked owner. Stale and closed reviews return 409. Success returns 204. |
| GET `/api/classifieds/{id}` and `/{id}/images/{imageId}` | Anonymous reads of the visible revision and its ready photos. Use the same exact expiry and block checks on both routes. |
| GET `/api/staff/classifieds/published`, POST `/{id}/block` | List other accounts' published listings and hide a listing using its current version and a reason. |
| GET `/api/staff/account-blocks`, POST `/{id}/block` or `/{id}/unblock` | Search eligible accounts by name or Email and apply versioned block decisions. Blocking requires a reason of 1 to 500 characters after trimming. |

The browser-rendered `/staff/classifieds/moderation` page hosts the listing
and account block operations. The owner's profile displays the current account
block reason. The working draft and its photos remain readable to their owner.
Publication keeps a separate snapshot and Media photo references. Public
responses and SSR HTML use `Cache-Control: no-store`.

Staff approval and account blocking take the exclusive Identity administration
lock. Approval checks the owner's block state under that lock. On account
blocking, Identity records the decision and Classifieds hides the account's
publications on the same database connection and transaction. Unblocking
removes the Identity restriction and leaves the hidden publication flags in place.
Public photo access rechecks visibility after opening the object. A response
already admitted at that check may finish streaming.

### Published revisions and listing lifecycle

Saving a valid change to a previously published listing updates its public
snapshot atomically with the photos. It preserves the expiry date and existing
visibility restrictions. A directly blocked listing instead stays private
until the owner submits changed content and staff approve it. Approval clears
the direct block and preserves the other visibility flags and expiry date.

The existing staff review queue includes a single item per listing with
pending live edits. Further edits replace its latest version and retain its
queue position. The preview compares text, contacts, catalog entries, price,
and photos from the first publication and the latest version. An acknowledgement
records the review without changing what visitors see. A block records the
reason and hides the latest version atomically. Both actions check the
listing version. Staff previews and photos require another account's current
review and staff permissions.

| Routes | Contract |
| --- | --- |
| PUT `/api/account/classifieds/drafts/{id}` | Save by version. An edit that will change the public snapshot must meet the same field and ready-photo requirements as a submission. |
| POST `/api/account/classifieds/drafts/{id}/renew` | Start a new seven-day window at the current time. Clears owner withdrawal and residual account visibility flags. Directly blocked listings cannot renew. |
| POST `/api/account/classifieds/drafts/{id}/withdraw` | Hide a currently visible listing. |
| DELETE `/api/account/classifieds/drafts/{id}` | Remove the listing and its photo references, preserving moderation decisions. Recall a pending submission before deletion. |
| GET `/api/account/classifieds/uploads/{imageId}` | Read the owner's listing upload processing status before an attachment is saved. |
| GET `/api/staff/classifieds/review/{id}/images/{imageId}?version={version}&original=true` | Open an original comparison photo while the specified post-publication review remains current. |

Renewal and withdrawal return the current owner view with expiry and available
actions. Version and listing state conflicts return 409. Blocked accounts
cannot write. The first publication's photos are retained for comparison until
the listing is deleted. Media releases intermediate photos once they have no
remaining references.

### Public catalog and homepage entry point

The anonymous SSR page at `/classifieds` shows 20 current public listings
per page. Listings sort by first publication, newest first, then by ID.
Search uses PostgreSQL Russian full-text search over titles and descriptions,
weighting the title higher. Search ranks descend, with the default sort as
the tie-breaker. Edits and renewals preserve the first-publication order.
Category, district and inclusive price bounds combine with each other and
search. Listings without a price match only when neither price bound is set.
Category-specific attributes are deferred.

| Routes | Contract |
| --- | --- |
| GET `/api/classifieds` | Anonymous page with `query`, `categoryId`, `districtId`, `minPrice`, `maxPrice` and `page`. Results include listing ID, title, category, district and price. |
| GET `/api/classifieds/filters` | Anonymous filter options. Include active entries and archived entries used by currently visible listings. |

The homepage provides one classifieds navigation button. The catalog links
to public details and the existing owner creation workflow. Catalog and
detail responses are served without storage in shared caches.

### Article and advertisement reports

Visitors choose a reason, then may add up to 500 characters of text. Article
reasons are spam, fraud, inaccurate information, misleading headline,
copyright infringement, personal data, insults, hate speech, inappropriate
media and other. Advertisement reasons are spam, fraud, prohibited item or
service, wrong category, inaccurate description, incorrect price,
inappropriate photos, insults, personal data and other.

The API uses the existing antiforgery flow and allows up to five attempts per
hour per IP and per signed-in account. Reports can be submitted only for
currently public content. Moderators and administrators use separate queues
for other users' articles and advertisements. Each queue row groups the
subject's reports. The preview shows the current publication if available,
all reports, and prior decisions. Staff can dismiss the reports they saw or
hide the subject with a trimmed reason of 1 to 500 characters. A hiding
decision checks the subject version and applies editorial withdrawal or the
direct listing block in the same transaction. Only reports up to the preview
sequence are closed. The history view retains the ID, last reported title,
reports and decisions after deletion. It omits IP addresses.

| Route | Contract |
| --- | --- |
| POST `/api/reports/{kind}/{id}` | Submit a reason and optional text. Returns 204, 400 for invalid input, 403 for a blocked account, 404 for unavailable content, or 429 when rate limited. |
| GET `/api/staff/reports/{kind}` | List grouped pending reports or resolved subjects when `history=true`. |
| GET `/api/staff/reports/{kind}/{id}` | Review the subject, reports, and decisions. |
| POST `/api/staff/reports/{kind}/{id}/decide` | Dismiss the reports shown or hide the subject. Returns 204 or 409 for a stale decision. |

The two subject kinds are `articles` and `classifieds`. The staff pages
are at `/staff/reports/articles` and `/staff/reports/classifieds`.

### Decision history and workflow notifications

Owners read listing decisions in My Advertisements and account block history
from Profile. The history shows the outcome, time and reason. Authorized
staff also see the responsible account. The listing audit includes deleted
listings by identifier. Staff history uses a publication title while one
exists and does not expose an owner's unsent edits. Moderators can view
account block history only for accounts without additional roles.
Administrators can review any account. Existing report history remains in
the report workspace.

| Route | Contract |
| --- | --- |
| GET `/api/account/classifieds/drafts/{id}/history` | Decisions for an existing listing owned by the caller. |
| GET `/api/account/block-history` | The signed-in account's block and unblock history. |
| GET `/api/staff/classifieds/history` | Listing decisions, including deleted listings. Optional `id` limits results to one listing. |
| GET `/api/staff/account-blocks/{id}/history` | Account block and unblock decisions, subject to staff roles. |

Each history returns 20 decisions per page, newest first, with `hasNextPage`.
Listing approval, return, rejection and direct block notify the listing owner.
Account block and unblock notify the affected account. Both channels default
to enabled per event and have separate switches. Email requires a confirmed
address. Notifications provide generic messages. Reasons remain in decision
history. A report that hides a listing or withdraws an article uses the
corresponding decision notification. The existing transactional outbox,
retry, replay, and retention rules apply. Deleting or disabling notifications
does not change decision history.

## Tools and technical contracts

- Classifieds owns listing revisions, taxonomy, visibility, expiry and listing
  moderation in its PostgreSQL schema, EF Core DbContext and migrations.
  Identity owns account state. Coordinate account blocks and listing visibility
  through explicit operations in one PostgreSQL transaction.
- Use ASP.NET Core controllers, Identity cookies and CSRF protection for writes.
  Apply current permissions, ownership and state at the API boundary. Use
  record versions for competing edits and decisions and return HTTP 409 on
  version conflicts. Define DTOs and routes for each agreed workflow in OpenAPI
  before generating the Angular client.
- Use Angular SSR for catalog and detail pages and browser rendering for the
  account and staff workspaces. Apply the same visibility predicate to the
  catalog, direct pages, public images and any cached results. Compare
  expiry at read time independently of scheduled work. Store timestamps in UTC.
- Reuse Media's private upload, processing and object storage pipeline. Public
  variant access follows the current public listing revision. Coordinate image
  references with revision changes. Keep photo references separate from the
  description in the API and enforce the five-photo limit on the server.
  Specify retention before adding cleanup.
- Record actor, target, time and required reason in the owning module. Enqueue
  agreed notifications in the business transaction using the existing outbox.
  Use the .NET Worker and Quartz if the agreed lifecycle needs scheduled work.
- Verify permission checks, version conflicts, rollback and commit order with
  xUnit and Testcontainers using PostgreSQL. Verify owner, visitor and staff
  journeys through Angular and the live HTTPS API.

## Delivery order

1. Define the listing model and API contract for the agreed draft form and
   catalog. Deliver private drafts and image uploads.
2. Agree moderation permissions and deliver submission, review, published
   revisions, expiry and renewal. Add
   the public catalog, detail pages and homepage link once public
   visibility and account-blocking behavior work together.
3. Deliver reports, decision history and notifications. Verify the combined
   owner and staff workflows.
4. Verify rollback, concurrency, outage recovery and browser journeys. Review
   the result and one failure scenario with the owner before closing the stage.

## Acceptance criteria

- A confirmed owner can save an incomplete draft. Submission validates the
  required fields, the selected category's price rule and photo alternative text.
  Private content and images are inaccessible to other residents and anonymous
  visitors.
- The owner selects a configured district and an active category. Archiving a
  category preserves existing associations and prevents new ones.
- First approval and subsequent valid owner edits appear anonymously in the
  catalog and detail page while the listing is visible. Search and
  filters use the current public revision. Staff compare the first published
  revision with the latest edit and can block it or finish the review.
  Corrections to a directly blocked listing remain private until approved.
- The API accepts up to five photos and rejects a sixth. The detail page shows
  the current public photos in a separate carousel immediately below the title,
  followed by the description. A listing without photos has no carousel.
- Confirmed moderators and administrators can view and decide other users'
  pending submissions. The queue shows oldest submissions first. Return and
  final rejection require a reason of up to 500 characters. Owners see the
  outcome and can resubmit only after a return. Concurrent decisions and recall
  use the submitted version and allow a single winner.
- At the seven-day boundary an expired listing disappears from all public reads
  and stays in the owner's account. Renewal of unchanged eligible content starts
  a new seven-day period. A directly blocked listing stays hidden through correction
  until approval.
- Forced failure while hiding listings rolls back the account block and listing
  changes. PostgreSQL tests cover both commit orders when blocking races with
  first publication, approval of a directly blocked correction, or renewal. After the block
  commits, every affected listing stays hidden. Unblocking alone restores none.
- Staff permissions prevent a moderator from blocking staff. The block, unblock,
  listing moderation and report decisions remain auditable under the agreed rules.
- An anonymous report leaves the listing public and enters the queue. Dismissal
  clears the reviewed reports from the queue, and a new report reopens the listing
  with earlier decisions visible.
- Agreed notifications survive a delivery outage and resume without repeating
  business decisions. The owner reviews the workflows and one failure scenario.

## Verification

On 2026-11-24 the solution built with no warnings or errors and passed the
format check. All 16 targeted xUnit tests for Classifieds, infrastructure and
installation passed using PostgreSQL and RabbitMQ containers.

- `PrivateDraft_EnforcesConfirmationOwnershipAndVersionAsync` verifies private
  reads and writes, CSRF, incomplete drafts, zero price, rejection of fractional
  and out-of-range amounts, version conflicts and deletion.
- `ArchivedCategory_PreservesExistingDraftAndRejectsNewAssignmentAsync` verifies
  repeat migration, the seven starter categories and three price requirements,
  administrator-only district creation and category archival.

The live OpenAPI document matched the snapshot, Angular client regeneration
matched the checked-in client files, and the Angular application type check
passed.

After the integer price and photo changes, all 17 targeted tests for classifieds
and existing media workflows passed. `DraftPhotos_EnforceLimitOwnershipOrderAndCleanupAsync`
uses PostgreSQL and RustFS to verify five attachments, rejection of a sixth,
foreign and duplicate IDs, required alternative text, private WebP delivery,
retained attachments, reordering, and cleanup on detachment or deletion.
The existing unreleased Classifieds initial migration now creates the price
column as `bigint`. The new Media migration upgrades existing media tables.

The solution format check, live OpenAPI comparison, production Angular build
and all 103 Angular tests passed. In Edge with mocked API responses, the account form rejected a decimal
price, uploaded two photos, saved a whole price, switched carousel photos,
and deleted the draft. At viewport widths of 390 and 1440 pixels the carousel
sat between the title and description without horizontal page overflow.

The catalog administration change passed the Angular production build and
all 103 Angular tests. An Edge check with mocked API responses verified adding
a category and a district, renaming the district, archiving and restoring a
category, and preserving form input after a version conflict. It verified that
a regular account cannot access the editor and that the page fits 390 and
1440 pixel viewports.

The submission and recall change passed all seven targeted Classifieds and
infrastructure tests, the solution format check, live OpenAPI comparison,
production Angular build and all 103 Angular tests.
`Submission_ValidatesFieldsFreezesDraftAndAllowsVersionedRecallAsync` verifies
field validation, owner-only access, a single successful concurrent submission,
the pending edit and deletion guards, and an unchanged draft after recall.
An Edge check with mocked responses verified field feedback, prevention of
submission with unsaved edits, read-only fields in review, and editing after
recall.

The staff review change passed the solution format check, live OpenAPI
comparison, generated client comparison, production Angular build, and all
103 Angular tests. All six targeted Classifieds tests passed with PostgreSQL,
RabbitMQ, and RustFS. The complete .NET suite passed all 201 tests with one
xUnit worker. The targeted tests cover role and ownership checks, queue order,
reason validation, concurrent staff decisions and owner recall, and photo
access across recall and resubmission. Changed Angular files passed Prettier.
The workspace-wide Prettier check flagged 34 files outside this change.
A live HTTPS Edge check exercised submission, staff preview,
return, correction, resubmission, final rejection, and deletion. It also
checked layout at 390 and 1440 pixels. The isolated PostgreSQL database retained
both decision records after deletion, with their submitted versions, staff
actors, and UTC decision timestamps. The browser fixture seeded the owner's
Email confirmation before exercising the advertisement workflow.

First publication and atomic account blocking passed 36 targeted integration
tests with one xUnit worker. The checks forced both PostgreSQL commit orders
between approval and a block, forced rollback during an account block, and
verified the exact expiry boundary and photo access. They also exercised
Classifieds, suggestions, account administration, and API foundation behavior.
The four publication tests passed again after adding a distinct blocked-owner
approval error. The solution format check, live OpenAPI comparison, generated
client comparison, production Angular build, and all 103 Angular tests passed.

On 2026-11-25, a live HTTPS Edge check exercised approval, phone-only SSR,
public photo delivery, account blocking, and unblocking. The same owner
session kept profile and read-only listing access during the block. The
profile showed the reason, and the public page and photo returned 404.
Unblocking restored content writes and kept the listing hidden. The browser
fixture seeded Email confirmation before exercising the flow.

All 17 targeted Classifieds integration tests passed with one xUnit worker.
The checks cover immediate edits and their review, stale versions, correction
after a direct block, photo reference cleanup, and both PostgreSQL commit
orders for account blocking racing renewal or correction approval.
All 103 Angular tests passed, along with the production Angular build,
solution format verification, changed-file Prettier, live OpenAPI comparison,
and generated client comparison.

On 2026-11-25, a live HTTPS Edge check exercised immediate owner edits,
replacement photos, the staff comparison at 390 and 1440 pixels, direct
blocking, correction and approval, withdrawal, relisting, and deletion.
The test confirmed that edits and approval preserved expiry, the public API
omitted the seller's Email, staff could open both photo versions, and
unavailable listings returned 404. The isolated stand used
PostgreSQL, RabbitMQ, RustFS and the Worker. The browser fixture seeded Email
confirmation for the owner.

On 2026-11-25, a live HTTPS Edge check verified the catalog with 21 listings.
It checked the homepage button, server-rendered results, pagination with the
district retained in the URL, Russian word-form search with combined filters,
navigation to the detail page and owner form, and removal after withdrawal.
The catalog fit viewports of 390 and 1440 pixels. The disposable stand
published two listings through the API and seeded the remaining rows.

All 19 targeted Classifieds integration tests passed. The catalog checks
cover first-publication ordering across pages, relevance, Russian word forms,
inclusive price filters, exact expiry, withdrawal, both block types, and
search updates after a live edit. The production Angular build, all 103
Angular tests, changed-file Prettier, and both OpenAPI checks passed.

On 2026-11-25, a live HTTPS Edge check submitted anonymous reports on an
article and an advertisement using different categories. It checked that the
optional comment appeared after category selection, that the two staff queues
remained separate, and that dismissal left a later report pending. It also
withdrew the article from the reports workspace and checked its public 404
response. The staff preview fit 390 and 1440 pixel viewports.

All 17 targeted PostgreSQL integration tests passed. They covered migration
and readiness, anonymous CSRF and rate limiting, separate queues grouped by
subject, dismissal up to the preview sequence, moderator withdrawal,
rollback of a failed hiding decision, and history after listing deletion.
All 103 Angular tests passed, along with the production build, formatting,
and both OpenAPI checks.

On 2026-11-25, eight targeted PostgreSQL tests verified listing and account
decision history, role access, independent notification preferences, and
rollback of a report-triggered listing block and its notification. They
also covered private reasons, an unverified Email address, and retained
decisions after listing deletion or notification removal. All 104 Angular
tests passed, including a failed-page retry and navigation between account
histories. The solution and production Angular build, C# formatting,
changed-file Prettier, and both OpenAPI checks passed.
The complete .NET suite passed all 219 tests with one xUnit worker in 15m 42s.

A live HTTPS Edge check at 390 and 1440 pixels verified the owner and staff
history, actor visibility, an administrator's own staff history, notification
preferences, unblock notifications, and access to a deleted listing's audit.
With RabbitMQ and SMTP stopped, an account block persisted, the owner saw one
account notice, and PostgreSQL kept one outgoing Email envelope. After the
broker restarted, the Worker scheduled a retry while SMTP was still down.
After SMTP restarted, the Email reached `Sent` on attempt two. The isolated
stand seeded the owner's Email confirmation before the check.

## Owner acceptance

On 2026-11-25, the owner accepted the draft-to-publication flow, including
immediate public edits after first approval and subsequent staff review. The
review covered the forced failure during account blocking, which rolled back
both the account block and the listing changes.

The owner also accepted expiry and renewal, report handling, direct listing
blocks and correction approval, account blocking and manual relisting after
unblocking, decision history, and notification recovery after a delivery
outage. The owner then approved closing Stage 4. CI passed for implementation
commit `9cc86ad`.

Category-specific catalog attributes remain deferred. No Stage 4 product
decisions remain open.

## Upgrade and recovery verification

On 2026-11-25, an isolated Docker Compose stand ran the Stage 3 build from
`7f79c62` and the Stage 4 build from `9cc86ad`. The Stage 3 fixture used
confirmed accounts and a published article with a processed cover. With the
API and Worker stopped, the Stage 4 migration added the Classifieds and
Reports schemas. A second run preserved the migration history. Over HTTPS,
the upgraded stand preserved an existing session, article history, the
server-rendered article, and the cover's SHA-256 hash.

The upgraded stand published two listings with photos and blocked one of
them. With the API and Worker stopped, a custom-format PostgreSQL dump,
an authenticated export of 16 S3 objects, and the Data Protection keys were
captured. The recovery stand used a new database, private bucket, key volume,
and RabbitMQ vhost. Explicit migrations succeeded after restore, and all
16 object hashes matched the backup. Over HTTPS the restored stand preserved
the old session, article and history, the public listing and its photo, and
the blocked listing's private photo and moderation history. Anonymous reads
kept the blocked listing and photo hidden, and the public catalog included
only the visible listing. This rehearsal used synthetic local data.
