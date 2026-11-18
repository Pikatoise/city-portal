# Stage 3: Citizen Story Suggestions

## Goal and scope

A resident can send a topic to the portal and track it while it exists. An author
can take it from a shared queue and create a draft for the existing News workflow.
Moderators and administrators can delete suggestions manually. An unpublished
suggestion expires after 72 accumulated hours in the queue. The clock pauses
while an author works on its draft. Published suggestions remain in the account.

The product and architecture baselines are [VALUE.md](../VALUE.md#citizen-story-suggestions)
and [CORE.md](../CORE.md#citizen-submissions).

## Workflow and permissions

| Actor | Confirmed behavior |
| --- | --- |
| Resident | A registered user with confirmed Email can submit a title, description, event date and location, media, and contact details when needed. The resident can track their own suggestion while it exists. Once an author takes the suggestion, the resident cannot change its materials. |
| Author | View the shared queue, take a suggestion and create its linked draft in one step, or delete the unpublished draft by returning the suggestion. The Author role grants no rejection or deletion rights over the suggestion itself. |
| Moderator or Administrator | Delete an unpublished suggestion manually. Published suggestions remain in the resident's account. Other capabilities still depend on the account's roles. |
| Worker | Delete expired records without keeping an archived copy. |

The author queue is separate from editorial review. Creating a draft starts the
existing News workflow. The article and materials transferred into it survive
deletion of the suggestion. Article history retains the fact that the topic came
from a resident.

Title and description are required. Event date, location, contact details and
images are optional. Submission immediately enters the queue. The first claim
permanently closes resident editing, including after a return to the queue.

## Technical contracts

- Citizen Submissions owns its PostgreSQL schema, EF Core DbContext, migrations,
  and suggestion state. ASP.NET Core controllers enforce current Identity
  permissions, ownership, state, and CSRF protection. OpenAPI supplies the
  Angular client contract.
- Store creation, the current queue deadline and the pause timestamp in UTC.
  Resume by shifting the deadline by the time spent paused. After 72 hours in
  the queue the suggestion becomes inaccessible even if the Worker has yet to
  delete its row. Use the existing
  persistent Quartz scheduler for cleanup. Recheck expiry and state when writing.
- Use explicit module operations for News drafts and Media references. Coordinate
  the draft, its transferred references, and the suggestion update in one database
  transaction. The draft and its attachments must have independent references.
- Use record versions and the portal's conflict response for competing changes.
  A concurrent claim can assign the suggestion to only one author.
- The resident endpoints are under `/api/account/suggestions`. Pages contain
  up to 20 suggestions in newest-first order. Edits replace the text and optional
  fields using the last read version. Initial text limits are 200 characters for
  title, 10,000 for description, and 300 each for location and contact.
- Use browser-rendered Angular account and staff pages. Verify permissions and
  concurrency with xUnit and Testcontainers, and test resident and staff workflows
  through the browser.

## Delivery order

1. Agree the submission form, editing rules, and initial status. Build submission
   and the resident's account view.
2. Build the author queue, assignment and return, staff deletion, and expiry
   cleanup after agreeing the relevant state and access rules.
3. Add article conversion, independent Media references, and the agreed
   notification and history events.
4. Verify split-role workflows, data ownership, failure recovery, and cleanup.
   Review the result with the owner before closing the stage.

## Acceptance criteria

- A confirmed resident can submit and see their own suggestion. Other residents
  cannot read it. The API enforces the agreed rules on editing and private media.
- An author can claim and return a suggestion. Concurrent claims leave a single
  assignee. The API denies rejection and deletion based on the Author role alone.
- A Moderator without Administrator and an Administrator without Moderator can
  each delete a suggestion. A resident, Editor, or Author without either role
  cannot invoke staff deletion.
- At 72 accumulated hours in the queue, the suggestion disappears from all reads and cannot
  be claimed or converted. Cleanup resumes after a Worker restart and removes
  the expired record without an archive.
- A linked draft follows normal editorial review. Deleting the suggestion
  preserves the draft, transferred materials, and its origin in article history.
  A failed conversion leaves no partial draft or Media references.
- Residents see Queued, In progress, and Published. Published links to the
  resulting public article. The public byline credits the resident for the
  idea alongside the author and exposes no contact details.
- Only confirmed audiences can access attachments. Import accepts only ready
  images from the assigned suggestion. The public article retains its imported
  images independently of the source. First publication queues one resident
  notice subject to the settings of each channel.
- The owner checks the resident and staff flows and reviews one failure scenario.

## Attachments and notifications

- A resident can stage up to five JPEG, PNG or static WebP images per
  suggestion, up to 10 MiB each. The usual Media processor validates and
  re-encodes each file, strips metadata and creates durable processing work.
- Submission and edits attach only the resident's own suggestion uploads.
  A file can belong to one suggestion. The author queue contains no attachment
  URLs. The resident, the assigned author and moderation staff can open
  attachments through access checks against the current suggestion.
- An assigned author can copy selected ready images into the draft gallery.
  Import preserves the existing draft and is safe to retry. The author
  supplies alternative text before editorial submission.
- Expiry or manual deletion removes the private attachment references in
  the same transaction. Media reclaims unreferenced images and preserves
  independent article references. Returning a draft removes the former
  author's image access through that deleted draft.
- The first publication queues a resident notice for the account and Email
  channels using normal preferences. Publication and notification work share
  a transaction. The notice contains no contact details or private link.

## Author queue and manual deletion

The owner confirmed these access and deletion rules.

- Show authors an available queue and a separate list of their own assignments.
  The queue contains the title, description, event date and location. The
  assigned author can see the full card and its contact details. Other authors
  lose access when the assignment starts. The resident keeps access to their
  own suggestion until it is deleted or expires.
- Claim and return require the current version. A claim sets the assignee,
  pauses expiry, records the first claim time if absent and creates an article
  draft in the same PostgreSQL transaction. Return deletes the unpublished
  draft through News, clears the assignment, keeps the first claim time and
  resumes expiry. Both increment the version. News owns article history and
  current publication visibility.
- If the Author role is removed, release unpublished assignments, delete
  their drafts and increment suggestion versions within the Identity role
  change transaction. Published suggestions and articles remain. Use the existing
  role-change observer and authorization scope so a concurrent claim cannot
  create an assignment after role removal commits.
- Moderators and administrators have a separate list and can view current
  cards without contact details. Manual deletion requires the current version
  and a reason of up to 1,000 characters. Delete the suggestion and record its ID, the acting account,
  time and reason together. The record holds no copy of the suggestion.
- Queue, assignments and moderation lists show up to 20 items per page, with
  the earliest expiry first. Staff endpoints are under `/api/staff/suggestions`.
- Ordinary News draft deletion also returns the linked suggestion in the
  deletion transaction. News retains its existing audit history rules.
- The resident can follow the current public address. Withdrawal hides the
  link and leaves the published suggestion in the account. For the public
  byline, News resolves the resident's current display name through Identity.
- The upgrade returns assignments from the earlier schema to the queue under
  their existing deadlines, because they had no linked draft. It preserves
  suggestions, resident editing locks and deletion audit records.
- Verify concurrent claim, edit versus claim, access before and after
  assignment, role removal versus claim, manual deletion versus competing
  writes, and expiry while the Worker is stopped.

## Implementation status

The owner confirmed the submission and editing rules and manual deletion by
Moderator as well as Administrator. The resident text workflow, paused expiry,
author assignment, linked drafts, publication status, public credit, manual
deletion, private image attachments and first-publication notices are implemented.
Stage closure awaits owner acceptance.

The resident integration test uses PostgreSQL and the real HTTP account
endpoints. It verifies repeat migration, Email confirmation, CSRF, ownership,
text validation, competing edits, the first-claim lock, the exact 72-hour
visibility boundary, and physical deletion by the job.

The staff integration test verifies competing claims, contact visibility,
return without restoring resident editing, and release during a role removal
racing another claim. It checks both staff roles can delete, stale versions
and blank reasons are rejected, and the audit entry survives the physical
deletion of the suggestion. An Angular regression verifies that a forbidden
refresh removes previously displayed contacts from the workspace.

The publication workflow test uses the production review endpoints. It verifies
that cleanup skips paused suggestions, return deletes the draft and resumes the
remaining time, publication retains the suggestion beyond its original deadline,
and the public byline shows the resident's name without contact details.

The image integration test runs against PostgreSQL and private S3-compatible
storage. It covers foreign uploads, the attachment limit, role-specific
reads, repeated import, loss of access after return, and cleanup after expiry
and manual deletion. It verifies an article can still read its imported image
after suggestion deletion and an image removed from the only draft is reclaimed.
The publication test verifies one account notice and one queued Email across
first publication and republication.

The isolated Compose check used current built images with PostgreSQL, RabbitMQ,
RustFS, Mailpit and Caddy. A 390 px resident browser submitted a real image and
had no horizontal overflow. A 1440 px author browser claimed the suggestion,
selected the processed image and opened its draft. The normal editorial API
completed review and publication. The resident reopened the article from the
account and saw the author and idea credit. The imported image loaded as public
WebP. The account notice appeared and Mailpit received the Email. The portal's
HTTPS hostname and local CA were verified separately from the isolated browser
contexts. The live OpenAPI document matched the client snapshot.

With the Worker stopped, an expired queued suggestion disappeared from the
resident UI while its row remained. The published suggestion still linked
to the article. The persistent Quartz job removed the expired row after
the Worker restarted.

The final Release build completed without warnings or errors. The backend
passed 188 tests with no failures or skips, and the TRX counters matched all
188 results. Angular passed 97 tests in 16 files. The production Angular
build, formatting, generated-client check and Media migration model check
passed.

## Owner verification

From the homepage choose **Suggest a story** to submit a suggestion with a
photo. From an Author account open **Resident suggestions**, take the item,
select a ready attachment in **My assignments**, then open the draft. Submit
the completed article and publish after an Editor approves it. Check the
published link and notice in the resident account and the byline on the public
article. To review the failure path, try claiming one queued suggestion with
two different authors or return an unpublished suggestion and verify that its
old draft is gone and its remaining queue time resumes.
