# Advertisement reports and staff resolution

## Dependencies

[02 First publication and atomic blocking](02-stage-4-first-publication-and-blocking.md).

## Outcome

Anonymous and signed-in visitors can report a public listing. Moderators review
reports grouped by listing and decide whether to dismiss them or block the
listing through the existing moderation action.

## Decisions to confirm

Agree report fields, input limits, abuse controls, administrator permissions,
and which reports a staff decision covers when a new report arrives
concurrently. Agree history retention after listing deletion.

## Acceptance criteria

- [ ] A visitor can report from the public listing page. The request uses the
  existing anonymous-write CSRF flow and the agreed abuse controls.
- [ ] A report leaves the listing public. The moderation queue shows the
  listing, total report count, and report text within authorized staff views.
- [ ] Staff can dismiss covered reports without giving a reason or block the
  listing with a reason. The result remains in history, and a later report
  returns the listing to the queue with prior decisions visible.
- [ ] Verify authorized and anonymous paths, invalid submissions, race
  behavior, and report history with PostgreSQL and a browser review flow.
