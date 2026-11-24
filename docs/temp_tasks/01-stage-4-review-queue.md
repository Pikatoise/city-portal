# Staff review and owner feedback

## Starting point

Read the current [stage specification](../stages/STAGE_4.md) before implementing.
Private drafts, photos, the catalog administration screen, and owner submission
and recall are already implemented through commit `2f648fb`. Track the stage in
[GitHub issue #15](https://github.com/Pikatoise/city-portal/issues/15).

Commit each verified task separately. Record durable verification in the stage
specification and remove the completed temporary task file.

## Dependencies

Start here. First publication is delivered in [task 02](02-stage-4-first-publication-and-blocking.md).

## Outcome

Moderators and administrators open other users' submitted listings, inspect the
saved text, contacts, and photos, and return or reject a submission with a
reason. The owner sees the decision in My Advertisements.

## Decisions to confirm

Confirm what an owner can do after rejection and how it differs from a return
for corrections. Agree any unresolved queue ordering and reason limits in the
stage specification before implementing the affected behavior.

## Acceptance criteria

- [ ] The staff queue, preview, and photo reads enforce the current role and
  submission state. Staff with either role can review other users' listings.
  Staff cannot review their own submissions.
- [ ] The reviewed content stays fixed while pending. Recall closes staff
  access to the current submission and lets the owner edit again.
- [ ] Return and rejection require a reason and record the decision, actor,
  time, and submitted version together. The owner sees the result and reason.
- [ ] Concurrent staff decisions and owner recall use the submission version.
  A stale action returns a conflict and cannot replace the winning result.
- [ ] Verify permission, conflict, and photo access cases against PostgreSQL and
  exercise the staff and owner flows in the browser.
