# Stage 4: Classifieds and Moderation

## Status and sources

In development. Stage 3 was accepted on 2026-11-19. The account's My
Advertisements page supports private drafts, their photo carousel, and
submission and recall. The category and district administration screen and
API are implemented. Staff can review, return, and reject pending submissions.
First publication, direct blocks, and public listing pages remain pending.
[VALUE.md](../VALUE.md#classified-advertisements) owns product rules and
[CORE.md](../CORE.md#classifieds-and-moderation) owns architecture and
transaction requirements.
Track specification decisions in [issue #15](https://github.com/Pikatoise/city-portal/issues/15)
in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3).

## Goal and scope

Residents can manage advertisements in their accounts. Visitors can browse
and report public listings. Staff can review listings and reports, block
listings, and restrict accounts within their permissions.

Deliver configurable categories and attributes, listing images, the public
catalog and detail pages, recent listings on the homepage, owner and staff
workspaces, decision history, and notifications. Listing contacts are phone
and/or Email.

## Workflows and permissions

| Actor | Agreed behavior |
| --- | --- |
| Visitor | Browse available listings, search and filter by category, price, location and relevant attributes, and submit a report without logging in. |
| Owner | With confirmed Email, create and save drafts, submit, edit, withdraw, delete and renew own listings subject to their current state. See listing status and review feedback in the account. |
| Moderator | Review other users' submitted versions and reports, block listings with a reason, and block or unblock ordinary accounts. Corrections belong to the listing owner. |
| Administrator | Review other users' submitted versions, configure the catalog, and block or unblock accounts, including staff, subject to the account safeguards to be agreed below. |

- First publication requires approval. Changes await moderation while the
  last published revision remains visible for its current publication period.
- Approval publishes immediately. Return for corrections and rejection require
  a reason. The owner can recall a pending submission to resume editing.
- Publication lasts seven days. The owner can renew unchanged active or expired
  content for seven days from renewal without a new review. Expiry hides the
  listing publicly and leaves it in the owner's account.
- A direct listing block hides the listing and prevents renewal and relisting.
  The owner corrects and resubmits it for approval.
- Account blocking requires a reason and persists until staff remove it. The
  block and hiding all of the account's listings commit together. A blocked
  owner cannot publish or renew through an owner or staff action. Unblocking
  leaves listings hidden until the owner acts. Direct listing blocks still apply.
- Reports leave listings visible until a moderator acts. The queue shows the
  total count and report text. Staff may dismiss reports without a reason.
  Decisions remain in history. A new report returns the listing to the queue.

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
returns the draft to `Draft`, preserves its fields and attachments, and
increments the version again. A stale version or invalid transition returns
HTTP 409. The shared account authorization lock serializes these actions
with concurrent owner writes and excludes catalog changes during validation.
First publication requires the later staff decision and account-blocking workflow.

### Staff review and owner feedback

Confirmed moderators and administrators can review pending submissions from
other accounts. The browser-rendered `/staff/classifieds/review` opens from
their profile. The queue shows 20 items per page in ascending submission time
with listing ID as a tie-breaker. The preview includes the saved title,
description, contacts, category, district, price, and attached photos.

| Routes under `/api/staff/classifieds/review` | Contract |
| --- | --- |
| GET the collection | List pending submissions from other accounts. |
| GET `/{id}` and `/{id}/images/{imageId}` | Read the submitted preview or a ready attached photo. Photo reads require the preview version in the query. Return 404 outside an accessible pending submission. |
| POST `/{id}/return` or `/{id}/reject` | Accept the preview version and a reason of 1 to 500 characters after trimming. Require CSRF. Return 204 on success and 409 for a stale version or closed review. |

Return sets the owner state to `Returned` and allows editing, deletion, and
resubmission. Rejection sets `Rejected` and allows owner reads and deletion.
The owner sees the decision and reason in My Advertisements. Resubmission
opens a new review without deleting the prior decision record. Classifieds
stores the decision, reason, actor, UTC time, and submitted version together
with the draft state change. Staff decisions use the exclusive administration
lock to serialize with owner writes and recheck the current version.

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
  catalog, homepage, direct pages, public images and any cached results. Compare
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
   the public catalog, detail pages and homepage listings once public
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
- An approved revision appears anonymously in the catalog, homepage and detail
  page as applicable. Pending text, contacts and images stay private while the
  published revision remains visible. Search and filters use public content.
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
  first publication, staff publication of a change, or renewal. After the block
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
RabbitMQ, and RustFS. They cover role and ownership checks, queue order,
reason validation, concurrent staff decisions and owner recall, and photo
access across recall and resubmission. Changed Angular files passed Prettier.
The workspace-wide Prettier check flagged 34 files outside this change.
A live HTTPS Edge check exercised submission, staff preview,
return, correction, resubmission, final rejection, and deletion. It also
checked layout at 390 and 1440 pixels. The isolated PostgreSQL database retained
both decision records after deletion, with their submitted versions, staff
actors, and UTC decision timestamps. The browser fixture seeded the owner's
Email confirmation before exercising the advertisement workflow.

## Open decisions

### Moderation and visibility

- Confirm whether administrators also handle advertisement reports.
- Define how renewal interacts with a pending revision, and which relisting
  operations are available after withdrawal, expiry or an account unblock.
- Agree public contact display, URL and unavailable-page behavior, search order,
  homepage size and listing deletion and image retention rules.
- Agree moderator authority over mixed-role accounts, safeguards for self-block
  and the last administrator, and how active sessions behave after unblock.

### Reports and notifications

- Set report input, abuse controls, and how a staff decision applies when a new
  report arrives concurrently. Agree the history retained after listing deletion.
- Agree notification events, recipients and default channels. Reuse the existing
  account and Email preferences and delivery rules when applicable.
